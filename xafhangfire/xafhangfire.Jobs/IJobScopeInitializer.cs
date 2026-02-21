namespace xafhangfire.Jobs;

/// <summary>
/// Initializes a DI scope for job execution (e.g., authenticating a service user
/// so that handlers requiring secured object spaces can function in background threads).
/// </summary>
public interface IJobScopeInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
