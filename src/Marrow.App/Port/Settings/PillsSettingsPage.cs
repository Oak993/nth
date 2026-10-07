// PORT-ONLY, T11 step 2 (OD-0114, OD-0117; multi-account-NOTES "Pills get their own Settings category"): the Pills Settings page, directly below Models. It is always listed. The body is the Pills page of Port/MultiAccount/Settings: the list of pills,
// New pill, "Use for new threads" and the editor, dimmed under a plain message while fewer than two accounts are connected.
using Marrow.App.Port.MultiAccount.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class PillsSettingsPage : Avalonia.Controls.Decorator, ISettingsPageChrome
{
    /// <summary>Built by T11 step 2: the page is listed in the sidebar for everyone and found by its search words.</summary>
    internal const bool IsReady = true;

    public PillsSettingsPage() => Child = new MultiAccountHost<PillsPageView>(context => new PillsPageView(context));

    /// <inheritdoc />
    public bool HasChromeControls => false;

    /// <inheritdoc />
    public IReadOnlyList<Avalonia.Controls.Control> ChromeAccessories { get; } = [];
}
