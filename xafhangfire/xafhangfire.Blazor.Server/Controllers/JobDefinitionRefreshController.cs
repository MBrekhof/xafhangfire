using DevExpress.ExpressApp;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Controllers;

public sealed class JobDefinitionRefreshController : ObjectViewController<DetailView, JobDefinition>
{
    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.ObjectChanged += ObjectSpace_ObjectChanged;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.ObjectChanged -= ObjectSpace_ObjectChanged;
        base.OnDeactivated();
    }

    private void ObjectSpace_ObjectChanged(object sender, ObjectChangedEventArgs e)
    {
        if (e.Object is not JobDefinition || e.PropertyName == null)
            return;

        if (e.PropertyName == nameof(JobDefinition.CronExpression))
        {
            View.FindItem(nameof(JobDefinition.CronDescription))?.Refresh();
            View.FindItem(nameof(JobDefinition.NextScheduledRuns))?.Refresh();
        }
    }
}
