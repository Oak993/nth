// PORT-ONLY, first-release SA-1 (OD-0100 place A: the Tools page of OD-0075 grown into Updates): the shell of the Updates Settings page. The page is hidden from the sidebar, from search and
// from SettingsNavigation until the unit that builds it sets IsReady to true (the pattern of voice-SPEC V-2.6's VoiceFeature.Enabled).
// Tracked as incomplete in porting/STATE.md while IsReady is false. The unit that builds the page replaces the empty content below and
// never edits SettingsView.cs (its factory line, title, symbol and keywords are SA-1's).
using Avalonia.Controls;

namespace Marrow.App.Port.Settings;

/// <summary>The Updates page: a shell until its unit fills it (OD-0100 place A: the Tools page of OD-0075 grown into Updates).</summary>
internal sealed class UpdatesSettingsPage : Decorator, ISettingsPageChrome
{
    /// <summary>False until the unit that builds this page sets it; while false the page is not listed or searchable.</summary>
    internal const bool IsReady = false;

    public UpdatesSettingsPage()
    {
        Child = new StackPanel();
    }

    /// <inheritdoc />
    public bool HasChromeControls => false;

    /// <inheritdoc />
    public IReadOnlyList<Control> ChromeAccessories { get; } = [];
}
