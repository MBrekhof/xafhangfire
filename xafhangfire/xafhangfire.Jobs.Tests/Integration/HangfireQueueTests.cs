using System.Diagnostics;
using FluentAssertions;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;

namespace xafhangfire.Jobs.Tests.Integration;

// TEST-001: JOB-013 regression. Hangfire.PostgreSql deletes a finished job's queue row with
// `WHERE id = @Id AND fetchedat = @FetchedAt`; under Npgsql's legacy timestamp switch fetchedat came back
// shifted, the delete matched nothing and the row stayed forever.
[Collection("Postgres")]
public class HangfireQueueTests(PostgresDatabase db)
{
    public static void Noop() { }

    [PostgresFact]
    public void FinishedJob_LeavesTheQueue()
    {
        _ = db; // the fixture created the database
        // The legacy switch read fetchedat in this machine's zone and wrote it back in the session's zone, so the bug
        // only shows when the two offsets differ: pin a session zone that differs from this machine's.
        var sessionZone = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow) == TimeSpan.Zero ? "Etc/GMT-2" : "UTC";
        var options = new PostgreSqlStorageOptions { QueuePollInterval = TimeSpan.FromMilliseconds(200) };
        var storage = new PostgreSqlStorage(
            new NpgsqlConnectionFactory($"{PostgresDatabase.ConnectionString};Timezone={sessionZone}", options), options);

        var jobId = new BackgroundJobClient(storage).Enqueue(() => Noop());
        var id = long.Parse(jobId);
        QueueRows(id).Should().Be(1, "the job is queued before a server runs");

        using (new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1, ServerName = "TEST-001" }, storage))
        {
            string? state = null;
            WaitUntil(() => (state = StateOf(storage, jobId)) == "Succeeded", TimeSpan.FromSeconds(30))
                .Should().BeTrue($"job {jobId} should succeed (last state: {state})");
            WaitUntil(() => QueueRows(id) == 0, TimeSpan.FromSeconds(10))
                .Should().BeTrue($"job {jobId} succeeded but its hangfire.jobqueue row is still there (JOB-013)");
        }
    }

    private static string? StateOf(JobStorage storage, string jobId)
    {
        using var connection = storage.GetConnection();
        return connection.GetStateData(jobId)?.Name;
    }

    private static long QueueRows(long jobId) =>
        (long)PostgresDatabase.Scalar("""select count(*) from hangfire.jobqueue where jobid = @id""", ("id", jobId))!;

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout) return false;
            Thread.Sleep(100);
        }
        return true;
    }
}
