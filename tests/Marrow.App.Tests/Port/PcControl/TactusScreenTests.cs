// PORT-ONLY: Tactus, the pill on screen (ED-NEW-pcctl-32; DESIGN.md 4.5, 10.1, 10.2, 10.3, 10.4, 10.6, 10.10). The engine's
// state reaches the pill; the pill's buttons and rows run the pause and stop paths; "Answer Tactus" opens the card on the
// waiting ask and puts the keyboard in it, and while the card holds it (and 300 ms after it hands it back) a borrow waits;
// with "Answer approvals and questions in the pill" off a click opens the ask's chat; the pause chord's problem shows once;
// Settings moves the pill, and a drag saves its anchor and monitor; the Stopped state is pulled again when it ends. Fake
// bands, fake Win32 calls, a fake hotkey thread, a manual clock; no input.
using Avalonia.Threading;
using Marrow.App.Port.PcControl;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-32")]
public sealed class TactusScreenTests(HeadlessSession headless)
{
    private const string Notepad = "exe:c:\\windows\\notepad.exe";

    private static void Pass(PcScreenRig rig, double seconds)
    {
        rig.Pill.Clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static PcPendingAsk Ask(PcScreenRig rig) =>
        rig.Assembly.Asks.Add(new PcAskDraft(PcAskKind.Approval, "chat-a", new PcApprovalAsk(PcCapability.Act, false, true)) { App = Notepad, AppName = "Notepad" });

    [Fact]
    public void A_chat_working_in_an_app_shows_the_pill_with_its_step() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        Assert.Null(rig.Pill.Host.Pill(PcTestMonitors.Main.Id));

        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        rig.Screen.Step(new PcAuditEntry(DateTimeOffset.UnixEpoch, PcToolSchemas.Click, "notepad.exe", "Untitled", "Save", "button", "ok", 9));
        var pill = rig.Pill.Host.Pill(PcTestMonitors.Main.Id);

        Assert.NotNull(pill);
        Assert.Contains(PcTestMonitors.Main.Id, rig.Pill.Overlays.Monitors);
        Assert.Equal("Using Notepad", pill.View!.Title);
        Assert.Equal("Notepad: Clicked Save", pill.View.Line);
    });

    [Fact]
    public void The_pill_s_buttons_and_rows_run_the_pause_and_stop_paths() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        var stops = new List<(PcScope, PcStopSource)>();
        rig.Assembly.Control.Stopped += (scope, source) => stops.Add((scope, source));
        var host = rig.Pill.Host;

        host.Pressed(new PillCommand(PillCommandKind.PauseApp, Notepad));
        Assert.Equal(PcPauseSource.Row, rig.Assembly.Pause.Status(Notepad)?.Source);
        host.Pressed(new PillCommand(PillCommandKind.ResumeApp, Notepad));
        Assert.False(rig.Assembly.Pause.IsPaused(Notepad));
        host.Pressed(new PillCommand(PillCommandKind.PauseAll));
        Assert.True(rig.Assembly.Pause.Latched);
        Assert.Equal(PcPauseSource.Pill, rig.Assembly.Pause.Status(Notepad)?.Source);
        host.Pressed(new PillCommand(PillCommandKind.ResumeAll));
        Assert.False(rig.Assembly.Pause.Latched);
        Assert.False(rig.Assembly.Pause.IsPaused(Notepad));
        host.Pressed(new PillCommand(PillCommandKind.StopApp, Notepad));
        host.Pressed(new PillCommand(PillCommandKind.Stop));

        Assert.Equal(new[] { (PcScope.App(Notepad), PcStopSource.Pill), (PcScope.All, PcStopSource.Pill) }, stops);
        Assert.Equal(1, rig.Parts.InterruptCalls);
        Assert.Equal(PcStopPath.PillStop, rig.Assembly.Stop.Last?.Path);
    });

    [Fact]
    public void Answer_Tactus_opens_the_card_on_the_waiting_ask_and_a_borrow_waits_while_it_holds_the_keyboard() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());

        // nothing waits: the chord does nothing
        rig.Screen.Answer();
        Assert.False(rig.Pill.Host.CardOpen);
        Assert.Equal(0, rig.KeyboardCalls);

        var ask = Ask(rig);
        rig.Screen.Answer();
        Assert.True(rig.Pill.Host.CardOpen);
        Assert.Equal(ask.Id, rig.Pill.Host.OpenAsk);
        Assert.Equal(1, rig.KeyboardCalls);
        Assert.True(rig.Focus.HasKeyboard);
        Assert.True(rig.Screen.CardHasKeyboard);

        // Later folds the card and hands the keyboard back; a borrow still waits 300 ms
        rig.Pill.Host.Pressed(new PillCommand(PillCommandKind.Later));
        Assert.False(rig.Pill.Host.CardOpen);
        Assert.False(rig.Focus.HasKeyboard);
        Assert.Equal(PcScreenRig.Word, rig.Native.Front);
        Assert.True(rig.Screen.CardHasKeyboard);
        rig.Pill.Clock.Advance(TimeSpan.FromSeconds(CardFocus.BorrowWaitSeconds));
        Assert.False(rig.Screen.CardHasKeyboard);
    });

    [Fact]
    public void With_answering_in_the_pill_off_the_card_is_not_offered_and_a_click_opens_the_ask_s_chat() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig(PcSettings.Default with { Enabled = true, AnswerInPill = false });
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        Ask(rig);
        Pass(rig, 2);
        Assert.False(rig.Pill.Host.CardOpen);

        rig.Pill.Host.Pressed(new PillCommand(PillCommandKind.Click));

        Assert.False(rig.Pill.Host.CardOpen);
        Assert.Equal(new[] { ("chat-a", (string?)null) }, rig.Chats);
    });

    [Fact]
    public void A_pause_key_that_does_not_work_shows_once_and_its_Settings_opens_Settings() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        rig.KeysHost.Refuse(TactusHotkeys.PauseId);
        Assert.True(rig.Hotkeys.Arm());

        var notice = Assert.Single(rig.Feed.Facts().Notices);
        Assert.Equal(PillNoticeKind.PauseKeyTaken, notice.Kind);
        Assert.Equal("Pause key is taken by another app", rig.Pill.Host.Pill(PcTestMonitors.Main.Id)!.View!.Title);
        Assert.Equal("", rig.Feed.Facts().PauseChord);
        rig.Pill.Host.Pressed(new PillCommand(PillCommandKind.Settings));

        Assert.Equal(1, rig.SettingsOpened);
        Assert.Empty(rig.Feed.Facts().Notices);
    });

    [Fact]
    public void Settings_moves_the_pill_and_a_release_saves_its_anchor_and_its_monitor() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        Assert.Equal(PillAnchor.Centre, rig.Pill.Host.Anchor);
        Assert.Equal(PillShowOn.LatestStep, rig.Pill.Screens.ShowOn);

        rig.Parts.Settings = rig.Parts.Settings with { PillAnchor = "right", PillMonitor = "main" };
        rig.Screen.SettingsChanged();
        Assert.Equal(PillAnchor.Right, rig.Pill.Host.Anchor);
        Assert.Equal(PillShowOn.Main, rig.Pill.Screens.ShowOn);

        var pill = rig.Pill.Host.Pill(PcTestMonitors.Main.Id)!;
        Pass(rig, Math.Max(0, pill.Motion.DoneAt() - rig.Pill.Now) + 0.1);
        pill.Place(new PillVector(150, 8), 0.5);
        rig.Pill.Host.Released(rig.Pill.Host.PairOn(PcTestMonitors.Main.Id)!, new PillVector(150, 30), new PillVector(150, 8), PillVector.Zero);
        Assert.Equal("left", Assert.Single(rig.Saved).PillAnchor);

        rig.Pill.Host.Released(rig.Pill.Host.PairOn(PcTestMonitors.Main.Id)!, new PillVector(2000, 100), new PillVector(2000, 8), PillVector.Zero);
        var saved = rig.Saved[^1];
        Assert.Equal(PcTestMonitors.Right.Id, saved.PillMonitor);
        Assert.Equal(PcTestMonitors.Right.Name, saved.PillMonitorName);
        // the anchor saved is the one the pill rests at (a release on another monitor picks its anchor there too)
        Assert.Equal(rig.Pill.Host.Anchor switch { PillAnchor.Left => "left", PillAnchor.Right => "right", _ => "centre" }, saved.PillAnchor);
        Assert.Equal(PillShowOn.Chosen, rig.Pill.Screens.ShowOn);
        Assert.Equal(PcTestMonitors.Right.Id, rig.Pill.Screens.ChosenId);
    });

    [Fact]
    public void The_Stopped_state_is_pulled_again_when_its_2_s_end() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());

        rig.Assembly.Stop.StopKey();
        var (seconds, pull) = Assert.Single(rig.Later);
        Assert.Equal(PillStop.Seconds + TactusScreen.ExpiryMargin, seconds, 6);
        Assert.Equal("Stopped", rig.Pill.Host.Pill(PcTestMonitors.Main.Id)!.View!.Title);

        Pass(rig, seconds);
        pull();
        Assert.Null(rig.Feed.Facts().Stopped);
        Assert.Single(rig.Later);
    });
}
