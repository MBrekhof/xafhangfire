using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Scheduler;
using DevExpress.ExpressApp.Scheduler.Blazor.Editors;
using DevExpress.ExpressApp.SystemModule;
using xafhangfire.Blazor.Server.Services;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Controllers;

// UI-001: the job calendar is read-only; opening a run opens its JobDefinition.
public sealed class JobCalendarController : ObjectViewController<ListView, JobCalendarEntry>
{
    private const string Key = "JobCalendar";
    private ListViewProcessCurrentObjectController processController;
    private SchedulerListEditor schedulerEditor;

    protected override void OnActivated()
    {
        base.OnActivated();
        View.AllowNew[Key] = false;
        View.AllowDelete[Key] = false;
        View.AllowEdit[Key] = false; // also turns off drag and resize (SchedulerListEditor.SetupEditModeDependentOptions)
        processController = Frame.GetController<ListViewProcessCurrentObjectController>();
        if (processController != null)
            processController.CustomProcessSelectedItem += ProcessController_CustomProcessSelectedItem;
    }

    protected override void OnViewControlsCreated()
    {
        base.OnViewControlsCreated();
        // The scheduler only syncs these from the New/Delete actions when those change after its controls exist.
        // AllowEditAppointment stays on: it is what raises the open (AppointmentFormShowing -> ProcessSelectedItem).
        ReleaseEditor();
        if (View.Editor is SchedulerListEditor { SchedulerModel: { } model } editor)
        {
            model.AllowCreateAppointment = false;
            model.AllowDeleteAppointment = false;
            schedulerEditor = editor;
            schedulerEditor.FilterDataSource += SchedulerEditor_FilterDataSource;
        }
    }

    // DX only ever widens ActiveViewFilter (SchedulerListViewControllerBase.FilterDataSourceUsingVisibleInterval), so
    // after Month view a Day view inside that month would keep the month's capped runs and lose today's. Narrow it to
    // what is visible; the DynamicCollection re-fetches for it. Equal bounds -> no change, so no reload loop.
    private void SchedulerEditor_FilterDataSource(object sender, FilterDataSourceEventArgs e)
    {
        var criteria = View.CollectionSource.Criteria["ActiveViewFilter"];
        if (JobCalendar.GetInterval(criteria, DateTime.Now) == (e.StartDate, e.EndDate))
            return;
        View.CollectionSource.Criteria["ActiveViewFilter"] = JobCalendar.IntervalCriteria(e.StartDate, e.EndDate);
    }

    private void ReleaseEditor()
    {
        if (schedulerEditor != null)
            schedulerEditor.FilterDataSource -= SchedulerEditor_FilterDataSource;
        schedulerEditor = null;
    }

    protected override void OnDeactivated()
    {
        ReleaseEditor();
        if (processController != null)
            processController.CustomProcessSelectedItem -= ProcessController_CustomProcessSelectedItem;
        processController = null;
        View.AllowNew.RemoveItem(Key);
        View.AllowDelete.RemoveItem(Key);
        View.AllowEdit.RemoveItem(Key);
        base.OnDeactivated();
    }

    private void ProcessController_CustomProcessSelectedItem(object sender, CustomProcessListViewSelectedItemEventArgs e)
    {
        if (e.InnerArgs.CurrentObject is not JobCalendarEntry entry)
            return;
        e.Handled = true;
        var objectSpace = Application.CreateObjectSpace(typeof(JobDefinition));
        var definition = objectSpace.GetObjectByKey<JobDefinition>(entry.JobDefinitionId);
        if (definition == null)
        {
            objectSpace.Dispose();
            return;
        }
        // The detail view owns the object space and disposes it on close.
        e.InnerArgs.ShowViewParameters.CreatedView = Application.CreateDetailView(objectSpace, definition);
    }
}
