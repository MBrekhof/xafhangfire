using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace xafhangfire.Jobs;

public static class JobServiceCollectionExtensions
{
    public static IServiceCollection AddJobDispatcher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Jobs:UseHangfire"))
        {
            services.AddSingleton<IJobDispatcher, HangfireJobDispatcher>();
        }
        else
        {
            services.AddSingleton<IJobDispatcher, DirectJobDispatcher>();
        }

        return services;
    }

    public static IServiceCollection AddJobHandler<TCommand, THandler>(
        this IServiceCollection services)
        where THandler : class, IJobHandler<TCommand>
    {
        services.AddTransient<IJobHandler<TCommand>, THandler>();
        services.AddTransient<JobExecutor<TCommand>>();
        return services;
    }
}
