using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using AutomationLauncher.Domain.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutomationLauncher.App;

public sealed class AutomationLauncherSettings
{
    public ArchiveOptions Archive { get; set; } = new();

    public ProjectSettings Project { get; set; } = new();

    public ControlFilesSettings ControlFiles { get; set; } = new();

    public StartupSettings Startup { get; set; } = new();

    public LoggingSettings Logging { get; set; } = new();

    public UiSettings Ui { get; set; } = new();
}

public sealed class StartupSettings
{
    public bool RunOnWindowsStartup { get; set; }

    public bool RunSequenceOnWindowsStartup { get; set; } = true;

    public string SplashBackgroundImagePath { get; set; } = string.Empty;

    public IList<StartupSequenceEntry> SequenceEntries { get; set; } = new List<StartupSequenceEntry>();
}

public sealed class ProjectSettings
{
    public IList<ProjectScriptEntry> PowerShellScripts { get; set; } = new List<ProjectScriptEntry>();
}

public sealed class ControlFilesSettings
{
    public IList<ControlFileScriptBinding> Bindings { get; set; } = ControlFileScriptBinding.CreateDefaultBindings();
}

public sealed class LoggingSettings
{
    public string DirectoryPath { get; set; } = "logs";

    public string MinimumLevel { get; set; } = "Information";

    public int RetainedFileCountLimit { get; set; } = 30;
}

public sealed class UiSettings
{
    public bool StartHiddenToTray { get; set; } = true;

    public string ControlFilesDirectory { get; set; } = string.Empty;
}

public partial class StartupSequenceEntry : ObservableObject
{
    [ObservableProperty]
    private string alias = string.Empty;

    [ObservableProperty]
    private string executablePath = string.Empty;

    [ObservableProperty]
    private int delaySeconds;

    public StartupSequenceEntry Clone()
    {
        return new StartupSequenceEntry
        {
            Alias = Alias,
            ExecutablePath = ExecutablePath,
            DelaySeconds = DelaySeconds
        };
    }
}

public partial class ProjectScriptEntry : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string scriptBody = string.Empty;

    [ObservableProperty]
    private int timeoutSeconds = 300;

    public ObservableCollection<ProjectScriptParameterEntry> Parameters { get; set; } = new();

    [ObservableProperty]
    [property: JsonIgnore]
    private bool isRunning;

    [ObservableProperty]
    [property: JsonIgnore]
    private string lastRunStatus = "Not run yet.";

    [ObservableProperty]
    [property: JsonIgnore]
    private DateTimeOffset? lastRunFinishedAt;

    [ObservableProperty]
    [property: JsonIgnore]
    private int? lastExitCode;

    [ObservableProperty]
    [property: JsonIgnore]
    private string lastOutput = string.Empty;

    public ProjectScriptEntry Clone()
    {
        return new ProjectScriptEntry
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Name = Name,
            ScriptBody = ScriptBody,
            TimeoutSeconds = TimeoutSeconds,
            Parameters = new ObservableCollection<ProjectScriptParameterEntry>(Parameters.Select(parameter => parameter.Clone()))
        };
    }
}

public partial class ProjectScriptParameterEntry : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string defaultValue = string.Empty;

    public ProjectScriptParameterEntry Clone()
    {
        return new ProjectScriptParameterEntry
        {
            Name = Name,
            DefaultValue = DefaultValue
        };
    }
}

public enum ControlFileScriptOutcomeAction
{
    ContinueControlFlow,
    AbortControlFlow,
    RunNextScript
}

public enum HostControlCommandAction
{
    Unspecified = 0,
    Start,
    Stop,
    Archive
}

public partial class ControlFileScriptSequenceStep : ObservableObject
{
    [ObservableProperty]
    private string scriptId = string.Empty;

    [ObservableProperty]
    private ControlFileScriptOutcomeAction onSuccess = ControlFileScriptOutcomeAction.RunNextScript;

    [ObservableProperty]
    private ControlFileScriptOutcomeAction onFailure = ControlFileScriptOutcomeAction.AbortControlFlow;

    public ObservableCollection<ControlFileScriptParameterOverrideEntry> ParameterOverrides { get; set; } = new();

    public ControlFileScriptSequenceStep Clone()
    {
        return new ControlFileScriptSequenceStep
        {
            ScriptId = ScriptId,
            OnSuccess = OnSuccess,
            OnFailure = OnFailure,
            ParameterOverrides = new ObservableCollection<ControlFileScriptParameterOverrideEntry>(ParameterOverrides.Select(overrideEntry => overrideEntry.Clone()))
        };
    }
}

public partial class ControlFileScriptParameterOverrideEntry : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string value = string.Empty;

    public ControlFileScriptParameterOverrideEntry Clone()
    {
        return new ControlFileScriptParameterOverrideEntry
        {
            Name = Name,
            Value = Value
        };
    }
}

public partial class ControlFileScriptBinding : ObservableObject
{
    public static IReadOnlyList<string> ReservedMarkerTypes { get; } = new[]
    {
        "run",
        "ready",
        "error",
        "archok"
    };

    [ObservableProperty]
    private string controlFileType = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private HostControlCommandAction action = HostControlCommandAction.Unspecified;

    [ObservableProperty]
    private string splashTitle = string.Empty;

    [ObservableProperty]
    private string splashBackgroundImagePath = string.Empty;

    [ObservableProperty]
    private int splashCountdownSeconds = -1;

    public ObservableCollection<ControlFileScriptSequenceStep> PreExecutionSteps { get; set; } = new();

    public ObservableCollection<ControlFileScriptSequenceStep> PostExecutionSteps { get; set; } = new();

    [JsonIgnore]
    public string EffectiveDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName.Trim();
            }

            return BuildDefaultDisplayName(Action, ControlFileType);
        }
    }

    [JsonIgnore]
    public string ActionDisplayName => Action switch
    {
        HostControlCommandAction.Start => "Start",
        HostControlCommandAction.Stop => "Stop",
        HostControlCommandAction.Archive => "Archive",
        _ => "Unspecified"
    };

    [JsonIgnore]
    public string CommandDescriptor => $"{ActionDisplayName} | .{ControlFileType}";

    [JsonIgnore]
    public string EffectiveSplashTitle => string.IsNullOrWhiteSpace(SplashTitle)
        ? BuildDefaultSplashTitle(Action)
        : SplashTitle.Trim();

    [JsonIgnore]
    public string SplashDescriptor => SplashCountdownSeconds == 0
        ? "Splash: immediate"
        : $"Splash: {SplashCountdownSeconds}s";

    public ControlFileScriptBinding Clone()
    {
        return new ControlFileScriptBinding
        {
            ControlFileType = ControlFileType,
            DisplayName = DisplayName,
            Action = Action,
            SplashTitle = SplashTitle,
            SplashBackgroundImagePath = SplashBackgroundImagePath,
            SplashCountdownSeconds = SplashCountdownSeconds,
            PreExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(PreExecutionSteps.Select(step => step.Clone())),
            PostExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(PostExecutionSteps.Select(step => step.Clone()))
        };
    }

    public static List<ControlFileScriptBinding> CreateDefaultBindings()
    {
        return new List<ControlFileScriptBinding>
        {
            new() { ControlFileType = "start", Action = HostControlCommandAction.Start },
            new() { ControlFileType = "stop", Action = HostControlCommandAction.Stop },
            new() { ControlFileType = "march", Action = HostControlCommandAction.Archive }
        };
    }

    public static string BuildDefaultDisplayName(HostControlCommandAction action, string? controlFileType)
    {
        var normalizedType = string.IsNullOrWhiteSpace(controlFileType)
            ? "command"
            : controlFileType!.Trim();

        return action switch
        {
            HostControlCommandAction.Start => $"Start command (.{normalizedType})",
            HostControlCommandAction.Stop => $"Stop command (.{normalizedType})",
            HostControlCommandAction.Archive => $"Archive command (.{normalizedType})",
            _ => $"Control command (.{normalizedType})"
        };
    }

    public static string BuildDefaultSplashTitle(HostControlCommandAction action)
    {
        return action switch
        {
            HostControlCommandAction.Start => "Automation Launcher - Startup",
            HostControlCommandAction.Stop => "Automation Launcher - Stop",
            HostControlCommandAction.Archive => "Automation Launcher - Archive",
            _ => "Automation Launcher"
        };
    }

    public static int BuildDefaultSplashCountdownSeconds(HostControlCommandAction action)
    {
        return action switch
        {
            HostControlCommandAction.Start => 10,
            HostControlCommandAction.Stop => 60,
            HostControlCommandAction.Archive => 60,
            _ => 10
        };
    }

    public static bool TryNormalizeControlFileType(string? controlFileType, out string normalizedControlFileType)
    {
        normalizedControlFileType = string.Empty;

        if (string.IsNullOrWhiteSpace(controlFileType))
        {
            return false;
        }

        var candidate = controlFileType!.Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains('.'))
        {
            return false;
        }

        if (candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || candidate.Any(char.IsWhiteSpace)
            || candidate.Contains(Path.DirectorySeparatorChar)
            || candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        normalizedControlFileType = candidate;
        return true;
    }

    public static bool IsReservedMarkerType(string? controlFileType)
    {
        return ReservedMarkerTypes.Contains(controlFileType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    partial void OnControlFileTypeChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveDisplayName));
        OnPropertyChanged(nameof(CommandDescriptor));
    }

    partial void OnDisplayNameChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveDisplayName));
    }

    partial void OnActionChanged(HostControlCommandAction value)
    {
        if (SplashCountdownSeconds < 0)
        {
            SplashCountdownSeconds = BuildDefaultSplashCountdownSeconds(value);
        }

        OnPropertyChanged(nameof(EffectiveDisplayName));
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(CommandDescriptor));
        OnPropertyChanged(nameof(EffectiveSplashTitle));
        OnPropertyChanged(nameof(SplashDescriptor));
    }

    partial void OnSplashTitleChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveSplashTitle));
    }

    partial void OnSplashCountdownSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(SplashDescriptor));
    }
}

public sealed class ScriptLibraryPackage
{
    public string Format { get; set; } = "AutomationLauncher.ScriptLibrary";

    public string Version { get; set; } = "1.0";

    public List<ProjectScriptEntry> Scripts { get; set; } = new();

    public List<ControlFileScriptBinding> ControlFileBindings { get; set; } = ControlFileScriptBinding.CreateDefaultBindings();
}

public sealed class PowerShellScriptExecutionContext
{
    public string ScriptName { get; set; } = string.Empty;

    public string ControlFileType { get; set; } = "manual";

    public string ExecutionPhase { get; set; } = "manual";

    public string MachineName { get; set; } = Environment.MachineName;

    public string HostState { get; set; } = string.Empty;

    public string AppBaseDirectory { get; set; } = AppContext.BaseDirectory;

    public string ControlFilesDirectory { get; set; } = AppContext.BaseDirectory;

    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public IDictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, string> ToRuntimeVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScriptName"] = ScriptName ?? string.Empty,
            ["ControlFileType"] = ControlFileType ?? string.Empty,
            ["ExecutionPhase"] = ExecutionPhase ?? string.Empty,
            ["MachineName"] = MachineName ?? string.Empty,
            ["HostState"] = HostState ?? string.Empty,
            ["AppBaseDirectory"] = AppBaseDirectory ?? string.Empty,
            ["ControlFilesDirectory"] = ControlFilesDirectory ?? string.Empty,
            ["StartedAtUtc"] = StartedAtUtc.ToString("O"),
            ["StartedAtLocal"] = StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")
        };

        foreach (var pair in Parameters)
        {
            variables[$"Parameter:{pair.Key}"] = pair.Value ?? string.Empty;
        }

        return variables;
    }
}
