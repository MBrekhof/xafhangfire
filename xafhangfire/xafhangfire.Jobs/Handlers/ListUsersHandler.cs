using DevExpress.ExpressApp;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Handlers;

public sealed class ListUsersHandler(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    ILogger<ListUsersHandler> logger) : IJobHandler<ListUsersCommand>
{
    public Task ExecuteAsync(
        ListUsersCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("ListUsersHandler: fetching up to {Max} users", command.MaxResults);

        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<PermissionPolicyUser>();
        var users = objectSpace.GetObjects<PermissionPolicyUser>()
            .Take(command.MaxResults)
            .ToList();

        foreach (var user in users)
        {
            logger.LogInformation("ListUsersHandler: found user '{UserName}'", user.UserName);
        }

        logger.LogInformation("ListUsersHandler: done — {Count} users found", users.Count);
        return Task.CompletedTask;
    }
}
