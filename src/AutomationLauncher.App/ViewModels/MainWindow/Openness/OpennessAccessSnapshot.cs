namespace AutomationLauncher.App;

internal sealed class OpennessAccessSnapshot
{
    public OpennessAccessSnapshot(
        string currentWindowsUser,
        bool isCurrentUserAdministrator,
        bool isOpennessGroupAvailable,
        bool isCurrentUserInOpennessGroup,
        string opennessGroupStatus,
        string scopeSummary,
        string resolvedAccountSummary,
        string discoverySummary,
        IReadOnlyList<string> relatedGroups,
        string resolvedGroupName,
        string? historyCode,
        string? historyMessage,
        string historyLevel)
    {
        CurrentWindowsUser = currentWindowsUser;
        IsCurrentUserAdministrator = isCurrentUserAdministrator;
        IsOpennessGroupAvailable = isOpennessGroupAvailable;
        IsCurrentUserInOpennessGroup = isCurrentUserInOpennessGroup;
        OpennessGroupStatus = opennessGroupStatus;
        ScopeSummary = scopeSummary;
        ResolvedAccountSummary = resolvedAccountSummary;
        DiscoverySummary = discoverySummary;
        RelatedGroups = relatedGroups;
        ResolvedGroupName = resolvedGroupName;
        HistoryCode = historyCode;
        HistoryMessage = historyMessage;
        HistoryLevel = historyLevel;
    }

    public string CurrentWindowsUser { get; }

    public bool IsCurrentUserAdministrator { get; }

    public bool IsOpennessGroupAvailable { get; }

    public bool IsCurrentUserInOpennessGroup { get; }

    public string OpennessGroupStatus { get; }

    public string ScopeSummary { get; }

    public string ResolvedAccountSummary { get; }

    public string DiscoverySummary { get; }

    public IReadOnlyList<string> RelatedGroups { get; }

    public string ResolvedGroupName { get; }

    public string? HistoryCode { get; }

    public string? HistoryMessage { get; }

    public string HistoryLevel { get; }
}
