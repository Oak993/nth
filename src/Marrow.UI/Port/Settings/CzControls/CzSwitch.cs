// PORT-ONLY CZ-1: accessibility/dimming adapter for the EXISTING port SettingsSwitch, CZ-12.3/15.
// The page supplies its App-layer SettingsSwitch; UI never references App or replaces the switch's renderer.
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
namespace Marrow.UI.Port.Settings;

public sealed class CzSwitch : Decorator
{
    private readonly Control toggle;
    private readonly IToggleProvider provider;
    private readonly CzNumber fade;
    private bool dimmed;
    public bool Checked => provider.ToggleState == ToggleState.On;
    public bool Dimmed
    {
        get => dimmed;
        set { dimmed = value; IsEnabled = !value; toggle.Focusable = !value; fade.To(value ? .5 : 1, CzMotion.Ease(.3)); }
    }
    public CzSwitch(Control settingsSwitch, string title, string detail = "")
    {
        Child = settingsSwitch; toggle = settingsSwitch;
        while (toggle is Decorator { Child: { } inner }) toggle = inner;
        provider = ControlAutomationPeer.CreatePeerForElement(toggle) as IToggleProvider ?? throw new ArgumentException("Supply the port's toggle control.", nameof(settingsSwitch));
        AutomationProperties.SetName(this, title); AutomationProperties.SetHelpText(this, detail);
        AutomationProperties.SetName(toggle, title); AutomationProperties.SetHelpText(toggle, detail);
        fade = new CzNumber(new CzMotion(this), 1, v => Opacity = v);
    }
    public void Toggle() { if (!Dimmed && IsEffectivelyEnabled) provider.Toggle(); }
    protected override AutomationPeer OnCreateAutomationPeer() => new CzSwitchPeer(this);
}
