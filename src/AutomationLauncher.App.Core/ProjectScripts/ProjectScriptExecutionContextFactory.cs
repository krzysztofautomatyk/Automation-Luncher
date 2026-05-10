using System.Linq;

namespace AutomationLauncher.App;

public static class ProjectScriptExecutionContextFactory
{
    public static PowerShellScriptExecutionContext CreateManual(
        ProjectScriptEntry script,
        string hostState,
        string controlFilesDirectory)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        return new PowerShellScriptExecutionContext
        {
            ScriptName = GetScriptLabel(script),
            ControlFileType = "manual",
            ExecutionPhase = "manual",
            MachineName = Environment.MachineName,
            HostState = hostState ?? string.Empty,
            AppBaseDirectory = AppContext.BaseDirectory,
            ControlFilesDirectory = controlFilesDirectory ?? AppContext.BaseDirectory,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Parameters = script.Parameters.ToDictionary(
                parameter => parameter.Name,
                parameter => parameter.DefaultValue ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    public static PowerShellScriptExecutionContext CreateForControlFile(
        ProjectScriptEntry script,
        ControlFileScriptSequenceStep step,
        string controlFileType,
        string phase,
        string hostState,
        string controlFilesDirectory)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        var parameterMap = script.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.DefaultValue ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

        foreach (var overrideEntry in step.ParameterOverrides.Where(overrideEntry => !string.IsNullOrWhiteSpace(overrideEntry.Name)))
        {
            parameterMap[overrideEntry.Name] = overrideEntry.Value ?? string.Empty;
        }

        return new PowerShellScriptExecutionContext
        {
            ScriptName = GetScriptLabel(script),
            ControlFileType = controlFileType ?? string.Empty,
            ExecutionPhase = phase ?? string.Empty,
            MachineName = Environment.MachineName,
            HostState = hostState ?? string.Empty,
            AppBaseDirectory = AppContext.BaseDirectory,
            ControlFilesDirectory = controlFilesDirectory ?? AppContext.BaseDirectory,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Parameters = parameterMap
        };
    }

    public static string GetScriptLabel(ProjectScriptEntry script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        return string.IsNullOrWhiteSpace(script.Name)
            ? script.Id
            : script.Name;
    }
}
