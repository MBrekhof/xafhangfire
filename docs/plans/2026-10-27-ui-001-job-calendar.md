# UI-001: Job calendar (scheduler view of upcoming job runs) — plan

Card: UI-001 (CARD-2559): "DevExpress Scheduler module is already registered. Show JobDefinitions (cron next-runs)
on a calendar view." Martin skipped the design questions ("just start", 2026-10-27), so the choices below are
Claude's calls; each is marked.

Built AFTER UPG-001 lands (DevExpress 26.1.6), so the calendar is written once against the 26.1 Scheduler.

## What the user gets

A "Job Calendar" navigation item under Jobs (Blazor app). It opens the DevExpress Scheduler (the XAF
`SchedulerListEditor`) showing, for every enabled JobDefinition with a valid cron expression, one appointment per
upcoming run in the visible date range. Subject = the definition's Name; the tooltip/description shows the cron
description and the job type. Clicking an appointment opens a read-only detail with a link to its JobDefinition.
Read-only: no creating, moving, resizing or deleting appointments.

Claude's calls (Martin can override):
- **Future runs only**, as the card says. Past runs (JobExecutionRecords) are not shown. Add later if wanted.
- **Blazor only.** The Win app has no Scheduler.Win package, and WIN-002 (WinForms parity) is undecided, so the
  type lives in the Blazor module and the Win app doesn't see a navigation item for it.
- **Times shown in the server's local time zone** (the same convention `NextScheduledRuns` already uses:
  `CronHelper.FormatNextRuns` → `ToLocalTime()`); cron is evaluated in UTC, as Hangfire's `RecurringJob.AddOrUpdate`
  does without a TimeZone option (HangfireJobDispatcher.cs:36).
- **Each appointment is 15 minutes long** (a cron run is an instant; the scheduler needs an end). Ponytail
  comment with the upgrade path (average duration from JobExecutionRecords).
- **Cap: 200 occurrences per definition per visible range**, so a `* * * * *` cron in month view can't produce
  40k appointments. Ponytail comment names the ceiling.
- **Disabled definitions are not shown** (they have no Hangfire recurring job, JOB-003).

## Design

1. `JobCalendarEntry` — a non-persistent class in xafhangfire.Blazor.Server (registered via
   `xafhangfireBlazorModule.AdditionalExportedTypes`), implementing `DevExpress.Persistent.Base.General.IEvent`
   (26.1 source `IEvent.cs`: Subject, Description, StartOn, EndOn, AllDay, Location, Label, Status, Type,
   ResourceId, AppointmentId). Key: a deterministic id (`$"{definitionId:N}-{startUtc.Ticks}"`) so XAF can find
   the same object again on refresh. `[NavigationItem("Jobs")]`, `[DefaultClassOptions]` off where it would add
   New. Property `JobDefinition` (reference, for the link) fetched from the persistent object space.
2. Occurrence generation is a pure static function in xafhangfire.Module/Helpers/CronHelper.cs, next to
   `GetNextRuns`: `GetRunsBetween(string cron, DateTime fromUtc, DateTime toUtc, int cap)` using Cronos
   `GetOccurrences(from, to)`. Invalid cron → empty (same as `GetNextRuns`).
3. Data supply: a Blazor ViewController on the `JobCalendarEntry` ListView (xaf-viewcontroller-patterns):
   - OnActivated: subscribe the list view's `NonPersistentObjectSpace.ObjectsGetting`; when the editor is a
     `SchedulerListEditor`, subscribe its `FilterDataSource` event (26.1 source `SchedulerListEditorBase.cs:196`,
     raised by `SchedulerDataSourceFilterController` with the visible interval), remember the interval and
     reload the collection.
   - ObjectsGetting: query enabled JobDefinitions from an additional (persistent) object space, compute the runs
     in the remembered interval (default: today − 1 day .. today + 31 days before the first FilterDataSource),
     convert to local time, return `JobCalendarEntry` objects.
   - OnDeactivated: unsubscribe in reverse order.
   Exact wiring (ObjectsGetting vs DynamicCollection, whether the scheduler's own interval criteria filter the
   result in memory, how to reload) is settled by the spike below against the 26.1 sources and dxdocs.
4. Read-only: disable the New/Delete actions and the scheduler's appointment editing (`SchedulerModel`
   AllowCreateAppointment/AllowEditAppointment/AllowDeleteAppointment/AllowDragAppointment = false, or the 26.1
   equivalents; verify the names in dxdocs).
5. Security: grant read on `JobCalendarEntry` to the role(s) that read JobDefinition (Updater.cs:454); Admin is
   covered. The entries only expose what JobDefinition read access already exposes.

## Spike first (throwaway, then fold into the real change)

Before writing the real code, a 30-minute spike on the 26.1 tree answers:
- Does `SchedulerListEditor` render non-persistent `IEvent` objects from `ObjectsGetting`, and does month/week
  navigation raise `FilterDataSource` with the new interval?
- Does its `ActiveViewFilter`/interval criteria get applied to non-persistent collections (in memory), or must
  the handler filter itself?
- Clicking an appointment: does it open the entry's DetailView (non-persistent, read-only) without errors?

## Tests

- Unit (Jobs.Tests): `CronHelper.GetRunsBetween` — every-5-min cron over one hour = 12 (bounds exclusive/inclusive
  as Cronos defines), cap respected, invalid/blank cron → empty, range with no runs → empty.
- Playwright (C#), light + dark: open Job Calendar; the seeded "Scheduled Demo (Every 5 Min)" definition shows
  appointments today; navigate to next week → appointments still present; click one → detail shows the
  JobDefinition link; no New/Delete buttons; dragging an appointment does nothing.
- Disable the definition → its appointments disappear after refresh; re-enable afterwards (restore test data).

## Spike result and Gemini plan review (2026-10-27) — settled

Checked against the 26.1 sources (Scheduler.Blazor, ExpressApp) and dxdocs ("Non-Persistent Objects", Filter and
Sort): the wiring is the documented `DynamicCollection` + `FetchObjects` + `ShapeData = true`, attached to every
`NonPersistentObjectSpace` in `Startup` (`ObjectSpaceProviders.Events.OnObjectSpaceCreated`). No controller event
wiring is needed for the interval: `SchedulerListViewControllerBase` puts it into `CollectionSource.Criteria
["ActiveViewFilter"]` (`1=2` first, then the visible interval; the Blazor controller sets `ExtendVisibleInterval =
false`), `BaseObjectSpace.ApplyCriteria` hands it to the DynamicCollection, which re-raises `FetchObjects`.
`JobCalendar.GetInterval` reads the interval from that criteria (the same `BetweenOperator` the DX controller reads
back); ShapeData then filters in memory.

1. Blocking, reload/criteria on a List/BindingList: true (BaseObjectSpace.CanApplyCriteria). Settled by the
   DynamicCollection above, not by Gemini's ResetCollection + FilterDataSource loop guard.
2. Object space per fetch: adopted (`using`, from `IObjectSpaceFactory`, secured).
3. `[DomainComponent]` + `[Key]` on a public `AppointmentId` (the Blazor mapping name): adopted.
4. `AllowEditAppointment = false` would suppress the open: true (SchedulerListEditor.cs:109-121). Kept on; the
   controller handles `CustomProcessSelectedItem` and opens the JobDefinition detail view.
5. Read-only: `View.AllowNew/AllowDelete/AllowEdit = false` (AllowEdit drives drag/resize) plus
   `SchedulerModel.AllowCreateAppointment/AllowDeleteAppointment = false` in OnViewControlsCreated.
6. Security: not reproduced. Live as User (Default role): no Job Calendar navigation item, and the view opened by
   URL is empty (the per-fetch JobDefinition query is secured). No Updater change.
7. Time zone: the interval is local wall clock, converted with `SpecifyKind(Local).ToUniversalTime()`. Gemini's
   claim that HangfireJobDispatcher passes `TimeZone = TimeZoneInfo.Utc` is wrong (no options are passed); Hangfire's
   default is UTC either way.
8. Cap: raised to 3000 runs per definition per range; the last shown run's subject ends with "(more runs not shown)".
9. Projection tested: `JobCalendarTests` runs `Attach` through a real `NonPersistentObjectSpace` + `CollectionSource`
   whose criteria change like the scheduler's (1=2 → day → next day).

Found while building: 15-minute blocks for a */5 cron stacked three deep, so a run now ends at the next run's start
when that is sooner.

Live (Playwright, light + dark): day view shows the 288 runs of "Scheduled Demo (Every 5 Min)", Next Day re-fetches,
double-click opens the JobDefinition, no New/Delete, dragging does not move a run, no new errors in the log.

## Gemini diff review (2026-10-27) — settled

1. Month → Day inside the month kept the month's capped runs: DX only widens ActiveViewFilter. Reproduced live
   (Playwright `cal-month`: Day 289 → Month → Day **1**). Fixed: `JobCalendarController` handles
   `SchedulerListEditor.FilterDataSource` and narrows ActiveViewFilter to the visible interval
   (`JobCalendar.IntervalCriteria`); equal bounds are a no-op, so no reload loop. Live after the fix: Day after Month
   = 289. No unit test (needs the scheduler editor); the Playwright mode is the check.
2. The initial `1=2` fetch queried JobDefinitions for nothing, and `FindBetween` matched any Between. Reproduced by
   failing tests; fixed (`SelectsNothing` short-circuit; only a Between on StartOn counts).
3. The last run of a range got the full 15 minutes; the cap marker fired at exactly 3000 runs. Reproduced by failing
   tests; fixed by fetching one run past the cap and RunLength past the end.
4. Tests tightened: every */5 run is 5 minutes, exactly-cap vs cap+1, query count, `using` on the CollectionSource.

Month view of a */5 job still shows only the first ~10 days (the cap, with the marker on the last shown run): the
known ceiling named in the ponytail comment.

## Out of scope

Past runs; per-definition colours/labels; WinForms; showing Hangfire's actual next-execution (from its recurring
job hash) instead of the cron computation — they agree for UTC crons.
