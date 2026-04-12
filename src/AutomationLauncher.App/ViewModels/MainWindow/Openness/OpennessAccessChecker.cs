using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Security.Principal;

namespace AutomationLauncher.App;

internal static class OpennessAccessChecker
{
    internal const string GroupName = "Siemens TIA Portal Openness";

    internal static OpennessAccessSnapshot GetSnapshot()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var identityName = identity?.Name;
            var currentUser = !string.IsNullOrWhiteSpace(identityName)
                ? identityName!
                : System.Environment.UserName;

            var principal = identity is null ? null : new WindowsPrincipal(identity);
            var isAdministrator = principal?.IsInRole(WindowsBuiltInRole.Administrator) == true;
            var scopeSummary = BuildOpennessScopeSummary(currentUser);

            using var machineContext = new PrincipalContext(ContextType.Machine);
            var relatedGroups = DiscoverRelatedLocalGroups(machineContext);
            var (group, resolvedGroupName) = ResolveOpennessGroup(machineContext, relatedGroups);
            var discoverySummary = BuildLocalGroupDiscoverySummary(relatedGroups, group is not null, resolvedGroupName);

            var user = ResolveCurrentUserPrincipal(machineContext, currentUser, identity);
            var resolvedAccountSummary = BuildResolvedAccountSummary(currentUser, user, identity);

            if (group is null)
            {
                var missingMsg = $"No Openness group found. Searched for '{GroupName}' and any group containing 'Openness'. Confirm TIA Openness installation.";
                return new OpennessAccessSnapshot(
                    currentUser,
                    isAdministrator,
                    isOpennessGroupAvailable: false,
                    isCurrentUserInOpennessGroup: false,
                    opennessGroupStatus: missingMsg,
                    scopeSummary,
                    resolvedAccountSummary,
                    discoverySummary,
                    relatedGroups,
                    resolvedGroupName: string.Empty,
                    historyCode: "OpennessGroupMissing",
                    historyMessage: missingMsg,
                    historyLevel: "WARN");
            }

            if (user is null)
            {
                return new OpennessAccessSnapshot(
                    currentUser,
                    isAdministrator,
                    isOpennessGroupAvailable: true,
                    isCurrentUserInOpennessGroup: false,
                    opennessGroupStatus: "Could not resolve current Windows account in local principal context.",
                    scopeSummary,
                    resolvedAccountSummary,
                    discoverySummary,
                    relatedGroups,
                    resolvedGroupName,
                    historyCode: "OpennessUserResolveFailed",
                    historyMessage: "Could not resolve current Windows account in local principal context.",
                    historyLevel: "WARN");
            }

            var isMember = user.IsMemberOf(group);
            var status = isMember
                ? $"Access OK: current user belongs to '{resolvedGroupName}'."
                : $"Access missing: current user is not in '{resolvedGroupName}'.";

            return new OpennessAccessSnapshot(
                currentUser,
                isAdministrator,
                isOpennessGroupAvailable: true,
                isCurrentUserInOpennessGroup: isMember,
                opennessGroupStatus: status,
                scopeSummary,
                resolvedAccountSummary,
                discoverySummary,
                relatedGroups,
                resolvedGroupName,
                historyCode: isMember ? "OpennessAccessOk" : "OpennessAccessMissing",
                historyMessage: status,
                historyLevel: isMember ? "OK" : "WARN");
        }
        catch (PrincipalServerDownException ex)
        {
            return new OpennessAccessSnapshot(
                System.Environment.UserName,
                isCurrentUserAdministrator: false,
                isOpennessGroupAvailable: false,
                isCurrentUserInOpennessGroup: false,
                opennessGroupStatus: $"Local security account manager is not available: {ex.Message}",
                scopeSummary: "Target group scope: local machine only. The local security account manager was unavailable during the check.",
                resolvedAccountSummary: "Windows account could not be fully resolved because the principal server was unavailable.",
                discoverySummary: "Related local group discovery did not complete.",
                relatedGroups: System.Array.Empty<string>(),
                resolvedGroupName: "Unavailable",
                historyCode: "OpennessPrincipalServerDown",
                historyMessage: ex.Message,
                historyLevel: "ERROR");
        }
        catch (System.Exception ex)
        {
            return new OpennessAccessSnapshot(
                System.Environment.UserName,
                isCurrentUserAdministrator: false,
                isOpennessGroupAvailable: false,
                isCurrentUserInOpennessGroup: false,
                opennessGroupStatus: $"Openness group check failed: {ex.Message}",
                scopeSummary: "Target group scope: local machine only.",
                resolvedAccountSummary: "Windows account could not be fully resolved because the access check failed.",
                discoverySummary: "Related local group discovery did not complete.",
                relatedGroups: System.Array.Empty<string>(),
                resolvedGroupName: "Unavailable",
                historyCode: "OpennessCheckFailed",
                historyMessage: ex.Message,
                historyLevel: "ERROR");
        }
    }

    private static (GroupPrincipal? group, string resolvedGroupName) ResolveOpennessGroup(
        PrincipalContext machineContext, IReadOnlyList<string> relatedGroups)
    {
        var exact = GroupPrincipal.FindByIdentity(machineContext, IdentityType.Name, GroupName)
            ?? GroupPrincipal.FindByIdentity(machineContext, GroupName);
        if (exact is not null)
            return (exact, GroupName);

        var fallbackName = relatedGroups.FirstOrDefault(name =>
            name.IndexOf("Openness", System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            var fallback = GroupPrincipal.FindByIdentity(machineContext, IdentityType.Name, fallbackName);
            if (fallback is not null)
                return (fallback, fallbackName!);
        }

        return (null, string.Empty);
    }

    private static UserPrincipal? ResolveCurrentUserPrincipal(
        PrincipalContext machineContext, string? identityName, WindowsIdentity? identity)
    {
        if (!string.IsNullOrWhiteSpace(identityName))
        {
            var byName = UserPrincipal.FindByIdentity(machineContext, IdentityType.Name, identityName);
            if (byName is not null)
                return byName;

            var bySam = UserPrincipal.FindByIdentity(machineContext, IdentityType.SamAccountName, identityName);
            if (bySam is not null)
                return bySam;

            var normalizedIdentityName = identityName!;
            var shortName = normalizedIdentityName.Contains('\\')
                ? normalizedIdentityName.Split('\\').LastOrDefault()
                : normalizedIdentityName;
            if (!string.IsNullOrWhiteSpace(shortName))
            {
                var byShortSam = UserPrincipal.FindByIdentity(machineContext, IdentityType.SamAccountName, shortName);
                if (byShortSam is not null)
                    return byShortSam;
            }
        }

        if (!string.IsNullOrWhiteSpace(System.Environment.UserName))
        {
            var byEnvironment = UserPrincipal.FindByIdentity(machineContext, IdentityType.SamAccountName, System.Environment.UserName);
            if (byEnvironment is not null)
                return byEnvironment;
        }

        var sid = identity?.User?.Value;
        if (!string.IsNullOrWhiteSpace(sid))
            return UserPrincipal.FindByIdentity(machineContext, IdentityType.Sid, sid);

        return null;
    }

    private static string BuildOpennessScopeSummary(string currentUser)
    {
        var authority = currentUser.Contains('\\')
            ? currentUser.Split('\\')[0]
            : System.Environment.MachineName;
        var accountScope = string.Equals(authority, System.Environment.MachineName, System.StringComparison.OrdinalIgnoreCase)
            ? "local machine account"
            : $"external account authority '{authority}'";

        return $"Target group scope: local machine only. The signed-in account is evaluated as {currentUser} ({accountScope}). Domain or AzureAD identities can still be members of the local machine group.";
    }

    private static string BuildResolvedAccountSummary(string currentUser, UserPrincipal? user, WindowsIdentity? identity)
    {
        var sid = identity?.User?.Value ?? "n/a";
        var authenticationType = string.IsNullOrWhiteSpace(identity?.AuthenticationType)
            ? "n/a"
            : identity!.AuthenticationType;

        if (user is null)
        {
            return $"WindowsIdentity resolved as {currentUser}. SID: {sid}. Authentication: {authenticationType}. The account could not be resolved inside the local machine principal context.";
        }

        var resolvedName = !string.IsNullOrWhiteSpace(user.SamAccountName)
            ? user.SamAccountName
            : user.Name ?? currentUser;
        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? resolvedName : user.DisplayName;

        return $"WindowsIdentity resolved as {currentUser}. Local principal match: {displayName} ({resolvedName}). SID: {sid}. Authentication: {authenticationType}.";
    }

    private static IReadOnlyList<string> DiscoverRelatedLocalGroups(PrincipalContext machineContext)
    {
        var groups = new List<string>();
        using var query = new GroupPrincipal(machineContext);
        using var searcher = new PrincipalSearcher(query);

        foreach (var principal in searcher.FindAll())
        {
            using (principal)
            {
                if (principal is not GroupPrincipal group || string.IsNullOrWhiteSpace(group.Name))
                    continue;

                if (group.Name.IndexOf("Openness", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || group.Name.IndexOf("Siemens", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    groups.Add(group.Name);
                }
            }
        }

        return groups
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => string.Equals(name, GroupName, System.StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildLocalGroupDiscoverySummary(
        IReadOnlyList<string> relatedGroups, bool targetGroupExists, string resolvedGroupName)
    {
        if (relatedGroups.Count == 0)
            return "No local groups containing 'Openness' or 'Siemens' were found on this machine.";

        var targetMessage = targetGroupExists
            ? $"Group used for check: '{resolvedGroupName}'."
            : "The exact target group is missing.";

        return $"Found {relatedGroups.Count} local group(s) containing 'Openness' or 'Siemens'. {targetMessage}";
    }
}
