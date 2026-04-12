using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Forms = System.Windows.Forms;
using FileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AutomationLauncher.App;
public partial class MainWindowViewModel : ObservableObject
{
    public void SetStartupAutomationRunning(bool isRunning)
    {
        IsStartupAutomationRunning = isRunning;
    }

    public void SetHostControlState(HostControlState state)
    {
        CurrentHostControlState = state;
    }

    public void SetErrorControlFilePresent(bool isPresent)
    {
        HasErrorControlFile = isPresent;
    }

    public async Task RunArchiveFromControlFileAsync()
    {
        if (IsBusy)
        {
            AddHistory("INFO", "ArchiveCommandIgnored", "Archive command ignored because the launcher is already busy.");
            return;
        }

        await RunArchiveWorkflowAsync();
    }

    public async Task<bool> RunArchiveFromControlFileWithResultAsync()
    {
        if (IsBusy)
        {
            AddHistory("INFO", "ArchiveCommandIgnored", "Archive command ignored because the launcher is already busy.");
            return false;
        }

        return await RunArchiveWorkflowAsync();
    }
}

