using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// A popover is NSPopover on the Mac: its own window, drawn in the appearance of the window it hangs from. On Windows a
// Popup opens as a PopupRoot, a separate top-level. Two things were wrong in the native gallery (E:\Droppy\work\gallery,
// composer-model-picker-dark.png, composer-effort-*-dark.png, main-thread-popover-diff-dark.png): the popup took its
// appearance from nothing (a Popup that is not a child of any control, as every presented popover is, inherits nothing, so
// its content resolved Light in a dark app), and the PopupRoot's own fill is Avalonia's default of opaque white in both
// appearances. ED-238.
[Collection("Headless")]
public sealed class PopoverHostThemeTests(HeadlessSession headless)
{
    private static Color Drawn(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static readonly Color DarkPanel = Color.FromRgb(0x1E, 0x1E, 0x1E); // macOS 27 UI Kit, Panels / Dark: fill Window Background Dark
    private static readonly Color LightPanel = Colors.White; // Panels / Light: fill #FFFFFF

    // An app and a window in the given appearance, an anchor under a scope in it, and a popup built the way every site
    // builds one: new Popup { PlacementTarget = anchor }, never added to a tree.
    private static void InScene(ThemeVariant appearance, Action<Window, Border, Popup, Action<ThemeVariant>> body)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the presenter's grow-in asks for frames
        var application = Application.Current!;
        var before = application.RequestedThemeVariant;
        application.RequestedThemeVariant = appearance;
        var anchor = new Border { Width = 20, Height = 20 };
        var scope = new ThemeVariantScope { RequestedThemeVariant = appearance, Child = anchor };
        var window = HeadlessPopupHost.OverlayWindow(scope);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var popup = new Popup { PlacementTarget = anchor, IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        try
        {
            body(window, anchor, popup, theme => { application.RequestedThemeVariant = theme; scope.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs(); });
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
            application.RequestedThemeVariant = before;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [Trait("Decision", "ED-238")]
    public void A_presented_popover_takes_the_appearance_of_the_control_it_hangs_from(string name) => headless.OnUiThread(() =>
    {
        var appearance = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        InScene(appearance, (window, anchor, popup, _) =>
        {
            Assert.Null(popup.Parent); // a site never adds its popup to the tree
            popup.Child = new Border { Width = 40, Height = 40 };
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.Equal(appearance, popup.ActualThemeVariant);
        });
    });

    [Fact]
    [Trait("Decision", "ED-238")]
    public void A_popover_that_closes_lets_go_of_its_anchor_and_a_second_presentation_adopts_it_again() => headless.OnUiThread(() =>
    {
        InScene(ThemeVariant.Dark, (window, anchor, popup, _) =>
        {
            popup.Child = new Border { Width = 40, Height = 40 };
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(anchor, popup.Parent);
            popup.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(popup.Parent); // the anchor does not keep a popover it no longer shows
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(anchor, popup.Parent);
            Assert.Equal(ThemeVariant.Dark, popup.ActualThemeVariant);
        });
    });

    [Fact]
    [Trait("Decision", "ED-238")]
    public void A_popover_in_its_own_top_level_draws_the_dark_card_and_dark_text_in_a_dark_app_and_follows_a_switch_to_light() => headless.OnUiThread(() =>
    {
        InScene(ThemeVariant.Dark, (window, anchor, popup, setAppearance) =>
        {
            popup.Child = new Border { Width = 40, Height = 40 };
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            var secondary = new TextBlock { Text = "Select model" };
            secondary.Foreground = Marrow.UI.Chrome.Chrome.SecondaryText.Brush(secondary);
            var plain = new TextBlock { Text = "Opus" }; // no colour of its own: labelColor from the top-level's default
            // OD-0095: the panel is drawn by the card the presenter hangs the content on (one outline with its tip).
            var card = Assert.IsType<PopoverCard>(popup.Child);
            popup.Child = null;
            card.Child = new StackPanel { Children = { secondary, plain } };
            using var root = HeadlessPopupHost.Open(popup, window, card);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ThemeVariant.Dark, root.ActualThemeVariant);
            Assert.Equal(DarkPanel, Drawn(card.Background));
            Assert.Equal(new SwiftColor(white: 1, opacity: 0.2).Resolve(ThemeVariant.Dark), Drawn(card.BorderBrush)); // Panels / Dark: border 1 px #FFFFFF@0.2
            Assert.Equal(0, Drawn(root.Background).A); // the top-level is clear around the card's tip
            Assert.Equal(Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(ThemeVariant.Dark), Drawn(secondary.Foreground)); // white, not black, at 0.7
            Assert.Equal(SwiftColor.LabelColor.Resolve(ThemeVariant.Dark), Drawn(plain.Foreground));

            setAppearance(ThemeVariant.Light);
            Assert.Equal(ThemeVariant.Light, root.ActualThemeVariant);
            Assert.Equal(LightPanel, Drawn(card.Background));
            Assert.Equal(Marrow.UI.Chrome.Chrome.SecondaryText.Resolve(ThemeVariant.Light), Drawn(secondary.Foreground));
            Assert.Equal(SwiftColor.LabelColor.Resolve(ThemeVariant.Light), Drawn(plain.Foreground));
        });
    });

    [Fact]
    [Trait("Decision", "ED-238")]
    public void A_popover_in_its_own_top_level_draws_the_light_card_in_a_light_app() => headless.OnUiThread(() =>
    {
        InScene(ThemeVariant.Light, (window, anchor, popup, _) =>
        {
            popup.Child = new Border { Width = 40, Height = 40 };
            Presentation.Presenter.Current.SetPresented(popup, true);
            Dispatcher.UIThread.RunJobs();
            var plain = new TextBlock { Text = "Opus" };
            var card = Assert.IsType<PopoverCard>(popup.Child); // OD-0095: the card draws the panel
            popup.Child = null;
            card.Child = plain;
            using var root = HeadlessPopupHost.Open(popup, window, card);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Light, root.ActualThemeVariant);
            Assert.Equal(LightPanel, Drawn(card.Background));
            Assert.Equal(SwiftColor.Clear.Resolve(ThemeVariant.Light), Drawn(card.BorderBrush)); // Panels / Light draws no edge, only the window manager's shadow
            Assert.Equal(new Thickness(0), root.BorderThickness);
            Assert.Equal(SwiftColor.LabelColor.Resolve(ThemeVariant.Light), Drawn(plain.Foreground));
        });
    });

    [Fact]
    [Trait("Decision", "ED-238")]
    public void A_top_level_that_is_not_a_popovers_keeps_its_own_fill() => headless.OnUiThread(() =>
    {
        // A tooltip is a Popup too, and draws its own card inside a clear surround (PortControlThemes.TipTemplate): only
        // what the presenter shows as a popover gets the panel fill.
        InScene(ThemeVariant.Dark, (window, anchor, popup, _) =>
        {
            var tip = new Popup { PlacementTarget = anchor };
            using var root = HeadlessPopupHost.Open(tip, window, new TextBlock { Text = "tip" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.White, Drawn(root.Background)); // Avalonia's own default, untouched
        });
    });
}
