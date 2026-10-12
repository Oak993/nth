using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.PcControl;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, Settings › Tactus (OD-0140, OD-0145, ED-NEW-pcctl-29; DESIGN.md 4.7, 10.10; P16, P29, unit 13). The page
// is listed directly after Skills and found by its words (pill, pause, answer and position among them); Tactus and real
// mouse and keyboard start off; every change writes the whole settings and the master switch going off stops everything;
// the Stop, Pause and Answer chords are checked before they are saved (one key, a Windows-key chord, a clash, a chord that
// types a character) and Esc cancels a capture; the pill rows write their keys; the allowed apps list Use and Remove; the
// built-in kinds are listed read-only and "Block another app" adds the user's own.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-29")]
public sealed class PcControlSettingsPageTests(HeadlessSession headless)
{
    private static (PcSettingsPageParts Parts, List<PcSettings> Writes, Func<int> Offs) Parts(PcGrants? grants = null, Func<Task<string?>>? pick = null, Func<PcHotkey, bool>? types = null)
    {
        var value = PcSettings.Default;
        var writes = new List<PcSettings>();
        var offs = 0;
        var parts = new PcSettingsPageParts
        {
            Read = () => value,
            Write = next => { value = next; writes.Add(next); },
            SwitchedOff = () => offs++,
            Grants = grants,
            PickApp = pick,
            TypesCharacter = types ?? (_ => false),
            Monitors = () => [(@"\\.\DISPLAY2", "DELL U2720Q")],
        };
        return (parts, writes, () => offs);
    }

    private static void Show(PcControlSettingsPage page, Action<Avalonia.Controls.Window> body)
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var window = new Avalonia.Controls.Window { Width = 600, Height = 2400, Content = new ScrollViewer { Content = new Border { Width = 520, Child = page } } };
        window.Show();
        Settle(window);
        try { body(window); } finally { window.Close(); }
    }

    private static void Settle(Avalonia.Controls.Window window)
    {
        for (var i = 0; i < 4; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);

    private static void Toggle(Control root, string name)
    {
        var host = Named<SettingsSwitch>(root, name);
        var toggle = host.GetVisualDescendants().OfType<SmallSwitch>().Single();
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)).Toggle();
    }

    private static bool IsOn(Control root, string name)
    {
        var toggle = Named<SettingsSwitch>(root, name).GetVisualDescendants().OfType<SmallSwitch>().Single();
        return Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)).ToggleState == ToggleState.On;
    }

    private static void Segment(Control picker, int index) =>
        picker.GetVisualDescendants().OfType<Button>().ElementAt(index).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void The_page_is_listed_after_Skills_and_found_by_its_words() => headless.OnUiThread(() =>
    {
        var all = SwiftEnumAll();
        Assert.Equal(all.IndexOf(SettingsPage.Skills) + 1, all.IndexOf(SettingsPage.PcControl));
        Assert.Equal(("pcControl", "Tactus"), (SettingsPage.PcControl.Id, SettingsPage.PcControl.Title));
        Assert.True(SettingsPage.PcControl.IsReady);
        Assert.NotNull(SymbolCatalogue.Mapping(SettingsPage.PcControl.Symbol));
        Assert.Equal(new[] { "tactus", "pc control", "computer use", "control pc", "automation", "click", "apps", "pill", "pause", "answer", "position" }, SettingsPage.PcControl.Keywords);
        var listed = SettingsSidebar.Pages("").ToList();
        Assert.Equal(listed.IndexOf(SettingsPage.Skills) + 1, listed.IndexOf(SettingsPage.PcControl));
        Assert.Equal(new[] { SettingsPage.PcControl }, SettingsSidebar.Pages("tactus"));
        Assert.Equal(new[] { SettingsPage.PcControl }, SettingsSidebar.Pages("computer use"));
        foreach (var word in new[] { "pill", "pause", "answer", "position" }) Assert.Contains(SettingsPage.PcControl, SettingsSidebar.Pages(word));
        Assert.DoesNotContain(SettingsPage.PcControl, SettingsSidebar.Pages("git"));
        Assert.IsType<PcControlSettingsPage>(SettingsView.PageFor(SettingsPage.PcControl, "", ""));
    });

    private static List<SettingsPage> SwiftEnumAll() => [.. Marrow.Core.Port.SwiftEnum<SettingsPage>.AllCases];

    [Fact]
    public void Tactus_and_real_input_start_off_and_turning_Tactus_off_stops_everything() => headless.OnUiThread(() =>
    {
        var (parts, writes, offs) = Parts();
        var page = new PcControlSettingsPage(parts);
        Show(page, window =>
        {
            Assert.False(IsOn(page, PcControlSettingsPage.Title));
            Assert.False(IsOn(page, PcControlSettingsPage.RealInputRow));
            Assert.True(IsOn(page, PcControlSettingsPage.ConfirmRow));
            Assert.False(IsOn(page, PcControlSettingsPage.ScreenshotsRow));
            Assert.False(IsOn(page, PcControlSettingsPage.SoundRow));
            Assert.True(IsOn(page, PcControlSettingsPage.PointerRow));
            Assert.True(IsOn(page, PcControlSettingsPage.AnswerInPillRow));
            Assert.True(IsOn(page, PcControlSettingsPage.ContinueRow));
            Assert.Empty(writes);

            Toggle(page, PcControlSettingsPage.Title);
            Assert.True(Assert.Single(writes).Enabled);
            Assert.Equal(0, offs());
            Toggle(page, PcControlSettingsPage.RealInputRow);
            Assert.True(writes[^1].RealInput);
            Assert.True(writes[^1].Enabled);
            Toggle(page, PcControlSettingsPage.SoundRow);
            Assert.True(writes[^1].Sound);

            // the master switch going off is stop path 9, once
            Toggle(page, PcControlSettingsPage.Title);
            Assert.False(writes[^1].Enabled);
            Assert.True(writes[^1].RealInput);
            Assert.Equal(1, offs());
            Toggle(page, PcControlSettingsPage.ConfirmRow);
            Assert.Equal(1, offs());
            Assert.False(writes[^1].ConfirmGuardedActions);
        });
    });

    [Fact]
    public void Chords_are_checked_before_they_are_saved_and_Esc_cancels_a_capture() => headless.OnUiThread(() =>
    {
        var altGrQ = new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 'Q');
        var (parts, writes, _) = Parts(types: chord => chord == altGrQ);
        var page = new PcControlSettingsPage(parts);
        Show(page, window =>
        {
            var stop = page.Chords[PcControlSettingsPage.StopKeyRow];
            var pauseKey = page.Chords[PcControlSettingsPage.PauseKeyRow];
            var answer = page.Chords[PcControlSettingsPage.AnswerKeyRow];
            Assert.Equal(("Ctrl+Alt+Esc", "Ctrl+Alt+Space", PcControlSettingsPage.NotSet), (stop.Text, pauseKey.Text, answer.Text));
            Assert.Equal("Change " + PcControlSettingsPage.StopKeyRow, AutomationProperties.GetName(stop.Button));
            Assert.Equal("Set " + PcControlSettingsPage.AnswerKeyRow, AutomationProperties.GetName(answer.Button));

            Assert.Equal(PcChordProblem.Clash, page.SaveChord(PcControlSettingsPage.StopKeyRow, PcHotkey.PauseDefault));
            Assert.Equal(PcChordProblem.SingleKey, page.SaveChord(PcControlSettingsPage.PauseKeyRow, new PcHotkey(PcHotkeyModifiers.None, PcHotkey.Space)));
            Assert.Equal(PcChordProblem.WinKey, page.SaveChord(PcControlSettingsPage.AnswerKeyRow, new PcHotkey(PcHotkeyModifiers.Win | PcHotkeyModifiers.Alt, 'A')));
            Assert.Equal(PcChordProblem.TypesCharacter, page.SaveChord(PcControlSettingsPage.AnswerKeyRow, altGrQ));
            Assert.Empty(writes);

            Assert.Equal(PcChordProblem.None, page.SaveChord(PcControlSettingsPage.AnswerKeyRow, new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 'P')));
            Assert.Equal("Ctrl+Alt+P", Assert.Single(writes).AnswerChord);
            Assert.Equal("Ctrl+Alt+P", answer.Text);
            // the answer chord now counts as another Tactus key
            Assert.Equal(PcChordProblem.Clash, page.SaveChord(PcControlSettingsPage.PauseKeyRow, new PcHotkey(PcHotkeyModifiers.Control | PcHotkeyModifiers.Alt, 'P')));

            // a capture: Change listens, modifiers alone wait, the chord is saved
            stop.Button.Activate();
            Assert.True(stop.Listening);
            Assert.Equal(PcControlSettingsPage.PressKeys, stop.Text);
            stop.Take(Key.LeftCtrl, KeyModifiers.Control);
            Assert.True(stop.Listening);
            stop.Take(Key.F9, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.False(stop.Listening);
            Assert.Equal("Ctrl+Shift+F9", writes[^1].StopChord);
            Assert.Equal("Ctrl+Shift+F9", stop.Text);

            // a refused capture says why and keeps the old chord
            pauseKey.Button.Activate();
            pauseKey.Take(Key.Space, KeyModifiers.None);
            Assert.Equal("Ctrl+Alt+Space", pauseKey.Text);
            Assert.Equal(PcControlSettingsPage.ProblemText(PcChordProblem.SingleKey), pauseKey.Problem);
            Assert.Equal("Ctrl+Alt+Space", writes[^1].PauseChord);

            // Esc cancels
            var count = writes.Count;
            pauseKey.Button.Activate();
            Assert.Equal("", pauseKey.Problem);
            pauseKey.Take(Key.Escape, KeyModifiers.None);
            Assert.False(pauseKey.Listening);
            Assert.Equal("Ctrl+Alt+Space", pauseKey.Text);
            Assert.Equal(count, writes.Count);
        });
    });

    [Fact]
    public void The_pill_workspace_and_detail_rows_write_their_keys() => headless.OnUiThread(() =>
    {
        var (parts, writes, _) = Parts();
        var page = new PcControlSettingsPage(parts);
        Show(page, window =>
        {
            Segment(page.PositionPicker, 0);
            Assert.Equal("left", writes[^1].PillAnchor);
            Segment(page.PositionPicker, 2);
            Assert.Equal("right", writes[^1].PillAnchor);

            Assert.Equal("latest", page.ShowOnMenu.SelectedId);
            Assert.Equal(new[] { "latest", "main", @"\\.\DISPLAY2" }, page.ShowOnMenu.Items.Select(i => i.Id));
            page.ShowOnMenu.Select(@"\\.\DISPLAY2");
            Assert.Equal((@"\\.\DISPLAY2", "DELL U2720Q"), (writes[^1].PillMonitor, writes[^1].PillMonitorName));
            page.ShowOnMenu.Select("main");
            Assert.Equal(("main", (string?)null), (writes[^1].PillMonitor, writes[^1].PillMonitorName));

            Toggle(page, PcControlSettingsPage.AnswerInPillRow);
            Assert.False(writes[^1].AnswerInPill);
            Toggle(page, PcControlSettingsPage.ContinueRow);
            Assert.False(writes[^1].ResumeContinuesChat);

            Assert.Equal(PcControlSettingsPage.WorkspaceNotSetUp, page.WorkspaceStatusText.Text);
            Segment(page.WorkspacePicker, 2);
            Assert.Equal("never", writes[^1].WorkspaceUse);
            Segment(page.DetailPicker, 1);
            Assert.Equal(PcDetail.High, writes[^1].Detail);
            Toggle(page, PcControlSettingsPage.OcrRow);
            Assert.True(writes[^1].ReadTextFromImages);
            Toggle(page, PcControlSettingsPage.TypedRow);
            Assert.True(writes[^1].LogTypedText);
            Toggle(page, PcControlSettingsPage.PointerRow);
            Assert.False(writes[^1].ShowPointer);

            // every write keeps the earlier changes
            var last = writes[^1];
            Assert.Equal(("right", "main", false, false, "never", PcDetail.High), (last.PillAnchor, last.PillMonitor, last.AnswerInPill, last.ResumeContinuesChat, last.WorkspaceUse, last.Detail));
        });
    });

    [Fact]
    public void Apps_list_their_grants_and_Never_allowed_takes_the_users_own_blocks() => headless.OnUiThread(() =>
    {
        var grants = new PcGrants(new InMemorySettingsStore());
        var (parts, writes, _) = Parts(grants, () => Task.FromResult<string?>(@"c:\tools\remote.exe"));
        var page = new PcControlSettingsPage(parts);
        Show(page, window =>
        {
            Assert.Empty(page.Apps);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == PcControlSettingsPage.AppsEmpty);
            foreach (var kind in PcControlSettingsPage.BuiltInNever) Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == kind);

            grants.Grant("chat-1", new PcAppIdentity(@"c:\office\excel.exe"), "Excel", use: true, PcGrantScope.Always);
            grants.Grant("chat-1", new PcAppIdentity(@"c:\office\outlook.exe"), "Outlook", use: false, PcGrantScope.Chat);
            Settle(window);
            Assert.Equal(new[] { "Excel", "Outlook" }, page.Apps.Select(g => g.Name).Order());
            Assert.True(IsOn(page, "Use Excel"));
            // a chat's grant has no Use switch: it ends with the chat and is only removed here
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<SettingsSwitch>(), s => AutomationProperties.GetName(s) == "Use Outlook");
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Only sees · This chat");

            Toggle(page, "Use Excel");
            Settle(window);
            Assert.False(grants.All.Single(g => g.Name == "Excel").Use);
            Assert.False(IsOn(page, "Use Excel"));
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Only sees · Always");
            Named<CzCapsuleButton>(page, "Remove Excel").Activate();
            Settle(window);
            Assert.Equal(new[] { "Outlook" }, page.Apps.Select(g => g.Name));
            Assert.Equal(new[] { "Outlook" }, grants.All.Select(g => g.Name));
            Named<CzCapsuleButton>(page, "Remove Outlook").Activate();
            Settle(window);
            Assert.Empty(grants.All);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == PcControlSettingsPage.AppsEmpty);

            // "Block another app": the picked app joins the user's own blocks; Remove takes it out again
            page.BlockButton.Activate();
            Settle(window);
            Assert.Equal(new[] { @"c:\tools\remote.exe" }, writes[^1].UserDeniedApps);
            page.BlockButton.Activate();
            Settle(window);
            Assert.Single(writes[^1].UserDeniedApps);
            Named<CzCapsuleButton>(page, "Allow Remote again").Activate();
            Settle(window);
            Assert.Empty(writes[^1].UserDeniedApps);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<CzCapsuleButton>(), b => AutomationProperties.GetName(b) == "Allow Remote again");
        });
    });
}
