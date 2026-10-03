using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageTestFirst;

namespace Marrow.App.Tests.Views.Settings;

// The glass action buttons of the MCP page (MCPSettingsPage.swift:324-330,341-348,352-358,503-528,698-702).
// SOURCE FACT (reference video 1-settings-tour.mov, stills scene-1-settings-tour-186.00 and -187.00, 2560x1440, 1 px = 1 pt):
// the Swift `.buttonStyle(.glass).controlSize(.small)` buttons render as accent-filled capsules 20 pt tall (the pink-pixel
// bounding box of "Connect" is 65x20 and of "Sign in" 56x20, median fill (248,49,122)) with a white label, because the window
// tints its content (WindowManager.swift:177,191 ThemeTint, restated for popovers by Popovers.swift:11). The same style is
// 22 pt tall at the regular control size (S 174.50 "Login" 58x22). OD-0098: the other states of the same style (Try again,
// Sign in again, the custom row's buttons, the prominent Fix it and the editor's Add server / Save) are proven from the
// source, which gives the same modifiers; the prominent fill and the regular label size are ENGINEERING DECISIONS.
[Collection("Headless")]
public sealed class MCPSettingsPageActionPillTests(HeadlessSession headless)
{
    // The pink accent of the video: the median pink pixel of the Connect pill.
    private static readonly SwiftColor VideoAccent = SwiftColor.Hex("F8317A");

    private static readonly MCPSetupGuide Guide = new("Turn it on", "The server is off.", new[] { "Step one" }, "Open page", "", null);

    // The body is ONE main-loop step: a press queues a probe that must never run (no real server, browser or CLI).
    private void OneStep(Action<AppModel> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        var ran = false;
        Step(runtime, () => { try { test(new AppModel()); Dispatcher.UIThread.RunJobs(); ran = true; } finally { MCPKeyPanel.Shared.Close(); MCPSetupPanel.Shared.Close(); } });
        Assert.True(ran, "the test body did not run");
    });

    private static Border Surface(Button button) => Nodes<Border>(button).First(border => SwiftGlass.GetEffect(border) is not null);

    // The window's pixels that count as the accent fill by the rule the video was measured with (R > 200, G < 120, B > 90),
    // within the button's rectangle: their bounding box.
    private static Rect PinkBox(Control button)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(button)!;
        var origin = Origin(button); var area = new Rect(origin, button.Bounds.Size).Inflate(3);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes]; int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = Math.Max(0, (int)area.Top); y < Math.Min(buffer.Size.Height, (int)Math.Ceiling(area.Bottom)); y++)
        {
            Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            for (var x = Math.Max(0, (int)area.Left); x < Math.Min(buffer.Size.Width, (int)Math.Ceiling(area.Right)); x++)
            {
                if (row[x * 4] > 200 && row[x * 4 + 1] < 120 && row[x * 4 + 2] > 90) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            }
        }
        return maxX < 0 ? default : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // The five card buttons and the four custom row buttons of the page, with the state each shows in.
    private static IEnumerable<(string Title, Func<Control> Make, MCPConnectionState State, string Id, bool Prominent)> Pills(AppModel model)
    {
        var oauth = MCPSettingsPageFixture.Entry("oauth-pill", new MCPTransport.Oauth("https://example.invalid"));
        var custom = Custom();
        yield return ("Connect", () => new MCPServerCard(Entry("plain-pill")), new MCPConnectionState.NotConnected(), "plain-pill", false); // :324-330
        yield return ("Sign in", () => new MCPServerCard(oauth), new MCPConnectionState.NotConnected(), "oauth-pill", false); // :328
        yield return ("Try again", () => new MCPServerCard(Entry("plain-pill")), new MCPConnectionState.Failed("Rejected"), "plain-pill", false); // :345-348
        yield return ("Sign in again", () => new MCPServerCard(oauth), new MCPConnectionState.Failed("Rejected"), "oauth-pill", false); // :346
        yield return ("Fix it", () => new MCPServerCard(Entry("plain-pill")), new MCPConnectionState.NeedsSetup(Guide), "plain-pill", true); // :341-344
        yield return ("Connect", () => new MCPCustomRow(custom), new MCPConnectionState.NotConnected(), custom.Id, false); // :503-507
        yield return ("Try again", () => new MCPCustomRow(custom), new MCPConnectionState.Failed("Rejected"), custom.Id, false); // :525-528
        yield return ("Fix it", () => new MCPCustomRow(custom), new MCPConnectionState.NeedsSetup(Guide), custom.Id, true); // :521-524
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // MCPSettingsPage.swift:324-358,503-528 with reference video S 186.00-187.00; partial proof of UI-MCP-024: the hover fill and label weight are not asserted
    public void Glass_action_pills_are_20_point_accent_filled_capsules_with_white_labels_under_a_tinted_window()
    {
        OneStep(model =>
        {
            foreach (var (title, make, state, id, prominent) in Pills(model))
            {
                State(model, id, state); var owner = make(); Popovers.SetTint(owner, VideoAccent); using var window = Attach(owner, model); Layout(owner);
                var button = Button(owner, title); var surface = Surface(button); var label = Nodes<TextBlock>(button).Single();
                // SOURCE FACT: .controlSize(.small) draws the capsule 20 pt tall (video: Connect 65x20, Sign in 56x20).
                Assert.True(Math.Abs(surface.Bounds.Height - 20) < 0.01, title + " in " + state.GetType().Name + " is " + surface.Bounds.Height + " tall");
                // SOURCE FACT: the window tint fills the capsule at full strength (video median (248,49,122)) and the label is white.
                Colour(surface.Background, VideoAccent, surface); Colour(label.Foreground, SwiftColor.White, label);
                Assert.Equal(SwiftColor.White.Resolve(Avalonia.Styling.ThemeVariant.Dark), ((ISolidColorBrush)label.Foreground!).Color);
                // 11 pt label and 12 pt each side: Connect is its text plus 24 (65 in the video), and the whole capsule is the button.
                Assert.Equal(11, label.FontSize); Assert.Equal(label.Bounds.Width + 24, surface.Bounds.Width, 0.5); Assert.Equal(surface.Bounds.Size, button.Bounds.Size);
                // The effect data stays interactive glass in a capsule; the prominent style is tinted by the accent.
                var effect = SwiftGlass.GetEffect(surface)!; Assert.True(effect.IsInteractive); Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind);
                Assert.Equal(prominent ? VideoAccent : null, effect.TintColor);
                // The rendered frame, measured as the video was: the pink bounding box is the capsule, 20 tall.
                var box = PinkBox(button); Assert.True(Math.Abs(box.Height - 20) <= 1, title + " draws " + box.Width + "x" + box.Height); Assert.True(Math.Abs(box.Width - surface.Bounds.Width) <= 2, title + " draws " + box.Width + " wide");
            }
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // MCPSettingsPage.swift:324-330 under a nil tint (System, Light and Dark themes, Popovers.swift:11); UI-MCP-024 partial
    public void Glass_action_pills_without_a_window_tint_stay_neutral_glass_at_the_same_20_points()
    {
        OneStep(model =>
        {
            foreach (var (title, make, state, id, prominent) in Pills(model).Where(p => !p.Prominent))
            {
                State(model, id, state); var owner = make(); using var window = Attach(owner, model); Layout(owner);
                var button = Button(owner, title); var surface = Surface(button); var label = Nodes<TextBlock>(button).Single();
                // ENGINEERING DECISION (OD-0098): with no tint .glass is the system's neutral glass, a label in the primary colour.
                Assert.True(Math.Abs(surface.Bounds.Height - 20) < 0.01, title + " is " + surface.Bounds.Height + " tall");
                Assert.Equal(0, ((ISolidColorBrush)surface.Background!).Color.A); Colour(label.Foreground, Chrome.PrimaryText, label); Assert.Null(SwiftGlass.GetEffect(surface)!.TintColor);
            }
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // MCPSettingsPage.swift:698-702 (.glassProminent at the default control size); UI-MCP-063 partial, the prominent look is an ENGINEERING DECISION
    public void The_editor_save_button_is_a_regular_size_accent_filled_prominent_capsule()
    {
        OneStep(model =>
        {
            foreach (var server in new MCPCustomServer?[] { null, Custom(1) })
            {
                var editor = new MCPCustomEditor(server); Popovers.SetTint(editor, VideoAccent); using var window = Attach(editor, model); Layout(editor);
                var save = Button(editor, server is null ? "Add server" : "Save"); var surface = Surface(save); var label = Nodes<TextBlock>(save).Single();
                // SOURCE FACT: the regular .glass button is 22 pt tall (video S 174.50, Login 58x22); its label is the regular 13 pt.
                Assert.True(Math.Abs(surface.Bounds.Height - 22) < 0.01, "Save is " + surface.Bounds.Height + " tall"); Assert.Equal(13, label.FontSize);
                Colour(surface.Background, VideoAccent, surface); Colour(label.Foreground, SwiftColor.White, label); Assert.Equal(VideoAccent, SwiftGlass.GetEffect(surface)!.TintColor);
                Assert.Equal(label.Bounds.Width + 24, surface.Bounds.Width, 0.5);
            }
        });
    }
}
