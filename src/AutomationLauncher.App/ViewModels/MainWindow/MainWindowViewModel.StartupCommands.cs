using System.IO;
using System.Linq;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileDialog = Microsoft.Win32.OpenFileDialog;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand]
    private void AddStartupSequenceEntry()
    {
        if (!EnsureAuthenticated())
            return;

        var dialog = new FileDialog
        {
            Title = "Select application for Windows startup sequence",
            Filter = "Applications (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        var entry = new StartupSequenceEntry
        {
            Alias = Path.GetFileNameWithoutExtension(dialog.FileName) ?? string.Empty,
            ExecutablePath = dialog.FileName,
            DelaySeconds = 0
        };

        StartupSequenceEntries.Add(entry);
        SelectedStartupSequenceEntry = entry;
        PersistSettings("Startup sequence updated.");
    }

    [RelayCommand]
    private void RemoveSelectedStartupSequenceEntry()
    {
        if (!EnsureAuthenticated() || SelectedStartupSequenceEntry is null)
            return;

        var entryToRemove = SelectedStartupSequenceEntry;
        StartupSequenceEntries.Remove(entryToRemove);
        SelectedStartupSequenceEntry = StartupSequenceEntries.FirstOrDefault();
        PersistSettings("Startup sequence updated.");
    }

    [RelayCommand]
    private void MoveStartupSequenceEntryUp()
    {
        if (!EnsureAuthenticated() || SelectedStartupSequenceEntry is null)
            return;

        var currentIndex = StartupSequenceEntries.IndexOf(SelectedStartupSequenceEntry);
        if (currentIndex <= 0)
            return;

        StartupSequenceEntries.Move(currentIndex, currentIndex - 1);
        PersistSettings("Startup sequence order updated.");
    }

    [RelayCommand]
    private void MoveStartupSequenceEntryDown()
    {
        if (!EnsureAuthenticated() || SelectedStartupSequenceEntry is null)
            return;

        var currentIndex = StartupSequenceEntries.IndexOf(SelectedStartupSequenceEntry);
        if (currentIndex < 0 || currentIndex >= StartupSequenceEntries.Count - 1)
            return;

        StartupSequenceEntries.Move(currentIndex, currentIndex + 1);
        PersistSettings("Startup sequence order updated.");
    }

    [RelayCommand]
    private void BrowseStartupSplashBackground()
    {
        if (!EnsureAuthenticated())
            return;

        var dialog = new FileDialog
        {
            Title = "Select splash screen background image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
            StartupSplashBackgroundImagePath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearStartupSplashBackground()
    {
        if (!EnsureAuthenticated())
            return;

        StartupSplashBackgroundImagePath = string.Empty;
        PersistSettings("Startup splash background cleared.");
    }
}
