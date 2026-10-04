using Avalonia.Controls;

namespace Marrow.App.Port.Settings;

/// <summary>
/// PORT-ONLY, first-release SA-1. A Settings page that wants the window chrome row (the compact title and the accessories to its
/// right, which upstream builds in SettingsView.cs :246-267) says so here and supplies the controls, so a page unit never edits
/// SettingsView.cs: SettingsView asks the page it built whether <see cref="HasChromeControls"/> and puts
/// <see cref="ChromeAccessories"/> into the row. The pages upstream names keep SettingsView's own list.
/// </summary>
internal interface ISettingsPageChrome
{
    /// <summary>Whether the page has controls in the chrome row (the content then starts below the row, as upstream :225 does).</summary>
    bool HasChromeControls { get; }

    /// <summary>The controls to the right of the compact title, left to right.</summary>
    IReadOnlyList<Control> ChromeAccessories { get; }
}
