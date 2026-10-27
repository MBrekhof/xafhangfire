using System.ComponentModel;
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.Base.General;
using xafhangfire.Module.BusinessObjects;
using xafhangfire.Module.Helpers;

namespace xafhangfire.Blazor.Server.Services;

// UI-001: one upcoming run of an enabled JobDefinition, shown in the Scheduler. Non-persistent and read-only.
[DomainComponent]
[NavigationItem("Jobs")]
[XafDisplayName("Job Calendar")]
[DefaultProperty(nameof(Subject))]
public class JobCalendarEntry : IEvent
{
    // The Blazor scheduler maps the appointment id to a property named AppointmentId.
    [DevExpress.ExpressApp.Data.Key]
    [Browsable(false)]
    public string AppointmentId { get; set; }
    object IEvent.AppointmentId => AppointmentId;

    [Browsable(false)]
    public Guid JobDefinitionId { get; set; }

    public string Subject { get; set; }
    public string Description { get; set; }
    public DateTime StartOn { get; set; }
    public DateTime EndOn { get; set; }
    [Browsable(false)] public bool AllDay { get; set; }
    [Browsable(false)] public string Location { get; set; }
    [Browsable(false)] public int Label { get; set; }
    [Browsable(false)] public int Status { get; set; }
    [Browsable(false)] public int Type { get; set; }
    [Browsable(false)] public string ResourceId { get; set; }
}

public static class JobCalendar
{
    // ponytail: a cron run is an instant; the scheduler needs an end. Upgrade: average duration from JobExecutionRecords.
    public static readonly TimeSpan RunLength = TimeSpan.FromMinutes(15);

    // ponytail: caps a */5 cron in month view (~12k runs) at about ten days; the last shown entry says so.
    public const int MaxRunsPerDefinition = 3000;

    public const string MoreRunsSuffix = " (more runs not shown)";

    // The documented non-persistent pattern: a DynamicCollection whose FetchObjects is re-raised whenever the
    // view's criteria change. The scheduler's ActiveViewFilter criteria carry the visible interval (1=2 until the
    // scheduler has rendered); ShapeData applies them in memory.
    public static void Attach(NonPersistentObjectSpace objectSpace, IServiceProvider services)
    {
        objectSpace.ObjectsGetting += (_, e) =>
        {
            if (e.ObjectType != typeof(JobCalendarEntry))
                return;
            var collection = new DynamicCollection(objectSpace, e.ObjectType, e.Criteria, e.Sorting, e.InTransaction);
            collection.FetchObjects += (_, f) =>
            {
                if (SelectsNothing(f.Criteria))
                {
                    f.Objects = Array.Empty<JobCalendarEntry>(); // the scheduler's 1=2 before it has rendered: skip the query
                    return;
                }
                var (startLocal, endLocal) = GetInterval(f.Criteria, DateTime.Now);
                // Per fetch, so edited definitions show up; secured, so a role without JobDefinition read sees nothing.
                using var definitions = services.GetRequiredService<IObjectSpaceFactory>().CreateObjectSpace(typeof(JobDefinition));
                var enabled = definitions.GetObjectsQuery<JobDefinition>()
                    .Where(d => d.IsEnabled && d.CronExpression != null && d.CronExpression != "")
                    .ToList();
                f.Objects = Build(enabled, startLocal, endLocal);
                f.ShapeData = true;
            };
            e.Objects = collection;
        };
    }

    // The scheduler's visible interval (local wall clock) from its criteria; outside the scheduler, today-1 .. today+31.
    public static (DateTime StartLocal, DateTime EndLocal) GetInterval(CriteriaOperator criteria, DateTime nowLocal)
    {
        var between = FindBetween(criteria);
        if (between?.BeginExpression is OperandValue { Value: DateTime start }
            && between.EndExpression is OperandValue { Value: DateTime end })
            return (start, end);
        return (nowLocal.Date.AddDays(-1), nowLocal.Date.AddDays(31));
    }

    // What the controller sets to narrow ActiveViewFilter: the overlap part of the scheduler's own criteria.
    public static CriteriaOperator IntervalCriteria(DateTime startLocal, DateTime endLocal) => CriteriaOperator.Or(
        new BetweenOperator(nameof(JobCalendarEntry.StartOn), startLocal, endLocal),
        new BetweenOperator(nameof(JobCalendarEntry.EndOn), startLocal, endLocal),
        CriteriaOperator.And(
            new BinaryOperator(nameof(JobCalendarEntry.StartOn), startLocal, BinaryOperatorType.LessOrEqual),
            new BinaryOperator(nameof(JobCalendarEntry.EndOn), endLocal, BinaryOperatorType.GreaterOrEqual)));

    private static readonly CriteriaOperator SelectNothing = CriteriaOperator.Parse("1=2");

    private static bool SelectsNothing(CriteriaOperator criteria) => criteria switch
    {
        GroupOperator { OperatorType: GroupOperatorType.And } and => and.Operands.Any(SelectsNothing),
        _ => SelectNothing.Equals(criteria),
    };

    private static BetweenOperator FindBetween(CriteriaOperator criteria) => criteria switch
    {
        BetweenOperator { TestExpression: OperandProperty { PropertyName: nameof(JobCalendarEntry.StartOn) } } between => between,
        GroupOperator group => group.Operands.Select(FindBetween).FirstOrDefault(b => b is not null),
        _ => null,
    };

    public static List<JobCalendarEntry> Build(IEnumerable<JobDefinition> definitions, DateTime startLocal, DateTime endLocal)
    {
        // Cron runs in UTC (Hangfire's default); the scheduler shows local time. Start one RunLength early so a run
        // that began just before the interval but overlaps it is still shown.
        var fromUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime() - RunLength;
        var toUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Local).ToUniversalTime();
        var entries = new List<JobCalendarEntry>();
        foreach (var definition in definitions.Where(d => d.IsEnabled))
        {
            // One extra run past the cap and RunLength past the end: the next run that shortens the last shown one,
            // and the proof that runs were hidden.
            var runs = CronHelper.GetRunsBetween(definition.CronExpression, fromUtc, toUtc + RunLength, MaxRunsPerDefinition + 1);
            var description = $"{CronHelper.GetDescription(definition.CronExpression)} · {definition.JobTypeName}";
            for (var i = 0; i < runs.Count && i < MaxRunsPerDefinition && runs[i] < toUtc; i++)
            {
                var runUtc = runs[i];
                var start = DateTime.SpecifyKind(runUtc.ToLocalTime(), DateTimeKind.Unspecified);
                // Shorter than RunLength when the next run comes sooner, so a */5 cron doesn't stack three deep.
                var length = i + 1 < runs.Count && runs[i + 1] - runUtc < RunLength ? runs[i + 1] - runUtc : RunLength;
                entries.Add(new JobCalendarEntry
                {
                    AppointmentId = $"{definition.Id:N}-{runUtc.Ticks}",
                    JobDefinitionId = definition.Id,
                    Subject = definition.Name,
                    Description = description,
                    StartOn = start,
                    EndOn = start + length,
                });
            }
            if (runs.Count > MaxRunsPerDefinition && runs[MaxRunsPerDefinition] < toUtc)
                entries[^1].Subject += MoreRunsSuffix;
        }
        return entries;
    }
}
