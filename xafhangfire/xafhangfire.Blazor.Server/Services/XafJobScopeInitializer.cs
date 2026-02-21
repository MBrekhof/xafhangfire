#nullable enable
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Services;

public sealed class XafJobScopeInitializer(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    UserManager userManager,
    SignInManager signInManager,
    ILogger<XafJobScopeInitializer> logger) : IJobScopeInitializer
{
    private const string ServiceUserName = "HangfireJob";

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<ApplicationUser>();
        var user = userManager.FindUserByName<ApplicationUser>(objectSpace, ServiceUserName);

        if (user != null)
        {
            signInManager.SignIn(user);
            logger.LogDebug("Job scope authenticated as '{UserName}'", ServiceUserName);
        }
        else
        {
            logger.LogWarning(
                "'{UserName}' user not found — jobs requiring secured access will fail",
                ServiceUserName);
        }

        return Task.CompletedTask;
    }
}
