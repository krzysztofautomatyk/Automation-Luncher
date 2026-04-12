using System.Diagnostics;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand(CanExecute = nameof(CanCheckOpennessGroupAccess))]
    private async Task CheckOpennessGroupAccess()
    {
        IsCheckingOpennessAccess = true;
        LastOpennessAccessCheck = "Check in progress...";
        OpennessGroupStatus = "Checking local machine group membership and related Siemens/Openness groups...";
        OpennessAccessActionMessage = "Checking local Windows account, administrator rights, and Siemens TIA Portal Openness group membership...";

        _opennessLog.Information("Openness access check started by user action");

        try
        {
            var snapshot = await System.Threading.Tasks.Task.Run(OpennessAccessChecker.GetSnapshot);
            ApplyOpennessAccessSnapshot(snapshot, addHistory: true);
            LastOpennessAccessCheck = $"Last check: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            OpennessAccessActionMessage = "Group access check completed.";
        }
        catch (System.Exception ex)
        {
            OpennessGroupStatus = $"Openness group check failed: {ex.Message}";
            OpennessAccessActionMessage = "Group access check failed.";
            LastOpennessAccessCheck = $"Last check failed: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            _opennessLog.Error(ex, "Openness access check failed with an unhandled exception");
            AddHistory("ERROR", "OpennessCheckFailed", ex.Message);
        }
        finally
        {
            IsCheckingOpennessAccess = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRepairOpennessGroupMembership))]
    private async Task RepairOpennessGroupMembership()
    {
        IsCheckingOpennessAccess = true;
        LastOpennessAccessCheck = "Repair in progress...";
        OpennessGroupStatus = "Starting elevated repair for the local Siemens Openness group...";
        OpennessAccessActionMessage = "Starting elevated repair. Confirm the Windows UAC prompt to continue.";

        if (IsCurrentUserInOpennessGroup == true)
        {
            OpennessGroupStatus = "Current user already belongs to Siemens TIA Portal Openness.";
            OpennessAccessActionMessage = "Repair skipped because user already has access.";
            _opennessLog.Information(
                "Group membership repair skipped – user {User} is already a member of {Group}",
                CurrentWindowsUser, ResolvedOpennessGroupName);
            IsCheckingOpennessAccess = false;
            return;
        }

        if (!IsOpennessGroupAvailable || string.IsNullOrWhiteSpace(ResolvedOpennessGroupName))
        {
            OpennessGroupStatus = "No Openness group was found on this machine. Confirm TIA Openness installation.";
            OpennessAccessActionMessage = "Repair unavailable because no Openness Windows group was found.";
            _opennessLog.Warning(
                "Group membership repair aborted – no Openness group exists on this machine. TIA Openness may not be installed");
            IsCheckingOpennessAccess = false;
            return;
        }

        var targetGroupName = ResolvedOpennessGroupName;
        var userIdentity = string.IsNullOrWhiteSpace(CurrentWindowsUser)
            ? System.Environment.UserName
            : CurrentWindowsUser;

        _opennessLog.Information(
            "Starting group membership repair – User={User}, TargetGroup={Group}. UAC elevation required",
            userIdentity, targetGroupName);

        var processInfo = new ProcessStartInfo
        {
            FileName = "net",
            Arguments = $"localgroup \"{targetGroupName}\" \"{userIdentity}\" /add",
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            var process = Process.Start(processInfo);
            if (process is null)
            {
                OpennessGroupStatus = "Unable to start elevated membership repair process.";
                OpennessAccessActionMessage = "Repair could not start.";
                _opennessLog.Error(
                    "Failed to start elevated net.exe process for group membership repair – User={User}, Group={Group}",
                    userIdentity, targetGroupName);
                AddHistory("ERROR", "OpennessRepairFailed", OpennessGroupStatus);
                return;
            }

            OpennessAccessActionMessage = "Waiting for elevated repair process to finish...";

            var exitCode = await System.Threading.Tasks.Task.Run(() =>
            {
                process.WaitForExit();
                return process.ExitCode;
            });

            if (exitCode == 0)
            {
                OpennessGroupStatus = $"User was added to '{targetGroupName}'. Sign out and sign in (or restart) to refresh Windows security token.";
                OpennessAccessActionMessage = "Repair finished successfully. Windows sign-out/sign-in is still required.";
                _opennessLog.Information(
                    "Group membership repair succeeded – User={User} added to group {Group}. Windows sign-out/sign-in required to refresh access token",
                    userIdentity, targetGroupName);
                AddHistory("OK", "OpennessRepairExecuted", OpennessGroupStatus);
            }
            else
            {
                OpennessGroupStatus = $"Membership repair finished with exit code {exitCode}.";
                OpennessAccessActionMessage = "Repair process finished, but Windows reported a non-zero exit code.";
                _opennessLog.Warning(
                    "Group membership repair completed with non-zero exit code {ExitCode} – User={User}, Group={Group}. User may already be a member or the command was rejected",
                    exitCode, userIdentity, targetGroupName);
                AddHistory("WARN", "OpennessRepairExitCode", OpennessGroupStatus);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            OpennessGroupStatus = "UAC elevation was cancelled. Membership was not changed.";
            OpennessAccessActionMessage = "Repair was cancelled in the UAC prompt.";
            _opennessLog.Warning(
                "Group membership repair cancelled – UAC prompt was dismissed by the user. No membership change was made for {User}",
                userIdentity);
            AddHistory("WARN", "OpennessRepairCancelled", OpennessGroupStatus);
        }
        catch (System.Exception ex)
        {
            OpennessGroupStatus = $"Failed to add user to Openness group: {ex.Message}";
            OpennessAccessActionMessage = "Repair failed.";
            _opennessLog.Error(ex,
                "Group membership repair failed with an unexpected exception – User={User}, Group={Group}",
                userIdentity, targetGroupName);
            AddHistory("ERROR", "OpennessRepairFailed", ex.Message);
        }
        finally
        {
            await RefreshOpennessAccessStatusAsync(addHistory: true, completionMessage: "Openness access state refreshed after repair.");
            IsCheckingOpennessAccess = false;
        }
    }
}
