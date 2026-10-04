// PORT-ONLY, first-release SA-1 (customization-SPEC CZ-2.2; its chrome row carries the Preview state control, CZ-3.2): the shell of the Customization Settings page. The page is hidden from the sidebar, from search and
// from SettingsNavigation until the unit that builds it sets IsReady to true (the pattern of voice-SPEC V-2.6's VoiceFeature.Enabled).
// Tracked as incomplete in porting/STATE.md while IsReady is false. The unit that builds the page replaces the empty content below and
// never edits SettingsView.cs (its factory line, title, symbol and keywords are SA-1's).
using Avalonia.Controls;

namespace Marrow.App.Port.Settings;

/// <summary>The Customization page: a shell until its unit fills it (customization-SPEC CZ-2.2; its chrome row carries the Preview state control, CZ-3.2).</summary>
internal sealed class CustomizationSettingsPage : Decorator, ISettingsPageChrome
{
    /// <summary>False until the unit that builds this page sets it; while false the page is not listed or searchable.</summary>
    internal const bool IsReady = false;

    public CustomizationSettingsPage()
    {
        Child = new StackPanel();
    }

    /// <inheritdoc />
    public bool HasChromeControls => true;

    /// <inheritdoc />
    public IReadOnlyList<Control> ChromeAccessories { get; } = [];
}
