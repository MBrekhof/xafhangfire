#nullable enable
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;

namespace xafhangfire.Blazor.Server.Services;

public sealed class HangfireDashboardAuthFilter(bool isDevelopment) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
            return false;

        // In development, any authenticated user can access the dashboard
        if (isDevelopment)
            return true;

        // In production, require Administrators role
        return httpContext.User.IsInRole("Administrators");
    }
}
