using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.Domain.Contracts;

/// <summary>
/// Evaluates whether a host-control command (start / stop / march) is allowed
/// to execute given the current runtime state. Business rules only — no side effects.
/// </summary>
public interface IHostControlGuard
{
    /// <summary>
    /// Checks whether a START command can run a startup automation sequence.
    /// </summary>
    /// <param name="currentState">Current host control state.</param>
    /// <param name="isSequenceRunning">True if a startup sequence is currently executing.</param>
    /// <param name="configuredEntryCount">Number of non-empty startup entries configured.</param>
    HostControlCommandReadiness CheckStart(
        HostControlState currentState,
        bool isSequenceRunning,
        int configuredEntryCount);

    /// <summary>
    /// Checks whether a STOP command can terminate managed applications.
    /// </summary>
    /// <param name="currentState">Current host control state.</param>
    /// <param name="isSequenceRunning">True if a startup sequence is currently executing.</param>
    HostControlCommandReadiness CheckStop(
        HostControlState currentState,
        bool isSequenceRunning);

    /// <summary>
    /// Checks whether a MARCH command can trigger the archive workflow.
    /// </summary>
    /// <param name="isArchiveBusy">True if an archive or other operation is already running.</param>
    HostControlCommandReadiness CheckMarch(bool isArchiveBusy);
}
