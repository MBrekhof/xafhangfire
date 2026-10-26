using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using Microsoft.Extensions.DependencyInjection;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Module.Controllers;

public sealed class JobSchedulerViewController : ViewController
{
    private readonly SimpleAction _runNowAction;

    public JobSchedulerViewController()
    {
        TargetObjectType = typeof(JobDefinition);

        _runNowAction = new SimpleAction(this, "RunJobNow", "Edit")
        {
            Caption = "Run Now",
            ConfirmationMessage = "Run this job immediately?",
            SelectionDependencyType = SelectionDependencyType.RequireSingleObject
        };
        _runNowAction.Execute += RunNowAction_Execute;
    }

    private void RunNowAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var job = (JobDefinition)e.CurrentObject;

        // Save pending edits first, synchronously, so XAF's validation UI handles a failed save
        // and the recorder (separate ObjectSpace) can find a newly created definition.
        if (ObjectSpace.IsModified)
            ObjectSpace.CommitChanges();

        RunNow(job.Id, job.Name, job.JobTypeName, job.ParametersJson, job.LastRunUtc);
    }

    // async void is the XAF Blazor action pattern; nothing may escape it.
    private async void RunNow(Guid jobId, string jobName, string jobTypeName, string parametersJson, DateTime? previousLastRunUtc)
    {
        try
        {
            var dispatchService = Application.ServiceProvider.GetRequiredService<JobDispatchService>();
            await dispatchService.DispatchByNameAsync(jobTypeName, parametersJson, jobId);

            if (IsViewGone()) return;
            // The recorder owns LastRun*; just show what it wrote.
            ObjectSpace.Refresh();
            Application.ShowViewStrategy.ShowMessage($"Job '{jobName}' dispatched successfully.");
        }
        catch (Exception ex)
        {
            try
            {
                if (IsViewGone()) return;
                ObjectSpace.Refresh();

                // Unchanged LastRunUtc = the recorder never saw this run (e.g. invalid parameters
                // rejected at dispatch), so record the failure here.
                var refreshed = ObjectSpace.FirstOrDefault<JobDefinition>(j => j.Id == jobId);
                if (refreshed != null && refreshed.LastRunUtc == previousLastRunUtc)
                {
                    refreshed.LastRunUtc = DateTime.UtcNow;
                    refreshed.LastRunStatus = JobRunStatus.Failed;
                    refreshed.LastRunMessage = ex.Message;
                    ObjectSpace.CommitChanges();
                }

                Application.ShowViewStrategy.ShowMessage($"Job '{jobName}' failed: {ex.Message}");
            }
            catch
            {
                // View closed or save conflict while reporting the failure; the job outcome is
                // in the execution history either way.
            }
        }
    }

    private bool IsViewGone() =>
        !Active.ResultValue || View == null || View.IsDisposed || ObjectSpace == null || ObjectSpace.IsDisposed;
}
