// PORT-ONLY: T11 step 2 (ENGINEERING DECISION, ED-NEW-cloud-ma-settings). "Use the Handshake" has a switch on the page and HandshakeOptions.Enabled in the engine but no stored value yet: this is its thin seam, on by default as the page says.
namespace Marrow.Core.Port.MultiAccount.Settings;

public sealed class HandshakeSwitch(ISettingsStore settings)
{
    public const string SettingsKey = "multiAccountHandshake";

    public bool Enabled
    {
        get => settings.Object(SettingsKey) is not bool on || on;
        set => settings.Set(SettingsKey, value);
    }
}
