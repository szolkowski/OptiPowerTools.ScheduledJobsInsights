namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Outcome of a single <see cref="EPiServer.Scheduler.ScheduledJobBase.Execute"/> run.
/// </summary>
/// <remarks>
/// Public because <see cref="Logging.IJobExecutionWriter.Complete"/> takes it — code recording an
/// execution directly has to be able to name the outcome. The query surface that reads these values
/// back is deliberately still internal.
/// </remarks>
public enum ExecutionStatus : byte
{
    /// <summary>The run has begun and has not reported an outcome yet.</summary>
    Running = 0,

    /// <summary>The run finished without throwing.</summary>
    Succeeded = 1,

    /// <summary>The run threw; the exception is recorded alongside it.</summary>
    Failed = 2,

    /// <summary>
    /// The run ended early because an administrator stopped it from the CMS. Distinct from
    /// <see cref="Succeeded"/>: the work was cut short, so its result is not a clean outcome.
    /// </summary>
    Stopped = 3
}
