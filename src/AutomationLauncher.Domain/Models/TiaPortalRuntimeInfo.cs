namespace AutomationLauncher.Domain.Models;

public sealed class TiaPortalRuntimeInfo
{
    public TiaPortalRuntimeInfo(string version, string displayName, string opennessAssemblyPath, string source)
    {
        Version = version;
        DisplayName = displayName;
        OpennessAssemblyPath = opennessAssemblyPath;
        Source = source;
    }

    public string Version { get; }

    public string DisplayName { get; }

    public string OpennessAssemblyPath { get; }

    public string Source { get; }

    public override string ToString()
    {
        return $"{DisplayName} | {Source}";
    }
}