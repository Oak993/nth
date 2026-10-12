// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.2, 9.1; unit AS-1; ED-NEW-dextracore-3). No Swift twin.
// A structured grant, never "Allow always": who, which class, which target, which fields, for how long, from which
// decision, and every audit entry that used it. Pure: the caller passes the time.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>What a grant or an action points at.</summary>
public enum GrantTargetKind { App, Connector, Site, Folder, Recipients }

/// <summary>
/// The target of a grant (12.2): an app, connector, site, folder or recipient list. As the target of an action it
/// holds the one thing the action touches (a site's origin, a folder path, the recipients of a message).
/// </summary>
public sealed record GrantTarget(GrantTargetKind Kind, ValueList<string> Values)
{
    public static GrantTarget App(string name) => new(GrantTargetKind.App, new ValueList<string>([name]));

    public static GrantTarget Connector(string name) => new(GrantTargetKind.Connector, new ValueList<string>([name]));

    public static GrantTarget Site(string origin) => new(GrantTargetKind.Site, new ValueList<string>([origin]));

    public static GrantTarget Folder(string path) => new(GrantTargetKind.Folder, new ValueList<string>([path]));

    public static GrantTarget Recipients(params string[] recipients) => new(GrantTargetKind.Recipients, new ValueList<string>(recipients));

    /// <summary>
    /// Whether this target (a grant's) covers <paramref name="requested"/> (an action's): the same kind, and every
    /// value requested is inside one of this target's values. A site is matched by origin (scheme, host and port), a
    /// folder by path containment, a recipient list by every recipient being listed, apps and connectors by name.
    /// </summary>
    public bool Covers(GrantTarget requested)
    {
        if (requested.Kind != Kind || requested.Values.Count == 0) return false;
        return requested.Values.All(want => Values.Any(have => Kind switch
        {
            GrantTargetKind.Site => SameOrigin(have, want),
            GrantTargetKind.Folder => PathRules.IsInside(want, have, strict: false),
            _ => string.Equals(have.Trim(), want.Trim(), StringComparison.OrdinalIgnoreCase),
        }));
    }

    /// <summary>The origin of a URL ("https://shop.example"), or null when it is not an absolute http(s) URL.</summary>
    public static string? OriginOf(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.IdnHost}" : $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
    }

    private static bool SameOrigin(string grant, string requested) =>
        OriginOf(grant) is { } a && OriginOf(requested) is { } b && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>How long a grant lasts (12.2): once, this mission, until a date, or standing.</summary>
public enum GrantScopeKind { Once, Mission, Until, Standing }

/// <summary>A grant's scope. Mission names the mission; Until names the end (exclusive).</summary>
public sealed record GrantScope(GrantScopeKind Kind, Guid? MissionId = null, DateTimeOffset? Until = null)
{
    public static GrantScope Once { get; } = new(GrantScopeKind.Once);

    public static GrantScope Standing { get; } = new(GrantScopeKind.Standing);

    public static GrantScope ForMission(Guid missionId) => new(GrantScopeKind.Mission, MissionId: missionId);

    public static GrantScope UntilDate(DateTimeOffset end) => new(GrantScopeKind.Until, Until: end);
}

/// <summary>
/// A structured grant (12.2). Fields lists what the grant allows ("sign in", "read"); when it is empty the grant
/// allows every field of its class on its target. Excluded lists what it never allows ("send", "upload"). Conditions
/// are shown on the card. UsedBy holds the audit entries that used it; a Once grant is spent after one use.
/// </summary>
public sealed record Grant(Guid Id, string AssistantId, ActionClass Class, GrantTarget Target, GrantScope Scope)
{
    public ValueList<string> Fields { get; init; } = ValueList<string>.Empty;

    public ValueList<string> Excluded { get; init; } = ValueList<string>.Empty;

    public ValueList<string> Conditions { get; init; } = ValueList<string>.Empty;

    public Guid? FromDecision { get; init; }

    public DateTimeOffset Created { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public ValueList<string> UsedBy { get; init; } = ValueList<string>.Empty;

    /// <summary>Whether the grant can be used at <paramref name="now"/> inside <paramref name="missionId"/>.</summary>
    public bool IsLive(DateTimeOffset now, Guid? missionId)
    {
        if (!GrantRules.Takes(Class, Target.Kind)) return false;
        if (RevokedAt is { } revoked && revoked <= now) return false;
        return Scope.Kind switch
        {
            GrantScopeKind.Once => UsedBy.Count == 0,
            GrantScopeKind.Mission => Scope.MissionId is { } id && missionId == id,
            GrantScopeKind.Until => Scope.Until is { } end && now < end,
            GrantScopeKind.Standing => true,
            _ => false,
        };
    }

    /// <summary>Whether this grant covers the class, target and field of one action part.</summary>
    public bool Covers(ActionClass actionClass, GrantTarget? target, string? field)
    {
        if (actionClass != Class || !GrantRules.Takes(Class, Target.Kind)) return false;
        if (target is null || !Target.Covers(target)) return false;
        if (field is not null && Excluded.Any(f => string.Equals(f, field, StringComparison.OrdinalIgnoreCase))) return false;
        if (Fields.Count == 0) return true;
        return field is not null && Fields.Any(f => string.Equals(f, field, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The grant with one more use recorded.</summary>
    public Grant UsedByEntry(string auditEntryId) => this with { UsedBy = UsedBy.Appending(auditEntryId) };

    /// <summary>The grant revoked at <paramref name="when"/>. Revoking is a state change, enforced at the next action.</summary>
    public Grant Revoked(DateTimeOffset when) => this with { RevokedAt = when };
}

/// <summary>The rules that keep grants honest (12.1, 12.2, 9.1).</summary>
public static class GrantRules
{
    /// <summary>
    /// Whether a class takes a grant on a kind of target: the "grant possible" classes (12.2), and Read local on a
    /// folder, which is how a read outside the access folders is allowed without asking each time.
    /// </summary>
    public static bool Takes(ActionClass actionClass, GrantTargetKind kind) =>
        ActionClasses.IsGrantable(actionClass) || (actionClass == ActionClass.ReadLocal && kind == GrantTargetKind.Folder);

    /// <summary>Why a grant cannot exist, or null when it can. Only "grant possible" classes take grants.</summary>
    public static string? Invalid(Grant grant)
    {
        if (!Takes(grant.Class, grant.Target.Kind))
            return ActionClasses.IsIrreversible(grant.Class)
                ? "Irreversible steps are always confirmed; no grant can cover them."
                : $"{grant.Class} cannot be granted.";
        if (grant.Target.Values.Count == 0) return "A grant names its target.";
        if (grant.Scope.Kind == GrantScopeKind.Mission && grant.Scope.MissionId is null) return "A mission grant names its mission.";
        if (grant.Scope.Kind == GrantScopeKind.Until && grant.Scope.Until is null) return "A dated grant names its end.";
        return null;
    }

    /// <summary>
    /// Whether <paramref name="child"/> is no broader than <paramref name="parent"/>: the same class, a target the
    /// parent's covers, no field the parent lacks, every field the parent excludes still excluded, and a scope that
    /// ends no later than the parent's.
    /// </summary>
    public static bool IsWithin(Grant child, Grant parent)
    {
        if (child.Class != parent.Class || Invalid(child) is not null || Invalid(parent) is not null) return false;
        if (!parent.Target.Covers(child.Target)) return false;
        if (parent.Fields.Count > 0)
        {
            if (child.Fields.Count == 0) return false;
            if (!child.Fields.All(f => parent.Fields.Contains(f, StringComparer.OrdinalIgnoreCase))) return false;
        }
        if (!parent.Excluded.All(f => child.Excluded.Contains(f, StringComparer.OrdinalIgnoreCase)
                                       || (child.Fields.Count > 0 && !child.Fields.Contains(f, StringComparer.OrdinalIgnoreCase))))
            return false;
        return ScopeWithin(child.Scope, parent.Scope);
    }

    private static bool ScopeWithin(GrantScope child, GrantScope parent) => parent.Kind switch
    {
        GrantScopeKind.Standing => true,
        GrantScopeKind.Until => child.Kind == GrantScopeKind.Once
                                || (child.Kind == GrantScopeKind.Until && child.Until <= parent.Until),
        GrantScopeKind.Mission => child.Kind == GrantScopeKind.Once
                                  || (child.Kind == GrantScopeKind.Mission && child.MissionId == parent.MissionId),
        GrantScopeKind.Once => child.Kind == GrantScopeKind.Once,
        _ => false,
    };
}

/// <summary>Path containment shared by grants and the classifier. Windows paths compare without case.</summary>
public static class PathRules
{
    /// <summary>
    /// Whether <paramref name="path"/> is inside <paramref name="root"/>; with <paramref name="strict"/> the root
    /// itself does not count. Both are made full first; a path that cannot be made full is never inside.
    /// </summary>
    public static bool IsInside(string path, string root, bool strict)
    {
        var full = Normalize(path);
        var top = Normalize(root);
        if (full is null || top is null) return false;
        if (string.Equals(full, top, StringComparison.OrdinalIgnoreCase)) return !strict;
        var prefix = top.EndsWith(Path.DirectorySeparatorChar) ? top : top + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A full path with no trailing separator (a drive root keeps its own), or null.</summary>
    public static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
