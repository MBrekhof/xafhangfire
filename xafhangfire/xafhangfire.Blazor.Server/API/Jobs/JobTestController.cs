using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xafhangfire.Jobs;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Blazor.Server.API.Jobs;

[ApiController]
[Route("api/jobs")]
[AllowAnonymous]
public sealed class JobTestController(IJobDispatcher dispatcher) : ControllerBase
{
    [HttpPost("demo-log")]
    public async Task<IActionResult> DemoLog(
        [FromQuery] string message = "Hello from POC",
        [FromQuery] int delaySeconds = 3,
        CancellationToken cancellationToken = default)
    {
        await dispatcher.DispatchAsync(
            new DemoLogCommand(message, delaySeconds),
            cancellationToken);

        return Ok(new { dispatched = "DemoLogCommand", message, delaySeconds });
    }

    [HttpPost("list-users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        await dispatcher.DispatchAsync(
            new ListUsersCommand(maxResults),
            cancellationToken);

        return Ok(new { dispatched = "ListUsersCommand", maxResults });
    }
}
