using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/NewThreadPrompt.swift (B3 52bc792), :13-55 (the body; the cited :536-576 in UI-CHAT-045 is past the end of the file).
// UI-CHAT-045 is proven by these tests together with NewThreadPromptTests (the three literals and the plain question, the picker toggle, the picker's
// rows) and TextScalingSitesTests (the 0.6 minimum scale); the capture comparison against the video is not made (no pixel reference for the
// port's own Marrow icon and Inter type, OD-0001, OD-0009, OD-0033).
[Collection("Headless")]
public sealed class NewThreadPromptGeometryTests(HeadlessSession headless)
{
    private static (AvaloniaWindow Window, NewThreadPrompt Prompt) Mount(AppModel model, string? projectName, ManualFrameSource frames)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var prompt = new NewThreadPrompt(Guid.NewGuid(), projectName);
        host.Child = prompt;
        var window = new AvaloniaWindow { Content = host, Width = 900, Height = 600 };
        using var scope = FrameClock.Source.Override(frames);
        window.Show();
        return (window, prompt);
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static Color Ink(Control on, SwiftColor expected) => expected.Resolve(on.ActualThemeVariant);

    private static Color Fill(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    [Trait("Req", "UI-CHAT-045")] // NewThreadPrompt.swift:16-48: a centred stack, the icon hidden from assistive technology, 40 points of horizontal padding, filling the pane; 28 regular, one line, primary text
    [Trait("Decision", "OD-0136")] // the icon is the 44 point nth tile, 14 points above the line (nth-brand.html .hero: .mk 44 px, gap 14 px)
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-CHAT-045: geometry, type and colour; the fade, the underline and the picker edge are the tests below
    public void The_prompt_fills_the_pane_with_a_centred_14_point_stack_the_44_point_nth_tile_and_28_point_regular_primary_text() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        AvaloniaWindow window = null!;
        NewThreadPrompt prompt = null!;
        harness.Main.OnMain(() => { (window, prompt) = Mount(model, "Marrow", frames); Settle(window); });
        harness.Main.Advance(TimeSpan.FromSeconds(0.4)); // the appearance (fade and scale from 0.97) has finished
        frames.Advance(TimeSpan.FromSeconds(0.4));
        harness.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Equal(HorizontalAlignment.Stretch, prompt.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Stretch, prompt.VerticalAlignment);
            Assert.Equal(new Rect(0, 0, 900, 600), new Rect(prompt.Bounds.Size)); // filling the pane

            var content = Assert.IsType<Border>(prompt.Child);
            Assert.Equal(new Thickness(40, 0), content.Padding);
            var column = Assert.IsType<StackPanel>(content.Child);
            Assert.Equal(Orientation.Vertical, column.Orientation);
            Assert.Equal(14, column.Spacing);
            Assert.Equal(HorizontalAlignment.Center, column.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, column.VerticalAlignment);
            // centred in the pane, both ways
            Assert.InRange(column.TranslatePoint(default, window)!.Value.X, (900 - column.Bounds.Width) / 2 - 0.51, (900 - column.Bounds.Width) / 2 + 0.51); // (layout rounding allows half a point)
            Assert.InRange(column.TranslatePoint(default, window)!.Value.Y, (600 - column.Bounds.Height) / 2 - 0.51, (600 - column.Bounds.Height) / 2 + 0.51);

            var icon = column.Children[0];
            MarrowLogoCheck.AssertIsTheNthTile(icon); // :17 Image(nsImage: NSApp.applicationIconImage); OD-0136: the nth tile B
            Assert.Equal(44, icon.Bounds.Width);
            Assert.Equal(44, icon.Bounds.Height);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(icon)); // :21 accessibilityHidden(true)

            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(prompt).Where(block => !string.IsNullOrEmpty(block.Text)).ToArray();
            Assert.Equal(["What should we build in ", "Marrow", "?"], texts.Select(block => block.Text ?? "").ToArray());
            Assert.All(texts, block =>
            {
                Assert.Equal(28, block.FontSize);
                Assert.Equal(FontWeight.Normal, block.FontWeight);
                Assert.Equal(1, block.MaxLines);
                Assert.Equal(Ink(block, Chrome.PrimaryText), Fill(block.Foreground));
            });
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-045")] // NewThreadPrompt.swift:28-34: the dotted underline is Chrome.secondaryText and rises to Chrome.primaryText while the pointer is over the name; the tooltip is exactly Choose another project
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-CHAT-045: the underline and its hover
    public void The_names_dotted_underline_is_secondary_text_and_primary_text_while_hovered() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        AvaloniaWindow window = null!;
        NewThreadPrompt prompt = null!;
        harness.Main.OnMain(() => { (window, prompt) = Mount(model, "Marrow", new ManualFrameSource()); Settle(window); });
        harness.Main.OnMain(() =>
        {
            var button = LogicalQuery.SelfAndDescendants<Button>(prompt).Single(b => (ToolTip.GetTip(b) as string) == "Choose another project");
            var underline = LogicalQuery.SelfAndDescendants<Line>(prompt).Single();
            Assert.Equal(1, underline.StrokeThickness);
            Assert.Equal([1d, 2d], underline.StrokeDashArray!.ToArray()); // a dotted line
            Assert.Equal(Ink(underline, Chrome.SecondaryText), Fill(underline.Stroke));

            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre, RawInputModifiers.None);
            Settle(window);
            Assert.Equal(Ink(underline, Chrome.PrimaryText), Fill(underline.Stroke));
            window.MouseMove(new Point(2, 2), RawInputModifiers.None);
            Settle(window);
            Assert.Equal(Ink(underline, Chrome.SecondaryText), Fill(underline.Stroke));
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-045")] // NewThreadPrompt.swift:35 arrowEdge: .bottom: the project picker opens on the name's bottom edge
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-CHAT-045: the picker's placement
    public void The_project_picker_opens_below_the_name() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        Button button = null!;
        harness.Main.OnMain(() =>
        {
            var (window, prompt) = Mount(model, "Marrow", new ManualFrameSource());
            Settle(window);
            button = LogicalQuery.SelfAndDescendants<Button>(prompt).Single(b => (ToolTip.GetTip(b) as string) == "Choose another project");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        harness.Main.OnMain(() =>
        {
            var (popup, presented) = Assert.Single(recorder.Calls);
            Assert.True(presented);
            Assert.Equal(PlacementMode.Bottom, popup.Placement);
            Assert.Same(button, popup.PlacementTarget);
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-045")] // NewThreadPrompt.swift:50-53: appears fading in and scaling from 0.97 over .easeOut(duration: 0.3)
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-CHAT-045: the appearance
    public void The_prompt_fades_in_and_scales_from_0_97_over_0_3_seconds_ease_out() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        Border content = null!;
        harness.Main.OnMain(() =>
        {
            var (window, prompt) = Mount(model, null, frames);
            Settle(window);
            content = Assert.IsType<Border>(prompt.Child);
            Assert.Equal(0, content.Opacity);
            Assert.Equal(0.97, Assert.IsType<ScaleTransform>(content.RenderTransform).ScaleX, 8);
            Assert.Equal(RelativePoint.Center, content.RenderTransformOrigin);
        });
        void Tick(double seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            harness.Main.Advance(span);
            frames.Advance(span);
            harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
        }
        Tick(0.15);
        harness.Main.OnMain(() =>
        {
            var progress = Animation.EaseOut(duration: 0.3).Progress(0.15);
            Assert.Equal(progress, content.Opacity, 6);
            var scale = Assert.IsType<ScaleTransform>(content.RenderTransform);
            Assert.Equal(0.97 + 0.03 * progress, scale.ScaleX, 6);
            Assert.Equal(scale.ScaleX, scale.ScaleY, 8);
        });
        Tick(0.2);
        harness.Main.OnMain(() =>
        {
            Assert.Equal(1, content.Opacity, 8);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(content.RenderTransform).ScaleX, 8);
        });
    });
}
