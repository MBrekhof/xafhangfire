namespace xafhangfire.Jobs;

/// <summary>Brings the dispatcher's recurring schedules in line with the stored job definitions.</summary>
public interface IJobScheduleSynchronizer
{
    void Sync();
}
