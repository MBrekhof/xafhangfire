using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;

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

    private void RunNowAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var job = (BusinessObjects.JobDefinition)e.CurrentObject;
        Application.ShowViewStrategy.ShowMessage($"Job '{job.Name}' ({job.JobTypeName}) triggered.");
    }
}
