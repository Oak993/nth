// PORT-ONLY: MA-3b-4, Handshake point dialog :2628-2660; OD-0119 menu, account override and plan defaults.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MultiAccount.Popover;
internal sealed class AccountHandshakePanel : Decorator
{
    private readonly AccountStore store;
    private readonly Guid id;
    private readonly ModelPulse pulse = new(_ => { });
    private readonly CzCrossfadeText hint = new("") { FontSize = 11, Secondary = true };
    private readonly CzCrossfadeText scope = new("") { FontSize = 11, Secondary = true };
    private readonly Button reset;
    internal AccountHandshakePanel(AccountStore store, Guid id, string name, ProviderKind service)
    {
        this.store = store; this.id = id; Width = 248;
        Slider = new HandshakeSliderControl(() => store.Get(id).Defaults.HandshakePoint ?? store.GeneralHandshakePoint, SetPoint,
            "Handshake point for this account", EffortBrand.Create(service), pulse);
        Slider.ShowPrefix = false;
        Slider.Slider.Named("Handshake point for this account", "account-handshake-slider");
        reset = PlainControls.Button(LibraryUi.Text("Use the default", 11.5), UseDefault); reset.Focusable = true; reset.Named("Use the default", "account-handshake-default");
        Child = new Border { Padding = new Thickness(12), Child = new StackPanel { Spacing = 8, Children =
        { LibraryUi.Text("Handshake point", 13, weight: SwiftFont.Weight.Semibold), LibraryUi.Text(name, 11.5, secondary: true), Slider, hint, scope, reset } } };
        this.Named("Handshake point", "account-handshake-dialog");
        AttachedToVisualTree += (_, _) => { store.Changed += Changed; Changed(); };
        DetachedFromVisualTree += (_, _) => store.Changed -= Changed; Changed();
    }
    internal HandshakeSliderControl Slider { get; }
    internal void SetPoint(HandshakePoint point) => store.SetDefaults(id, store.Get(id).Defaults with { HandshakePoint = point });
    internal void UseDefault() => store.SetDefaults(id, store.Get(id).Defaults with { HandshakePoint = null });
    private void Changed()
    {
        pulse.Bump(); var account = store.Get(id); var point = account.Defaults.HandshakePoint ?? store.GeneralHandshakePoint;
        hint.SetText(point.Percentage is null ? HandshakeStops.NearText(account.Plan) : "At " + point.Percentage.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%");
        reset.IsVisible = account.Defaults.HandshakePoint is not null;
        scope.SetText(account.Defaults.HandshakePoint is null ? "Like all accounts" : "This account only");
    }
}
