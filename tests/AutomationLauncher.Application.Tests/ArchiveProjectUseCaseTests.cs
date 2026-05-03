using AutomationLauncher.Application.UseCases;
using AutomationLauncher.Domain.Contracts;
using AutomationLauncher.Domain.Models;
using System.IO;
using Xunit;

namespace AutomationLauncher.Application.Tests;

public sealed class ArchiveProjectUseCaseTests
{
    [Fact]
    public async Task ReturnsTiaNotRunning_WhenPortalMissing()
    {
        var useCase = BuildUseCase(new FakeGateway(new TiaProjectContext(false, null, null, null, null, false, "TiaNotRunning", "TIA Portal process was not found.")));

        var result = await useCase.ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.TiaNotRunning, result.Outcome);
    }

    [Fact]
    public async Task ReturnsConnectionFailure_WhenDiagnosticsReportOpennessIssue()
    {
        var context = new TiaProjectContext(true, null, null, "101", null, false, "GetProcessesFailed", "Failed to load Siemens Openness dependency.");
        var useCase = BuildUseCase(new FakeGateway(context));

        var result = await useCase.ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.TiaConnectionFailed, result.Outcome);
        Assert.Equal("Failed to load Siemens Openness dependency.", result.Message);
    }

    [Fact]
    public async Task ReturnsWrongProject_WhenOpenProjectDoesNotMatchConfiguredPath()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Other.ap19", "Other", "101", false, true);
        var useCase = BuildUseCase(new FakeGateway(context));

        var result = await useCase.ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.WrongProjectOpen, result.Outcome);
    }

    [Fact]
    public async Task ProceedsWithArchive_WhenPlcComparisonUnavailable()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var gateway = new FakeGateway(context)
        {
            ComparisonResult = new PlcOnlineOfflineComparisonResult(false, false, "PlcCompareUnavailable", "Compare service unavailable."),
            ArchiveResult = true
        };

        var result = await BuildUseCase(gateway).ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Success, result.Outcome);
        Assert.True(gateway.ArchiveCalled);
    }

    [Fact]
    public async Task ProceedsWithArchive_WhenNoOnlineDevicesDetected()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var gateway = new FakeGateway(context)
        {
            OnlineStateResultValue = new OnlineStateResult(true, false, 0),
            ArchiveResult = true
        };

        var result = await BuildUseCase(gateway).ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Success, result.Outcome);
        Assert.True(gateway.OnlineStateChecked);
        Assert.True(gateway.ArchiveCalled);
    }

    [Fact]
    public async Task ProceedsWithCompare_WhenOnlineStateCheckSucceeds()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var gateway = new FakeGateway(context)
        {
            OnlineStateResultValue = new OnlineStateResult(true, true, 2),
            ComparisonResult = new PlcOnlineOfflineComparisonResult(true, true),
            GoOfflineResultValue = new GoOfflineResult(true, 2, 2),
            ArchiveResult = true
        };

        var result = await BuildUseCase(gateway).ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Success, result.Outcome);
        Assert.True(gateway.OnlineStateChecked);
        Assert.True(gateway.GoOfflineCalled);
        Assert.True(gateway.ArchiveCalled);
    }

    [Fact]
    public async Task ReturnsPlcComparisonMismatch_WhenOnlineOfflineDiffer()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var gateway = new FakeGateway(context)
        {
            ComparisonResult = new PlcOnlineOfflineComparisonResult(true, false)
        };

        var result = await BuildUseCase(gateway).ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.PlcComparisonMismatch, result.Outcome);
        Assert.False(gateway.SaveCalled);
        Assert.False(gateway.ArchiveCalled);
        Assert.False(gateway.GoOfflineCalled);
    }

    [Fact]
    public async Task ReturnsGoOfflineFailed_WhenNoDevicesCouldBeSwitched()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var gateway = new FakeGateway(context)
        {
            GoOfflineResultValue = new GoOfflineResult(false, 2, 0, "GoOfflineFailed", "All devices failed to go offline.")
        };

        var result = await BuildUseCase(gateway).ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.GoOfflineFailed, result.Outcome);
        Assert.True(gateway.GoOfflineCalled);
        Assert.False(gateway.SaveCalled);
        Assert.False(gateway.ArchiveCalled);
    }

    [Fact]
    public async Task ProceedsWithArchive_WhenGoOfflineSucceeds()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
        var gateway = new FakeGateway(context)
        {
            GoOfflineResultValue = new GoOfflineResult(true, 2, 1),
            ArchiveResult = true
        };

        var result = await BuildUseCase(gateway).ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Success, result.Outcome);
        Assert.True(gateway.GoOfflineCalled);
        Assert.True(gateway.ArchiveCalled);
    }

    [Fact]
    public async Task ReturnsSuccess_WhenSaveAndArchiveSucceed()
    {
        var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", true, true);
        var gateway = new FakeGateway(context) { SaveResult = true, ArchiveResult = true };
        var useCase = BuildUseCase(gateway);

        var result = await useCase.ExecuteAsync(DefaultOptions(), CancellationToken.None);

        Assert.Equal(ArchiveOutcome.Success, result.Outcome);
        Assert.NotNull(result.ArchivePath);
        Assert.True(gateway.SaveCalled);
        Assert.True(gateway.ArchiveCalled);
    }

    [Fact]
    public async Task TimestampedRetention_KeepsOnlyConfiguredSuccessfulBackups()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, $"{Environment.MachineName}_automaticBackup_20260101_010101.zap19"), "old-1");
            File.WriteAllText(Path.Combine(tempDirectory, $"{Environment.MachineName}_automaticBackup_20260102_010101.zap19"), "old-2");

            var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
            var gateway = new FakeGateway(context)
            {
                ArchiveHandler = destinationArchivePath =>
                {
                    File.WriteAllText(destinationArchivePath, "new");
                    return true;
                }
            };

            var options = DefaultOptions();
            options.ArchiveOutputDirectory = tempDirectory;
            options.BackupFlow = ArchiveBackupFlow.TimestampedRetention;
            options.SuccessfulBackupRetentionCount = 2;

            var result = await BuildUseCase(gateway).ExecuteAsync(options, CancellationToken.None);

            Assert.Equal(ArchiveOutcome.Success, result.Outcome);
            var remainingFiles = Directory.GetFiles(tempDirectory, Environment.MachineName + "_automaticBackup_*.zap19");
            Assert.Equal(2, remainingFiles.Length);
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task StableFileWithOld_DeletesOldAfterSuccessfulArchive()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var currentPath = Path.Combine(tempDirectory, Environment.MachineName + "_automaticBackup.zap19");
            var oldPath = Path.Combine(tempDirectory, Environment.MachineName + "_automaticBackup_old.zap19");
            File.WriteAllText(currentPath, "previous");

            var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
            var gateway = new FakeGateway(context)
            {
                ArchiveHandler = destinationArchivePath =>
                {
                    File.WriteAllText(destinationArchivePath, "new");
                    return true;
                }
            };

            var options = DefaultOptions();
            options.ArchiveOutputDirectory = tempDirectory;
            options.BackupFlow = ArchiveBackupFlow.StableFileWithOld;

            var result = await BuildUseCase(gateway).ExecuteAsync(options, CancellationToken.None);

            Assert.Equal(ArchiveOutcome.Success, result.Outcome);
            Assert.True(File.Exists(currentPath));
            Assert.False(File.Exists(oldPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Fact]
    public async Task StableFileWithOld_KeepsOldWhenArchiveFails()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var currentPath = Path.Combine(tempDirectory, Environment.MachineName + "_automaticBackup.zap19");
            var oldPath = Path.Combine(tempDirectory, Environment.MachineName + "_automaticBackup_old.zap19");
            File.WriteAllText(currentPath, "previous");

            var context = new TiaProjectContext(true, @"C:\Projects\Target.ap19", "Target", "101", false, true);
            var gateway = new FakeGateway(context) { ArchiveResult = false };

            var options = DefaultOptions();
            options.ArchiveOutputDirectory = tempDirectory;
            options.BackupFlow = ArchiveBackupFlow.StableFileWithOld;

            var result = await BuildUseCase(gateway).ExecuteAsync(options, CancellationToken.None);

            Assert.Equal(ArchiveOutcome.ArchiveFailed, result.Outcome);
            Assert.True(File.Exists(oldPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    private static ArchiveProjectUseCase BuildUseCase(FakeGateway gateway)
    {
        return new ArchiveProjectUseCase(gateway, new FakePathService(), new FakeOperationLogger());
    }

    private static ArchiveOptions DefaultOptions()
    {
        return new ArchiveOptions
        {
            ExpectedProjectPath = @"C:\Projects\Target.ap19",
            ArchiveOutputDirectory = @"C:\Archive",
            TryDetectUnsavedChanges = true,
            ForceSaveWhenDetectionUnavailable = true,
            SaveTimeoutSeconds = 10,
            ArchiveTimeoutSeconds = 10,
            RetryCount = 0,
            RetryDelayMilliseconds = 1
        };
    }

    private sealed class FakeGateway : ITiaPortalGateway
    {
        private readonly TiaProjectContext _context;

        public FakeGateway(TiaProjectContext context)
        {
            _context = context;
        }

        public bool SaveResult { get; set; } = true;
        public bool ArchiveResult { get; set; } = true;
        public PlcOnlineOfflineComparisonResult ComparisonResult { get; set; } = new(true, true);
        public GoOfflineResult GoOfflineResultValue { get; set; } = new(true, 1, 0);
        public OnlineStateResult OnlineStateResultValue { get; set; } = new(true, true, 1);
        public Func<string, bool>? ArchiveHandler { get; set; }
        public bool SaveCalled { get; private set; }
        public bool ArchiveCalled { get; private set; }
        public bool GoOfflineCalled { get; private set; }
        public bool OnlineStateChecked { get; private set; }

        public Task<TiaProjectContext> GetCurrentContextAsync(CancellationToken cancellationToken) => Task.FromResult(_context);

        public Task<OnlineStateResult> CheckOnlineStateAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            OnlineStateChecked = true;
            return Task.FromResult(OnlineStateResultValue);
        }

        public Task<PlcOnlineOfflineComparisonResult> CompareOnlineOfflineAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return Task.FromResult(ComparisonResult);
        }

        public Task<GoOfflineResult> GoOfflineAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            GoOfflineCalled = true;
            return Task.FromResult(GoOfflineResultValue);
        }

        public Task<bool> SaveProjectAsync(string sessionId, TimeSpan timeout, CancellationToken cancellationToken)
        {
            SaveCalled = true;
            return Task.FromResult(SaveResult);
        }

        public Task<bool> ArchiveProjectAsync(string sessionId, string destinationArchivePath, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ArchiveCalled = true;
            return Task.FromResult(ArchiveHandler?.Invoke(destinationArchivePath) ?? ArchiveResult);
        }
    }

    private sealed class FakePathService : IPathService
    {
        public string NormalizePath(string path) => path.Trim().Replace('/', '\\');

        public string BuildArchiveFilePath(string projectPath, string outputDirectory, DateTimeOffset timestamp)
            => BuildArchiveFilePath(projectPath, outputDirectory, $"{Environment.MachineName}_automaticBackup_{timestamp:yyyyMMdd_HHmmss}");

        public string BuildArchiveFilePath(string projectPath, string outputDirectory, string fileNameWithoutExtension)
            => Path.Combine(outputDirectory, fileNameWithoutExtension + ".zap19");

        public void EnsureDirectoryExists(string path)
        {
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AutomationLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeOperationLogger : IOperationLogger
    {
        public void ArchiveStarted(string correlationId, string expectedProjectPath, string outputDirectory)
        {
        }

        public void TiaContextRead(string correlationId, TiaProjectContext context)
        {
        }

        public void TiaDiagnostic(string correlationId, string diagnosticCode, string message)
        {
        }

        public void OnlineStateCheckAttempted(string correlationId, string sessionId)
        {
        }

        public void OnlineStateCheckCompleted(string correlationId, OnlineStateResult result)
        {
        }

        public void PlcComparisonAttempted(string correlationId, string sessionId)
        {
        }

        public void PlcComparisonCompleted(string correlationId, PlcOnlineOfflineComparisonResult result)
        {
        }

        public void GoOfflineAttempted(string correlationId, string sessionId)
        {
        }

        public void GoOfflineCompleted(string correlationId, GoOfflineResult result)
        {
        }

        public void SaveAttempted(string correlationId, bool shouldSave, string reason)
        {
        }

        public void SaveCompleted(string correlationId, bool success)
        {
        }

        public void ArchiveAttempted(string correlationId, string archivePath, int attempt)
        {
        }

        public void ArchiveCompleted(string correlationId, bool success, string? archivePath, TimeSpan duration)
        {
        }

        public void Failed(string correlationId, string stage, string message, Exception? exception = null)
        {
        }
    }
}
