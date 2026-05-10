using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forms = System.Windows.Forms;
using FileDialog = Microsoft.Win32.OpenFileDialog;

namespace AutomationLauncher.App;

public partial class MainWindowViewModel : ObservableObject
{
    [RelayCommand]
    private void BrowseProjectFile()
    {
        if (!EnsureAuthenticated())
            return;

        var dialog = new FileDialog
        {
            Title = "Select TIA project file",
            Filter = "TIA projects (*.ap*;*.zap*)|*.ap*;*.zap*|All files (*.*)|*.*",
            FileName = ExpectedProjectPath
        };

        if (dialog.ShowDialog() == true)
            ExpectedProjectPath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseArchiveDirectory()
    {
        if (!EnsureAuthenticated())
            return;

        var selectedPath = SelectFolder("Select archive output directory", ArchiveOutputDirectory);
        if (!string.IsNullOrWhiteSpace(selectedPath))
            ArchiveOutputDirectory = selectedPath!;
    }

    [RelayCommand]
    private void BrowseLogDirectory()
    {
        if (!EnsureAuthenticated())
            return;

        var selectedPath = SelectFolder("Select log directory", LogDirectory);
        if (!string.IsNullOrWhiteSpace(selectedPath))
            LogDirectory = selectedPath!;
    }

    [RelayCommand]
    private void OpenStartupFolder()
    {
        OpenPath(StartupFolderPath);
    }

    [RelayCommand]
    private void OpenControlFilesFolder()
    {
        OpenPath(ControlFilesFolderPath);
    }

    [RelayCommand]
    private void BrowseControlFilesFolder()
    {
        if (!EnsureAuthenticated())
            return;

        var selectedPath = SelectFolder("Select control files directory", ControlFilesFolderPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
            ControlFilesFolderPath = selectedPath!;
    }

    [RelayCommand]
    private void CreateAllControlFilesInSelectedFolder()
    {
        if (!EnsureAuthenticated())
            return;

        try
        {
            var parentDirectory = string.IsNullOrWhiteSpace(ControlFilesFolderPath)
                ? AppContext.BaseDirectory
                : ControlFilesFolderPath.Trim();

            // Create "Control Files" subdirectory
            var targetDirectory = Path.Combine(parentDirectory, "Control Files");
            Directory.CreateDirectory(targetDirectory);

            var states = new List<string> { "run", "ready", "error" };
            states.AddRange(GetConfiguredControlFileTypes().Distinct(StringComparer.OrdinalIgnoreCase));
            states.Add("archok");

            var createdCount = 0;
            foreach (var state in states)
            {
                var filePath = Path.Combine(targetDirectory, $"{HostName}.{state}");
                File.WriteAllText(filePath, $"{HostName} {DateTimeOffset.Now:O}");
                createdCount++;
            }

            SettingsStatusMessage = $"Created {createdCount} control files in {targetDirectory}";
            AddHistory("OK", "ControlFilesCreated", SettingsStatusMessage);
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Control files creation failed: {ex.Message}";
            AddHistory("ERROR", "ControlFilesCreateFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        var path = LogPathHelper.ResolveDirectory(LogDirectory);
        Directory.CreateDirectory(path);
        OpenPath(path);
    }

    private static string? SelectFolder(string description, string? initialPath)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            SelectedPath = string.IsNullOrWhiteSpace(initialPath) ? string.Empty : initialPath,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
