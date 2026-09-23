using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e), part 1 of 4 continued (R-TEST-3: a fourth file for
// the same Swift file). The three sites the unit's stops left undrawn, as the orchestrator's run4 ruling settles them:
//   (1) Image(systemName:) and Image(_:) (:340, :422, :452, :487, :492, :531, :786, :800, :835, :849, :891, :897, :905)
//       are SwiftImage.SystemName and SwiftImage.Named (F.SymbolImage, ED-179, ED-182; OD-0010 draws each SF Symbol
//       as its Lucide counterpart from porting/icons/sf-symbols-to-lucide.json), with the modifiers the Swift applies;
//   (2) the glass (:180, :182, :245, :254) is recorded as data on the layer the site draws (ED-180): the source-defined
//       shape and radius, the tint colour and opacity and the .interactive() flag, over the backdrop already drawn;
//   (3) every `.continuous` shape (:165, :218, :326, :500, :541, :811, :813, :912) goes through the shared helper
//       SwiftShapes with RoundedCornerStyle.Continuous (ED-181).
// Expected values come from those lines and the records: :28 iconFont = .system(.callout, weight: .semibold);
// :29 inlineIconFont = .system(.subheadline, weight: .semibold); :30 chevronFont = .system(size: 8, weight: .bold);
// :33 capsuleContentHeight = 28; :38 capsuleHeight = 32; :76 primaryText = labelColor (ED-051, SwiftColor.LabelColor);
// :79 secondaryText = primaryText.opacity(0.7); :96 glassTint = ThemeManager.spec.surface; the Lucide names chevron-down
// and circle-x are the records' (PR-WIN-188, PR-SIDEBAR-007).
// Sample values are real call sites: ChatView.swift:1165 "Terminal", :1415 "Open in another app", :1091 "Jump to
// latest", :1334 the runtime mode menu ("hand.raised"), ChatView.swift:1355-1358 the branch menu, SettingsView.swift:883-888
// "Delete all", MCPSettingsPage.swift:636-640 the transport picker (MCPCustomServer.Kind, MCPCustom.swift:9, trimmed to
// 0 = stdio, 1 = http), SettingsView.swift:837 the text generation picker (TextGenerationChoice, AppSettings.swift:3-17,
// with ProviderKind.iconName, Provider.swift:60, as its asset).
// PORT-ONLY surface these tests fix, namespace Marrow.UI.Port, src/Marrow.UI/Port/SwiftGlass.cs (ED-180):
//   enum GlassShapeKind { RoundedRectangle, Capsule, Circle }
//   sealed record GlassShape(GlassShapeKind Kind, double? CornerRadius, RoundedCornerStyle? Style)
//       { static RoundedRectangle(double cornerRadius, RoundedCornerStyle style); static Capsule(RoundedCornerStyle style);
//         static GlassShape Circle }
//   sealed record GlassEffect(GlassShape Shape, SwiftColor? TintColor, double? TintOpacity, bool IsInteractive)
//       { static Regular(GlassShape shape); GlassEffect Tint(SwiftColor color, double opacity); GlassEffect Interactive() }
//   static class SwiftGlass { AttachedProperty<GlassEffect?> EffectProperty; GlassEffect? GetEffect(Control control);
//       void SetEffect(Control control, GlassEffect? effect) }
// and SwiftShapes / RoundedCornerStyle as SwiftShapesTests states them (ED-181).
// Not asserted: the app-drawn glass material and its press response (MARROW_DEFERRED ED-180), the continuous corner
// curve (MARROW_DEFERRED ED-181), and the drawn size of a symbol (ED-182, oracle O-63).
[Collection("Headless")]
public sealed class ChromeFourthTests(HeadlessSession headless)
{
    private static Window Show(Control content, ThemeVariant? theme = null, double width = 600, double height = 300)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        if (theme is not null)
            window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        return control;
    }

    private static T WithTheme<T>(AppTheme theme, Func<T> body)
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = theme;
            return body();
        }
        finally
        {
            ThemeManager.Current = original;
        }
    }

    private static List<SwiftImage> Images(Control root) => VisualQuery.Descendants<SwiftImage>(root).ToList();

    private static Rect BoundsIn(Control control, Visual ancestor) =>
        new(control.TranslatePoint(new Point(0, 0), ancestor)!.Value, control.Bounds.Size);

    private static Color Effective(IBrush? brush)
    {
        if (brush is null)
            return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1); // alpha x opacity may round either way by one step
    }

    private static Color Label(double opacity, ThemeVariant theme) => SwiftColor.LabelColor.Opacity(opacity).Resolve(theme);

    private static void AssertSymbol(SwiftImage image, string sf, SwiftFont font)
    {
        Assert.Equal(sf, image.SymbolName);
        Assert.NotNull(image.Mapping); // drawn through the OD-0010 table
        Assert.NotNull(image.Icon);
        Assert.Equal(font, SwiftImage.GetFont(image));
    }

    private static WriteableBitmap Picture() =>
        new(new PixelSize(8, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    private static List<Border> Glass(Control root) =>
        VisualQuery.Descendants<Border>(root).Where(b => SwiftGlass.GetEffect(b) is not null).ToList();

    private static List<Border> Continuous(Control root) =>
        VisualQuery.Descendants<Border>(root).Where(b => SwiftShapes.GetCornerStyle(b) == RoundedCornerStyle.Continuous).ToList();

    // ---- (1) Symbols ----

    [Fact]
    [Trait("Req", "PR-WIN-183")] // UI/Chrome/Chrome.swift:340-343 Image(systemName: symbol).font(Chrome.iconFont).foregroundStyle(foreground).frame(28, 28)
    public void An_icon_label_draws_its_symbol_at_the_callout_semibold_icon_font_centred_in_its_square() => headless.OnUiThread(() =>
    {
        var label = new ChromeIconLabel(symbol: "terminal");
        var window = Show(TopLeft(label), ThemeVariant.Light);

        var image = Assert.Single(Images(label));
        AssertSymbol(image, "terminal", SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold));
        Assert.Equal(new Point(14, 14), BoundsIn(image, label).Center);
        AssertColor(Label(0.92, ThemeVariant.Light), Effective(image.Foreground)); // :358 at rest
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-184")] // UI/Chrome/Chrome.swift:371-372 ChromeIconLabel(symbol: symbol, ...) as the button's label
    [Trait("Req", "PR-WIN-185")] // UI/Chrome/Chrome.swift:397 ChromeIconLabel(symbol: symbol, isActive: isPresented, isHovering: isHovering)
    public void Icon_and_menu_buttons_draw_their_symbol_through_the_icon_label() => headless.OnUiThread(() =>
    {
        var button = new ChromeIconButton(symbol: "terminal", help: "Terminal", action: () => { });
        var window = Show(TopLeft(button));
        AssertSymbol(Assert.Single(Images(button)), "terminal", Chrome.IconFont);
        window.Close();

        var menu = new ChromeMenuButton(symbol: "arrow.up.forward.app", help: "Open in another app", content: () => new Border());
        window = Show(TopLeft(menu));
        AssertSymbol(Assert.Single(Images(menu)), "arrow.up.forward.app", Chrome.IconFont);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-186")] // UI/Chrome/Chrome.swift:422-425 Image(systemName: symbol).font(Chrome.iconFont).foregroundStyle(primary 0.92).frame(32, 32)
    public void A_circle_button_draws_its_symbol_at_the_icon_font_centred_in_its_32_point_circle() => headless.OnUiThread(() =>
    {
        var control = new ChromeCircleButton(symbol: "arrow.down", help: "Jump to latest", action: () => { });
        var window = Show(TopLeft(control), ThemeVariant.Dark);

        var image = Assert.Single(Images(control));
        AssertSymbol(image, "arrow.down", Chrome.IconFont);
        Assert.Equal(new Point(16, 16), BoundsIn(image, control).Center);
        AssertColor(Label(0.92, ThemeVariant.Dark), Effective(image.Foreground));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-187")] // UI/Chrome/Chrome.swift:452-455 Image(systemName: symbol).font(Chrome.iconFont) in the 32 point circle
    public void A_circle_menu_draws_its_symbol_at_the_icon_font_centred_in_its_circle() => headless.OnUiThread(() =>
    {
        var control = new ChromeCircleMenu(symbol: "hand.raised", help: "Supervised", content: () => new Border());
        var window = Show(TopLeft(control), ThemeVariant.Light);

        var image = Assert.Single(Images(control));
        AssertSymbol(image, "hand.raised", Chrome.IconFont);
        Assert.Equal(new Point(16, 16), BoundsIn(image, control).Center);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-188")] // UI/Chrome/Chrome.swift:486-494 HStack(spacing: 6) { Image(symbol).font(inlineIconFont); Text; Image("chevron.down").font(chevronFont).foregroundStyle(secondaryText) }
    public void A_text_menu_draws_its_symbol_then_the_title_then_a_secondary_chevron_down() => headless.OnUiThread(() =>
    {
        var control = new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border());
        var window = Show(TopLeft(control), ThemeVariant.Dark);

        var images = Images(control);
        Assert.Equal(2, images.Count);
        var symbol = images.Single(i => i.SymbolName == "arrow.triangle.branch");
        var chevron = images.Single(i => i.SymbolName == "chevron.down");
        AssertSymbol(symbol, "arrow.triangle.branch", SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold));
        AssertSymbol(chevron, "chevron.down", SwiftFont.System(8, SwiftFont.Weight.Bold));
        Assert.Equal("chevron-down", chevron.Mapping!.Lucide);
        var title = Assert.Single(VisualQuery.Descendants<TextBlock>(control), t => t.Text == "Detached");
        Assert.True(BoundsIn(symbol, control).Right <= BoundsIn(title, control).X);
        Assert.True(BoundsIn(title, control).Right <= BoundsIn(chevron, control).X);
        Assert.False(symbol.IsSet(SwiftImage.ForegroundProperty)); // the row's foreground (:496)
        AssertColor(Label(0.92, ThemeVariant.Dark), Effective(symbol.Foreground));
        AssertColor(Label(0.7, ThemeVariant.Dark), Effective(chevron.Foreground)); // :494 Chrome.secondaryText
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-189")] // UI/Chrome/Chrome.swift:530-532 HStack(spacing: 6) { Image(systemName: symbol).font(Chrome.inlineIconFont); Text }, no chevron
    public void A_text_button_draws_its_symbol_before_the_title_and_no_chevron() => headless.OnUiThread(() =>
    {
        var control = new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => { });
        var window = Show(TopLeft(control), ThemeVariant.Light);

        var symbol = Assert.Single(Images(control));
        AssertSymbol(symbol, "trash", Chrome.InlineIconFont);
        var title = Assert.Single(VisualQuery.Descendants<TextBlock>(control), t => t.Text == "Delete all");
        Assert.True(BoundsIn(symbol, control).Right <= BoundsIn(title, control).X);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-190")] // UI/Chrome/Chrome.swift:835-838 magnifyingglass .callout semibold in secondaryText, hidden; :849-851 xmark.circle.fill .callout at secondaryText 0.8
    public void The_chrome_search_field_draws_a_hidden_magnifier_and_a_clear_glyph_in_secondary_text() => headless.OnUiThread(() =>
    {
        var query = new CellSample<string>("fix");
        var control = new ChromeSearchField(query: new Binding<string>(() => query.Value, v => query.Value = v));
        var window = Show(TopLeft(control), ThemeVariant.Light);

        var magnifier = Assert.Single(Images(control), i => i.SymbolName == "magnifyingglass");
        AssertSymbol(magnifier, "magnifyingglass", SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold));
        AssertColor(Label(0.7, ThemeVariant.Light), Effective(magnifier.Foreground));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(magnifier));

        var clear = Assert.Single(Images(control), i => i.SymbolName == "xmark.circle.fill");
        AssertSymbol(clear, "xmark.circle.fill", SwiftFont.System(SwiftFont.TextStyle.Callout));
        Assert.Equal("circle-x", clear.Mapping!.Lucide);
        AssertColor(Label(0.7 * 0.8, ThemeVariant.Light), Effective(clear.Foreground));
        var button = Assert.Single(VisualQuery.Descendants<Control>(control),
            c => ControlAutomationPeer.CreatePeerForElement(c).GetAutomationControlType() == AutomationControlType.Button);
        Assert.True(button.IsVisualAncestorOf(clear)); // the glyph is the clear button's label (:846-852)
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-191")] // UI/Chrome/Chrome.swift:786-788 magnifyingglass .callout medium at secondaryText 0.85; :800-802 xmark.circle.fill .callout at secondaryText 0.75
    [Trait("Req", "PR-SIDEBAR-007")] // UI/Chrome/Chrome.swift:796-806 the clear glyph exists only while the field has text
    public void The_sidebar_search_field_draws_its_magnifier_and_a_clear_glyph_only_while_it_has_text() => headless.OnUiThread(() =>
    {
        var text = new CellSample<string>("");
        var control = new SidebarSearchField(text: new Binding<string>(() => text.Value, v => text.Value = v));
        var window = Show(TopLeft(control), ThemeVariant.Dark);

        var magnifier = Assert.Single(Images(control));
        AssertSymbol(magnifier, "magnifyingglass", SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium));
        AssertColor(Label(0.7 * 0.85, ThemeVariant.Dark), Effective(magnifier.Foreground));

        text.Value = "a";
        Dispatcher.UIThread.RunJobs();
        var clear = Assert.Single(Images(control), i => i.SymbolName == "xmark.circle.fill");
        AssertSymbol(clear, "xmark.circle.fill", SwiftFont.System(SwiftFont.TextStyle.Callout));
        Assert.Equal("circle-x", clear.Mapping!.Lucide);
        AssertColor(Label(0.7 * 0.75, ThemeVariant.Dark), Effective(clear.Foreground));

        text.Value = "";
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Images(control), i => i.SymbolName == "xmark.circle.fill");
        window.Close();
    });

    private static readonly (int Value, string Title)[] Transports =
    [
        (0, "stdio \U00002014 a command on this Mac"), // U+2014 EM DASH
        (1, "http \U00002014 a remote server"), // U+2014 EM DASH
    ];

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:896-900 Image(systemName: symbol).font(.system(.callout, weight: .medium)).foregroundStyle(secondaryText); :905-907 chevron.down
    public void A_picker_draws_the_selections_symbol_before_the_title_and_the_chevron_after_it() => headless.OnUiThread(() =>
    {
        var kind = new CellSample<int>(0);
        var control = new GlassPickerButton<int>(
            options: Transports,
            selection: new Binding<int>(() => kind.Value, v => kind.Value = v),
            symbol: v => v == 0 ? "terminal" : "network");
        var window = Show(TopLeft(control), ThemeVariant.Light);

        var symbol = Assert.Single(Images(control), i => i.SymbolName == "terminal");
        AssertSymbol(symbol, "terminal", SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium));
        AssertColor(Label(0.7, ThemeVariant.Light), Effective(symbol.Foreground));
        var chevron = Assert.Single(Images(control), i => i.SymbolName == "chevron.down");
        AssertSymbol(chevron, "chevron.down", Chrome.ChevronFont);
        AssertColor(Label(0.7, ThemeVariant.Light), Effective(chevron.Foreground));
        var title = Assert.Single(VisualQuery.Descendants<TextBlock>(control));
        Assert.True(BoundsIn(symbol, control).Right <= BoundsIn(title, control).X);
        Assert.True(BoundsIn(title, control).Right <= BoundsIn(chevron, control).X);

        kind.Value = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Images(control), i => i.SymbolName == "network");
        Assert.DoesNotContain(Images(control), i => i.SymbolName == "terminal");
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:890-895 `if let name = asset(selection) { Image(name).resizable().aspectRatio(contentMode: .fit).frame(width: 14, height: 14) }`
    public void A_picker_draws_the_selections_asset_resizable_to_fit_in_a_14_point_square_before_the_title() => headless.OnUiThread(() =>
    {
        (string Value, string Title)[] choices = [("automatic", "Automatic"), ("claude", "Claude"), ("codex", "Codex"), ("off", "Off")];
        var choice = new CellSample<string>("claude");
        var control = new GlassPickerButton<string>(
            options: choices,
            selection: new Binding<string>(() => choice.Value, v => choice.Value = v),
            asset: v => v is "claude" or "codex" ? "provider-" + v : null); // ProviderKind(rawValue:)?.iconName
        var window = Show(TopLeft(control), ThemeVariant.Light);

        var asset = Assert.Single(Images(control), i => i.AssetName is not null);
        Assert.Equal("provider-claude", asset.AssetName);
        Assert.True(asset.IsResizable);
        Assert.Equal(ContentMode.Fit, asset.AspectContentMode);
        var frame = Assert.IsAssignableFrom<Control>(asset.GetVisualParent());
        Assert.Equal(new Size(14, 14), frame.Bounds.Size);
        var title = Assert.Single(VisualQuery.Descendants<TextBlock>(control));
        Assert.True(BoundsIn(frame, control).Right <= BoundsIn(title, control).X);

        choice.Value = "codex";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("provider-codex", Assert.Single(Images(control), i => i.AssetName is not null).AssetName);

        choice.Value = "off";
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Images(control), i => i.AssetName is not null);
        window.Close();
    });

    // ---- (2) Glass as data, ED-180 ----

    [Theory]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:165 Capsule(style: .continuous), :180 .glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)).interactive(), in: shape)
    [Trait("Req", "PR-WIN-181")]
    [Trait("Req", "PR-THEME-038")]
    [Trait("Decision", "ED-180")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_chrome_capsule_records_interactive_regular_glass_tinted_with_the_surface_at_0_3_in_a_continuous_capsule(bool dark) => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var capsule = new ChromeCapsule(content: () => new Border { Width = 56, Height = 28 });
            var window = Show(TopLeft(capsule), theme);

            var layer = Assert.Single(Glass(capsule));
            var effect = SwiftGlass.GetEffect(layer)!;
            Assert.Equal(GlassShape.Capsule(RoundedCornerStyle.Continuous), effect.Shape);
            Assert.Equal(ThemeManager.Spec.Surface.Resolve(theme), effect.TintColor!.Resolve(theme));
            Assert.Equal(0.3, effect.TintOpacity);
            Assert.True(effect.IsInteractive);
            Assert.Equal(new Rect(0, 0, 62, 32), BoundsIn(layer, capsule));
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-186")] // UI/Chrome/Chrome.swift:141-143 chromeGlassCircle, :167 Circle(), :180 the interactive tinted glass
    [Trait("Decision", "ED-180")]
    public void A_circle_button_records_interactive_tinted_glass_in_a_circle() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var control = new ChromeCircleButton(symbol: "arrow.down", help: "Jump to latest", action: () => { });
            var window = Show(TopLeft(control), ThemeVariant.Dark);

            var effect = SwiftGlass.GetEffect(Assert.Single(Glass(control)))!;
            Assert.Equal(GlassShape.Circle, effect.Shape);
            Assert.Equal(0.3, effect.TintOpacity);
            Assert.True(effect.IsInteractive);
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-190")] // UI/Chrome/Chrome.swift:137-139 chromeGlassFieldCapsule, :182 the same tint without .interactive(), :864
    [Trait("Decision", "ED-180")]
    public void The_chrome_search_field_records_tinted_glass_without_the_press_response() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var control = new ChromeSearchField(query: Binding<string>.Constant(""));
            var window = Show(TopLeft(control), ThemeVariant.Light);

            var effect = SwiftGlass.GetEffect(Assert.Single(Glass(control)))!;
            Assert.Equal(GlassShape.Capsule(RoundedCornerStyle.Continuous), effect.Shape);
            Assert.Equal(ThemeManager.Spec.Surface.Resolve(ThemeVariant.Light), effect.TintColor!.Resolve(ThemeVariant.Light));
            Assert.Equal(0.3, effect.TintOpacity);
            Assert.False(effect.IsInteractive);
            window.Close();
            return 0;
        });
    });

    [Theory]
    [Trait("Req", "PR-WIN-058")] // UI/Chrome/Chrome.swift:218 the continuous 26 point shape, :252-254 shape.fill(.clear).glassEffect(in: shape)
    [Trait("Req", "PR-WIN-059")] // UI/Chrome/Chrome.swift:251-259 the glass under the scrim
    [Trait("Decision", "ED-180")]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.97)]
    public void Without_a_wallpaper_below_0_98_the_backdrop_records_default_glass_under_the_scrim(double setting) => headless.OnUiThread(() =>
    {
        var backdrop = new WindowBackdrop(opacity: setting);
        var window = Show(backdrop, ThemeVariant.Dark, 400, 300);

        var layer = Assert.Single(Glass(backdrop));
        var effect = SwiftGlass.GetEffect(layer)!;
        Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(26, RoundedCornerStyle.Continuous)), effect);
        Assert.Null(effect.TintColor); // `.glassEffect(in:)`: no tint, no .interactive()
        Assert.False(effect.IsInteractive);
        Assert.Null(layer.Background); // drawn as data only, over the window's Acrylic (OD-0005, OD-0014)
        Assert.Equal(new Rect(backdrop.Bounds.Size), BoundsIn(layer, backdrop));
        var scrim = Assert.Single(VisualQuery.Descendants<Border>(backdrop),
            b => b.Background is not null && b.Opacity == WindowBackdrop.Scrim(setting, isDark: true));
        var layers = VisualQuery.Descendants<Border>(backdrop).ToList();
        Assert.True(layers.IndexOf(layer) < layers.IndexOf(scrim)); // :255 the scrim is an overlay on the glass
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-060")] // UI/Chrome/Chrome.swift:249-250 at 0.98 or above a plain fill, no glass layer
    [Trait("Decision", "ED-180")]
    public void At_the_solid_end_the_backdrop_records_no_glass() => headless.OnUiThread(() =>
    {
        var backdrop = new WindowBackdrop(opacity: 0.98);
        var window = Show(backdrop, ThemeVariant.Light, 400, 300);
        Assert.Empty(Glass(backdrop));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-061")] // UI/Chrome/Chrome.swift:241-247 `.overlay { if !isSolid { shape.fill(.clear).glassEffect(in: shape) } }` over the picture
    [Trait("Decision", "ED-180")]
    public void Over_a_wallpaper_the_backdrop_records_default_glass_over_the_picture_only_while_not_solid() => headless.OnUiThread(() =>
    {
        var picture = Picture();
        var backdrop = new WindowBackdrop(opacity: 0.4, wallpaper: picture);
        var window = Show(backdrop, ThemeVariant.Dark, 400, 300);

        var layer = Assert.Single(Glass(backdrop));
        Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(26, RoundedCornerStyle.Continuous)), SwiftGlass.GetEffect(layer));
        Assert.Equal(new Rect(backdrop.Bounds.Size), BoundsIn(layer, backdrop));
        var image = Assert.Single(VisualQuery.Descendants<Image>(backdrop));
        var all = VisualQuery.Descendants<Visual>(backdrop).ToList();
        Assert.True(all.IndexOf(layer) > all.IndexOf(image)); // an overlay on the picture

        backdrop.Update(opacity: 0.98, wallpaper: picture);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Glass(backdrop));

        backdrop.Update(opacity: 0.5, wallpaper: picture);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Glass(backdrop));
        window.Close();
    });

    // ---- (3) Continuous shapes, ED-181 ----

    [Theory]
    [Trait("Req", "PR-WIN-058")] // UI/Chrome/Chrome.swift:218 RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style: .continuous)
    [Trait("Decision", "ED-181")]
    [InlineData(0.5, false)]
    [InlineData(1.0, false)]
    [InlineData(0.6, true)]
    public void Every_backdrop_layer_is_the_continuous_26_point_rounded_rectangle(double setting, bool withWallpaper) => headless.OnUiThread(() =>
    {
        var backdrop = new WindowBackdrop(opacity: setting, wallpaper: withWallpaper ? Picture() : null);
        var window = Show(backdrop, ThemeVariant.Light, 400, 300);

        var layers = VisualQuery.Descendants<Border>(backdrop).ToList();
        Assert.NotEmpty(layers);
        foreach (var layer in layers)
        {
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(layer));
            Assert.Equal(new CornerRadius(26), layer.CornerRadius);
        }
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")] // UI/Chrome/Chrome.swift:164-167 Capsule(style: .continuous) for the capsule, Circle() for the circle
    [Trait("Decision", "ED-181")]
    public void The_capsule_surface_is_a_continuous_capsule_and_the_circle_surface_has_no_corner_style() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var capsule = new ChromeCapsule(content: () => new Border { Width = 56, Height = 28 });
            var window = Show(TopLeft(capsule));
            var surface = Assert.Single(Glass(capsule));
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface));
            Assert.Equal(new CornerRadius(16), surface.CornerRadius);
            window.Close();

            var circle = new ChromeCircleButton(symbol: "arrow.down", help: "Jump to latest", action: () => { });
            window = Show(TopLeft(circle));
            Assert.Null(SwiftShapes.GetCornerStyle(Assert.Single(Glass(circle))));
            window.Close();
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-182")] // UI/Chrome/Chrome.swift:326 RoundedRectangle(cornerRadius: 0.5, style: .continuous)
    [Trait("Decision", "ED-181")]
    public void The_divider_is_a_continuous_rounded_rectangle_of_radius_0_5() => headless.OnUiThread(() =>
    {
        var divider = new ChromeDivider();
        var window = Show(TopLeft(divider));
        var bar = Assert.Single(Continuous(divider));
        Assert.Equal(new CornerRadius(0.5), bar.CornerRadius);
        Assert.Equal(new Size(1, 14), bar.Bounds.Size);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-188")] // UI/Chrome/Chrome.swift:500 .contentShape(Capsule(style: .continuous)), :504 the glass capsule
    [Trait("Req", "PR-WIN-189")] // UI/Chrome/Chrome.swift:541 .contentShape(Capsule(style: .continuous)), :546 the glass capsule
    [Trait("Decision", "ED-181")]
    public void Text_menus_and_buttons_take_presses_and_glass_in_continuous_capsules() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            Control[] controls =
            [
                new ChromeTextMenu(symbol: "arrow.triangle.branch", title: "Detached", help: "Branch", content: () => new Border()),
                new ChromeTextButton(symbol: "trash", title: "Delete all", help: "Delete every archived thread", action: () => { }),
            ];
            foreach (var control in controls)
            {
                var window = Show(TopLeft(control));
                var shapes = Continuous(control);
                Assert.Equal(2, shapes.Count); // the content shape (:500, :541) and the glass surface (:165)
                Assert.Contains(shapes, s => SwiftGlass.GetEffect(s) is null && s.Background is not null); // the hit shape
                foreach (var shape in shapes)
                {
                    Assert.Equal(control.Bounds.Size, shape.Bounds.Size);
                    Assert.Equal(new CornerRadius(16), shape.CornerRadius); // half of 28 + 2 * 2
                }
                window.Close();
            }
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-191")] // UI/Chrome/Chrome.swift:810-813 Capsule(style: .continuous).fill(Chrome.overlay(0.07)) and .contentShape(Capsule(style: .continuous))
    [Trait("Decision", "ED-181")]
    public void The_sidebar_search_field_is_a_continuous_30_point_capsule() => headless.OnUiThread(() =>
    {
        var control = new SidebarSearchField(text: Binding<string>.Constant(""));
        var window = Show(new Border { Width = 240, Child = control }, ThemeVariant.Dark);

        var capsule = Assert.Single(Continuous(control));
        Assert.Equal(30, capsule.Bounds.Height);
        Assert.Equal(new CornerRadius(15), capsule.CornerRadius);
        AssertColor(Label(0.07, ThemeVariant.Dark), Effective(capsule.Background));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-192")] // UI/Chrome/Chrome.swift:912 .contentShape(Capsule(style: .continuous)), :916 the glass capsule
    [Trait("Decision", "ED-181")]
    public void A_picker_takes_presses_and_glass_in_continuous_capsules() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Codex, () =>
        {
            var control = new GlassPickerButton<int>(options: Transports, selection: Binding<int>.Constant(1));
            var window = Show(TopLeft(control));
            var shapes = Continuous(control);
            Assert.Equal(2, shapes.Count); // the content shape (:912) and the glass surface (:165)
            foreach (var shape in shapes)
            {
                Assert.Equal(28, shape.Bounds.Height); // :911 .frame(height: 28), no vertical padding
                Assert.Equal(new CornerRadius(14), shape.CornerRadius);
            }
            window.Close();
            return 0;
        });
    });
}
