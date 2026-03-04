using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using xafhangfire.Blazor.Server.API.Jobs;

namespace xafhangfire.Jobs.Tests;

public class JobTestControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization()
    {
        var attributes = typeof(JobTestController).GetCustomAttributes(inherit: true);

        attributes.OfType<AllowAnonymousAttribute>().Should().BeEmpty();
        attributes.OfType<AuthorizeAttribute>().Should().NotBeEmpty();
    }
}
