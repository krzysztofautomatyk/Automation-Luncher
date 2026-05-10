namespace AutomationLauncher.Domain.Models;

/// <summary>
/// Describes whether a host-control command (start/stop/march) may execute.
/// </summary>
public enum HostControlCommandReadiness
{
    /// <summary>The command is allowed to proceed.</summary>
    Ready,

    /// <summary>
    /// Start/march: the sequence or archive is already running.
    /// </summary>
    AlreadyRunning,

    /// <summary>
    /// Stop: no managed runtime is active — nothing to stop.
    /// </summary>
    NothingToStop,

    /// <summary>
    /// Start: no startup-sequence entries are configured.
    /// </summary>
    NoEntriesConfigured
}
