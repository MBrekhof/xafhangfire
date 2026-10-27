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
    private static int _longJobRuns;

    public static void Noop() { }

    public static void LongJob()
    {
        // Count on entry: a second worker's run is still sleeping when the first one finishes.
        Interlocked.Increment(ref _longJobRuns);
        Thread.Sleep(TimeSpan.FromSeconds(8));
    }

    [PostgresFact]
    public void FinishedJob_LeavesTheQueue()
    {
        var storage = Storage(new PostgreSqlStorageOptions { QueuePollInterval = TimeSpan.FromMilliseconds(200) });

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

    // JOB-008: the keep-alive must match its own fetchedat round trip, or a job longer than the invisibility timeout
    // is handed to a second worker. 3 s timeout vs an 8 s job: without a working keep-alive it runs twice.
    [PostgresFact]
    public void SlidingTimeout_LongJobRunsOnce()
    {
        var storage = Storage(new PostgreSqlStorageOptions
        {
            QueuePollInterval = TimeSpan.FromMilliseconds(200),
            UseSlidingInvisibilityTimeout = true,
            InvisibilityTimeout = TimeSpan.FromSeconds(3),
        });
        Interlocked.Exchange(ref _longJobRuns, 0);

        var jobId = new BackgroundJobClient(storage).Enqueue(() => LongJob());
        var id = long.Parse(jobId);

        using (new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 2, ServerName = "JOB-008" }, storage))
        {
            string? state = null;
            WaitUntil(() => (state = StateOf(storage, jobId)) == "Succeeded", TimeSpan.FromSeconds(30))
                .Should().BeTrue($"job {jobId} should succeed (last state: {state})");
            WaitUntil(() => QueueRows(id) == 0, TimeSpan.FromSeconds(10))
                .Should().BeTrue($"job {jobId} succeeded but its hangfire.jobqueue row is still there");
        } // Dispose waits for every worker, so a second run has been counted by now.

        _longJobRuns.Should().Be(1, "the keep-alive should keep the second worker from fetching the running job");
    }

    private PostgreSqlStorage Storage(PostgreSqlStorageOptions options)
    {
        _ = db; // the fixture created the database
        // The legacy switch read fetchedat in this machine's zone and wrote it back in the session's zone, so the bug
        // only shows when the two offsets differ: pin a session zone that differs from this machine's.
        var sessionZone = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow) == TimeSpan.Zero ? "Etc/GMT-2" : "UTC";
        var storage = new PostgreSqlStorage(
            new NpgsqlConnectionFactory($"{PostgresDatabase.ConnectionString};Timezone={sessionZone}", options), options);
        // The constructor installs the schema; clear rows a failed earlier test may have left for this server to fetch.
        PostgresDatabase.Scalar("delete from hangfire.jobqueue");
        return storage;
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
