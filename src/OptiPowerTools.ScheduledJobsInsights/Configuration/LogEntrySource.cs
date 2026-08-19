namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Distinguishes log lines automatically captured from <c>OnStatusChanged</c> from
/// lines a job author explicitly wrote via <see cref="Logging.LoggedScheduledJobBase.Log"/>.
/// </summary>
public enum LogEntrySource : byte
{
    /// <summary>Automatically captured from an <c>OnStatusChanged</c> call.</summary>
    StatusChanged = 0,

    /// <summary>Explicitly written by job code via <see cref="Logging.LoggedScheduledJobBase.Log"/>.</summary>
    DevLog = 1
}
