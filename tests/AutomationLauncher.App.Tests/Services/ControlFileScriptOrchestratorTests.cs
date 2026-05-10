using System.Linq;
using AutomationLauncher.App;
using AutomationLauncher.App.Services;
using Xunit;

namespace AutomationLauncher.App.Tests.Services;

public sealed class ControlFileScriptOrchestratorTests
{
    [Fact]
    public async Task ReturnsNoStepsResult_WhenNoBindingsConfigured()
    {
        var orchestrator = BuildOrchestrator(new AutomationLauncherSettings());

        var result = await orchestrator.ExecuteAsync("start", isPreExecution: true, "Ready", AppContext.BaseDirectory);

        Assert.True(result.ShouldContinueControlFlow);
        Assert.Equal("No configured script steps.", result.Message);
    }

    [Fact]
    public async Task ReturnsNoStepsResult_WhenBindingExistsButHasNoSteps()
    {
        var settings = new AutomationLauncherSettings();
        settings.ControlFiles.Bindings.Add(new ControlFileScriptBinding { ControlFileType = "start" });
        var orchestrator = BuildOrchestrator(settings);

        var result = await orchestrator.ExecuteAsync("start", isPreExecution: true, "Ready", AppContext.BaseDirectory);

        Assert.True(result.ShouldContinueControlFlow);
    }

    [Fact]
    public async Task ReturnsAbort_WhenScriptNotFoundAndOnFailureIsAbort()
    {
        var settings = new AutomationLauncherSettings();
        var binding = settings.ControlFiles.Bindings.First(b => b.ControlFileType == "march");
        binding.PreExecutionSteps.Add(new ControlFileScriptSequenceStep
        {
            ScriptId = "missing-script-id",
            OnFailure = ControlFileScriptOutcomeAction.AbortControlFlow
        });
        var orchestrator = BuildOrchestrator(settings);

        var result = await orchestrator.ExecuteAsync("march", isPreExecution: true, "Ready", AppContext.BaseDirectory);

        Assert.False(result.ShouldContinueControlFlow);
        Assert.Contains("missing-script-id", result.Message);
    }

    [Fact]
    public async Task ContinuesControlFlow_WhenScriptNotFoundAndOnFailureIsContinue()
    {
        var settings = new AutomationLauncherSettings();
        var binding = settings.ControlFiles.Bindings.First(b => b.ControlFileType == "stop");
        binding.PostExecutionSteps.Add(new ControlFileScriptSequenceStep
        {
            ScriptId = "ghost-script",
            OnFailure = ControlFileScriptOutcomeAction.ContinueControlFlow
        });
        var orchestrator = BuildOrchestrator(settings);

        var result = await orchestrator.ExecuteAsync("stop", isPreExecution: false, "Ready", AppContext.BaseDirectory);

        Assert.True(result.ShouldContinueControlFlow);
    }

    [Fact]
    public async Task SkipsMissingScript_WhenOnFailureIsRunNextScript()
    {
        var settings = new AutomationLauncherSettings();
        var binding = settings.ControlFiles.Bindings.First(b => b.ControlFileType == "start");
        binding.PreExecutionSteps.Add(new ControlFileScriptSequenceStep
        {
            ScriptId = "ghost-script",
            OnFailure = ControlFileScriptOutcomeAction.RunNextScript
        });
        var orchestrator = BuildOrchestrator(settings);

        var result = await orchestrator.ExecuteAsync("start", isPreExecution: true, "Ready", AppContext.BaseDirectory);

        Assert.True(result.ShouldContinueControlFlow);
        Assert.Equal("All configured script steps finished.", result.Message);
    }

    [Fact]
    public async Task ReturnsNoStepsResult_WhenControlFileTypeDoesNotMatch()
    {
        var settings = new AutomationLauncherSettings();
        var binding = new ControlFileScriptBinding { ControlFileType = "march" };
        binding.PreExecutionSteps.Add(new ControlFileScriptSequenceStep { ScriptId = "some-script" });
        settings.ControlFiles.Bindings.Add(binding);
        var orchestrator = BuildOrchestrator(settings);

        var result = await orchestrator.ExecuteAsync("stop", isPreExecution: true, "Ready", AppContext.BaseDirectory);

        Assert.True(result.ShouldContinueControlFlow);
        Assert.Equal("No configured script steps.", result.Message);
    }

    private static ControlFileScriptOrchestrator BuildOrchestrator(AutomationLauncherSettings settings)
    {
        return new ControlFileScriptOrchestrator(settings, new PowerShellScriptRunner());
    }
}
