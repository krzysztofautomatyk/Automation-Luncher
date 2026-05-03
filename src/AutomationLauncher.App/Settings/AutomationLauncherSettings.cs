using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public static IReadOnlyList<string> KnownControlFileTypes { get; } = new[]
    {
        "start",
        "stop",
        "march"
    };

    [ObservableProperty]
    private string controlFileType = string.Empty;

    public ObservableCollection<ControlFileScriptSequenceStep> PreExecutionSteps { get; set; } = new();

    public ObservableCollection<ControlFileScriptSequenceStep> PostExecutionSteps { get; set; } = new();

    [JsonIgnore]
    public string DisplayName => ControlFileType switch
    {
        "run" => "RUN marker (.run)",
        "ready" => "READY marker (.ready)",
        "error" => "ERROR marker (.error)",
        "start" => "START command (.start)",
        "stop" => "STOP command (.stop)",
        "march" => "MARCH command (.march)",
        "archok" => "ARCHOK marker (.archok)",
        _ => ControlFileType
    };

    public ControlFileScriptBinding Clone()
    {
        return new ControlFileScriptBinding
        {
            ControlFileType = ControlFileType,
            PreExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(PreExecutionSteps.Select(step => step.Clone())),
            PostExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(PostExecutionSteps.Select(step => step.Clone()))
        };
    }

    public static List<ControlFileScriptBinding> CreateDefaultBindings()
    {
        return KnownControlFileTypes
            .Select(type => new ControlFileScriptBinding { ControlFileType = type })
            .ToList();
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
