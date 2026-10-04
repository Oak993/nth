// PORT-ONLY, first-release SA-1 (OD-0099 Sections C: its own page, directly above Skills): the shell of the Plugins Settings page. The page is hidden from the sidebar, from search and
// from SettingsNavigation until the unit that builds it sets IsReady to true (the pattern of voice-SPEC V-2.6's VoiceFeature.Enabled).
// Tracked as incomplete in porting/STATE.md while IsReady is false. The unit that builds the page replaces the empty content below and
// never edits SettingsView.cs (its factory line, title, symbol and keywords are SA-1's).
using Avalonia.Controls;

namespace Marrow.App.Port.Settings;

/// <summary>The Plugins page: a shell until its unit fills it (OD-0099 Sections C: its own page, directly above Skills).</summary>
internal sealed class PluginsSettingsPage : Decorator, ISettingsPageChrome
{
    /// <summary>False until the unit that builds this page sets it; while false the page is not listed or searchable.</summary>
    internal const bool IsReady = false;

    public PluginsSettingsPage()
    {
        Child = new StackPanel();
    }

    /// <inheritdoc />
    public bool HasChromeControls => false;

    /// <inheritdoc />
    public IReadOnlyList<Control> ChromeAccessories { get; } = [];
}
