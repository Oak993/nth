using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, Settings › Tactus › Separate workspace (unit P18, ED-NEW-pcws-10; DESIGN.md 2.13, 4.7). The status is
// read without holding the page; "Set up" folds open exactly what changes, the command with Copy, the undo and the limits,
// and Esc folds it again with focus back on Set up; a long install path wraps; Watch is offered only while a workspace runs;
// the preview line shows until the build releases the workspace; nothing on the page changes a Windows setting.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcws-10")]
public sealed class PcWorkspaceSettingsTests(HeadlessSession headless)
{
    private const string Exe = "C:\\Users\\Me\\AppData\\Local\\nth\\current\\nth.exe";

    private static PcSettingsPageParts Parts(Func<CancellationToken, Task<WorkspaceStatus>>? read = null, Func<string, Task>? copy = null, Func<bool>? watch = null, string exe = Exe, bool released = false)
    {
        var value = PcSettings.Default;
        return new PcSettingsPageParts
        {
            Read = () => value,
            Write = next => value = next,
            ReadWorkspace = read ?? (_ => Task.FromResult(WorkspaceStatus.NotSetUp)),
            Copy = copy,
            WatchWorkspace = watch,
            WorkspaceExe = exe,
            WorkspaceReleased = released,
        };
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

    private static List<string> Texts(Control root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

    [Fact]
    public void The_status_is_read_without_holding_the_page_and_Watch_is_offered_only_while_a_workspace_runs() => headless.OnUiThread(() =>
    {
        var status = new TaskCompletionSource<WorkspaceStatus>();
        var watched = 0;
        var page = new PcControlSettingsPage(Parts(read: _ => status.Task, watch: () => ++watched > 0));
        Show(page, window =>
        {
            Assert.Equal("Not set up", page.WorkspaceStatusText.Text);
            Assert.True(page.WatchButton.Dimmed);
            status.SetResult(WorkspaceStatus.Ready);
            Settle(window);
            Assert.Equal("Ready", page.WorkspaceStatusText.Text);
            Assert.True(page.WatchButton.Dimmed);
            var running = page.RefreshWorkspace();
            Settle(window);
            Assert.True(running.IsCompleted);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(page.WorkspaceStatusText));
        });
        var live = new PcControlSettingsPage(Parts(read: _ => Task.FromResult(new WorkspaceStatus(WorkspaceState.Running, 2)), watch: () => ++watched > 0));
        Show(live, window =>
        {
            Assert.Equal("Running, 2 apps", live.WorkspaceStatusText.Text);
            Assert.False(live.WatchButton.Dimmed);
            live.WatchButton.Activate();
            Assert.Equal(1, watched);
        });
    });

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-2")]
    public void Set_up_shows_exactly_what_changes_the_command_the_undo_and_the_limits_and_Copy_only_copies() => headless.OnUiThread(() =>
    {
        var copied = new List<string>();
        var page = new PcControlSettingsPage(Parts(copy: text => { copied.Add(text); return Task.CompletedTask; }));
        Show(page, window =>
        {
            Assert.False(page.SetUpFold.Open);
            Assert.Equal(PcControlSettingsPage.SetUpOpenHelp, AutomationProperties.GetHelpText(page.SetUpButton));
            page.SetUpButton.Activate();
            Settle(window);
            Assert.True(page.SetUpFold.Open);
            Assert.Equal(PcControlSettingsPage.SetUpCloseHelp, AutomationProperties.GetHelpText(page.SetUpButton));
            var texts = Texts(page.SetUpFold);
            foreach (var line in new[] { WorkspaceSetup.WhatChanges, WorkspaceSetup.OpenTerminal, WorkspaceSetup.PasteCommand, WorkspaceSetup.ComeBack, WorkspaceSetup.UndoLine, WorkspaceSetup.RemoteDesktopNote, PcControlSettingsPage.LimitsTitle })
                Assert.Contains(line, texts);
            foreach (var limit in WorkspaceSetup.Limits) Assert.Contains("\u2022 " + limit, texts);
            Assert.Equal("& \"" + Exe + "\" --tactus-workspace-enable | Out-Host", page.EnableCommandText);
            Assert.Equal("& \"" + Exe + "\" --tactus-workspace-disable | Out-Host", page.DisableCommandText);
            Assert.Contains(page.EnableCommandText, texts);
            Assert.Contains(page.DisableCommandText, texts);
            Assert.Equal(PcControlSettingsPage.CopyEnableName, AutomationProperties.GetName(page.CopyEnableButton));
            Assert.Equal(PcControlSettingsPage.CopyUndoName, AutomationProperties.GetName(page.CopyDisableButton));
            page.CopyEnableButton.Activate();
            Settle(window);
            page.CopyDisableButton.Activate();
            Settle(window);
            Assert.Equal(new[] { page.EnableCommandText, page.DisableCommandText }, copied);
            Assert.Equal((PcControlSettingsPage.Copied, true), (page.CopyNote.Text, page.CopyNote.IsVisible));
        });
    });

    [Fact]
    public void A_copy_that_fails_says_so_and_Esc_folds_the_steps_with_focus_back_on_Set_up() => headless.OnUiThread(() =>
    {
        var page = new PcControlSettingsPage(Parts(copy: _ => throw new InvalidOperationException("clipboard busy")));
        Show(page, window =>
        {
            window.Activate();
            page.SetUpButton.Activate();
            Settle(window);
            page.CopyEnableButton.Activate();
            Settle(window);
            Assert.Equal(PcControlSettingsPage.CopyFailed, page.CopyNote.Text);
            page.SetUpFold.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, KeyModifiers = KeyModifiers.None });
            Settle(window);
            Assert.False(page.SetUpFold.Open);
            Assert.False(page.CopyNote.IsVisible);
            Assert.True(page.SetUpButton.IsFocused);
        });
    });

    [Fact]
    public void A_long_install_path_wraps_inside_the_steps_without_widening_the_page() => headless.OnUiThread(() =>
    {
        var deep = "C:\\Users\\" + string.Concat(Enumerable.Repeat("AVeryLongFolderNameThatKeepsGoing\\", 8)) + "nth.exe";
        var page = new PcControlSettingsPage(Parts(exe: deep));
        Show(page, window =>
        {
            page.SetUpButton.Activate();
            Settle(window);
            var command = page.SetUpFold.GetVisualDescendants().OfType<SelectableTextBlock>().First(t => t.Text == page.EnableCommandText);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, command.TextWrapping);
            Assert.True(command.Bounds.Width <= page.Bounds.Width, command.Bounds.Width + " > " + page.Bounds.Width);
            Assert.True(command.Bounds.Height > command.FontSize * 2, "the command wraps onto more than one line");
            Assert.Equal(520, page.Bounds.Width);
        });
    });

    [Fact]
    public void The_preview_line_shows_until_the_build_releases_the_workspace() => headless.OnUiThread(() =>
    {
        var preview = new PcControlSettingsPage(Parts());
        Show(preview, _ => Assert.Contains(Texts(preview), t => t.EndsWith(WorkspacePreview.NotReleasedLine, StringComparison.Ordinal)));
        var released = new PcControlSettingsPage(Parts(released: true));
        Show(released, _ => Assert.DoesNotContain(Texts(released), t => t.Contains(WorkspacePreview.NotReleasedLine, StringComparison.Ordinal)));
    });
}
