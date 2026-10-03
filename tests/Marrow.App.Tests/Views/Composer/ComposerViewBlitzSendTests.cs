using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :783-866 (the send and stop button): the blitz-composerview
// unit's proof for UI-COMPOSER-016.
public sealed partial class ComposerViewTests
{
    private static string QueueTail(string idleOrRunning) => SendShortcutText.Queue is { } queue ? " · " + queue + idleOrRunning : "";

    private sealed record SendLook(bool Enabled, string Symbol, string Name, string Tip, Color Fill, Color Ink);

    private static SendLook LookAt(Control send)
    {
        var button = One<Button>(send);
        var label = Assert.IsType<Border>(button.Content);
        var image = LogicalQuery.SelfAndDescendants<SwiftImage>(label).First();
        return new SendLook(button.IsEnabled, image.SymbolName!, AutomationProperties.GetName(button)!, (string)ToolTip.GetTip(button)!,
            Assert.IsAssignableFrom<ISolidColorBrush>(label.Background).Color, Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color);
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-016")] // App/Views/Composer/ComposerView.swift:817-866
    [Trait("Rule", "R-TEST-4")] // The 28 point circle, the 4 point margin, the focus and the first states: B3_send_is_28_circle_..., B3_stop_click_is_plain_interrupt_...; arrow to stop: SendSymbolReplaceTests; stop to arrow, the shrink and the grow from the bottom: CAPTURE-ONLY (the frames C 125.762-126.250 are not compared)
    public void The_send_button_reads_its_state_from_the_run_the_draft_and_the_revert_in_glyph_fill_ink_name_and_help()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var theme = Avalonia.Application.Current!.ActualThemeVariant;
            (Control Send, AvaloniaWindow Window) Make(bool running, bool heads, bool goesToHead, bool queueGoesToHead)
            {
                var send = h.Main.OnMain(() => new SendDraftState(runtime, running, heads, goesToHead, queueGoesToHead, () => { }));
                return (send, h.Main.OnMain(() => Show(send, h.App, 200)));
            }
            Color Accent(Control control) => (Popovers.GetTint(control) ?? SwiftColor.AccentColor).Resolve(control.ActualThemeVariant);
            var dim = SendDraftState.DisabledLabelOpacity;
            Assert.Equal(0.5, dim);

            // Idle with an empty draft: disabled, the faint quaternary disc, the arrow dimmed with it.
            var (idle, idleWindow) = Make(running: false, heads: false, goesToHead: false, queueGoesToHead: false);
            Pump(h, idleWindow);
            var look = h.Main.OnMain(() => LookAt(idle));
            Assert.False(look.Enabled);
            Assert.Equal("arrow.up", look.Symbol);
            Assert.Equal("Send", look.Name);
            Assert.Equal("Send (Return)" + QueueTail(" queues while running"), look.Tip);
            Assert.Equal(SwiftColor.Quaternary.Opacity(dim).Resolve(idle.ActualThemeVariant), look.Fill);
            Assert.Equal(SwiftColor.White.Opacity(dim).Resolve(idle.ActualThemeVariant), look.Ink);

            // The first typed character: enabled, the accent disc and the white arrow, reached over 0.12 s.
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "hello" });
            Pump(h, idleWindow);
            Assert.True(h.Main.OnMain(() => LookAt(idle).Enabled));
            h.Main.Advance(TimeSpan.FromSeconds(0.06));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.06)));
            var middle = h.Main.OnMain(() => LookAt(idle));
            var start = SwiftColor.Quaternary.Opacity(dim).Resolve(idle.ActualThemeVariant);
            var end = h.Main.OnMain(() => Accent(idle));
            Assert.True(middle.Fill != start && middle.Fill != end, $"mid-way between {start} and {end}: {middle.Fill}");
            Assert.True(middle.Ink.A > Math.Round(255 * dim) && middle.Ink.A < 255, $"the arrow brightens: {middle.Ink}");
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(1)));
            look = h.Main.OnMain(() => LookAt(idle));
            Assert.Equal(end, look.Fill); // .tint
            Assert.Equal(SwiftColor.White.Resolve(idle.ActualThemeVariant), look.Ink); // .white
            Close(h, idleWindow);

            // Running: stop.fill, enabled whatever the draft says, named Stop, the help promises what Return and the queue chord do.
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft());
            var (running, runningWindow) = Make(running: true, heads: false, goesToHead: false, queueGoesToHead: false);
            Pump(h, runningWindow);
            look = h.Main.OnMain(() => LookAt(running));
            Assert.True(look.Enabled);
            Assert.Equal("stop.fill", look.Symbol);
            Assert.Equal("Stop", look.Name);
            Assert.Equal("Stop" + SendShortcutText.Stop + " · Return steers" + QueueTail(" queues"), look.Tip);
            Assert.Equal(h.Main.OnMain(() => Accent(running)), look.Fill);
            // A take-back in flight disables it even while it runs, and the dimming goes with it.
            h.Main.OnMain(() => typeof(ThreadRuntime).GetProperty("IsReverting")!.SetValue(runtime, true));
            Pump(h, runningWindow);
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(1)));
            look = h.Main.OnMain(() => LookAt(running));
            Assert.False(look.Enabled);
            Assert.Equal("stop.fill", look.Symbol);
            Assert.Equal(h.Main.OnMain(() => (Popovers.GetTint(running) ?? SwiftColor.AccentColor).Opacity(dim).Resolve(running.ActualThemeVariant)), look.Fill);
            Close(h, runningWindow);
            h.Main.OnMain(() => typeof(ThreadRuntime).GetProperty("IsReverting")!.SetValue(runtime, false));

            // The other helps, and the name that goes to a head.
            foreach (var (isRunning, heads, goesToHead, queueGoesToHead, name, tip) in new (bool, bool, bool, bool, string, string)[]
            {
                (true, true, false, true, "Stop", "Heads are at work: your message goes to a head"),
                (true, true, false, false, "Stop", "Heads are at work: your message waits for the lead"),
                (false, false, true, false, "Send to a head", "Send to a head (Return) · the lead reports back" + QueueTail(" queues while running")),
                (false, true, false, false, "Send", "Send (Return)" + QueueTail(" queues until the heads report")),
            })
            {
                var (send, window) = Make(isRunning, heads, goesToHead, queueGoesToHead);
                Pump(h, window);
                var shown = h.Main.OnMain(() => LookAt(send));
                Assert.Equal(name, shown.Name);
                Assert.Equal(tip, shown.Tip);
                Close(h, window);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-016")] // App/Views/Composer/ComposerView.swift:828-829
    [Trait("Rule", "R-TEST-4")] // The state matrix: The_send_button_reads_its_state_...; a stop click: B3_stop_click_is_plain_interrupt_...
    public void An_idle_click_sends_the_draft_once_and_never_interrupts()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness();
            var sent = 0;
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = "go" });
            var send = h.Main.OnMain(() => new SendDraftState(h.Runtime, false, false, false, false, () => sent++));
            var window = h.Main.OnMain(() => Show(send, h.App, 200));
            Pump(h, window);
            h.Main.OnMain(() => Click(One<Button>(send)));
            Pump(h, window);
            Assert.Equal(1, sent);
            Assert.Equal("go", h.Runtime.Draft.Text); // the stand-in send leaves the draft; a click never takes anything back
            Close(h, window);
        });
    }
}
