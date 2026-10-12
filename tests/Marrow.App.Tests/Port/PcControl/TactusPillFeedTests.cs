// PORT-ONLY: Tactus (ED-NEW-pcctl-32; DESIGN.md 4.5 "States", 10.1, 10.2, 10.4, 10.5). What the pill says, from the shared
// parts on fakes: the apps the chats used this turn (first seen first) with their names, steps and pause phases; the app of
// the latest step; the broker's asks; Stopped for 2 s after a stop of everything and "Stopped by you" on a stopped app's
// row until a new step runs there; "<app> opened <title>" for 2 s; "Pause key is taken" for 4 s; the pause chord left out
// while it does not work. No window, no input.
using Marrow.App.Port.PcControl;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-32")]
public sealed class TactusPillFeedTests
{
    private const string Notepad = "exe:c:\\windows\\notepad.exe";
    private const string Excel = "exe:c:\\office\\excel.exe";

    private double now = 10;

    [Fact]
    public void The_apps_the_chats_used_this_turn_show_working_with_their_names_and_latest_steps()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now);
        var changes = 0;
        feed.Changed += () => changes++;

        using var notepad = assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        using var excel = assembly.Pause.Join("chat-b", Excel, new PcWaitState());
        feed.Step(Entry(PcToolSchemas.Click, "notepad.exe", "Save"));
        feed.Step(Entry(PcToolSchemas.Type, "EXCEL.EXE", "Subject") with { TextLength = 42 });
        var facts = feed.Facts();

        Assert.Equal(new[] { Notepad, Excel }, facts.Apps.Select(a => a.Key));
        Assert.Equal(new[] { "Notepad", "Excel" }, facts.Apps.Select(a => a.Name));
        Assert.Equal(new[] { "Clicked Save", "Typed 42 characters into Subject" }, facts.Apps.Select(a => a.Step));
        Assert.All(facts.Apps, a => Assert.Equal(PillAppPhase.Working, a.Phase));
        Assert.Equal(Excel, facts.LatestApp);
        Assert.Null(facts.Stopped);
        Assert.Empty(facts.Asks);
        Assert.Equal("Ctrl+Alt+Space", facts.PauseChord);
        Assert.Equal("Ctrl+Alt+Esc", facts.StopChord);
        Assert.True(changes >= 4, $"{changes} changes");
    }

    [Fact]
    public void A_pointer_aim_names_its_app_and_makes_it_the_latest()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now);
        using var notepad = assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        using var excel = assembly.Pause.Join("chat-a", Excel, new PcWaitState());
        feed.Step(Entry(PcToolSchemas.Click, "excel.exe", "Bold"));

        assembly.Pointer.Aim(new PcPointerAim(Notepad, "Notepad (Windows)", "c:\\windows\\notepad.exe", 0x1a, new PcPoint(40, 50), PcPointerAct.Press, true));
        var facts = feed.Facts();

        Assert.Equal(Notepad, facts.LatestApp);
        Assert.Equal("Notepad (Windows)", facts.Apps[0].Name);
        Assert.Equal("", facts.Apps[0].Step);
        Assert.Equal("Clicked Bold", facts.Apps[1].Step);
    }

    [Fact]
    public void A_paused_app_reads_Pausing_while_its_step_runs_then_Paused_and_Working_again_after_Resume()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now);
        using var notepad = assembly.Pause.Join("chat-a", Notepad, new PcWaitState());

        using (assembly.Pause.Acting(Notepad))
        {
            assembly.Pause.Pause(PcScope.App(Notepad), PcPauseSource.Pill);
            var pausing = Assert.Single(feed.Facts().Apps);
            Assert.Equal(PillAppPhase.Pausing, pausing.Phase);
            Assert.Equal(10, pausing.PhaseSince, 6);
        }
        Assert.Equal(PillAppPhase.Paused, Assert.Single(feed.Facts().Apps).Phase);
        assembly.Pause.Resume(PcScope.App(Notepad), PcPauseSource.Pill);

        Assert.Equal(PillAppPhase.Working, Assert.Single(feed.Facts().Apps).Phase);
    }

    [Fact]
    public void A_stop_of_everything_shows_Stopped_for_2_s_and_every_app_reads_Stopped_by_you()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now);
        using var notepad = assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        using var excel = assembly.Pause.Join("chat-b", Excel, new PcWaitState());

        assembly.Stop.EscapeThreeTimes();
        var facts = feed.Facts();

        Assert.Equal(new PillStop(10, EscThreeTimes: true), facts.Stopped);
        Assert.All(facts.Apps, a => Assert.Equal(PillAppPhase.Stopped, a.Phase));
        now = 10 + PillStop.Seconds;
        facts = feed.Facts();
        Assert.Null(facts.Stopped);
        Assert.All(facts.Apps, a => Assert.Equal(PillAppPhase.Stopped, a.Phase));
    }

    [Fact]
    public void A_row_stop_marks_only_that_app_until_a_new_step_runs_there()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now);
        using var notepad = assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        using var excel = assembly.Pause.Join("chat-a", Excel, new PcWaitState());

        assembly.Stop.PillRow(Notepad);
        var facts = feed.Facts();
        Assert.Null(facts.Stopped);
        Assert.Equal(new[] { PillAppPhase.Stopped, PillAppPhase.Working }, facts.Apps.Select(a => a.Phase));

        // the step the stop ended changes nothing; a new one runs there again
        feed.Step(Entry(PcToolSchemas.Click, "notepad.exe", "Save", "stopped_by_user"));
        Assert.Equal(PillAppPhase.Stopped, feed.Facts().Apps[0].Phase);
        Assert.Equal("", feed.Facts().Apps[0].Step);
        feed.Step(Entry(PcToolSchemas.Click, "notepad.exe", "Open"));
        facts = feed.Facts();
        Assert.Equal(PillAppPhase.Working, facts.Apps[0].Phase);
        Assert.Equal("Clicked Open", facts.Apps[0].Step);
    }

    [Fact]
    public void An_ask_about_an_app_is_carried_and_its_app_waits_on_it()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now);
        using var notepad = assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        using var excel = assembly.Pause.Join("chat-a", Excel, new PcWaitState());

        var ask = assembly.Asks.Add(new PcAskDraft(PcAskKind.Approval, "chat-a", new PcApprovalAsk(PcCapability.Act, false, true)) { App = Notepad, AppName = "Notepad" });
        var facts = feed.Facts();

        Assert.Equal(ask.Id, Assert.Single(facts.Asks).Id);
        Assert.Equal(new[] { true, false }, facts.Apps.Select(a => a.WaitsOnAsk));
    }

    [Fact]
    public void A_window_a_step_opened_shows_for_2_s()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        using var feed = new TactusPillFeed(assembly, cards, () => now);

        assembly.Control.Opened(new PcOpenedNotice(Excel, "Excel", "Save As"));

        Assert.Equal(PillNotice.OpenedWindow("Excel", "Save As", 10), Assert.Single(feed.Facts().Notices));
        now = 12;
        Assert.Empty(feed.Facts().Notices);
    }

    [Fact]
    public void Pause_key_is_taken_shows_for_4_s_or_until_Settings_and_the_pause_chord_is_left_out_while_it_does_not_work()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        var broken = true;
        using var feed = new TactusPillFeed(assembly, cards, () => now, () => broken);

        feed.PauseKeyTaken();
        var facts = feed.Facts();
        var notice = Assert.Single(facts.Notices);
        Assert.Equal(PillNoticeKind.PauseKeyTaken, notice.Kind);
        Assert.Equal("Pause key is taken by another app", notice.Title);
        Assert.Equal(TactusPillFeed.PauseKeyTakenSeconds, notice.Lasts);
        Assert.Equal("", facts.PauseChord);

        now += TactusPillFeed.PauseKeyTakenSeconds;
        facts = feed.Facts();
        Assert.Empty(facts.Notices);
        Assert.Equal("", facts.PauseChord);
        feed.PauseKeyTaken();
        feed.SettingsOpened();
        Assert.Empty(feed.Facts().Notices);
        broken = false;
        Assert.Equal("Ctrl+Alt+Space", feed.Facts().PauseChord);
    }

    [Fact]
    public void After_Dispose_the_feed_hears_nothing_more()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var cards = new PillAskBridge(assembly.Asks);
        var feed = new TactusPillFeed(assembly, cards, () => now);
        var changes = 0;
        feed.Changed += () => changes++;
        feed.Dispose();

        using var notepad = assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        assembly.Stop.PillStop();
        assembly.Control.Opened(new PcOpenedNotice(Excel, "Excel", "Save As"));

        Assert.Equal(0, changes);
    }

    private static PcAuditEntry Entry(string tool, string exe, string element, string result = "ok") =>
        new(DateTimeOffset.UnixEpoch, tool, exe, "Untitled", element, "button", result, 12);
}
