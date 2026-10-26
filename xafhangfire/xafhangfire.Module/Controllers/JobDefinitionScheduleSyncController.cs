using DevExpress.ExpressApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Module.Controllers;

/// <summary>
/// Re-syncs recurring schedules after any JobDefinition save or delete (create, cron/params edit,
/// enable/disable, delete — ListView and DetailView). Edits made outside the UI (OData Web API) are
/// picked up by the startup sync.
/// </summary>
public sealed class JobDefinitionScheduleSyncController : ObjectViewController<ObjectView, JobDefinition>
{
    protected override void OnActivated()
    {
        base.OnActivated();
        ObjectSpace.Committed += ObjectSpace_Committed;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.Committed -= ObjectSpace_Committed;
        base.OnDeactivated();
    }

    private void ObjectSpace_Committed(object sender, EventArgs e)
    {
        // Not registered in WinForms → no-op there.
        var synchronizer = Application.ServiceProvider.GetService<IJobScheduleSynchronizer>();
        if (synchronizer == null) return;

        try
        {
            synchronizer.Sync();
        }
        catch (Exception ex)
        {
            // The save itself succeeded; a sync failure must not surface as a failed save.
            Application.ServiceProvider.GetService<ILoggerFactory>()
                ?.CreateLogger<JobDefinitionScheduleSyncController>()
                .LogError(ex, "Schedule sync after JobDefinition commit failed");
        }
    }
}
