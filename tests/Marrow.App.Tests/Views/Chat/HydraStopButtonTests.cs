using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The stop button beside a working head in the heads popover (App/Views/Chat/HydraPanel.swift:343-347, :365-383): a 28-point
/// circle in the app tint holding a white filled stop mark at the chrome icon font, labelled "Stop {name}", present only while
/// the head runs and can be stopped.
/// </summary>
[Collection("Headless")]
public sealed class HydraStopButtonTests(HeadlessSession headless)
{
    private void Run(Action<HydraPanelContext, Func<ChatThread, HydraHeadRow>> test) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var context = new HydraPanelContext();
        var hosts = new List<Avalonia.Controls.Window>();
        try
        {
            test(context, head =>
            {
                var row = new HydraHeadRow(context.Model.Thread(head.Id)!, false, () => { });
                var window = new Avalonia.Controls.Window { Content = new Border { Child = row }, Width = 420, Height = 120 };
                hosts.Add(window);
                window.Show();
                context.Drain();
                return row;
            });
        }
        finally { foreach (var host in hosts) host.Close(); }
    });

    private static HydraStopButton[] Stops(HydraHeadRow row) => LogicalQuery.SelfAndDescendants<HydraStopButton>(row).ToArray();

    [Fact]
    [Trait("Req", "PR-HYDRA-176")] // HydraPanel.swift:372-381 Button { Image(systemName: "stop.fill").font(Chrome.iconFont).foregroundStyle(.white).frame(width: 28, height: 28).background(.tint, in: .circle) }
    [Trait("Req", "UI-HYDRA-016")] // UI-HYDRA-016: 28 by 28, a circle; the stop.fill symbol at Chrome.iconFont (callout, semibold); white on the app tint
    public void The_stop_button_is_a_28_point_tinted_circle_with_a_white_stop_mark_at_the_chrome_icon_font() => Run((c, makeRow) =>
    {
        c.MakeStoppable(c.First);
        var row = makeRow(c.First);
        var stop = Assert.Single(Stops(row));
        var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(stop));
        Assert.Equal(new Avalonia.Size(28, 28), button.Bounds.Size);
        Assert.Equal(28, button.Width);
        Assert.Equal(28, button.Height);
        Assert.Equal(new Avalonia.CornerRadius(14), button.CornerRadius);
        Assert.Equal(Chrome.Accent.Resolve(ThemeVariant.Light), Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        var mark = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(button));
        Assert.Equal("stop.fill", mark.SymbolName);
        Assert.Equal(Chrome.IconFont, SwiftImage.GetFont(mark));
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(mark));
        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(mark.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-176")] // HydraPanel.swift:380-381 .help("Stop \(name)") and .accessibilityLabel(Text("Stop \(name)"))
    [Trait("Req", "UI-HYDRA-016")] // UI-HYDRA-016 interactions: tooltip exactly "Stop {head display name}", labelled the same
    public void The_tooltip_and_the_label_are_stop_and_the_head_display_name() => Run((c, makeRow) =>
    {
        c.MakeStoppable(c.First);
        c.MakeStoppable(c.Second);
        foreach (var (head, name) in new[] { (c.First, "Hank"), (c.Second, "Walter") }) // Core/Models/Hydra.swift:232-233
        {
            var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(Assert.Single(Stops(makeRow(head)))));
            Assert.Equal($"Stop {name}", ToolTip.GetTip(button));
            Assert.Equal($"Stop {name}", Avalonia.Automation.AutomationProperties.GetName(button));
        }
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-176")] // HydraPanel.swift:344-346 HydraStopButton(name: info.displayName) { model.stopHydraHead(head.id) }
    [Trait("Req", "UI-HYDRA-016")] // UI-HYDRA-016 interactions: click stops that head
    public void Pressing_it_stops_that_head_and_no_other() => Run((c, makeRow) =>
    {
        c.MakeStoppable(c.First);
        c.MakeStoppable(c.Second);
        var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(Assert.Single(Stops(makeRow(c.Second)))));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        c.Drain();
        // The lead has no provider session, so the native head's stop is refused and its stop affordance goes (AppModel+Hydra.swift:926-931).
        Assert.False(c.Model.Thread(c.Second.Id)!.Hydra!.CanStop);
        Assert.True(c.Model.Thread(c.First.Id)!.Hydra!.CanStop);
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed)]
    [InlineData(HydraHeadInfo.Status.Failed)]
    [InlineData(HydraHeadInfo.Status.Stopped)]
    [Trait("Req", "UI-HYDRA-016")] // HydraPanel.swift:343 if info.status == .running, info.canStop: only a running head has the button
    public void A_head_that_is_not_running_has_no_stop_button(HydraHeadInfo.Status status) => Run((c, makeRow) =>
    {
        c.MakeStoppable(c.First);
        c.Status(c.First, status);
        Assert.Empty(Stops(makeRow(c.First)));
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-016")] // HydraPanel.swift:343 a running head the provider refused to stop (canStop cleared) loses the button
    public void A_running_head_that_cannot_be_stopped_has_no_stop_button() => Run((c, makeRow) =>
    {
        Assert.False(c.Model.Thread(c.First.Id)!.Hydra!.CanStop);
        Assert.Empty(Stops(makeRow(c.First)));
        c.MakeStoppable(c.First);
        Assert.Single(Stops(makeRow(c.First)));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-176")] // HydraPanel.swift:372-378 the circle is drawn: tint behind (.background(.tint, in: .circle)) and the white stop mark centred on it
    [Trait("Req", "UI-HYDRA-016")] // UI-HYDRA-016 colors: white symbol on the app tint
    public void The_circle_and_the_stop_mark_are_really_drawn() => Run((c, makeRow) =>
    {
        c.MakeStoppable(c.First);
        var stop = Assert.Single(Stops(makeRow(c.First)));
        var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(stop));
        using var bitmap = new RenderTargetBitmap(new PixelSize(28, 28));
        bitmap.Render(button);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(stream.ToArray());
        var tint = Chrome.Accent.Resolve(ThemeVariant.Light);
        // The tint fills the circle (a point near its top, inside it), and the corner outside it is clear.
        Assert.Equal(new SKColor(tint.R, tint.G, tint.B, tint.A), pixels.GetPixel(14, 3));
        Assert.Equal(0, pixels.GetPixel(0, 0).Alpha);
        Assert.Equal(0, pixels.GetPixel(27, 27).Alpha);
        // The white stop mark sits on the middle.
        Assert.Equal(new SKColor(255, 255, 255, 255), pixels.GetPixel(14, 14));
    });
}
