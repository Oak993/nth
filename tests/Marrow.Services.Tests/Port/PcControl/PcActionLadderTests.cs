using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.6, 2.8; critique C6, C7, C16, C31).
// The action ladder on its own: the framework gate, the order of the ways (R1 patterns, R2 messages, R4 refusing until it
// is built), probe ways and what the capability store learned, the keyboard ladder (accelerator, dialog buttons, menu
// commands with their state, posted keys), text by SetValue, EM_REPLACESEL and WM_CHAR, and the clipboard refusals.
[Trait("Decision", "ED-NEW-pcctl-7")]
public sealed class PcActionLadderTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static (PcActionLadder Ladder, PcFakeDesktop Desktop, PcFakeApp App, PcTree Tree) Classic(bool realInput = false, Func<DateTimeOffset>? now = null)
    {
        var harness = new PcEngineHarness();
        var app = harness.Classic();
        var ladder = new PcActionLadder(
            [new PcUiaRung(harness.Desktop), new PcMessageRung(harness.Desktop), new PcBorrowRung(() => PcSettings.Default with { Enabled = true, RealInput = realInput })],
            new PcCapabilities(now ?? (() => Start)));
        return (ladder, harness.Desktop, app, TreeFormatter.Format(app.Root.Snapshot(true)));
    }

    private static PcInputRequest Request(PcInputKind kind, PcFakeApp app, PcTree tree, int element) =>
        new(kind, app.Info) { Tree = tree, Element = element == 0 ? null : tree.Elements[element] };

    private static string Ways(PcLadderPlan plan) => string.Join(", ", plan.Plans.Select(p => p.Rung + " " + p.Method + (p.Probe ? " (probe)" : "")));

    private static KeyChord Chord(string keys) => KeyChordParser.Parse(keys).Chord!;

    [Fact]
    public void The_framework_gate_reads_the_framework_id_then_the_class_and_the_exe()
    {
        var plain = new PcWindowInfo(1, "t", "FakeWindow", 1, "C:\\a\\app.exe");
        Assert.Equal(PcFramework.Win32, PcFrameworkGate.Of(plain, new PcUiNode("1", "button", "OK") { Framework = "Win32" }, null));
        Assert.Equal(PcFramework.WinForms, PcFrameworkGate.Of(plain, new PcUiNode("1", "button", "OK") { Framework = "Win32", ClassName = "WindowsForms10.BUTTON.app.0.1" }, null));
        Assert.Equal(PcFramework.WinForms, PcFrameworkGate.Of(plain, new PcUiNode("1", "button", "OK") { Framework = "WinForm" }, null));
        Assert.Equal(PcFramework.Chromium, PcFrameworkGate.Of(plain with { ClassName = "Chrome_WidgetWin_1" }, null, null));
        Assert.Equal(PcFramework.Office, PcFrameworkGate.Of(new PcWindowInfo(1, "t", "OpusApp", 1, "C:\\o\\WINWORD.EXE"), null, null));
        Assert.Equal(PcFramework.Qt, PcFrameworkGate.Of(plain with { ClassName = "Qt5QWindowIcon" }, null, null));
        Assert.Equal(PcFramework.Unknown, PcFrameworkGate.Of(plain, null, null));
        Assert.Equal(PcGate.Yes, PcFrameworkGate.Messages(PcFramework.Win32, PcMessageKind.Keys));
        Assert.Equal(PcGate.Yes, PcFrameworkGate.Messages(PcFramework.WinForms, PcMessageKind.Text));
        Assert.Equal(PcGate.Probe, PcFrameworkGate.Messages(PcFramework.WinForms, PcMessageKind.Keys));
        Assert.Equal(PcGate.Probe, PcFrameworkGate.Messages(PcFramework.Chromium, PcMessageKind.Click));
        Assert.Equal(PcGate.No, PcFrameworkGate.Messages(PcFramework.Chromium, PcMessageKind.Keys));
        Assert.Equal(PcGate.Probe, PcFrameworkGate.Messages(PcFramework.Qt, PcMessageKind.Text));
        foreach (var never in new[] { PcFramework.Wpf, PcFramework.Xaml, PcFramework.Office, PcFramework.Java, PcFramework.Unknown })
            Assert.Equal(PcGate.No, PcFrameworkGate.Messages(never, PcMessageKind.Click));
    }

    [Fact]
    public void A_click_takes_the_pattern_first_then_the_stock_button_message()
    {
        var (ladder, _, app, tree) = Classic();
        var plan = ladder.Plan(Request(PcInputKind.Click, app, tree, 3));
        Assert.Equal("R1 invoke, R2 bm_click", Ways(plan));
        Assert.False(plan.Refused);
    }

    [Fact]
    public void Inserting_into_a_stock_edit_takes_em_replacesel_before_set_value_and_wm_char_only_as_a_probe()
    {
        var (ladder, desktop, app, tree) = Classic();
        var insert = Request(PcInputKind.Type, app, tree, 2) with { Text = "abc" };
        Assert.Equal("R2 em_replacesel, R2 wm_char (probe), R1 set_value", Ways(ladder.Plan(insert)));
        // Replace mode never uses WM_CHAR, and the pattern goes first.
        Assert.Equal("R1 set_value, R2 em_replaceall", Ways(ladder.Plan(insert with { Insert = false })));
        // A physical modifier key down: no posted characters.
        Assert.DoesNotContain("wm_char", Ways(ladder.Plan(insert with { ModifiersDown = true })), StringComparison.Ordinal);
        // An ANSI window would turn Hindi and Urdu into question marks: no WM_CHAR there.
        desktop.AnsiControls.Add(app.Root.Children[0].NativeWindow);
        Assert.DoesNotContain("wm_char", Ways(ladder.Plan(insert)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_ways_really_write_into_the_control()
    {
        var (ladder, desktop, app, tree) = Classic();
        var plan = ladder.Plan(Request(PcInputKind.Type, app, tree, 2) with { Text = "\u0627\u0631\u062f\u0648" });
        Assert.True((await plan.Plans[0].Run(CancellationToken.None)).Done);
        Assert.Equal("\u0627\u0631\u062f\u0648", app.Root.Children[0].Value);
        Assert.True((await plan.Plans[1].Run(CancellationToken.None)).Done);
        Assert.Equal("\u0627\u0631\u062f\u0648\u0627\u0631\u062f\u0648", app.Root.Children[0].Value);
        Assert.Contains("em_replacesel \u0627\u0631\u062f\u0648", desktop.Log);
        Assert.True(plan.Plans[0].Writes);
    }

    [Fact]
    public void A_probe_way_is_promoted_only_after_it_passed_and_a_refused_way_stays_out_for_seven_days()
    {
        var now = Start;
        var (ladder, _, app, tree) = Classic(now: () => now);
        var insert = Request(PcInputKind.Type, app, tree, 2) with { Text = "x" };
        var wmChar = ladder.Plan(insert).Plans.Single(p => p.Method == "wm_char");
        Assert.True(wmChar.Probe);
        ladder.Capabilities.Passed(wmChar.Key!);
        Assert.False(ladder.Plan(insert).Plans.Single(p => p.Method == "wm_char").Probe);
        var replaceSel = ladder.Plan(insert).Plans[0];
        ladder.Capabilities.Refuse(replaceSel.Key!);
        // Passing later never lifts a refusal.
        ladder.Capabilities.Passed(replaceSel.Key!);
        Assert.Equal("R2 wm_char, R1 set_value", Ways(ladder.Plan(insert)));
        now = Start + PcCapabilities.RefusedFor - TimeSpan.FromMinutes(1);
        Assert.DoesNotContain("em_replacesel", Ways(ladder.Plan(insert)), StringComparison.Ordinal);
        now = Start + PcCapabilities.RefusedFor;
        Assert.StartsWith("R2 em_replacesel", Ways(ladder.Plan(insert)), StringComparison.Ordinal);
    }

    [Fact]
    public void Learning_is_kept_per_exe_version_rung_method_and_control_type()
    {
        var (ladder, _, app, tree) = Classic();
        var click = Request(PcInputKind.Click, app, tree, 3);
        var key = ladder.Plan(click).Plans[0].Key!;
        Assert.Equal(new PcCapabilityKey("legacy.exe", "1.0", PcRung.R1, "invoke", "button"), key);
        ladder.Capabilities.Refuse(key);
        Assert.Equal("R2 bm_click", Ways(ladder.Plan(click)));
        app.Version = "2.0";
        Assert.Equal("R1 invoke, R2 bm_click", Ways(ladder.Plan(click with { Window = app.Info })));
    }

    [Fact]
    public void A_focus_moving_way_is_left_out_while_the_user_works_in_the_window()
    {
        var (ladder, _, app, tree) = Classic();
        var click = Request(PcInputKind.Click, app, tree, 3);
        ladder.Capabilities.FocusMoving(ladder.Plan(click).Plans[0].Key!);
        Assert.Equal("R1 invoke, R2 bm_click", Ways(ladder.Plan(click)));
        Assert.Equal("R2 bm_click", Ways(ladder.Plan(click with { UserActive = true })));
    }

    [Fact]
    public void Learned_behaviour_survives_export_and_import()
    {
        var (ladder, _, app, tree) = Classic();
        var key = ladder.Plan(Request(PcInputKind.Click, app, tree, 3)).Plans[0].Key!;
        ladder.Capabilities.Refuse(key);
        var copy = new PcCapabilities(() => Start);
        copy.Import(ladder.Capabilities.Export());
        Assert.Equal(PcLearned.Refused, copy.State(key));
        copy.Import("not json");
        copy.Clear();
        Assert.Equal(PcLearned.Unknown, copy.State(key));
    }

    [Fact]
    public void When_no_way_is_left_the_answer_is_needs_real_input_with_its_reason()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var tree = TreeFormatter.Format(app.Root.Snapshot(true));
        var key = Request(PcInputKind.Key, app, tree, 0) with { Chord = Chord("ctrl+shift+q"), KeysText = "ctrl+shift+q" };
        var off = harness.Ladder.Plan(key);
        Assert.True(off.Refused);
        Assert.Equal(PcErrorCode.NeedsRealInput, off.Refusal);
        Assert.Equal(PcRealInputReason.Off, off.Reason);
        harness.Settings = harness.Settings with { RealInput = true };
        Assert.Equal(PcRealInputReason.ActivationUnproven, harness.Ladder.Plan(key).Reason);
        // A drag in an app whose framework takes no messages has no cursor-free way either.
        var drag = Request(PcInputKind.Drag, app, tree, 0) with { Path = [new PcPoint(200, 200), new PcPoint(300, 300)] };
        Assert.Equal(PcErrorCode.NeedsRealInput, harness.Ladder.Plan(drag).Refusal);
    }

    [Fact]
    public void The_pattern_rung_refuses_what_must_not_be_done()
    {
        var (ladder, _, app, tree) = Classic();
        app.Root.Children[1].States |= PcStates.Disabled;
        var disabled = TreeFormatter.Format(app.Root.Snapshot(true));
        Assert.Equal(PcErrorCode.Disabled, ladder.Plan(Request(PcInputKind.Click, app, disabled, 3)).Refusal);
        var editClick = ladder.Plan(Request(PcInputKind.Click, app, tree, 2));
        Assert.Equal(PcErrorCode.NotSupported, editClick.Refusal);
        Assert.Contains("needs no click", editClick.Reason, StringComparison.Ordinal);
        app.Root.Children[0].States |= PcStates.Password;
        var password = TreeFormatter.Format(app.Root.Snapshot(true));
        Assert.Equal(PcErrorCode.PasswordField, ladder.Plan(Request(PcInputKind.Type, app, password, 2) with { Text = "x" }).Refusal);
        var item = new PcUiNode("9", "list item", "Inbox") { Actions = PcActions.Select, States = PcStates.Selected };
        var selected = ladder.Plan(new PcInputRequest(PcInputKind.Click, app.Info) { Tree = tree, Element = item });
        Assert.Equal(PcErrorCode.NotSupported, selected.Refusal);
        Assert.Contains("already selected", selected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_become_the_command_that_shows_them_or_the_dialog_button_never_a_guess()
    {
        var (ladder, _, app, _) = Classic();
        app.Root.Add(new PcFakeNode("1.44.5", "menu item", "Save") { Actions = PcActions.Invoke, Shortcut = "Ctrl+S", Bounds = new PcRect(10, 30, 60, 20) });
        app.Root.Add(new PcFakeNode("1.44.6", "button", "Save") { Actions = PcActions.Invoke, Shortcut = "Ctrl+S", Bounds = new PcRect(80, 30, 60, 20) });
        var tree = TreeFormatter.Format(app.Root.Snapshot(true));
        var save = ladder.Plan(Request(PcInputKind.Key, app, tree, 0) with { Chord = Chord("ctrl+s"), KeysText = "ctrl+s" });
        Assert.Equal("R1 accelerator", Ways(save).Split(", ")[0]);
        app.Root.Add(new PcFakeNode("1.44.7", "button", "Save as") { Actions = PcActions.Invoke, AccessKey = "Ctrl+S", Bounds = new PcRect(150, 30, 60, 20) });
        var two = TreeFormatter.Format(app.Root.Snapshot(true));
        Assert.Equal(PcErrorCode.AmbiguousTarget, ladder.Plan(Request(PcInputKind.Key, app, two, 0) with { Chord = Chord("ctrl+s"), KeysText = "ctrl+s" }).Refusal);
        var enter = ladder.Plan(Request(PcInputKind.Key, app, tree, 0) with { Chord = Chord("enter"), KeysText = "enter" });
        Assert.Equal("R1 dialog_button", Ways(enter).Split(", ")[0]);
        Assert.Equal("OK", enter.Plans[0].Target!.Name);
    }

    [Fact]
    public async Task A_chord_runs_a_menu_command_only_after_the_menu_state_check()
    {
        var (ladder, desktop, app, tree) = Classic();
        var printed = 0;
        app.Menu.Add(("Ctrl+P", new PcMenuItem(57607, "&Print...\tCtrl+P", false), _ => printed++));
        app.Menu.Add(("Ctrl+E", new PcMenuItem(57608, "&Export\tCtrl+E", true), _ => { }));
        var print = ladder.Plan(Request(PcInputKind.Key, app, tree, 0) with { Chord = Chord("ctrl+p"), KeysText = "ctrl+p" });
        Assert.Equal("R2 wm_command", Ways(print).Split(", ")[0]);
        Assert.Equal("Print...", print.Plans[0].MenuText);
        await print.Plans[0].Run(CancellationToken.None);
        Assert.Equal(1, printed);
        Assert.Contains("wm_command 57607", desktop.Log);
        var export = ladder.Plan(Request(PcInputKind.Key, app, tree, 0) with { Chord = Chord("ctrl+e"), KeysText = "ctrl+e" });
        Assert.Equal(PcErrorCode.Disabled, export.Refusal);
        Assert.Contains("\"Export\"", export.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_key_is_posted_to_the_focused_control_but_never_with_a_modifier_held()
    {
        var (ladder, _, app, tree) = Classic();
        var tab = Request(PcInputKind.Key, app, tree, 0) with { Chord = Chord("tab"), KeysText = "tab" };
        var plan = ladder.Plan(tab);
        Assert.Equal("R2 post_key", Ways(plan).Split(", ")[0]);
        Assert.True(plan.Plans[0].Writes);
        Assert.True(ladder.Plan(tab with { ModifiersDown = true }).Refused);
    }

    [Fact]
    public void Window_commands_never_activate_a_window_that_is_behind()
    {
        var (ladder, _, app, tree) = Classic();
        var maximize = Request(PcInputKind.Command, app, tree, 1) with { Command = new PcElementCommand(PcElementAction.Maximize) };
        Assert.True(ladder.Plan(maximize).Refused);
        Assert.True(ladder.Plan(maximize with { InFront = true, UserActive = true }).Refused);
        Assert.Equal("R1 maximize", Ways(ladder.Plan(maximize with { InFront = true })));
        var minimize = Request(PcInputKind.Command, app, tree, 1) with { Command = new PcElementCommand(PcElementAction.Minimize) };
        Assert.Equal("R2 sw_showminnoactive", Ways(ladder.Plan(minimize)));
        var restore = Request(PcInputKind.Command, app, tree, 1) with { Command = new PcElementCommand(PcElementAction.Restore) };
        Assert.Equal("R2 sw_shownoactivate", Ways(ladder.Plan(restore)));
    }

    [Fact]
    public void Copy_cut_and_paste_are_recognised_as_chords_and_as_commands()
    {
        foreach (var keys in new[] { "ctrl+c", "ctrl+v", "ctrl+x", "ctrl+insert", "shift+insert", "shift+delete" })
            Assert.True(PcClipboard.IsClipboardChord(Chord(keys)), keys);
        foreach (var keys in new[] { "ctrl+s", "ctrl+shift+v", "ctrl+a", "delete" })
            Assert.False(PcClipboard.IsClipboardChord(Chord(keys)), keys);
        foreach (var name in new[] { "Copy", "Cut", "Paste", "paste special", "Paste." })
            Assert.True(PcClipboard.IsClipboardCommand(name), name);
        foreach (var name in new[] { "Copyright", "Paste link here", null })
            Assert.False(PcClipboard.IsClipboardCommand(name), name);
    }
}
