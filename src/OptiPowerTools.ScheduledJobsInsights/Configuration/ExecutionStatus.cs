namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Outcome of a single <see cref="EPiServer.Scheduler.ScheduledJobBase.Execute"/> run.
/// </summary>
internal enum ExecutionStatus : byte
{
    Running = 0,
    Succeeded = 1,
    Failed = 2
}
