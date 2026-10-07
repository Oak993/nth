// PORT-ONLY: T11 step 2 (ENGINEERING DECISION, ED-449). "Use the Handshake" has a switch on the page and HandshakeOptions.Enabled in the engine but no stored value yet: this is its thin seam, OFF by default (OD-0116: the Handshake into another account is off until the user chooses it; the page's "On by default" is outdated).
namespace Marrow.Core.Port.MultiAccount.Settings;

public sealed class HandshakeSwitch(ISettingsStore settings)
{
    public const string SettingsKey = "multiAccountHandshake";

    public bool Enabled
    {
        get => settings.Object(SettingsKey) is bool on && on;
        set => settings.Set(SettingsKey, value);
    }
}
