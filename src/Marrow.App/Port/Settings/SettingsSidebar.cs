using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.App.Port.Settings;

/// <summary>
/// PORT-ONLY, first-release SA-1. The pages the Settings sidebar lists for a search query: upstream's rule (SettingsView.swift :100-106,
/// a page is listed when the query is empty or is contained in its title or one of its keywords, ignoring case) in the sidebar order
/// (the order of <c>SettingsPage</c>'s cases), minus every page whose shell has not set IsReady (customization-SPEC CZ-2.1.3).
/// </summary>
internal static class SettingsSidebar
{
    /// <summary>The listed pages for <paramref name="query"/>; <paramref name="isReady"/> defaults to each page's own <c>IsReady</c> const.</summary>
    internal static IReadOnlyList<SettingsPage> Pages(string query, Func<SettingsPage, bool>? isReady = null)
    {
        isReady ??= static page => page.IsReady;
        var trimmed = FoundationText.Trim(query, includeNewlines: true);
        return SwiftEnum<SettingsPage>.AllCases
            .Where(page => isReady(page) && (trimmed.Length == 0
                || SwiftString.LocalizedCaseInsensitiveContains(page.Title, trimmed)
                || page.Keywords.Any(word => SwiftString.LocalizedCaseInsensitiveContains(word, trimmed))))
            .ToList();
    }
}
