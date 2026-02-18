using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using Microsoft.Extensions.DependencyInjection;
using xafhangfire.Jobs;

namespace xafhangfire.Module.Controllers;

public sealed class JobSchedulerViewController : ViewController
{
    private readonly SimpleAction _runNowAction;

    public JobSchedulerViewController()
    {
        TargetObjectType = typeof(BusinessObjects.JobDefinition);

        _runNowAction = new SimpleAction(this, "RunJobNow", "Edit")
        {
            Caption = "Run Now",
            ConfirmationMessage = "Run this job immediately?",
            SelectionDependencyType = SelectionDependencyType.RequireSingleObject
        };
        _runNowAction.Execute += RunNowAction_Execute;
    }

    private async void RunNowAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var job = (BusinessObjects.JobDefinition)e.CurrentObject;

        try
        {
            var dispatchService = Application.ServiceProvider.GetRequiredService<JobDispatchService>();
            await dispatchService.DispatchByNameAsync(job.JobTypeName, job.ParametersJson);

            job.LastRunUtc = DateTime.UtcNow;
            job.LastRunStatus = BusinessObjects.JobRunStatus.Running;
            ObjectSpace.CommitChanges();

            Application.ShowViewStrategy.ShowMessage($"Job '{job.Name}' dispatched successfully.");
        }
        catch (Exception ex)
        {
            job.LastRunUtc = DateTime.UtcNow;
            job.LastRunStatus = BusinessObjects.JobRunStatus.Failed;
            job.LastRunMessage = ex.Message;
            ObjectSpace.CommitChanges();

            Application.ShowViewStrategy.ShowMessage($"Job '{job.Name}' failed: {ex.Message}");
        }
    }
}
