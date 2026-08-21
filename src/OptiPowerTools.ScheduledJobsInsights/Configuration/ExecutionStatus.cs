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
    Stopped = 3,

    /// <summary>
    /// The run never reported an outcome and has been given up on — the process was recycled, the
    /// container replaced, or the host crashed while the job was running.
    /// </summary>
    /// <remarks>
    /// Applied retrospectively by the cleanup job, not by the run itself: a process that dies
    /// mid-execution writes nothing further by definition, which is precisely why the row would
    /// otherwise sit at <see cref="Running"/> for ever. Distinct from <see cref="Failed"/> — nothing
    /// is known to have gone wrong with the work — and from <see cref="Stopped"/>, which was somebody
    /// deciding.
    /// </remarks>
    Interrupted = 4
}
