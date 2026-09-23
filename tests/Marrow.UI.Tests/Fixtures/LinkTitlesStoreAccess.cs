using System.Collections.Generic;
using System.Reflection;
using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Fixtures;

// PORT-ONLY test helper: DroppyCode/UI/Markdown/LinkTitles.swift's nested `Store` (B2 1fbf81e, :79-111) and its
// private `oembedURL(for:)` (:62-69) are Swift `private`. Store.Claim, Store.Finish and Store.Title are pure,
// network-free logic (PR-THEME-135's claim-once memoization and PR-THEME-136's 400-entry flush); `TitleForVideo`
// (public, :43-56) exercises them together with a real fetch, so tests reach the shared `LinkTitles.Store.Shared`
// singleton and the private `OembedURL` by reflection, the way StdioFramerTests and StdioProcessTests already
// reach private port state in this codebase.

/// <summary>Reflection access to <c>LinkTitles.Store.Shared</c> for tests that cannot reach it directly.</summary>
public static class LinkTitlesStoreAccess
{
    private static readonly Type StoreType =
        typeof(LinkTitles).GetNestedType("Store", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("LinkTitles.Store was not found; has it been renamed?");

    private static readonly object Shared =
        StoreType.GetField("Shared", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static readonly MethodInfo ClaimMethod = StoreType.GetMethod("Claim", BindingFlags.Public | BindingFlags.Instance)!;
    private static readonly MethodInfo FinishMethod = StoreType.GetMethod("Finish", BindingFlags.Public | BindingFlags.Instance)!;
    private static readonly MethodInfo TitleMethod = StoreType.GetMethod("Title", BindingFlags.Public | BindingFlags.Instance)!;

    // :62-69 oembedURL(for:) is `private`; reached here to check the endpoint and query items it builds
    // without ever sending a request (PR-THEME-135).
    private static readonly MethodInfo OembedUrlMethod =
        typeof(LinkTitles).GetMethod("OembedURL", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("LinkTitles.OembedURL was not found; has it been renamed?");

    /// <summary>:96-99. True the first time an id is claimed, false every time after.</summary>
    public static bool Claim(string id) => (bool)ClaimMethod.Invoke(Shared, [id])!;

    /// <summary>:101-110. Stores a title (or none) and always marks the id claimed.</summary>
    public static void Finish(string id, string? title) => FinishMethod.Invoke(Shared, [id, title]);

    /// <summary>:92-94. The title cached for an id, or null.</summary>
    public static string? Title(string id) => (string?)TitleMethod.Invoke(Shared, [id]);

    /// <summary>:62-69. The oEmbed request URL for a video id, or null.</summary>
    public static Uri? OembedUrl(string id) => (Uri?)OembedUrlMethod.Invoke(null, [id]);

    /// <summary>
    /// The raw entry counts of the private `State` behind the lock (:85-88), read without going through
    /// `WithLock` since the test thread is the only writer at the point it calls this.
    /// </summary>
    public static (int TitleCount, int ClaimedCount) RawCounts()
    {
        var stateField = StoreType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var mutex = stateField.GetValue(Shared)!;
        var valueField = mutex.GetType().GetField("value", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var state = valueField.GetValue(mutex)!;
        var stateType = state.GetType();
        var titles = (System.Collections.IDictionary)stateType.GetField("Titles")!.GetValue(state)!;
        var claimed = (ICollection<string>)stateType.GetField("Claimed")!.GetValue(state)!;
        return (titles.Count, claimed.Count);
    }
}
