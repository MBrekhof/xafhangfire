using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using xafhangfire.Module.Controllers;

namespace xafhangfire.Jobs.Tests;

// UI-006: Run Now is hidden where no JobDispatchService is registered (the WinForms app).
// The OnActivated/OnDeactivated wiring needs a real XAF View; it's checked live in both apps.
public class JobSchedulerViewControllerTests
{
    [Fact]
    public void NoDispatcherRegistered_IsFalse()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();
        JobSchedulerViewController.HasJobDispatcher(sp).Should().BeFalse();
    }

    [Fact]
    public void DispatcherRegistered_IsTrue_WithoutConstructingIt()
    {
        var services = new ServiceCollection();
        // A factory that throws proves the check never builds the service or its dependencies.
        services.AddTransient<JobDispatchService>(_ => throw new InvalidOperationException("constructed"));
        using var sp = services.BuildServiceProvider();

        JobSchedulerViewController.HasJobDispatcher(sp).Should().BeTrue();
    }

    [Fact]
    public void DispatcherRegistered_IsTrue_FromAScope()
    {
        // Both apps hand XAF a scoped provider (Blazor: the circuit scope; WinForms: an app-lifetime scope).
        var services = new ServiceCollection();
        services.AddTransient<JobDispatchService>(_ => throw new InvalidOperationException("constructed"));
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        JobSchedulerViewController.HasJobDispatcher(scope.ServiceProvider).Should().BeTrue();
    }

    [Fact]
    public void NoServiceProvider_IsFalse() =>
        JobSchedulerViewController.HasJobDispatcher(null!).Should().BeFalse();
}
