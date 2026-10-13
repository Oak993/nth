using Marrow.App.Views.Settings;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The Settings pages of the pinned upstream source (SettingsView.swift :4-15), in its order: <see cref="SettingsPage"/>'s cases without
/// the five the first release adds (SA-1: Customization, Voice typing, Updates, Plugins, Skills). The upstream-parity tests of the page
/// order, symbols and indexes read this list, so they keep proving the source's facts while the sidebar order grows.
/// </summary>
public static class UpstreamSettingsPages
{
    public static readonly IReadOnlyList<SettingsPage> PortAdded =
        [SettingsPage.Customization, SettingsPage.VoiceTyping, SettingsPage.Updates, SettingsPage.Plugins, SettingsPage.Skills, SettingsPage.Pills];

    /// <summary>PORT-ONLY features with a page of their own (OD-0134 Ignis, directly below Hydra; Dextra AS-8 Assistant, after Ignis): not upstream's either.</summary>
    public static readonly IReadOnlyList<SettingsPage> FeatureAdded = [SettingsPage.Ignis, SettingsPage.Assistant];

    public static IReadOnlyList<SettingsPage> All { get; } = SwiftEnum<SettingsPage>.AllCases.Where(page => !PortAdded.Contains(page) && !FeatureAdded.Contains(page)).ToList();
}
