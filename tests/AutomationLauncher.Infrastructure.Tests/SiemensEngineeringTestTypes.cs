using System.Collections;
using System.IO;

namespace Siemens.Engineering;

public static class FakeOpennessRuntimeState
{
    private static readonly List<object> Processes = new();

    public static int SaveCalls { get; private set; }

    public static int ArchiveCalls { get; private set; }

    public static string? LastArchiveArtifactPath { get; private set; }

    public static void Reset(params object[] processes)
    {
        Processes.Clear();
        Processes.AddRange(processes);
        SaveCalls = 0;
        ArchiveCalls = 0;
        LastArchiveArtifactPath = null;
    }

    public static IEnumerable GetProcesses()
    {
        return Processes.ToArray();
    }

    public static void OnSave()
    {
        SaveCalls++;
    }

    public static void OnArchive(DirectoryInfo directory, string name)
    {
        ArchiveCalls++;
        directory.Create();
        LastArchiveArtifactPath = Path.Combine(directory.FullName, name);
        File.WriteAllText(LastArchiveArtifactPath, "archive");
    }
}

public sealed class TiaPortal
{
    public static IEnumerable GetProcesses()
    {
        return FakeOpennessRuntimeState.GetProcesses();
    }
}

public sealed class TiaPortalProcess
{
    public int Id { get; set; }

    public object? AttachedPortal { get; set; }

    public object? Attach()
    {
        return AttachedPortal;
    }
}

public sealed class AttachedPortalProjects
{
    public object[] Projects { get; set; } = Array.Empty<object>();
}

public sealed class AttachedPortalSingleProject
{
    public object? Project { get; set; }
}

public enum ProjectArchivationMode
{
    Compressed,
    Default
}

public enum ProjectArchiveMode
{
    Compressed,
    Default
}

public sealed class LegacyProject
{
    public string Name { get; set; } = string.Empty;

    public bool Modified { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public void Save()
    {
        FakeOpennessRuntimeState.OnSave();
    }

    public void Archive(DirectoryInfo directory, string name, ProjectArchiveMode mode)
    {
        FakeOpennessRuntimeState.OnArchive(directory, name);
    }
}

public sealed class StandardProject
{
    public string Name { get; set; } = string.Empty;

    public bool IsModified { get; set; }

    public string Path { get; set; } = string.Empty;

    public void Save()
    {
        FakeOpennessRuntimeState.OnSave();
    }

    public void Archive(DirectoryInfo directory, string name, ProjectArchivationMode mode)
    {
        FakeOpennessRuntimeState.OnArchive(directory, name);
    }
}

public sealed class LatestProject
{
    public string Name { get; set; } = string.Empty;

    public bool IsModified { get; set; }

    public string Location { get; set; } = string.Empty;

    public void Save()
    {
        FakeOpennessRuntimeState.OnSave();
    }

    public void Archive(DirectoryInfo directory, string name, ProjectArchivationMode mode)
    {
        FakeOpennessRuntimeState.OnArchive(directory, name);
    }
}