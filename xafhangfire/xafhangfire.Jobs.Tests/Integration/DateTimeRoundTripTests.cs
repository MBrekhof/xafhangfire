using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Jobs.Tests.Integration;

// TEST-001: the UPG-001 DateTime convention against the real database (WIN-001 failed on exactly these writes).
[Collection("Postgres")]
public class DateTimeRoundTripTests(PostgresDatabase db)
{
    [PostgresFact]
    public void EveryKind_IsStoredAsWritten_Filtered_AndUpdated()
    {
        foreach (var kind in new[] { DateTimeKind.Utc, DateTimeKind.Local, DateTimeKind.Unspecified })
        {
            var value = new DateTime(2026, 10, 27, 12, 0, 0, kind);
            Guid projectId, recordId;
            using (var context = db.CreateContext())
            {
                var project = context.CreateProxy<Project>();
                project.Name = $"TEST-001 {kind}";
                project.DueDate = value; // DateTime?
                var record = context.CreateProxy<JobExecutionRecord>();
                record.JobName = $"TEST-001 {kind}";
                record.StartedUtc = value; // DateTime
                context.AddRange(project, record);
                context.SaveChanges();
                (projectId, recordId) = (project.Id, record.Id);
            }
            StoredText(projectId, recordId).Should().Be(("2026-10-27 12:00:00", "2026-10-27 12:00:00"), kind.ToString());

            using (var context = db.CreateContext())
            {
                var project = context.Projects.Single(p => p.Id == projectId && p.DueDate == value);
                var record = context.JobExecutionRecords.Single(r => r.Id == recordId && r.StartedUtc == value);
                project.DueDate!.Value.Ticks.Should().Be(value.Ticks, kind.ToString());
                project.DueDate.Value.Kind.Should().Be(DateTimeKind.Unspecified, kind.ToString());
                record.StartedUtc.Ticks.Should().Be(value.Ticks, kind.ToString());
                record.StartedUtc.Kind.Should().Be(DateTimeKind.Unspecified, kind.ToString());

                project.DueDate = value.AddDays(1);
                record.StartedUtc = value.AddDays(1);
                context.SaveChanges();
            }
            StoredText(projectId, recordId).Should().Be(("2026-10-28 12:00:00", "2026-10-28 12:00:00"), kind.ToString());
        }
    }

    // The raw column text: proves the wall clock went in unshifted, whatever the reader converts.
    private static (string?, string?) StoredText(Guid projectId, Guid recordId) => (
        (string?)PostgresDatabase.Scalar("""select "DueDate"::text from "Projects" where "Id" = @id""", ("id", projectId)),
        (string?)PostgresDatabase.Scalar("""select "StartedUtc"::text from "JobExecutionRecords" where "Id" = @id""", ("id", recordId)));
}
