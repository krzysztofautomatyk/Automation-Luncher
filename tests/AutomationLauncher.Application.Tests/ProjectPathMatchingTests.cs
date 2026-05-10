using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using System.IO;
using Xunit;

namespace AutomationLauncher.Application.Tests;

/// <summary>
/// Tests for ProjectPathsMatch logic which handles .ap19 extension comparison.
/// Uses the use case public API with FakeGateway to exercise path matching.
/// </summary>
public sealed class ProjectPathMatchingTests
{
    [Theory]
    [InlineData(@"C:\Projects\Target.ap19", @"C:\Projects\Target.ap19")]
    [InlineData(@"C:\Projects\Target.ap19", @"C:\PROJECTS\TARGET.AP19")]       // case insensitive
    [InlineData(@"C:\Projects\Target.ap19", @"C:\Projects\Target")]             // path without extension
    [InlineData(@"C:\Projects\Target", @"C:\Projects\Target.ap19")]             // configured without extension
    public async Task ReturnsNotWrongProject_WhenPathsMatch(string expectedPath, string openProjectPath)
    {
        var context = new TiaProjectContext(true, openProjectPath, "Target", "101", false, true);
        var gateway = new FakeGateway(context) { ArchiveResult = true };
        var useCase = BuildUseCase(gateway);
        var options = DefaultOptions(expectedPath);

        var result = await useCase.ExecuteAsync(options, CancellationToken.None);

        Assert.NotEqual(ArchiveOutcome.WrongProjectOpen, result.Outcome);
    }

    [Theory]
    [InlineData(@"C:\Projects\Target.ap19", @"C:\Projects\Other.ap19")]
    [InlineData(@"C:\Projects\Target.ap19", @"C:\Projects\Other")]
    [InlineData(@"C:\Projects\Alpha\Target.ap19", @"C:\Projects\Beta\Target.ap19")]  // different directory
    public async Task ReturnsWrongProjectOpen_WhenPathsDontMatch(string expectedPath, string openProjectPath)
    {
        var context = new TiaProjectContext(true, openProjectPath, "Other", "101", false, true);
        var gateway = new FakeGateway(context);
        var useCase = BuildUseCase(gateway);
        var options = DefaultOptions(expectedPath);

        var result = await useCase.ExecuteAsync(options, CancellationToken.None);

        Assert.Equal(ArchiveOutcome.WrongProjectOpen, result.Outcome);
    }

    [Fact]
    public async Task ReturnsConfigurationError_WhenExpectedProjectPathIsEmpty()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var useCase = BuildUseCase(new FakeGateway(context));
        var options = DefaultOptions(@"C:\Projects\Target.ap19");
        options.ExpectedProjectPath = string.Empty;

        var result = await useCase.ExecuteAsync(options, CancellationToken.None);

        Assert.Equal(ArchiveOutcome.ConfigurationError, result.Outcome);
    }

    [Fact]
    public async Task ReturnsConfigurationError_WhenArchiveOutputDirectoryIsEmpty()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var useCase = BuildUseCase(new FakeGateway(context));
        var options = DefaultOptions(@"C:\Projects\Target.ap19");
        options.ArchiveOutputDirectory = "   ";

        var result = await useCase.ExecuteAsync(options, CancellationToken.None);

        Assert.Equal(ArchiveOutcome.ConfigurationError, result.Outcome);
    }

    private static ArchiveProjectUseCase BuildUseCase(FakeGateway gateway)
        => new(gateway, new FakePathService(), new FakeArchiveArtifactService(), new FakeOperationLogger());

    private static ArchiveOptions DefaultOptions(string expectedPath) => new()
    {
        ExpectedProjectPath = expectedPath,
        ArchiveOutputDirectory = @"C:\Archive",
        TryDetectUnsavedChanges = true,
        ForceSaveWhenDetectionUnavailable = false,
        SaveTimeoutSeconds = 10,
        ArchiveTimeoutSeconds = 10,
        RetryCount = 0,
        RetryDelayMilliseconds = 1
    };

    private sealed class FakeGateway : ITiaPortalGateway
    {
        private readonly TiaProjectContext _context;
        public bool ArchiveResult { get; set; } = false;

        public FakeGateway(TiaProjectContext context) => _context = context;

        public Task<TiaProjectContext> GetCurrentContextAsync(CancellationToken cancellationToken) => Task.FromResult(_context);
        public Task<OnlineStateResult> CheckOnlineStateAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(new OnlineStateResult(true, true, 1));
        public Task<PlcOnlineOfflineComparisonResult> CompareOnlineOfflineAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(new PlcOnlineOfflineComparisonResult(true, true));
        public Task<GoOfflineResult> GoOfflineAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(new GoOfflineResult(true, 1, 1));
        public Task<bool> SaveProjectAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ArchiveProjectAsync(string sessionId, string destinationArchivePath, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(ArchiveResult);
    }

    private sealed class FakePathService : IPathService
    {
        public string NormalizePath(string path) => path.Trim().Replace('/', '\\');
        public string BuildArchiveFilePath(string projectPath, string outputDirectory, DateTimeOffset timestamp)
            => BuildArchiveFilePath(projectPath, outputDirectory, $"{Environment.MachineName}_automaticBackup_{timestamp:yyyyMMdd_HHmmss}");
        public string BuildArchiveFilePath(string projectPath, string outputDirectory, string fileNameWithoutExtension)
            => Path.Combine(outputDirectory, fileNameWithoutExtension + ".zap19");
        public void EnsureDirectoryExists(string path) { }
    }

    private sealed class FakeArchiveArtifactService : IArchiveArtifactService
    {
        public long? TryGetPathSizeBytes(string path) => null;

        public void PrepareStableBackupTarget(string archivePath, string oldArchivePath) { }

        public void FinalizeSuccessfulBackup(ArchiveOptions options, string archivePath, string? oldArchivePath, string archiveIdentity) { }

        public void WriteMetricsLog(ArchiveMetricsLogEntry entry) { }
    }

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public void ArchiveStarted(string correlationId, string expectedProjectPath, string outputDirectory) { }
        public void TiaContextRead(string correlationId, TiaProjectContext context) { }
        public void TiaDiagnostic(string correlationId, string diagnosticCode, string message) { }
        public void OnlineStateCheckAttempted(string correlationId, string sessionId) { }
        public void OnlineStateCheckCompleted(string correlationId, OnlineStateResult result) { }
        public void PlcComparisonAttempted(string correlationId, string sessionId) { }
        public void PlcComparisonCompleted(string correlationId, PlcOnlineOfflineComparisonResult result) { }
        public void GoOfflineAttempted(string correlationId, string sessionId) { }
        public void GoOfflineCompleted(string correlationId, GoOfflineResult result) { }
        public void SaveAttempted(string correlationId, bool shouldSave, string reason) { }
        public void SaveCompleted(string correlationId, bool success) { }
        public void ArchiveAttempted(string correlationId, string archivePath, int attempt) { }
        public void ArchiveCompleted(string correlationId, bool success, string? archivePath, TimeSpan duration) { }
        public void Failed(string correlationId, string stage, string message, Exception? exception = null) { }
    }
}
