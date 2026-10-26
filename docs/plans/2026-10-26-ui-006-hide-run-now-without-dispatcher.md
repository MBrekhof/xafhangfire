# UI-006: hide Run Now where no job dispatcher is registered

Card: UI-006 (CARD-2578). Decision (Martin, 2026-10-26, option a): hide the action. Do not register the
dispatcher in the WinForms app.

## Problem (reproduced 2026-10-26)

`JobSchedulerViewController` lives in the shared Module, so the WinForms app shows Run Now too. Only
`xafhangfire.Blazor.Server/Startup.cs:272` registers `JobDispatchService`, as transient. In the Win app, Run Now
calls `GetRequiredService<JobDispatchService>()`, which throws. Martin ran it in the Win app (Admin, "Generate
Contact List Report"), and it showed "Job '…' failed: No service for type 'xafhangfire.Jobs.JobDispatchService' has
been registered." The catch also marks the definition `Failed`, a false failure in the job's status.

## Change

`xafhangfire.Module/Controllers/JobSchedulerViewController.cs`:

```csharp
private const string HasDispatcherKey = "HasDispatcher";

protected override void OnActivated()
{
    base.OnActivated();
    // UI-006: only the Blazor host registers JobDispatchService; elsewhere (WinForms) Run Now can't work.
    _runNowAction.Active[HasDispatcherKey] =
        Application?.ServiceProvider?.GetService<IServiceProviderIsService>()?.IsService(typeof(JobDispatchService)) == true;
}

protected override void OnDeactivated()
{
    _runNowAction.Active.RemoveItem(HasDispatcherKey);
    base.OnDeactivated();
}
```

- The registration check builds nothing (see the review section below).
- `RunNow` keeps `GetRequiredService` and its catch. It is unreachable while the action is hidden, but remains the
  safety net.
- `Application.ServiceProvider` is set in both the Win and the Blazor apps (XAF builder apps).

## Cases and how each is verified

| Case | Expected | Verified by |
|------|----------|-------------|
| Win app, JobDefinition list and detail view | No Run Now button | Martin, same steps as the repro |
| Blazor app, JobDefinition list and detail view | Run Now visible and dispatches as before | Playwright (C#) `runnow` mode against the running app |
| Controller re-activated (navigate away and back) | Key re-set on activation, removed on deactivation; no stale state | Code (skill pattern: set after `base.OnActivated`, `RemoveItem` before `base.OnDeactivated`) |
| Existing tests + JobSchedulerViewControllerTests | Pass | `dotnet test` |

## Gemini plan review (2026-10-26): nothing blocking, both should-fix points adopted

1. `GetService<JobDispatchService>()` constructs the service and its dependency graph. If a dependency throws,
   `OnActivated` throws and `Frame.SetView` refuses to open any JobDefinition view. Changed to
   `IServiceProviderIsService.IsService(typeof(JobDispatchService))`, which checks the registration without
   constructing anything; both hosts use Microsoft DI. No provider, or no IsService → hidden.
2. Gemini suggested unit-testing activation with a stub XafApplication and `Active[ViewIsAssignedReason]`. Tried and
   rejected: all three tests threw NullReferenceException in `ViewController.IsFitToView` (activation needs a real
   View). Instead, the check is a small `internal static HasJobDispatcher(IServiceProvider)`, which
   `JobSchedulerViewControllerTests` covers: no registration → false; registered with a throwing factory → true (so
   nothing is constructed); the same from a scope (both apps hand XAF a scoped provider); null provider → false. The
   OnActivated/OnDeactivated wiring is checked live in both apps. The Module gets `InternalsVisibleTo` for the
   test project, as Blazor.Server already has.

## Gemini diff review (2026-10-26): nothing blocking

1. `HasDispatcherKey` was `internal`, a leftover of the dropped activation test → back to `private`.
2. The controller targets any nesting; a future nested JobDefinition list would make Run Now's `CommitChanges` only
   flush to the parent ObjectSpace. No such list exists today → carded as unverified hardening, not changed here.

Notes carded: WIN-001 (the Win app doesn't set `EnableLegacyTimestampBehavior`) and WIN-002 (JobDefinition editing
in WinForms is degraded: refresh controller, editors, schedule sync, scheduler module).
