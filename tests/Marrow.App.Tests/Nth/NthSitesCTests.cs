using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Nth;

// OD-0136 amendment 2, sites C: step 8 (the tool row's spinner, tick and shining label) and step 9 (the send button's face).
// Steps 2 B (the working line) and 5 (the diff skeleton) are proved in ThreadTimelineWorkingLineTests, ThreadTimelineKeepTests,
// AuroraDiffLoadingStateTests and DiffInspectorChromeTests, where the old visuals they replace were asserted.
[Collection("Headless")]
public sealed class NthSitesCTests(HeadlessSession headless)
{
    private static ToolRow ToolRowFor(ThreadRuntimePart12Harness h, TimelineEntry entry) => new(entry, h.Runtime, h.Project.Path);

    private static NthStatusState Status(NthStatusMark mark) => Assert.IsType<NthStatusState>(mark.Snapshot().State);

    // A plain tool row (no output, no diff): the icon slot, the label.
    private static TimelineEntry Running() => ToolEntry(Call(ToolCall.Kind.Search, "grep auth", ToolCall.Status.Running));

    [Fact]
    [Trait("Req", "OD-0136")] // step 8: running = the spinner in the status slot and the light through the label
    public void A_running_tool_row_has_the_working_mark_and_a_shining_label() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var row = ToolRowFor(h, Running());
            var window = Show(row);
            try
            {
                var mark = Assert.Single(Mounted<NthStatusMark>(row));
                Assert.Equal(NthStatus.Working, mark.Status);
                Assert.Equal(NthDoneMark.Tick, mark.DoneMark);
                Assert.Equal(14, mark.MarkSize);
                Assert.Empty(Mounted<ProgressBar>(row)); // the 10 x 10 indeterminate bar is gone
                var label = Assert.Single(Mounted<NthToolLabel>(row));
                Assert.True(label.IsShining);
                Assert.Equal(ToolPresentation.Label(Call(ToolCall.Kind.Search, "grep auth", ToolCall.Status.Running)), label.FullText);
                Assert.Equal(ChromeTokens.PrimaryText.Resolve(label.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(label.Foreground).Color); // --text while running
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "OD-0136")] // step 8: done = the spinner fades, the tick draws, the label steps to --text2 over 0.25 s, the light stops
    public void A_tool_row_that_completes_while_shown_draws_its_tick_and_stops_the_light() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource(); using var frames = FrameClock.Source.Override(clock);
        var entry = Running();
        NthStatusMark? shown = null; NthToolLabel? light = null; AvaloniaWindow? window = null; ToolRow? row = null;
        h.Main.OnMain(() =>
        {
            row = ToolRowFor(h, entry); window = Show(row);
            shown = Assert.Single(Mounted<NthStatusMark>(row)); light = Assert.Single(Mounted<NthToolLabel>(row));
            Assert.True(double.IsNegativeInfinity(Status(shown).ChangedAt) || Status(shown).Status == NthStatus.Working);
            var call = ((TimelineItem.Content.Tool)entry.Item.TimelineItemContent).Value;
            entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Tool(call with { ToolCallStatus = ToolCall.Status.Completed }) };
        });
        h.Main.OnMain(() =>
        {
            {
                Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport.Settle(window!);
                // the row built its body again, and took the same mark and the same label over: the change plays on them
                Assert.Same(shown, Assert.Single(Mounted<NthStatusMark>(row!)));
                Assert.Same(light, Assert.Single(Mounted<NthToolLabel>(row!)));
                Assert.Equal(NthStatus.Done, shown!.Status);
                var state = Status(shown);
                Assert.Equal(NthStatus.Working, state.Previous);
                Assert.True(state.ChangedAt > double.NegativeInfinity, "the tick draws in while the spinner fades");
                Assert.False(light!.IsShining);
            }
        });
        Animate(h.Main, clock, 400); // the label's colour steps over 0.25 s
        h.Main.OnMain(() =>
        {
            try { Assert.Equal(SwiftColor.Secondary.Resolve(light!.ActualThemeVariant), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(light.Foreground).Color); } // --text2
            finally { Close(window!); }
        });
    });

    [Fact]
    [Trait("Req", "OD-0136")] // step 8: a row that is already done shows its tick in place (scrolling, recycling) and no light at all
    public void A_tool_row_made_already_done_shows_the_tick_in_place_with_a_plain_label() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var row = ToolRowFor(h, ToolEntry(Call(ToolCall.Kind.Search, "grep auth")));
            var window = Show(row);
            try
            {
                var mark = Assert.Single(Mounted<NthStatusMark>(row));
                Assert.Equal(NthStatus.Done, mark.Status);
                Assert.True(double.IsNegativeInfinity(Status(mark).ChangedAt), "no replay of the tick");
                Assert.Empty(Mounted<NthToolLabel>(row));
                Assert.Single(Mounted<MiddleTruncatingTextBlock>(row));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "OD-0136")] // step 8: a failed or declined call keeps its own glyph
    public void A_failed_or_declined_tool_row_keeps_its_glyph_and_has_no_mark() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            foreach (var (status, symbol) in new[] { (ToolCall.Status.Failed, "xmark.circle.fill"), (ToolCall.Status.Declined, "hand.raised.slash") })
            {
                var icon = new ToolStatusIcon(Call(ToolCall.Kind.Command, "make", status));
                Assert.Equal(symbol, Assert.IsType<SwiftImage>(((Border)icon.Child!).Child).SymbolName);
            }
        });
    });

    [Fact]
    [Trait("Req", "OD-0136")] // step 9: the send button is the 32 px face; running follows the state, the face survives the run flipping
    public void The_send_button_is_the_32_px_face_whose_running_state_follows_the_run_on_the_same_face() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() =>
        {
            var face = new NthSendFace();
            var idle = new SendDraftState(h.Runtime, false, false, false, false, () => { }, face);
            var window = Show(idle, h.App, 200);
            try
            {
                var button = One<Button>(idle);
                var label = Assert.IsType<Border>(button.Content);
                Assert.Same(face, label.Child);
                Assert.Equal(32, NthSendFace.Size); Assert.Equal(32, label.Width); Assert.Equal(32, label.Height); Assert.Equal(32, face.Width); Assert.Equal(32, face.Height);
                Assert.False(face.IsRunning);
                // the run starts: the next button takes the same face, shown running with the change playing (it was on screen)
                var running = new SendDraftState(h.Runtime, true, false, false, false, () => { }, face);
                Assert.Null(label.Child);
                Assert.Same(face, Assert.IsType<Border>(One<Button>(running).Content).Child);
                Assert.True(face.IsRunning);
                Assert.True(Assert.IsType<NthSendState>(face.Snapshot().State).ChangedAt > double.NegativeInfinity);
                // pressed follows the pointer
                var pressedBy = One<Button>(running);
                typeof(Button).GetProperty(nameof(Button.IsPressed))!.SetValue(pressedBy, true);
                Assert.True(face.IsPressed);
                typeof(Button).GetProperty(nameof(Button.IsPressed))!.SetValue(pressedBy, false);
                Assert.False(face.IsPressed);
            }
            finally { Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport.Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "OD-0136")] // step 9: a button built running (a thread opened mid-run) shows the stop square in place
    public void A_send_button_built_running_shows_the_stop_square_without_animating() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() =>
        {
            var send = new SendDraftState(h.Runtime, true, false, false, false, () => { });
            var face = One<NthSendFace>(send);
            Assert.True(face.IsRunning);
            Assert.True(double.IsNegativeInfinity(Assert.IsType<NthSendState>(face.Snapshot().State).ChangedAt));
        });
    });

    [Fact]
    [Trait("Req", "OD-0136")] // step 9: disabled = the face at half (the site's DisabledLabelOpacity), enabled = whole
    public void The_send_face_dims_to_half_while_the_button_is_disabled() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() =>
        {
            var send = new SendDraftState(h.Runtime, false, false, false, false, () => { }); // an empty draft: disabled
            var face = One<NthSendFace>(send);
            Assert.False(One<Button>(send).IsEnabled);
            Assert.Equal(SendDraftState.DisabledLabelOpacity, face.Dim);
            Assert.Equal(0.5, face.Dim);
        });
    });
}
