namespace AutomationLauncher.Domain.Contracts;

public interface IPathService
{
    string NormalizePath(string path);
    string BuildArchiveFilePath(string projectPath, string outputDirectory, DateTimeOffset timestamp);
    void EnsureDirectoryExists(string path);
}
