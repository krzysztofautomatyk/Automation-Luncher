using System.IO;
using AutomationLauncher.Domain.Contracts;

namespace AutomationLauncher.Infrastructure.FileSystem;

public sealed class PathService : IPathService
{
    public string NormalizePath(string path)
    {
        var cleaned = path.Trim().Trim('"', '\'');
        var full = Path.GetFullPath(cleaned);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string BuildArchiveFilePath(string projectPath, string outputDirectory, DateTimeOffset timestamp)
    {
        var normalizedDirectory = NormalizePath(outputDirectory);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = "TIAProject";
        }

        var fileName = $"{projectName}_{timestamp:yyyyMMdd_HHmmss}.zap19";
        return Path.Combine(normalizedDirectory, fileName);
    }

    public void EnsureDirectoryExists(string path)
    {
        Directory.CreateDirectory(path);
    }
}
