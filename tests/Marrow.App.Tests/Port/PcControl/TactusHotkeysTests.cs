using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-15; DESIGN.md 4.4, 10.1, 10.2, 10.11). The chords on a fake hotkey
// thread: the stop chord fails closed (no stop key, no Tactus), the pause chord fails open (Tactus runs and Settings
// says why); an injected pause press, auto-repeat and presses under 400 ms apart toggle nothing; the chord pauses and
// resumes; Esc three times stops, from a Tactus window, not through an IME; the stop key works while the UI is busy; the
// chords stay for the whole turn and go when Tactus is idle.
[Trait("Decision", "ED-NEW-pcctl-15")]
public sealed class TactusHotkeysTests
{
    private const string Excel = "exe:c:\\office\\excel.exe";

    [Fact]
    public void A_stop_chord_that_cannot_be_registered_fails_closed()
    {
        using var rig = new TactusHotkeysRig();
        rig.Host.Refuse(TactusHotkeys.StopId);

        Assert.False(rig.Hotkeys.Arm());
        Assert.False(rig.Hotkeys.Armed);
        Assert.Empty(rig.Host.Registered);
        Assert.False(rig.Host.Watching);
    }

    [Fact]
    public void A_stop_chord_set_to_a_single_key_fails_closed()
    {
        using var rig = new TactusHotkeysRig(new PcSettings { StopChord = "Esc" });

        Assert.False(rig.Hotkeys.Arm());
        Assert.Empty(rig.Host.Registered);
    }

    [Fact]
    public void A_pause_chord_that_cannot_be_registered_fails_open_and_says_so_once()
    {
        using var rig = new TactusHotkeysRig();
        rig.Host.Refuse(TactusHotkeys.PauseId);
        var problems = new List<PcChordProblem>();
        rig.Hotkeys.PauseKeyProblem += problems.Add;

        Assert.True(rig.Hotkeys.Arm());
        Assert.True(rig.Hotkeys.Arm());

        Assert.Equal(new[] { PcChordProblem.Clash }, problems);
        Assert.Equal(PcChordProblem.Clash, rig.Hotkeys.PauseProblem);
        Assert.Equal(new[] { TactusHotkeys.StopId }, rig.Host.Registered.Keys);
        Assert.Equal("Ctrl+Alt+Esc", rig.Hotkeys.Chord);
        Assert.True(rig.Host.Watching);
    }

    [Fact]
    public void A_pause_chord_that_types_on_a_layout_is_not_registered()
    {
        using var rig = new TactusHotkeysRig(new PcSettings { PauseChord = "Ctrl+Alt+E" });
        rig.Host.Types = chord => chord.Key == 'E';

        Assert.True(rig.Hotkeys.Arm());

        Assert.Equal(PcChordProblem.TypesCharacter, rig.Hotkeys.PauseProblem);
        Assert.False(rig.Host.Registered.ContainsKey(TactusHotkeys.PauseId));
    }

    [Fact]
    public void The_defaults_register_stop_and_pause_and_no_answer_key()
    {
        using var rig = new TactusHotkeysRig();

        Assert.True(rig.Hotkeys.Arm());

        var registered = rig.Host.Registered;
        Assert.Equal(PcHotkey.StopDefault, registered[TactusHotkeys.StopId]);
        Assert.Equal(PcHotkey.PauseDefault, registered[TactusHotkeys.PauseId]);
        Assert.False(registered.ContainsKey(TactusHotkeys.AnswerId));
        Assert.Null(rig.Hotkeys.PauseProblem);
    }

    [Fact]
    public void The_pause_chord_pauses_and_a_later_press_resumes()
    {
        using var rig = new TactusHotkeysRig();
        using var call = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        rig.Hotkeys.Arm();

        Assert.True(rig.Host.Press(TactusHotkeys.PauseId));
        Assert.True(rig.Pause.IsPaused(Excel));

        rig.Now += 500;
        Assert.True(rig.Host.Press(TactusHotkeys.PauseId));
        Assert.False(rig.Pause.IsPaused(Excel));
    }

    [Fact]
    public void Presses_under_400_ms_apart_toggle_once()
    {
        using var rig = new TactusHotkeysRig();
        using var call = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        rig.Hotkeys.Arm();

        rig.Host.Press(TactusHotkeys.PauseId);
        rig.Now += 150;
        rig.Host.Press(TactusHotkeys.PauseId);

        Assert.True(rig.Pause.IsPaused(Excel));
    }

    [Fact]
    public void An_injected_pause_press_toggles_nothing()
    {
        using var rig = new TactusHotkeysRig();
        using var call = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        rig.Hotkeys.Arm();

        rig.Host.Key(new PcHookKeyDown(PcHookKey.Chord, PcHotkey.Space, Repeat: false, Injected: true));
        rig.Host.Press(TactusHotkeys.PauseId);
        Assert.False(rig.Pause.IsPaused(Excel));

        rig.Now += 1_000;
        rig.Host.Key(new PcHookKeyDown(PcHookKey.Chord, PcHotkey.Space, Repeat: false, Injected: false));
        rig.Host.Press(TactusHotkeys.PauseId);
        Assert.True(rig.Pause.IsPaused(Excel));
    }

    [Fact]
    public void The_stop_key_stops_everything_even_while_paused_and_while_the_UI_is_busy()
    {
        using var rig = new TactusHotkeysRig();
        using var call = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        rig.Hotkeys.Arm();
        rig.Pause.Pause(PcScope.All, PcPauseSource.Pill);
        var ui = new Lock();

        bool finished;
        lock (ui) finished = rig.Host.Press(TactusHotkeys.StopId);

        Assert.True(finished);
        Assert.Equal(new[] { (PcScope.All, PcStopSource.StopKey) }, rig.Stops);
        Assert.False(rig.Pause.IsPaused(Excel));
        Assert.False(rig.Pause.Latched);
    }

    [Fact]
    public void Esc_three_times_from_a_Tactus_window_stops_with_the_notice()
    {
        using var rig = new TactusHotkeysRig();
        rig.Hotkeys.Arm();

        for (var i = 0; i < 3; i++)
        {
            rig.Now += 200;
            rig.Host.Key(new PcHookKeyDown(PcHookKey.Escape, PcHotkey.Escape, false, false));
        }

        Assert.Equal(new[] { (PcScope.All, PcStopSource.EscapeThreeTimes) }, rig.Stops);
        Assert.Equal(PcStopControl.EscapeNotice, rig.Stop.Last?.Notice);
    }

    [Fact]
    public void Esc_three_times_elsewhere_through_an_IME_or_injected_does_not_stop()
    {
        using var rig = new TactusHotkeysRig();
        rig.Hotkeys.Arm();
        void Escs(bool injected)
        {
            for (var i = 0; i < 3; i++)
            {
                rig.Now += 100;
                rig.Host.Key(new PcHookKeyDown(PcHookKey.Escape, PcHotkey.Escape, false, injected));
            }
        }

        rig.Host.Front = 0x99;
        Escs(injected: false);
        rig.Host.Front = 0x10;
        rig.Host.ImeIsOpen = true;
        Escs(injected: false);
        rig.Host.ImeIsOpen = false;
        Escs(injected: true);

        Assert.Empty(rig.Stops);
    }

    [Fact]
    public void The_chords_stay_for_the_turn_and_go_when_Tactus_is_idle()
    {
        using var rig = new TactusHotkeysRig();
        var call = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        rig.Hotkeys.Arm();

        call.Dispose();
        rig.Pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        rig.Pause.Resume(PcScope.App(Excel), PcPauseSource.Row);
        Assert.True(rig.Hotkeys.Armed);

        rig.Pause.EndTurn("chat-1");

        Assert.False(rig.Hotkeys.Armed);
        Assert.Empty(rig.Host.Registered);
        Assert.False(rig.Host.Watching);
    }

    [Fact]
    public void A_paused_app_keeps_the_chords_after_the_turn_ends()
    {
        using var rig = new TactusHotkeysRig();
        var call = rig.Pause.Join("chat-1", Excel, new PcWaitState());
        rig.Hotkeys.Arm();
        rig.Pause.Pause(PcScope.App(Excel), PcPauseSource.Row);
        call.Dispose();

        rig.Pause.EndTurn("chat-1");
        Assert.True(rig.Hotkeys.Armed);

        rig.Pause.Resume(PcScope.App(Excel), PcPauseSource.Row);
        rig.Pause.EndTurn("chat-1");
        Assert.False(rig.Hotkeys.Armed);
    }

    [Fact]
    public void An_answer_key_is_registered_when_set_and_runs_the_answer()
    {
        var answered = 0;
        using var rig = new TactusHotkeysRig(new PcSettings { AnswerChord = "Ctrl+Alt+A" }, () => answered++);

        rig.Hotkeys.Arm();
        rig.Host.Press(TactusHotkeys.AnswerId);

        Assert.Equal(new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 'A'), rig.Host.Registered[TactusHotkeys.AnswerId]);
        Assert.Equal(1, answered);
    }
}
