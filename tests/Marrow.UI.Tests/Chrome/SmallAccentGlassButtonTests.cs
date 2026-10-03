using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;
using Xunit;
using SwiftChrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

namespace Marrow.UI.Tests.Chrome;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :826-833 (.buttonStyle(.glass).controlSize(.small)) with
// App/Window/WindowManager.swift:162,170-191 (the hosted window's .buttonBorderShape(.capsule) and .tint(theme accent)).
// The reference is the recorded Mac runtime, E:\Droppy\work\reference-video\1-settings-tour.mov (SOURCE FACT for what is on screen;
// OD-0042 "as the reference videos show them"), the Check now button at S 211.7-213.9 on the Monokai theme (accent F92672),
// 2560x1440 at 1 px = 1 pt, measured on the extracted frames: 20 pt tall, about 78 wide; fill (247,46,120) over a (52,59,60)
// backdrop; hover (254,68,141); press (254,79,189) with a violet glow under the pointer; disabled "Checking..." about 82 wide, fill
// (75,84,85) over (53,60,59). The port's material is its own (OD-0005) and its label is Inter (ED-059, OD-0009), so widths and
// colours are compared with a tolerance. Rendering needs the material, so this lives with the UI project's glass tests.
[Collection("Headless")]
public sealed class SmallAccentGlassButtonTests(HeadlessSession headless)
{
    private static readonly Color Backdrop = Color.FromRgb(52, 59, 60);

    private static void Drain()
    {
        for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    private static Color Pixel(Window window, int x, int y)
    {
        Drain();
        using var bitmap = window.CaptureRenderedFrame()!;
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        var p = (y * size.Width + x) * 4;
        var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        return Color.FromRgb(bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
    }

    private static void AssertNear(string what, Color actual, (int R, int G, int B) expected, int tolerance) =>
        Assert.True(Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance,
            $"{what}: ({actual.R},{actual.G},{actual.B}) is not within {tolerance} of ({expected.R},{expected.G},{expected.B})");

    // The button as built. Avalonia.Headless only composites the glass material for an owner that is a direct child of the window's
    // panel beside its backdrop (GlassMaterialTests uses the same arrangement; a glass border one panel deeper, as the button's own
    // panel makes it, shows no material here). So the button's own parts, its glass fill and its Button, are moved from that panel
    // to the window's panel, in the same order over the reference backdrop; nothing about them changes.
    private static (Control Button, Size Size, Window Window) Mount(string title, bool enabled)
    {
        var panel = Assert.IsType<Panel>(SmallAccentGlassButton.Create(title, () => { }, enabled));
        panel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        var host = new Window { Width = 300, Height = 100, UseLayoutRounding = false, RequestedThemeVariant = ThemeVariant.Dark, Content = new StackPanel { Children = { panel } } };
        host.Show(); Drain();
        var size = panel.Bounds.Size;
        var fill = panel.Children[0]; var button = panel.Children[1];
        host.Close(); host.Content = null; panel.Children.Clear();
        foreach (var part in new[] { fill, button })
        {
            part.Width = size.Width; part.Height = size.Height; part.Margin = new Thickness(10);
            part.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; part.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        }
        var window = new Window { Width = 300, Height = 100, UseLayoutRounding = false, RequestedThemeVariant = ThemeVariant.Dark,
            Content = new Panel { Children = { new SolidBackdrop(Backdrop), fill, button } } };
        window.Show(); Drain();
        return (button, size, window);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-071")] // App/Views/Settings/AboutUpdateViews.swift:830-831: the small glass button as the reference video draws it: 20 pt accent capsule, lighter on hover and press
    [Trait("Req", "UI-UPDATE-022")] // App/Views/Settings/AboutUpdateViews.swift:826-833: small accent glass (reference capture comparison)
    [Trait("Req", "PR-SETTINGS-240")] // App/Views/Settings/AboutUpdateViews.swift:826-833: the Check now control is the small glass button (reference capture comparison)
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:826-833: the row's control against the reference capture
    [Trait("Decision", "ED-059")] // Inter label widths against the reference's SF, compared with a tolerance
    public void The_enabled_button_is_the_reference_accent_capsule_lighter_on_hover_and_press() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch); using var timeScope = SwiftClock.Provider.Override(clock);
        void Advance(int milliseconds) { clock.Advance(TimeSpan.FromMilliseconds(milliseconds)); frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Dispatcher.UIThread.RunJobs(); }
        var previous = ThemeManager.Current;
        try
        {
            ThemeManager.Current = AppTheme.Monokai;
            var (button, size, window) = Mount("Check now", enabled: true);
            Assert.Equal(20, size.Height); // reference 20
            Assert.InRange(size.Width, 78 - 8, 78 + 8); // reference about 78
            var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(11, label.FontSize); Assert.Equal(FontWeight.Medium, label.FontWeight);
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); // the reference label is white on the accent
            var x = 10 + 5; var y = 10 + 10; var centre = new Point(10 + size.Width / 2, y);
            var rest = Pixel(window, x, y);
            AssertNear("rest", rest, (247, 46, 120), 14);
            window.MouseMove(centre); Advance(200); var hover = Pixel(window, x, y);
            AssertNear("hover", hover, (254, 68, 141), 14);
            Assert.True(hover.G > rest.G + 8 && hover.B > rest.B + 8, $"hover is lighter than rest: ({rest.R},{rest.G},{rest.B}) -> ({hover.R},{hover.G},{hover.B})");
            window.MouseDown(centre, MouseButton.Left); Advance(200); var pressed = Pixel(window, x, y);
            Assert.True(pressed.G >= hover.G + 12 && pressed.B >= hover.B + 8, $"press lights further (the wash steps from {SmallAccentGlassButton.HoverLight * 100:0} % to {SmallAccentGlassButton.PressLight * 100:0} % white: HoverLight and PressLight): ({hover.R},{hover.G},{hover.B}) -> ({pressed.R},{pressed.G},{pressed.B})"); // reference (254,79,189)
            // The reference's press frame also has a violet glow under the pointer (blue 189 against 141 on hover), which the port does not
            // draw: red and green are compared, blue only has to be lighter than on hover and not past the reference.
            Assert.True(Math.Abs(pressed.R - 254) <= 20 && Math.Abs(pressed.G - 79) <= 20 && pressed.B <= 189 + 10, $"press: ({pressed.R},{pressed.G},{pressed.B}) against the reference (254,79,189)");
            window.MouseUp(centre, MouseButton.Left); window.MouseMove(new Point(290, 90)); Advance(200);
            var gone = Pixel(window, x, y); AssertNear("after the pointer left", gone, (rest.R, rest.G, rest.B), 3); // the light goes with the pointer
            window.Close();
        }
        finally { ThemeManager.Current = previous; }
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-071")] // App/Views/Settings/AboutUpdateViews.swift:827,832: "Checking…" disabled: the reference's grey translucent capsule with a dim label
    [Trait("Req", "UI-UPDATE-022")] // App/Views/Settings/AboutUpdateViews.swift:827,832
    [Trait("Req", "PR-SETTINGS-240")] // App/Views/Settings/AboutUpdateViews.swift:827,832
    [Trait("Req", "PR-UPDATE-076")] // App/Views/Settings/AboutUpdateViews.swift:827,832
    [Trait("Decision", "ED-059")] // Inter label widths against the reference's SF, compared with a tolerance
    public void The_disabled_button_is_the_reference_grey_translucent_capsule_with_a_dim_label() => headless.OnUiThread(() =>
    {
        var previous = ThemeManager.Current;
        try
        {
            ThemeManager.Current = AppTheme.Monokai;
            var (button, size, window) = Mount("Checking…", enabled: false);
            Assert.Equal(20, size.Height); Assert.InRange(size.Width, 82 - 8, 82 + 8); // reference about 82
            var x = 10 + 5; var y = 10 + 10;
            AssertNear("disabled fill", Pixel(window, x, y), (75, 84, 85), 14);
            window.MouseMove(new Point(10 + size.Width / 2, y));
            AssertNear("a disabled capsule does not light under the pointer", Pixel(window, x, y), (75, 84, 85), 14);
            var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(SwiftChrome.PrimaryText.Opacity(0.3).Resolve(label.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); // reference label (123,133,136) over (75,84,85): about 0.3 of white
            window.Close();
        }
        finally { ThemeManager.Current = previous; }
    });
}
