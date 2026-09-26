using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Testing;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e). Part 1 of 4: the window backdrop and its scrim,
// the detail sheet, the glass tint, the chrome capsule, divider and icon, menu and circle buttons, the scroll model
// and the two drag regions. The text menus and buttons, the search fields and the picker are in ChromeSecondTests.
// Every expected value is transcribed from the Swift and the records, never from the C# under test:
//   Chrome.swift:8 windowCornerRadius = 26; :33 capsuleContentHeight = 28; :34 capsuleVerticalPadding = 2;
//   :36 iconCapsuleInnerPadding = 3; :37 dividerHeight = 14; :38 capsuleHeight = 28 + 2 * 2 = 32;
//   :42-44 titleStartOffset 10, titleEndOffset 44, progressQuantum 0.04; :71-73 overlay(o) = primaryText.opacity(o);
//   :76 primaryText = labelColor (ED-051 literals, SwiftColor.LabelColor); :95-96 glassTint = ThemeManager.spec.surface;
//   :98 hover = .easeOut(duration: 0.1); :187-199 the detail sheet; :209-215 the scrim; :226-240 the wallpaper;
//   :308-322 ChromeCapsule; :324-331 ChromeDivider; :333-358 ChromeIconLabel; :361-383 ChromeIconButton;
//   :385-410 ChromeMenuButton; :412-437 ChromeCircleButton; :439-470 ChromeCircleMenu; :564-577 ChromeScrollModel;
//   the drag regions (B3 52bc792 :1091-1137, ED-200: WindowDragArea/WindowDragView collapse into one View at
//   :1091-1099; NoWindowDragArea/NoWindowDragView unchanged apart from the citation move to :1101-1137).
// Help strings are real call sites: ChatView.swift:1091 "Jump to latest", :1415 "Open in another app",
// :1165 "Terminal", :1334 the runtime mode menu, DiffInspector.swift:61 "Hide changes".
// Port shape these tests read (R-VIEW-1, R-VIEW-9, ED-095): a SwiftUI Button is found through UI Automation as a control
// whose peer is a Button; `.help` is its ToolTip tip and `.accessibilityLabel` its automation name;
// `.foregroundStyle` is the inherited TextElement foreground set on the styled view; `.accessibilityHidden(true)` is
// AccessibilityView.Raw; `.popover` goes through the Presentation seam, recorded here by PopoverRecorder.
// Not asserted here, because the dependency is not in the tree (see the unit report): the glass material and its
// press response (OD-0005, ADR-0002), `.continuous` shape geometry, SF Symbol glyphs as Lucide icons (OD-0010) and
// the in-flight frames of an animated change. Hover end states are read after advancing the manual frame clock well
// past the 0.1 s of Chrome.hover, so they hold whether or not the change animates.
[Collection("Headless")]
public sealed class ChromeTests(HeadlessSession headless)
{
    private static readonly Point Away = new(395, 295);

    private static Window Show(Control content, ThemeVariant? theme = null, double width = 400, double height = 300)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        if (theme is not null)
            window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // Window.InputHitTest reads the last composed scene; once the session has handled any input, a new window's
        // scene is composed only on a render tick, so tick once before a test hit-tests it directly.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control TopLeft(Control control)
    {
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        return control;
    }

    private static void Frames(ManualFrameSource frames, int count = 30)
    {
        for (var i = 0; i < count; i++)
        {
            (SwiftClock.Provider.Current as ManualTimeProvider)?.Advance(TimeSpan.FromMilliseconds(10));
            frames.Advance(TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    private static IEnumerable<Control> SelfAndDescendants(Control root) =>
        new[] { root }.Concat(VisualQuery.Descendants<Control>(root));

    private static Control TheButton(Control root) =>
        Assert.Single(SelfAndDescendants(root), c => Peer(c).GetAutomationControlType() == AutomationControlType.Button);

    private static Rect BoundsIn(Control control, Visual ancestor) =>
        new(control.TranslatePoint(new Point(0, 0), ancestor)!.Value, control.Bounds.Size);

    private static Point Centre(Control control, Window window)
    {
        var bounds = BoundsIn(control, window);
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    // The view whose `.foregroundStyle` the port sets: the one control in the subtree with a local TextElement foreground.
    private static Control Styled(Control root) =>
        Assert.Single(SelfAndDescendants(root), c => c.IsSet(TextElement.ForegroundProperty));

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

    private static WriteableBitmap Picture() =>
        new(new PixelSize(8, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - 1e-12, expected + 1e-12);

    // ---- WindowBackdrop.scrim(for:isDark:), :209-215 ----

    [Fact]
    [Trait("Req", "PR-WIN-059")] // UI/Chrome/Chrome.swift:211-215
    [Trait("Req", "UI-WIN-034")] // UI/Chrome/Chrome.swift:202-273 window backdrop scrim formula input, :211-215
    public void The_scrim_is_none_at_zero_the_stock_value_at_the_midpoint_and_solid_at_one()
    {
        Assert.Equal(0, WindowBackdrop.Scrim(0, isDark: true));
        Assert.Equal(0, WindowBackdrop.Scrim(0, isDark: false));
        Assert.Equal(0.26, WindowBackdrop.Scrim(0.5, isDark: true));
        Assert.Equal(0.18, WindowBackdrop.Scrim(0.5, isDark: false));
        Assert.Equal(1, WindowBackdrop.Scrim(1, isDark: true));
        Assert.Equal(1, WindowBackdrop.Scrim(1, isDark: false));
    }

    [Fact]
    [Trait("Req", "PR-WIN-059")] // UI/Chrome/Chrome.swift:214: stock * (t / 0.5) below the midpoint, stock + (1 - stock) * ((t - 0.5) / 0.5) above
    public void The_scrim_is_linear_within_each_half()
    {
        Near(0.26 * 0.5, WindowBackdrop.Scrim(0.25, isDark: true)); // 0.13
        Near(0.18 * 0.5, WindowBackdrop.Scrim(0.25, isDark: false)); // 0.09
        Near(0.26 + 0.74 * 0.5, WindowBackdrop.Scrim(0.75, isDark: true)); // 0.63
        Near(0.18 + 0.82 * 0.5, WindowBackdrop.Scrim(0.75, isDark: false)); // 0.59
        Near(0.26 * 0.2, WindowBackdrop.Scrim(0.1, isDark: true));
        Near(0.18 + 0.82 * 0.8, WindowBackdrop.Scrim(0.9, isDark: false));
    }

    [Fact]
    [Trait("Req", "PR-WIN-059")] // UI/Chrome/Chrome.swift:213 `let t = min(max(opacity, 0), 1)`
    public void The_scrim_clamps_the_setting_to_zero_through_one_first()
    {
        Assert.Equal(0, WindowBackdrop.Scrim(-0.5, isDark: true));
        Assert.Equal(0, WindowBackdrop.Scrim(-3, isDark: false));
        Assert.Equal(1, WindowBackdrop.Scrim(1.5, isDark: true));
        Assert.Equal(1, WindowBackdrop.Scrim(40, isDark: false));
    }

    // ---- WindowBackdrop, :201-273 ----

    [Fact]
    [Trait("Req", "PR-WIN-058")] // UI/Chrome/Chrome.swift:8, :218 RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style: .continuous)
    [Trait("Req", "UI-WIN-034")] // UI/Chrome/Chrome.swift:8, :218 continuous rounded rect radius 26 window backdrop shape
    public void The_window_backdrop_corner_radius_is_26_points()
    {
        Assert.Equal(26, Chrome.WindowCornerRadius);
    }

    [Fact]
    [Trait("Req", "PR-WIN-061")] // UI/Chrome/Chrome.swift:232-240 Image(nsImage:).resizable().interpolation(.high).aspectRatio(contentMode: .fill).opacity(opacity)
    [Trait("Req", "UI-WIN-034")] // UI/Chrome/Chrome.swift:232-240 wallpaper image aspect-filled at opacity, no scrim
    public void A_wallpaper_is_painted_aspect_fill_with_high_interpolation_at_the_settings_own_opacity() => headless.OnUiThread(() =>
    {
        foreach (var setting in new[] { 0.0, 0.4, 0.98, 1.0 })
        {
            var picture = Picture();
            var backdrop = new WindowBackdrop(opacity: setting, wallpaper: picture);
            var window = Show(backdrop);

            var image = Assert.Single(VisualQuery.Descendants<Image>(backdrop));
            Assert.Same(picture, image.Source);
            Assert.Equal(setting, image.Opacity);
            Assert.Equal(Stretch.UniformToFill, image.Stretch);
            Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(image));
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-061")] // UI/Chrome/Chrome.swift:226, :249, :251: the picture is drawn only in the `if let wallpaper` branch
    public void Without_a_wallpaper_the_backdrop_paints_no_picture() => headless.OnUiThread(() =>
    {
        var backdrop = new WindowBackdrop(opacity: 0.5);
        var window = Show(backdrop);

        Assert.Empty(VisualQuery.Descendants<Image>(backdrop));
        window.Close();
    });

    // ---- The detail sheet, :145-148, :187-199 ----

    private static (Rectangle First, Rectangle Second) SheetLayers(Control sheet, Control content)
    {
        var layers = VisualQuery.Descendants<Rectangle>(sheet).ToList();
        Assert.Equal(2, layers.Count);
        foreach (var layer in layers)
        {
            Assert.Equal(sheet.Bounds.Size, layer.Bounds.Size); // fills the sheet
            Assert.Equal(0, layer.RadiusX); // square: the window's own clip rounds the sheet (:191-192)
            Assert.Equal(0, layer.RadiusY);
            // Behind the content: an earlier sibling of the content's branch under their common parent.
            var parent = Assert.IsAssignableFrom<Panel>(layer.GetVisualParent());
            var contentBranch = content;
            while (contentBranch.GetVisualParent() != parent)
                contentBranch = Assert.IsAssignableFrom<Control>(contentBranch.GetVisualParent());
            Assert.True(parent.Children.IndexOf(layer) < parent.Children.IndexOf(contentBranch));
        }
        var ordered = layers.OrderBy(l => ((Panel)l.GetVisualParent()!).Children.IndexOf(l)).ToList();
        return (ordered[0], ordered[1]);
    }

    [Fact]
    [Trait("Req", "PR-WIN-078")] // UI/Chrome/Chrome.swift:195-196, dark: Color.black.opacity(0.22), then Chrome.glassTint.opacity(0.22)
    [Trait("Req", "UI-WIN-035")] // UI/Chrome/Chrome.swift:187-199 detail sheet dark: black 0.22 then theme surface 0.22
    [Trait("Req", "PR-THEME-038")]
    public void In_dark_the_detail_sheet_is_black_at_0_22_then_the_theme_surface_at_0_22() => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(new Size(120, 80));
        var sheet = content.DetailSheet();
        var window = Show(sheet, ThemeVariant.Dark);

        var (first, second) = SheetLayers(sheet, content);
        AssertColor(SwiftColor.Black.Opacity(0.22).Resolve(ThemeVariant.Dark), Effective(first.Fill));
        AssertColor(ThemeManager.Spec.Surface.Opacity(0.22).Resolve(ThemeVariant.Dark), Effective(second.Fill));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-078")] // UI/Chrome/Chrome.swift:195-196, light: Color.white.opacity(0.3), then Chrome.glassTint.opacity(0.16)
    [Trait("Req", "UI-WIN-035")] // UI/Chrome/Chrome.swift:187-199 detail sheet light: white 0.3 then theme surface 0.16
    [Trait("Req", "PR-THEME-038")]
    public void In_light_the_detail_sheet_is_white_at_0_3_then_the_theme_surface_at_0_16() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        try
        {
            // Codex's surface is not white, so the two layers are told apart by colour as well as by order.
            ThemeManager.Current = AppTheme.Codex;
            var content = new LayoutProbe(new Size(120, 80));
            var sheet = content.DetailSheet();
            var window = Show(sheet, ThemeVariant.Light);

            var (first, second) = SheetLayers(sheet, content);
            AssertColor(SwiftColor.White.Opacity(0.3).Resolve(ThemeVariant.Light), Effective(first.Fill));
            AssertColor(AppTheme.Codex.Spec().Surface.Opacity(0.16).Resolve(ThemeVariant.Light), Effective(second.Fill));
            window.Close();
        }
        finally
        {
            ThemeManager.Current = original;
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-078")] // UI/Chrome/Chrome.swift:188 @Environment(\.colorScheme), read by :195-196
    public void The_detail_sheet_follows_a_change_of_appearance() => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(new Size(120, 80));
        var sheet = content.DetailSheet();
        var window = Show(sheet, ThemeVariant.Light);

        window.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();

        var (first, second) = SheetLayers(sheet, content);
        AssertColor(SwiftColor.Black.Opacity(0.22).Resolve(ThemeVariant.Dark), Effective(first.Fill));
        AssertColor(ThemeManager.Spec.Surface.Opacity(0.22).Resolve(ThemeVariant.Dark), Effective(second.Fill));
        window.Close();
    });

    // ---- The glass tint, :95-96 ----

    [Fact]
    [Trait("Req", "PR-THEME-038")] // UI/Chrome/Chrome.swift:96 `static var glassTint: Color { ThemeManager.spec.surface }`
    public void The_glass_tint_is_the_active_themes_surface() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { AppTheme.Codex, AppTheme.Matrix, AppTheme.System })
            {
                ThemeManager.Current = theme;
                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                    Assert.Equal(theme.Spec().Surface.Resolve(variant), Chrome.GlassTint.Resolve(variant));
            }
        }
        finally
        {
            ThemeManager.Current = original;
        }
    });

    // ---- ChromeScrollModel, :564-577 ----

    [Fact]
    [Trait("Req", "PR-WIN-195")] // UI/Chrome/Chrome.swift:571-576 with :42-44
    public void Scroll_progress_is_zero_until_10_points_of_travel_and_one_from_44()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var model = new ChromeScrollModel();
            Assert.Equal(0, model.Progress);

            foreach (var travel in new[] { -40.0, 0, 5, 10 })
            {
                model.Update(travel: travel);
                Assert.Equal(0, model.Progress);
            }
            foreach (var travel in new[] { 44.0, 45, 300 })
            {
                model.Update(travel: travel);
                Assert.Equal(25 * 0.04, model.Progress); // clamped to 1, 1 / 0.04 = 25 steps
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-195")] // UI/Chrome/Chrome.swift:572-574: raw, clamped, then `(clamped / Chrome.progressQuantum).rounded() * Chrome.progressQuantum`
    [Trait("Rule", "R-NUM-3")]
    public void Scroll_progress_is_rounded_half_away_from_zero_to_a_multiple_of_0_04()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var model = new ChromeScrollModel();

            model.Update(travel: 11); // (11 - 10) / 34 / 0.04 = 0.735... -> 1
            Assert.Equal(1 * 0.04, model.Progress);
            model.Update(travel: 30); // 20 / 34 / 0.04 = 14.70... -> 15
            Assert.Equal(15 * 0.04, model.Progress);
            model.Update(travel: 43); // 33 / 34 / 0.04 = 24.26... -> 24
            Assert.Equal(24 * 0.04, model.Progress);

            // 17 / 34 = 0.5 exactly, and 0.5 / 0.04 is 12.5 exactly in binary64: Swift's .rounded() goes away from zero.
            model.Update(travel: 27);
            Assert.Equal(13 * 0.04, model.Progress);
            // The obvious .NET call rounds that half to even and lands a step lower (R-TEST-4).
            Assert.Equal(12 * 0.04, Math.Round(0.5 / 0.04) * 0.04);
            Assert.NotEqual(Math.Round(0.5 / 0.04) * 0.04, model.Progress);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-195")] // UI/Chrome/Chrome.swift:44 progressQuantum = 0.04: 0...1 in 0.04 steps
    public void Scroll_progress_takes_exactly_26_distinct_values()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var model = new ChromeScrollModel();
            var seen = new HashSet<double>();
            for (var hundredths = -500; hundredths <= 6000; hundredths++)
            {
                model.Update(travel: hundredths / 100.0);
                seen.Add(model.Progress);
            }

            Assert.Equal(26, seen.Count);
            Assert.Equal(Enumerable.Range(0, 26).Select(step => step * 0.04).ToHashSet(), seen);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-195")] // UI/Chrome/Chrome.swift:575 `if quantized != progress { progress = quantized }`
    public void Scroll_progress_is_written_only_when_the_quantized_value_changes()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var model = new ChromeScrollModel();
            var writes = 0;
            ((INotifyPropertyChanged)model).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChromeScrollModel.Progress)) writes++;
            };

            model.Update(travel: 3); // still 0
            Assert.Equal(0, writes);
            model.Update(travel: 11); // 0.04
            Assert.Equal(1, writes);
            model.Update(travel: 11.2); // 1.2 / 34 / 0.04 = 0.88 -> 1, the same step
            model.Update(travel: 12); // 2 / 34 / 0.04 = 1.47 -> 1, the same step
            Assert.Equal(1, writes);
            model.Update(travel: 12.1); // 2.1 / 34 / 0.04 = 1.54 -> 2
            Assert.Equal(2, writes);
            Assert.Equal(2 * 0.04, model.Progress);
        });
    }

    // ---- The drag regions. WindowDragArea :1091-1099 (B3, ED-200: collapsed from B2's WindowDragArea/WindowDragView
    // NSViewRepresentable/NSView pair); NoWindowDragArea/NoWindowDragView :1101-1137 (B3, unchanged from B2 apart
    // from the citation move). ----

    [Fact]
    [Trait("Req", "PR-WIN-193")] // UI/Chrome/Chrome.swift:1091-1099 (B3): the collapsed View is a caption to the hit test and takes the hit over its whole frame
    [Trait("Req", "UI-WIN-005")] // UI/Chrome/Chrome.swift:1091-1099 (B3)
    public void A_window_drag_region_is_a_single_control_that_is_a_caption_to_the_hit_test() => headless.OnUiThread(() =>
    {
        var area = new WindowDragArea();

        Assert.True(area.AcceptsFirstMouse(null));
        Assert.Equal(Win32Properties.Win32HitTestValue.Caption, Win32Properties.GetNonClientHitTestResult(area));
    });

    // R-TEST-6 (Traceability.AssertEveryTestNamesWhatItProves): an Oracle test cannot also carry a Req trait, since
    // whether Windows should begin the move exactly on press is contingent on O-68 (6.1 E6) - if it settles to show
    // WindowDragGesture waits for a threshold or ignores a bare click, this is the assertion that changes. It
    // carries ED-200 (the DECISION that chose this behaviour under ED-063) instead of a Req trait.
    [Fact]
    [Trait("Decision", "ED-200")] // UI/Chrome/Chrome.swift:1091-1099 (B3): ED-200's BeginMoveDrag fallback starts on press; mouseUp's B2 emptiness is now the release simply going to the system
    [Trait("Oracle", "O-68")] // press-start timing is ED-063's own measured behaviour, not a documented WindowDragGesture semantic (6.1 E6)
    public void Neither_the_press_nor_the_release_on_a_window_drag_region_reaches_what_is_behind_it() => headless.OnUiThread(() =>
    {
        var reached = new List<string>();
        var behind = new Border { Width = 200, Height = 44, Child = new WindowDragArea() };
        behind.AddHandler(InputElement.PointerPressedEvent, (_, _) => reached.Add("pressed"), RoutingStrategies.Bubble);
        behind.AddHandler(InputElement.PointerReleasedEvent, (_, _) => reached.Add("released"), RoutingStrategies.Bubble);
        var window = Show(TopLeft(behind));

        Click(window, new Point(100, 22));

        Assert.Empty(reached);
        window.Close();
    });

    // Apple's WindowDragGesture documentation states this rationale for allowsWindowActivationEvents(true) - "so
    // that dragging the containing window works even when it's inactive" (ED-200) - so unlike the press-start test
    // above, this is a documented intent, not an open oracle question; it carries ordinary Req traits.
    [Fact]
    [Trait("Req", "PR-WIN-193")] // UI/Chrome/Chrome.swift:1091-1099 (B3): allowsWindowActivationEvents(true) - dragging works even from an inactive window
    [Trait("Req", "UI-WIN-005")] // UI/Chrome/Chrome.swift:1091-1099 (B3)
    public void An_inactive_window_s_activating_click_still_drags_through_the_region() => headless.OnUiThread(() =>
    {
        var reached = new List<string>();
        var behind = new Border { Width = 200, Height = 44, Child = new WindowDragArea() };
        behind.AddHandler(InputElement.PointerPressedEvent, (_, _) => reached.Add("pressed"), RoutingStrategies.Bubble);
        var window = Show(TopLeft(behind));
        // Headless never activates a window on Show(); this stands in for AppKit's inactive state (see
        // AttachmentPreviewPanelTests' SimulateDeactivate for the equivalent Deactivated-side technique).
        Assert.False(window.IsActive);

        Click(window, new Point(100, 22));

        Assert.Empty(reached); // the press is handled (and BeginMoveDrag reached) the same as from an active window
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-194")] // UI/Chrome/Chrome.swift:1120 acceptsFirstMouse true, :1123 mouseDownCanMoveWindow false (B3, was B2 :1123/:1126); ED-063: a client hit
    public void A_no_drag_region_takes_the_activating_click_refuses_the_window_drag_and_is_client_to_the_hit_test() => headless.OnUiThread(() =>
    {
        var area = new NoWindowDragArea();
        var view = Assert.IsType<NoWindowDragView>(area.Child);

        Assert.True(view.AcceptsFirstMouse(null));
        Assert.False(view.MouseDownCanMoveWindow);
        Assert.Equal(Win32Properties.Win32HitTestValue.Client, Win32Properties.GetNonClientHitTestResult(view));
        Assert.IsType<NoWindowDragView>(area.MakeNSView());
    });

    [Fact]
    [Trait("Req", "PR-WIN-194")] // UI/Chrome/Chrome.swift:1128-1136 (B3, was B2 :1131-1139): mouseDown, mouseDragged, mouseUp, rightMouseDown, rightMouseUp each go to the superview
    public void A_no_drag_region_takes_the_hit_and_passes_all_five_mouse_events_on_to_its_parent() => headless.OnUiThread(() =>
    {
        var reached = new List<string>();
        var parent = new Border { Width = 120, Height = 32, Child = new NoWindowDragArea() };
        parent.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            Assert.IsType<NoWindowDragView>(e.Source);
            reached.Add("down " + e.GetCurrentPoint(parent).Properties.PointerUpdateKind);
        }, RoutingStrategies.Bubble);
        parent.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(parent).Properties.IsLeftButtonPressed) reached.Add("dragged");
        }, RoutingStrategies.Bubble);
        parent.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            Assert.IsType<NoWindowDragView>(e.Source);
            reached.Add("up " + e.GetCurrentPoint(parent).Properties.PointerUpdateKind);
        }, RoutingStrategies.Bubble);
        var window = Show(TopLeft(parent));

        window.MouseDown(new Point(20, 16), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(60, 16), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(60, 16), MouseButton.Left, RawInputModifiers.None);
        window.MouseDown(new Point(20, 16), MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(new Point(20, 16), MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(
            ["down LeftButtonPressed", "dragged", "up LeftButtonReleased", "down RightButtonPressed", "up RightButtonReleased"],
            reached);
        window.Close();
    });

    // ---- ChromeCapsule, :308-322 ----

    [Fact]
    [Trait("Req", "PR-WIN-181")] // UI/Chrome/Chrome.swift:315-316 .padding(.horizontal, 3).padding(.vertical, 2), with :34 and :36
    [Trait("Req", "UI-WIN-036")] // UI/Chrome/Chrome.swift:308-322 chrome capsule row zero spacing, padding
    public void A_chrome_capsule_insets_its_content_3_points_across_and_2_points_down() => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(new Size(56, 28));
        var capsule = new ChromeCapsule(content: () => content);
        var window = Show(TopLeft(capsule));

        Assert.Equal(new Size(56 + 3 * 2, 28 + 2 * 2), capsule.Bounds.Size);
        Assert.Equal(new Rect(3, 2, 56, 28), BoundsIn(content, capsule));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-181")] // UI/Chrome/Chrome.swift:318-320 `.background { NoWindowDragArea() }`: presses on the capsule never drag the window
    public void A_chrome_capsule_has_a_no_drag_region_behind_its_whole_surface() => headless.OnUiThread(() =>
    {
        var content = new LayoutProbe(new Size(56, 28));
        var capsule = new ChromeCapsule(content: () => content);
        var window = Show(TopLeft(capsule));

        var region = Assert.Single(VisualQuery.Descendants<NoWindowDragView>(capsule));
        Assert.Equal(new Rect(0, 0, 62, 32), BoundsIn(region, capsule));
        // In the inset, where no control claims the press, the region takes the hit.
        Assert.IsType<NoWindowDragView>(window.InputHitTest(new Point(1, 1)));
        window.Close();
    });

    // ---- ChromeDivider, :324-331 ----

    [Fact]
    [Trait("Req", "PR-WIN-182")] // UI/Chrome/Chrome.swift:328-329 .frame(width: 1, height: Chrome.dividerHeight).accessibilityHidden(true), with :37
    [Trait("Req", "UI-WIN-037")] // UI/Chrome/Chrome.swift:324-331 chrome divider 1x14 radius 0.5
    public void A_chrome_divider_is_1_by_14_points_and_hidden_from_assistive_technology() => headless.OnUiThread(() =>
    {
        var divider = new ChromeDivider();
        var window = Show(TopLeft(divider));

        Assert.Equal(new Size(1, 14), divider.Bounds.Size);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(divider));
        window.Close();
    });

    // ---- ChromeIconLabel, :333-358 ----

    [Fact]
    [Trait("Req", "PR-WIN-183")] // UI/Chrome/Chrome.swift:343 .frame(width: Chrome.capsuleContentHeight, height: Chrome.capsuleContentHeight), :351 .contentShape(.rect)
    [Trait("Req", "UI-WIN-038")] // UI/Chrome/Chrome.swift:333-383 chrome icon button 28x28 hit area
    public void An_icon_label_is_a_28_point_square_that_takes_the_hit_everywhere() => headless.OnUiThread(() =>
    {
        var label = new ChromeIconLabel(symbol: "terminal");
        var window = Show(TopLeft(label));

        Assert.Equal(new Size(28, 28), label.Bounds.Size);
        foreach (var corner in new[] { new Point(0.5, 0.5), new Point(27.5, 0.5), new Point(0.5, 27.5), new Point(27.5, 27.5), new Point(14, 14) })
        {
            var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(corner));
            Assert.True(hit == label || label.IsVisualAncestorOf(hit));
        }
        window.Close();
    });

    [Theory]
    [Trait("Req", "PR-WIN-183")] // UI/Chrome/Chrome.swift:354-358: disabled 0.32, then active full, then hover full, else 0.92
    [Trait("Req", "UI-WIN-038")] // UI/Chrome/Chrome.swift:354-358 disabled/active/hover/rest precedence
    [InlineData(false, false, false, 0.32)]
    [InlineData(false, true, true, 0.32)]
    [InlineData(false, false, true, 0.32)]
    [InlineData(true, true, false, 1.0)]
    [InlineData(true, true, true, 1.0)]
    [InlineData(true, false, true, 1.0)]
    [InlineData(true, false, false, 0.92)]
    public void An_icon_label_foreground_is_disabled_first_then_active_then_hovered(bool isEnabled, bool isActive, bool isHovering, double opacity) => headless.OnUiThread(() =>
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var label = new ChromeIconLabel(symbol: "terminal", isEnabled: isEnabled, isActive: isActive, isHovering: isHovering);
            var window = Show(TopLeft(label), theme);

            AssertColor(Label(opacity, theme), Effective(TextElement.GetForeground(Styled(label))));
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "PR-WIN-183")] // UI/Chrome/Chrome.swift:344-350 `if isActive { Circle().fill(Chrome.overlay(0.12)).padding(1) }`
    public void An_active_icon_label_backs_its_symbol_with_a_circle_at_0_12_inset_one_point() => headless.OnUiThread(() =>
    {
        var active = new ChromeIconLabel(symbol: "terminal", isActive: true);
        var window = Show(TopLeft(active), ThemeVariant.Dark);

        var circle = Assert.Single(VisualQuery.Descendants<Ellipse>(active));
        Assert.Equal(new Rect(1, 1, 26, 26), BoundsIn(circle, active));
        AssertColor(Label(0.12, ThemeVariant.Dark), Effective(circle.Fill));
        window.Close();

        var inactive = new ChromeIconLabel(symbol: "terminal");
        window = Show(TopLeft(inactive), ThemeVariant.Dark);
        Assert.Empty(VisualQuery.Descendants<Ellipse>(inactive));
        window.Close();
    });

    // ---- ChromeIconButton, :361-383 ----

    [Fact]
    [Trait("Req", "PR-WIN-184")] // UI/Chrome/Chrome.swift:379-380 .help(help).accessibilityLabel(Text(help))
    public void An_icon_buttons_help_is_both_its_tooltip_and_its_accessible_name() => headless.OnUiThread(() =>
    {
        var control = new ChromeIconButton(symbol: "xmark", help: "Hide changes", action: () => { });
        var window = Show(TopLeft(control));

        var button = TheButton(control);
        Assert.Equal("Hide changes", ToolTip.GetTip(button));
        Assert.Equal("Hide changes", Peer(button).GetName());
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-184")] // UI/Chrome/Chrome.swift:371 Button(action: action), :375 .disabled(!isEnabled)
    public void An_icon_button_acts_once_per_click_and_not_at_all_while_disabled() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource()); // a click also hovers, and the hover animates (ED-117)
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var runs = 0;
        var enabled = new ChromeIconButton(symbol: "terminal", help: "Terminal", action: () => runs++);
        var window = Show(TopLeft(enabled));
        Click(window, Centre(enabled, window));
        Assert.Equal(1, runs);
        window.Close();

        var disabled = new ChromeIconButton(symbol: "terminal", help: "Terminal", action: () => runs++, isEnabled: false);
        window = Show(TopLeft(disabled));
        Click(window, Centre(disabled, window));
        Assert.Equal(1, runs);
        Assert.False(TheButton(disabled).IsEffectivelyEnabled);
        AssertColor(Label(0.32, window.ActualThemeVariant), Effective(TextElement.GetForeground(Styled(disabled))));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-184")] // UI/Chrome/Chrome.swift:376-378 onHover under withAnimation(Chrome.hover), feeding ChromeIconLabel's isHovering (:372)
    [Trait("Req", "PR-WIN-183")]
    public void Hovering_an_icon_button_brings_its_symbol_to_full_opacity_and_leaving_returns_it_to_0_92() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        var control = new ChromeIconButton(symbol: "terminal", help: "Terminal", action: () => { });
        var window = Show(TopLeft(control));
        var theme = window.ActualThemeVariant;
        AssertColor(Label(0.92, theme), Effective(TextElement.GetForeground(Styled(control))));

        window.MouseMove(Centre(control, window), RawInputModifiers.None);
        Frames(frames);
        AssertColor(Label(1, theme), Effective(TextElement.GetForeground(Styled(control))));

        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);
        AssertColor(Label(0.92, theme), Effective(TextElement.GetForeground(Styled(control))));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-184")] // UI/Chrome/Chrome.swift:98 `static var hover: Animation { .easeOut(duration: 0.1) }`
    public void The_hover_animation_is_an_ease_out_over_one_tenth_of_a_second()
    {
        Assert.Equal(Animation.EaseOut(0.1), Chrome.Hover);
        Assert.Equal(0.1, Chrome.Hover.Duration);
    }

    [Fact]
    [Trait("Req", "PR-WIN-184")] // UI/Chrome/Chrome.swift:381 `.background { NoWindowDragArea() }`
    public void An_icon_button_has_a_no_drag_region_behind_it() => headless.OnUiThread(() =>
    {
        var control = new ChromeIconButton(symbol: "terminal", help: "Terminal", action: () => { });
        var window = Show(TopLeft(control));

        var region = Assert.Single(VisualQuery.Descendants<NoWindowDragView>(control));
        Assert.Equal(new Rect(0, 0, 28, 28), BoundsIn(region, control));
        Assert.Equal(new Size(28, 28), control.Bounds.Size);
        window.Close();
    });

    // ---- ChromeMenuButton, :385-410 ----

    [Fact]
    [Trait("Req", "PR-WIN-185")] // UI/Chrome/Chrome.swift:394-397 isPresented.toggle(), ChromeIconLabel(isActive: isPresented); :405 .popover(isPresented:, arrowEdge: .bottom)
    [Trait("Req", "UI-WIN-039")] // UI/Chrome/Chrome.swift:385-410 chrome menu button open state
    public void Clicking_a_menu_button_presents_its_menu_below_it_and_shows_the_button_active_while_open() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var control = new ChromeMenuButton(symbol: "arrow.up.forward.app", help: "Open in another app", content: () => new Border());
        var window = Show(TopLeft(control));
        var theme = window.ActualThemeVariant;
        Assert.Empty(VisualQuery.Descendants<Ellipse>(control));

        Click(window, Centre(control, window));
        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);

        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.Same(TheButton(control), popup.PlacementTarget);
        Assert.True(popup.IsLightDismissEnabled);
        // Active while open, hovered or not: full opacity over the 0.12 backing circle.
        AssertColor(Label(1, theme), Effective(TextElement.GetForeground(Styled(control))));
        var circle = Assert.Single(VisualQuery.Descendants<Ellipse>(control));
        AssertColor(Label(0.12, theme), Effective(circle.Fill));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-185")] // UI/Chrome/Chrome.swift:395 `isPresented.toggle()`: a second click closes it
    public void Clicking_an_open_menu_button_again_closes_its_menu_and_the_button_returns_to_rest() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var control = new ChromeMenuButton(symbol: "ellipsis", help: "More", content: () => new Border());
        var window = Show(TopLeft(control));

        Click(window, Centre(control, window));
        Click(window, Centre(control, window));
        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);

        Assert.Equal(2, recorder.Calls.Count);
        Assert.Same(recorder.Calls[0].Popup, recorder.Calls[1].Popup);
        Assert.False(recorder.Calls[1].Presented);
        Assert.Empty(VisualQuery.Descendants<Ellipse>(control));
        AssertColor(Label(0.92, window.ActualThemeVariant), Effective(TextElement.GetForeground(Styled(control))));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-185")] // UI/Chrome/Chrome.swift:403-404 .help(help).accessibilityLabel(Text(help)), :408 the no-drag background
    public void A_menu_buttons_help_is_its_tooltip_and_accessible_name_and_it_never_drags_the_window() => headless.OnUiThread(() =>
    {
        var control = new ChromeMenuButton(symbol: "play", help: "Run a project script", content: () => new Border());
        var window = Show(TopLeft(control));

        var button = TheButton(control);
        Assert.Equal("Run a project script", ToolTip.GetTip(button));
        Assert.Equal("Run a project script", Peer(button).GetName());
        var region = Assert.Single(VisualQuery.Descendants<NoWindowDragView>(control));
        Assert.Equal(control.Bounds.Size, region.Bounds.Size);
        window.Close();
    });

    // ---- ChromeCircleButton, :412-437 ----

    [Fact]
    [Trait("Req", "PR-WIN-186")] // UI/Chrome/Chrome.swift:425 .frame(width: Chrome.capsuleHeight, height: Chrome.capsuleHeight), :38 capsuleHeight = 28 + 2 * 2
    [Trait("Req", "UI-WIN-040")] // UI/Chrome/Chrome.swift:412-437 chrome circle button 32x32
    public void A_circle_button_is_32_points_square_with_a_no_drag_region_behind_it() => headless.OnUiThread(() =>
    {
        Assert.Equal(32, Chrome.CapsuleHeight);
        var control = new ChromeCircleButton(symbol: "arrow.down", help: "Jump to latest", action: () => { });
        var window = Show(TopLeft(control));

        Assert.Equal(new Size(32, 32), control.Bounds.Size);
        var region = Assert.Single(VisualQuery.Descendants<NoWindowDragView>(control));
        Assert.Equal(new Rect(0, 0, 32, 32), BoundsIn(region, control));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-186")] // UI/Chrome/Chrome.swift:424 `.foregroundStyle(Chrome.primaryText.opacity(isHovering ? 1 : 0.92))`, :430-432 onHover
    public void A_circle_buttons_symbol_is_0_92_at_rest_and_full_when_hovered() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        var control = new ChromeCircleButton(symbol: "arrow.down", help: "Jump to latest", action: () => { });
        var window = Show(TopLeft(control), ThemeVariant.Dark);
        AssertColor(Label(0.92, ThemeVariant.Dark), Effective(TextElement.GetForeground(Styled(control))));

        window.MouseMove(Centre(control, window), RawInputModifiers.None);
        Frames(frames);
        AssertColor(Label(1, ThemeVariant.Dark), Effective(TextElement.GetForeground(Styled(control))));

        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);
        AssertColor(Label(0.92, ThemeVariant.Dark), Effective(TextElement.GetForeground(Styled(control))));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-186")] // UI/Chrome/Chrome.swift:421 Button(action:), :426 .contentShape(Circle()), :433-434 help and label
    public void A_circle_button_acts_inside_its_circle_but_not_in_the_corners_of_its_square() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource()); // a click also hovers, and the hover animates (ED-117)
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var runs = 0;
        var control = new ChromeCircleButton(symbol: "arrow.down", help: "Jump to latest", action: () => runs++);
        var window = Show(TopLeft(control));

        Click(window, new Point(1.5, 1.5)); // the square's corner, outside the circle
        Assert.Equal(0, runs);
        Click(window, new Point(16, 16));
        Assert.Equal(1, runs);
        Click(window, new Point(16, 1.5)); // on the circle's top edge
        Assert.Equal(2, runs);

        var button = TheButton(control);
        Assert.Equal("Jump to latest", ToolTip.GetTip(button));
        Assert.Equal("Jump to latest", Peer(button).GetName());
        window.Close();
    });

    // ---- ChromeCircleMenu, :439-470 ----

    [Fact]
    [Trait("Req", "PR-WIN-187")] // UI/Chrome/Chrome.swift:449-454 toggle and `isHovering || isPresented ? 1 : 0.92`, :465 .popover(arrowEdge: .bottom)
    [Trait("Req", "UI-WIN-041")] // UI/Chrome/Chrome.swift:439-470 chrome circle menu open state
    public void A_circle_menu_stays_fully_opaque_while_its_menu_is_open() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider()); // ED-117: animation spans are measured on SwiftClock
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var control = new ChromeCircleMenu(symbol: "hand.raised", help: "Supervised", content: () => new Border());
        var window = Show(TopLeft(control));
        var theme = window.ActualThemeVariant;
        Assert.Equal(new Size(32, 32), control.Bounds.Size);

        Click(window, new Point(16, 16));
        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);
        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.Same(TheButton(control), popup.PlacementTarget);
        AssertColor(Label(1, theme), Effective(TextElement.GetForeground(Styled(control))));

        Click(window, new Point(16, 16));
        window.MouseMove(Away, RawInputModifiers.None);
        Frames(frames);
        Assert.False(recorder.Calls[^1].Presented);
        AssertColor(Label(0.92, theme), Effective(TextElement.GetForeground(Styled(control))));

        var button = TheButton(control);
        Assert.Equal("Supervised", ToolTip.GetTip(button));
        Assert.Equal("Supervised", Peer(button).GetName());
        Assert.Single(VisualQuery.Descendants<NoWindowDragView>(control));
        window.Close();
    });
}
