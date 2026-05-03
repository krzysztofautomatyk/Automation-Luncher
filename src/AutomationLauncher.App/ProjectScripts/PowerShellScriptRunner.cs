using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;

namespace AutomationLauncher.App;

public sealed class PowerShellScriptRunner
{
    private static readonly Regex RuntimeTokenRegex = new(@"\{\{(?<scope>Runtime|Parameter):(?<name>[^}]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<PowerShellScriptRunResult> RunAsync(string scriptBody, int timeoutSeconds, CancellationToken cancellationToken)
    {
        return await RunAsync(scriptBody, timeoutSeconds, new PowerShellScriptExecutionContext(), cancellationToken);
    }

    public async Task<PowerShellScriptRunResult> RunAsync(
        string scriptBody,
        int timeoutSeconds,
        PowerShellScriptExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var effectiveTimeoutSeconds = timeoutSeconds < 1 ? 300 : timeoutSeconds;
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"automation-launcher-{Guid.NewGuid():N}.ps1");
        var materializedScript = BuildExecutableScript(scriptBody ?? string.Empty, executionContext ?? new PowerShellScriptExecutionContext());
        File.WriteAllText(tempScriptPath, materializedScript, Encoding.UTF8);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory
                }
            };

            process.Start();

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();
            var waitForExitTask = Task.Run(() => process.WaitForExit(effectiveTimeoutSeconds * 1000), cancellationToken);

            var exited = await waitForExitTask;
            if (!exited)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }

                var timedOutOutput = await stdOutTask;
                var timedOutError = await stdErrTask;
                return new PowerShellScriptRunResult(false, null, CombineOutput(timedOutOutput, timedOutError), $"Timed out after {effectiveTimeoutSeconds} second(s).");
            }

            var standardOutput = await stdOutTask;
            var standardError = await stdErrTask;
            var exitCode = process.ExitCode;
            var combinedOutput = CombineOutput(standardOutput, standardError);
            var isSuccess = exitCode == 0;
            var statusMessage = isSuccess ? "Script finished successfully." : "PowerShell returned a non-zero exit code.";

            return new PowerShellScriptRunResult(isSuccess, exitCode, combinedOutput, statusMessage);
        }
        finally
        {
            try
            {
                if (File.Exists(tempScriptPath))
                {
                    File.Delete(tempScriptPath);
                }
            }
            catch
            {
            }
        }
    }

    public string PreviewScript(string scriptBody, PowerShellScriptExecutionContext executionContext)
    {
        return BuildExecutableScript(scriptBody ?? string.Empty, executionContext ?? new PowerShellScriptExecutionContext());
    }

    private static string BuildExecutableScript(string scriptBody, PowerShellScriptExecutionContext executionContext)
    {
        var runtimeVariables = executionContext.ToRuntimeVariables();
        var parameterMap = executionContext.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var replacedScriptBody = RuntimeTokenRegex.Replace(scriptBody, match =>
        {
            var scope = match.Groups["scope"].Value;
            var name = match.Groups["name"].Value.Trim();

            if (scope.Equals("Runtime", StringComparison.OrdinalIgnoreCase)
                && runtimeVariables.TryGetValue(name, out var runtimeValue))
            {
                return runtimeValue ?? string.Empty;
            }

            if (scope.Equals("Parameter", StringComparison.OrdinalIgnoreCase)
                && parameterMap.TryGetValue(name, out var parameterValue))
            {
                return parameterValue ?? string.Empty;
            }

            return string.Empty;
        });

        var bootstrapBuilder = new StringBuilder();
        bootstrapBuilder.AppendLine("$AutomationLauncherRuntime = @{}");
        foreach (var pair in runtimeVariables.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            bootstrapBuilder.AppendLine($"$AutomationLauncherRuntime['{EscapeSingleQuotedString(pair.Key)}'] = '{EscapeSingleQuotedString(pair.Value)}'");
            bootstrapBuilder.AppendLine($"$Runtime_{SanitizePowerShellIdentifier(pair.Key)} = '{EscapeSingleQuotedString(pair.Value)}'");
        }

        bootstrapBuilder.AppendLine("$AutomationLauncherParameters = @{}");
        foreach (var pair in parameterMap.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            bootstrapBuilder.AppendLine($"$AutomationLauncherParameters['{EscapeSingleQuotedString(pair.Key)}'] = '{EscapeSingleQuotedString(pair.Value)}'");
            bootstrapBuilder.AppendLine($"$Param_{SanitizePowerShellIdentifier(pair.Key)} = '{EscapeSingleQuotedString(pair.Value)}'");
        }

        bootstrapBuilder.AppendLine();
        bootstrapBuilder.AppendLine(replacedScriptBody);
        return bootstrapBuilder.ToString();
    }

    private static string EscapeSingleQuotedString(string? value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

    private static string SanitizePowerShellIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Value";
        }

        var builder = new StringBuilder();
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private static string CombineOutput(string standardOutput, string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return standardError ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(standardError))
        {
            return standardOutput;
        }

        return standardOutput + Environment.NewLine + Environment.NewLine + "STDERR:" + Environment.NewLine + standardError;
    }
}

public sealed class PowerShellScriptRunResult
{
    public PowerShellScriptRunResult(bool isSuccess, int? exitCode, string combinedOutput, string statusMessage)
    {
        IsSuccess = isSuccess;
        ExitCode = exitCode;
        CombinedOutput = combinedOutput;
        StatusMessage = statusMessage;
    }

    public bool IsSuccess { get; }

    public int? ExitCode { get; }

    public string CombinedOutput { get; }

    public string StatusMessage { get; }
}