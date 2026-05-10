using System.Collections.ObjectModel;
using AutomationLauncher.App.Services;
using AutomationLauncher.Domain.Models;
using Xunit;

namespace AutomationLauncher.App.Tests.Services;

public sealed class ProjectScriptWorkflowServiceTests
{
    private readonly ProjectScriptWorkflowService _service = new(new PowerShellScriptRunner());

    [Fact]
    public void BuildManualPreview_ExpandsRuntimeAndParameters()
    {
        var script = new ProjectScriptEntry
        {
            Name = "Hello Script",
            ScriptBody = "Write-Host $Runtime_HostState\nWrite-Host {{Parameter:Target}}",
            Parameters = new ObservableCollection<ProjectScriptParameterEntry>
            {
                new() { Name = "Target", DefaultValue = "World" }
            }
        };

        var preview = _service.BuildManualPreview(script, HostControlState.Running, @"C:\ControlFiles");

        Assert.Contains("$Runtime_HostState = 'Running'", preview);
        Assert.Contains("Write-Host World", preview);
    }

    [Fact]
    public void BuildControlFileStepPreview_ReturnsMissingScriptStatus_WhenStepDoesNotResolve()
    {
        var binding = new ControlFileScriptBinding { ControlFileType = "march" };
        var step = new ControlFileScriptSequenceStep { ScriptId = "missing-script" };

        var preview = _service.BuildControlFileStepPreview(
            Array.Empty<ProjectScriptEntry>(),
            binding,
            step,
            "pre",
            HostControlState.Ready,
            @"C:\ControlFiles");

        Assert.Equal("The selected step does not reference an existing script.", preview.Status);
        Assert.Equal(string.Empty, preview.Preview);
    }

    [Fact]
    public async Task RunManualScriptAsync_ReturnsSuccessAndOutput()
    {
        var script = new ProjectScriptEntry
        {
            Name = "Echo",
            ScriptBody = "Write-Host 'Hello workflow'\nexit 0",
            TimeoutSeconds = 30
        };

        var result = await _service.RunManualScriptAsync(script, HostControlState.Ready, AppContext.BaseDirectory, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsRunnerError);
        Assert.Contains("Hello workflow", result.CombinedOutput);
    }

    [Fact]
    public async Task RunManualScriptAsync_ReturnsFailureForNonZeroExit()
    {
        var script = new ProjectScriptEntry
        {
            Id = "script-1",
            ScriptBody = "Write-Error 'boom'\nexit 5",
            TimeoutSeconds = 30
        };

        var result = await _service.RunManualScriptAsync(script, HostControlState.Ready, AppContext.BaseDirectory, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(result.IsRunnerError);
        Assert.Equal(5, result.ExitCode);
    }
}
