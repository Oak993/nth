using System.Reflection;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792), :191-318.
// These are deliberately real Windows tests: TerminalSession starts a ConPTY-backed PowerShell process and the
// resize tests drive the same Win32 window messages consumed by WindowLiveResize. The application executable is never
// launched.
[Collection("Headless")]
public sealed class TerminalPanelHighRiskTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-TERM-031")] // App/Views/Terminal/TerminalPanel.swift:214-221
    public void Bare_Escape_closes_at_the_shell_prompt_but_modified_Escape_and_a_foreground_child_do_not()
    {
        var closeCount = 0;
        WithAttachedHost("escape prompt", null, (session, host, window, _, runtime) =>
        {
            Assert.True(UiPump.Until(
                () => session.IsRunning && TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId),
                TimeSpan.FromSeconds(15)), "the real ConPTY shell did not reach the prompt");

            Assert.True(session.View.Focus());
            Assert.True(session.View.IsFocused);
            // The host was created with this callback; the first bare Escape is the only one that should close.
            foreach (var modifier in new[] { RawInputModifiers.Control, RawInputModifiers.Shift,
                         RawInputModifiers.Alt, RawInputModifiers.Meta })
                window.KeyPress(Key.Escape, modifier, PhysicalKey.Escape, null);
            Assert.Equal(0, closeCount);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(1, closeCount);

            // The real child is launched by the same PowerShell process inside the ConPTY. While it owns the
            // foreground, the host must leave Escape for the terminal rather than closing the drawer.
            var childCommand = "$exe=(Get-Process -Id $PID).Path; Start-Process -FilePath $exe -ArgumentList '-NoLogo','-NoProfile','-Command','Start-Sleep -Seconds 10'; Start-Sleep -Seconds 2";
            var childSession = new TerminalSession(Guid.NewGuid(), TerminalTestFolders.Create("escape child"), "PowerShell", childCommand);
            var childHost = new TerminalHost(childSession, runtime, () => closeCount++);
            var childRoot = new Border { Child = childHost };
            var childWindow = new AvaloniaWindow { Content = childRoot, Width = 640, Height = 240 };
            var childResize = new WindowLiveResize(childWindow);
            AppEnvironment.SetWindowLiveResize(childRoot, childResize);
            childWindow.Show();
            Settle(childWindow);
            try
            {
                Assert.True(UiPump.Until(
                    () => childSession.IsRunning && !TerminalSession.ForegroundJobIsTheShell(childSession.View.ProcessId),
                    TimeSpan.FromSeconds(15)), "the real ConPTY foreground child was not observed");
                Assert.True(childSession.View.Focus());
                Assert.True(childSession.View.IsFocused);
                childWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.Equal(1, closeCount);
                // The source guard's own answer while the child holds the foreground (a live descendant).
                Assert.False(TerminalSession.ForegroundJobIsTheShell(childSession.View.ProcessId));
            }
            finally
            {
                childWindow.Close();
                childSession.Terminate();
                Dispatcher.UIThread.RunJobs();
            }

            // ED-NEW-shellleak-1: Terminate ends the shell and its child. While the job's processes are being ended, a
            // snapshot can hold the dying shell after its child has gone, so the guard is read once the shell has exited.
            Assert.True(ShellProbe.WaitForExit(childSession.View.ProcessId, TimeSpan.FromSeconds(15)), "Terminate left the shell running");
            // Keep this assertion tied to the source guard, rather than merely to the callback count.
            Assert.False(TerminalSession.ForegroundJobIsTheShell(childSession.View.ProcessId));
        }, (session, _, _, _, _) => { }, () => closeCount++);
    }

    // PR-TERM-031's second half: "the event is consumed (the monitor returns nil), so Escape never reaches the shell" and
    // "if any condition fails the event is returned unchanged ... Escape inside vim, less or an agent CLI still reaches
    // that program". The probe waits for one key in the shell (or in a real child program) and prints its character code,
    // so what reached the ConPTY is read back from the screen: 27 is Escape, 120 is the later "x".
    private const string KeyProbe = "Write-Output 'KEYWAIT'; $k = [Console]::ReadKey($true); "
        + "Write-Output ('KEY=' + [int]$k.KeyChar); Start-Sleep -Seconds 5";

    [Fact]
    [Trait("Req", "PR-TERM-031")]
    [Trait("Rule", "R-TEST-4")] // proves the swallow half and the focus guard; the modifier and child guards are the test above
    public void Escape_at_the_shell_prompt_closes_the_drawer_and_never_reaches_the_shell()
    {
        var closes = 0;
        WithAttachedHost("escape swallowed", KeyProbe, (session, host, window, _, _) =>
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEYWAIT", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the shell did not wait for a key");
            Assert.True(UiPump.Until(() => TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId),
                TimeSpan.FromSeconds(15)), "the shell did not own the foreground");
            Assert.True(session.View.Focus());

            // :215-218 the terminal must be the first responder: with focus elsewhere the drawer is left alone and the
            // terminal gets nothing (the event is not the terminal's).
            window.FocusManager!.Focus(null);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(0, closes);
            UiPump.For(TimeSpan.FromMilliseconds(300));
            Assert.DoesNotContain("KEY=", session.View.ScrollbackText, StringComparison.Ordinal);

            Assert.True(session.View.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(1, closes); // :219 onEscapeAtPrompt()
            UiPump.For(TimeSpan.FromMilliseconds(500)); // an Escape byte sent to the shell would have been read by now
            Assert.DoesNotContain("KEY=", session.View.ScrollbackText, StringComparison.Ordinal); // :220 return nil

            window.KeyTextInput("x");
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEY=", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the shell did not read the later key");
            Assert.Contains("KEY=120", session.View.ScrollbackText, StringComparison.Ordinal); // the first key it saw was the "x"
            Assert.DoesNotContain("KEY=27", session.View.ScrollbackText, StringComparison.Ordinal);
            Assert.Equal(1, closes);
        }, (session, _, _, _, _) => { }, () => closes++);
    }

    [Fact]
    [Trait("Req", "PR-TERM-031")]
    [Trait("Rule", "R-TEST-4")] // the unknown-answer branch (OD-0031: "if it can't tell, it also goes to the terminal")
    public void Escape_reaches_the_shell_and_the_drawer_stays_when_the_foreground_state_cannot_be_determined()
    {
        var closes = 0;
        WithAttachedHost("escape unknown", KeyProbe, (session, host, window, _, _) =>
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEYWAIT", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the shell did not wait for a key");
            Assert.True(UiPump.Until(() => TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId),
                TimeSpan.FromSeconds(15)), "the shell did not own the foreground");
            Assert.True(session.View.Focus());

            var previous = ProcessDescendants.ReadProcessTable;
            ProcessDescendants.ReadProcessTable = () => null; // the process snapshot fails
            try { window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); }
            finally { ProcessDescendants.ReadProcessTable = previous; }

            Assert.Equal(0, closes);
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEY=27", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the Escape did not reach the shell");
        }, (session, _, _, _, _) => { }, () => closes++);
    }

    [Fact]
    [Trait("Req", "PR-TERM-031")]
    [Trait("Rule", "R-TEST-4")] // a real foreground program receives the Escape byte
    public void Escape_reaches_a_foreground_program_started_by_the_shell_and_the_drawer_stays()
    {
        var closes = 0;
        var child = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(KeyProbe));
        var command = "$exe = (Get-Process -Id $PID).Path; Start-Process -FilePath $exe -NoNewWindow -Wait "
            + $"-ArgumentList '-NoLogo','-NoProfile','-EncodedCommand','{child}'";
        WithAttachedHost("escape child", command, (session, host, window, _, _) =>
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEYWAIT", StringComparison.Ordinal),
                TimeSpan.FromSeconds(30)), "the child program did not wait for a key");
            Assert.True(UiPump.Until(() => !TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId),
                TimeSpan.FromSeconds(15)), "the child program was not seen as the foreground");
            Assert.True(session.View.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(0, closes);
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEY=27", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the Escape did not reach the foreground program");
            Assert.Equal(0, closes);
        }, (session, _, _, _, _) => { }, () => closes++);
    }

    [Fact]
    [Trait("Req", "PR-TERM-032")] // App/Views/Terminal/TerminalPanel.swift:199-205,225-251
    public void Escape_listener_is_scoped_to_the_attached_terminal_and_is_removed_on_detach()
    {
        var closes = 0;
        Assert.Equal(0, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        WithAttachedHost("escape listener", null, (session, host, window, _, _) =>
        {
            var attached = StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount");
            Assert.Equal(1, attached);
            Assert.Equal(1, RealEscapeSubscriptions(session)); // the view's own KeyDown subscriptions, not the host's counter
            var originalRoot = Assert.IsType<Border>(window.Content);
            originalRoot.Child = null;
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
            Assert.Equal(0, RealEscapeSubscriptions(session)); // :202-204 the monitor is really removed from the view

            // Reattaching the same session creates one fresh scoped listener, never an accumulating document listener.
            var root = new Border { Child = host };
            var resize = new WindowLiveResize(window);
            AppEnvironment.SetWindowLiveResize(root, resize);
            window.Content = root;
            window.Show();
            Settle(window);
            Assert.Equal(1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
            Assert.Equal(1, RealEscapeSubscriptions(session));
            Assert.True(UiPump.Until(() => TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId),
                TimeSpan.FromSeconds(15)), "the ConPTY shell did not reach its prompt");
            Assert.True(session.View.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(1, closes);
        }, (session, _, _, _, _) => { }, () => closes++);
    }

    [Fact]
    [Trait("Req", "PR-TERM-032")] // App/Views/Terminal/TerminalPanel.swift:199-222,60: one monitor per hosted session
    public void Switching_tabs_repeatedly_keeps_exactly_one_escape_listener_and_removing_the_panel_removes_it() => TerminalPanelRig.Run(headless, rig =>
    {
        var baseline = StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount");
        var directory = TerminalPanelRig.Folder("escape listeners");
        rig.Step(() =>
        {
            for (var i = 0; i < 3; i++) rig.App.Terminals.Open(rig.Thread.Id, directory);
        });
        var (host, _, window) = rig.Show(directory, 600, 300);
        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id).ToArray();
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        // The host's counter is its own bookkeeping; what proves the removal is what the views really carry. Exactly one
        // monitor exists, on the hosted session's view, and none on a view whose host was released.
        void AssertOneMonitorOn(TerminalSession hosted)
        {
            foreach (var session in sessions)
                Assert.Equal(session.Id == hosted.Id ? 1 : 0, RealEscapeSubscriptions(session));
        }
        AssertOneMonitorOn(rig.App.Terminals.Selected(rig.Thread.Id)!);

        // :60 .id(selected.id): every selection change makes a new host and releases the old one, so the count never grows.
        for (var turn = 0; turn < 6; turn++)
        {
            var next = sessions[turn % 3];
            rig.Step(() => rig.App.Terminals.Selection = rig.App.Terminals.Selection.Mutating(map => map[rig.Thread.Id] = next.Id));
            Assert.Equal(next.Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
            Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
            AssertOneMonitorOn(next);
        }

        // A tap on a tab switches the same way.
        var tabs = LogicalQuery.SelfAndDescendants<TerminalTab>(host).ToArray();
        var target = tabs[0];
        var at = target.TranslatePoint(new Point(4, target.Bounds.Height / 2), window)!.Value;
        rig.Step(() => window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton));
        rig.Step(() => window.MouseUp(at, MouseButton.Left, RawInputModifiers.None));
        Assert.Equal(sessions[0].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        AssertOneMonitorOn(sessions[0]);

        // Closing the selected tab hosts another session, still one listener; closing the others leaves the empty state.
        rig.Step(() => rig.App.Terminals.Close(sessions[0]));
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        Assert.Equal(0, RealEscapeSubscriptions(sessions[0]));
        Assert.Equal(1, sessions.Sum(RealEscapeSubscriptions));
        rig.Step(() => rig.App.Terminals.Close(sessions[1]));
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        Assert.Equal(1, sessions.Sum(RealEscapeSubscriptions));
        rig.Step(() => rig.App.Terminals.Close(sessions[2]));
        Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalHost>(host));
        Assert.Equal(baseline, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount")); // no hosted session, no monitor
        Assert.Equal(0, sessions.Sum(RealEscapeSubscriptions));

        rig.Step(() => rig.App.Terminals.Open(rig.Thread.Id, directory));
        Assert.Equal(baseline + 1, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        var reopened = rig.App.Terminals.Sessions(rig.Thread.Id).Single();
        Assert.Equal(1, RealEscapeSubscriptions(reopened));
        rig.Close(window); // the drawer is gone: the host is destroyed
        Assert.Equal(baseline, StaticMetric(typeof(TerminalHost), "ActiveEscapeListenerCount"));
        Assert.Equal(0, RealEscapeSubscriptions(reopened));
    });

    [Fact]
    [Trait("Req", "PR-TERM-033")] // App/Views/Terminal/TerminalPanel.swift:268-303
    public void Live_resize_parks_autoresizing_and_coalesces_multiple_frames_to_one_50ms_sync()
    {
        const string command = "Write-Output 'BEATWAIT'; $null = [Console]::ReadLine(); "
            + "Write-Output ('BEATGRID=' + [Console]::WindowWidth + 'x' + [Console]::WindowHeight); Start-Sleep -Seconds 5";
        WithAttachedHost("resize coalescing", command, (session, host, window, resize, _) =>
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("BEATWAIT", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the real ConPTY child did not wait for the coalesced frame");
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            var view = Assert.IsAssignableFrom<Control>(session.View);
            var before = IntMetric(container, "SyncFrameCount");
            var beforeNative = session.View.ResizeCount;
            var initialGrid = session.View.LastPseudoConsoleSize;
            var original = view.Bounds;

            BeginWin32Resize(resize);
            window.Width = 800;
            window.Height = 360;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(BoolMetric(container, "IsAutoResizing"));
            Assert.True(BoolMetric(container, "IsResizePending"));
            var firstBeat = Field(container, "pendingSync").GetValue(container);
            Assert.NotNull(firstBeat);
            Assert.Equal(original, view.Bounds);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();

            window.Width = 860;
            window.Height = 400;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, IntMetric(container, "SyncFrameCount"));
            Assert.Same(firstBeat, Field(container, "pendingSync").GetValue(container));

            while (IntMetric(container, "SyncFrameCount") == before && elapsed.Elapsed < TimeSpan.FromSeconds(2))
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                System.Threading.Thread.Sleep(1);
            }
            Assert.True(IntMetric(container, "SyncFrameCount") > before);
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(35),
                $"the live-resize frame synced after only {elapsed.Elapsed.TotalMilliseconds} ms");
            Assert.Equal(before + 1, IntMetric(container, "SyncFrameCount"));
            Assert.Equal(new Size(860, 400), view.Bounds.Size);
            var grid = session.View.LastPseudoConsoleSize;
            Assert.True(grid.Columns > initialGrid.Columns && grid.Rows > initialGrid.Rows);
            Assert.Equal(beforeNative + 1, session.View.ResizeCount);
            session.View.SendBytes("go\r"u8.ToArray());
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains(
                $"BEATGRID={grid.Columns}x{grid.Rows}", StringComparison.Ordinal), TimeSpan.FromSeconds(15)),
                "the real ConPTY child did not receive the coalesced column and row count");
            EndWin32Resize(resize);
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "PR-TERM-033")] // App/Views/Terminal/TerminalPanel.swift:289-301: the beat clears itself (:296), so the next layout schedules a new one
    [Trait("Rule", "R-TEST-4")] // proves the repeat: at most 20 syncs a second while the drag continues; the first beat is the tests above
    public void Live_resize_schedules_a_new_beat_after_each_sync_while_the_drag_continues()
    {
        WithAttachedHost("resize sustained", null, (session, host, window, resize, _) =>
        {
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            var view = (Control)session.View;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // The timings are taken where they happen, not when the polling loop below notices them: a beat starts when
            // the container asks for its 50 ms delay (inside the layout pass that scheduled it), and the sync lands when
            // the hosted view's frame really changes (inside the container's SyncFrame).
            var beatStartedAt = new List<TimeSpan>();
            container.BeatDelay = (duration, cancellation) =>
            {
                beatStartedAt.Add(clock.Elapsed);
                return Task.Delay(duration, cancellation);
            };
            var syncedAt = new List<(TimeSpan At, Size Size)>();
            EventHandler<AvaloniaPropertyChangedEventArgs> frameChanged = (_, change) =>
            {
                if (change.Property == Visual.BoundsProperty) syncedAt.Add((clock.Elapsed, view.Bounds.Size));
            };
            view.PropertyChanged += frameChanged;
            BeginWin32Resize(resize);
            foreach (var width in new double[] { 700, 760, 820, 880 })
            {
                var before = IntMetric(container, "SyncFrameCount");
                window.Width = width;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(BoolMetric(container, "IsResizePending"), $"no beat was scheduled for the layout at {width}");
                var waited = System.Diagnostics.Stopwatch.StartNew();
                while (IntMetric(container, "SyncFrameCount") == before && waited.Elapsed < TimeSpan.FromSeconds(2))
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    System.Threading.Thread.Sleep(1);
                }
                // :296 pendingSync = nil: the beat cleared itself, so each layout pass got a new beat and a new sync.
                Assert.Equal(before + 1, IntMetric(container, "SyncFrameCount")); // exactly one sync per beat
                Assert.Equal(new Size(width, window.Height), view.Bounds.Size);
            }
            Assert.True(syncedAt.Count == 4, string.Join(";", syncedAt.Select(x => x.Size))); // the hosted frame changed once per beat, nowhere else
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(new Size(new double[] { 700, 760, 820, 880 }[i], window.Height), syncedAt[i].Size);
                // The beat that fired a sync is the last one started before it. A beat can already be pending when the
                // next layout pass of the drag arrives (that pass then starts none, :293), but a beat never starts while
                // the previous one is still pending: it starts at or after the previous sync (:296 pendingSync = nil).
                var firedBy = beatStartedAt.Last(start => start <= syncedAt[i].At);
                Assert.True(syncedAt[i].At - firedBy >= TimeSpan.FromMilliseconds(35),
                    $"sync {i} landed only {(syncedAt[i].At - firedBy).TotalMilliseconds} ms after its beat started");
                if (i > 0)
                {
                    Assert.True(firedBy >= syncedAt[i - 1].At, $"the beat of sync {i} started before sync {i - 1} had landed");
                    Assert.True(syncedAt[i].At - syncedAt[i - 1].At >= TimeSpan.FromMilliseconds(35),
                        $"sync {i} landed only {(syncedAt[i].At - syncedAt[i - 1].At).TotalMilliseconds} ms after the one before");
                }
            }

            // Twenty layout passes inside about 300 ms: a beat is never restarted by a pass, so at most 6 or 7 beats land.
            var syncsBefore = IntMetric(container, "SyncFrameCount");
            var burst = System.Diagnostics.Stopwatch.StartNew();
            for (var pass = 0; pass < 20; pass++)
            {
                window.Width = 900 + pass * 5;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                System.Threading.Thread.Sleep(15);
            }
            var syncs = IntMetric(container, "SyncFrameCount") - syncsBefore;
            var seconds = burst.Elapsed.TotalSeconds;
            Assert.InRange(syncs, 2, Math.Floor(seconds * 20) + 1); // the drag kept following the window, never faster than the 50 ms beat
            EndWin32Resize(resize);
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "PR-TERM-033")]
    public void A_live_resize_without_a_hosted_terminal_schedules_no_beat()
    {
        headless.OnUiThread(() =>
        {
            var container = new TerminalHostContainer();
            var window = new AvaloniaWindow { Width = 640, Height = 240, Content = container };
            var resize = new WindowLiveResize(window);
            AppEnvironment.SetWindowLiveResize(container, resize);
            window.Show();
            Settle(window);
            try
            {
                BeginWin32Resize(resize);
                window.Width = 800;
                window.Height = 360;
                Settle(window);
                Assert.False(container.IsResizePending);
                Assert.Equal(0, container.SyncFrameCount);
                EndWin32Resize(resize);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-033")]
    public void A_single_live_resize_frame_waits_for_the_source_50ms_beat()
    {
        WithAttachedHost("single resize beat", null, (session, host, window, resize, _) =>
        {
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            var before = IntMetric(container, "SyncFrameCount");
            TimeSpan? requestedBeat = null;
            container.BeatDelay = (duration, cancellation) =>
            {
                requestedBeat = duration;
                return Task.Delay(duration, cancellation);
            };
            BeginWin32Resize(resize);
            window.Width = 800;
            window.Height = 360;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(BoolMetric(container, "IsResizePending"));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (IntMetric(container, "SyncFrameCount") == before && elapsed.Elapsed < TimeSpan.FromSeconds(2))
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                System.Threading.Thread.Sleep(1);
            }
            Assert.Equal(before + 1, IntMetric(container, "SyncFrameCount"));
            Assert.Equal(TimeSpan.FromMilliseconds(50), requestedBeat);
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(35),
                $"the frame synced after only {elapsed.Elapsed.TotalMilliseconds} ms");
            EndWin32Resize(resize);
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "PR-TERM-034")] // App/Views/Terminal/TerminalPanel.swift:305-312
    public void Ending_live_resize_cancels_pending_work_and_syncs_the_final_frame_once()
    {
        const string command = "Write-Output 'RESIZEWAIT'; $null = [Console]::ReadLine(); "
            + "Write-Output ('FINALGRID=' + [Console]::WindowWidth + 'x' + [Console]::WindowHeight); "
            + "$null = [Console]::ReadLine(); "
            + "Write-Output ('FOLLOWGRID=' + [Console]::WindowWidth + 'x' + [Console]::WindowHeight); Start-Sleep -Seconds 5";
        WithAttachedHost("resize finish", command, (session, host, window, resize, _) =>
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("RESIZEWAIT", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the real ConPTY child did not wait for the final grid");
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            var view = Assert.IsAssignableFrom<Control>(session.View);
            var before = IntMetric(container, "SyncFrameCount");
            var beforeNative = session.View.ResizeCount;
            var initialGrid = session.View.LastPseudoConsoleSize;
            var initialFrame = view.Bounds.Size;
            BeginWin32Resize(resize);
            window.Width = 900;
            window.Height = 420;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(BoolMetric(container, "IsResizePending"));
            Assert.Equal(initialFrame, view.Bounds.Size);
            Assert.Equal(initialGrid, session.View.LastPseudoConsoleSize);

            EndWin32Resize(resize);
            Assert.False(BoolMetric(container, "IsResizePending"));
            Assert.True(BoolMetric(container, "IsAutoResizing"));
            Assert.Equal(before + 1, IntMetric(container, "SyncFrameCount"));
            Assert.Equal(beforeNative + 1, session.View.ResizeCount);
            Assert.Equal(new Size(900, 420), view.Bounds.Size);
            var final = session.View.LastPseudoConsoleSize;
            // "the final column count matches the settled width": the grid is the settled 900 x 420 frame divided by the
            // measured cell (the same measurement the outside-live-resize test uses), not whatever the last beat left.
            var finalFont = session.View.Font!.Resolve();
            var finalTypeface = new Typeface(finalFont.FontFamily, finalFont.FontStyle, finalFont.FontWeight);
            var finalGlyph = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                finalTypeface, finalFont.FontSize, Brushes.White);
            var finalScale = window.RenderScaling;
            var finalCellWidth = Math.Max(1, Math.Round(finalGlyph.Width * finalScale) / finalScale);
            var finalCellHeight = Math.Clamp(Math.Ceiling(finalGlyph.Height * finalScale) / finalScale, 1, 8192);
            Assert.Equal((int)Math.Floor(900 / finalCellWidth), final.Columns);
            Assert.Equal((int)Math.Floor(420 / finalCellHeight), final.Rows);
            session.View.SendBytes("go\r"u8.ToArray());
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains(
                $"FINALGRID={final.Columns}x{final.Rows}", StringComparison.Ordinal), TimeSpan.FromSeconds(15)),
                "the real ConPTY child did not receive the settled column and row count");

            UiPump.For(TimeSpan.FromMilliseconds(120));
            Assert.Equal(before + 1, IntMetric(container, "SyncFrameCount"));

            window.Width = 1000;
            window.Height = 500;
            Settle(window);
            Assert.Equal(new Size(1000, 500), view.Bounds.Size);
            var following = session.View.LastPseudoConsoleSize;
            Assert.True(following.Columns > final.Columns);
            Assert.True(following.Rows > final.Rows);
            session.View.SendBytes("go\r"u8.ToArray());
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains(
                $"FOLLOWGRID={following.Columns}x{following.Rows}", StringComparison.Ordinal), TimeSpan.FromSeconds(15)),
                "the real ConPTY child did not follow the next ordinary layout change");
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "PR-TERM-034")]
    [Trait("Req", "UI-TERM-010")]
    [Trait("Req", "PR-TERM-033")]
    public void A_layout_change_outside_live_resize_updates_the_real_ConPTY_grid()
    {
        const string command = "Write-Output ('BEFOREGRID=' + [Console]::WindowWidth + 'x' + [Console]::WindowHeight); "
            + "$null = [Console]::ReadLine(); "
            + "Write-Output ('AFTERGRID=' + [Console]::WindowWidth + 'x' + [Console]::WindowHeight); "
            + "Start-Sleep -Seconds 5";
        WithAttachedHost("outside live resize", command, (session, host, window, _, _) =>
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("BEFOREGRID=", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the real ConPTY child did not report its initial grid");
            var initial = session.View.LastPseudoConsoleSize;
            var initialFont = session.View.Font!.Resolve();
            var initialTypeface = new Typeface(initialFont.FontFamily, initialFont.FontStyle, initialFont.FontWeight);
            var initialGlyph = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                initialTypeface, initialFont.FontSize, Brushes.White);
            var initialScale = window.RenderScaling;
            Assert.Equal((int)Math.Floor(640 / (Math.Round(initialGlyph.Width * initialScale) / initialScale)), initial.Columns);
            Assert.Equal((int)Math.Floor(240 / (Math.Ceiling(initialGlyph.Height * initialScale) / initialScale)), initial.Rows);
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains(
                $"BEFOREGRID={initial.Columns}x{initial.Rows}", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the real ConPTY child did not observe its measured initial grid");
            // TerminalFont.preferred() may select a user's 16-point terminal face (TerminalFont.swift:20-90).
            // A 16-point W advance distinguishes font measurement from the old 0.6-point-size guess.
            session.View.Font = SwiftFont.MonospacedSystemFont(16, SwiftFont.Weight.Regular);

            window.Width = 1000;
            window.Height = 600;
            Settle(window);
            Assert.False(BoolMetric(LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single(), "IsResizePending"));
            Assert.Equal(new Size(1000, 600), session.View.Bounds.Size);
            Assert.True(UiPump.Until(() => session.View.LastPseudoConsoleSize.Columns > initial.Columns,
                TimeSpan.FromSeconds(5)), "the layout change did not resize the pseudoconsole");

            session.View.SendBytes("go\r"u8.ToArray());
            var final = session.View.LastPseudoConsoleSize;
            var font = session.View.Font!.Resolve();
            var typeface = new Typeface(font.FontFamily, font.FontStyle, font.FontWeight);
            var glyph = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, font.FontSize, Brushes.White);
            var scale = window.RenderScaling;
            var cellWidth = Math.Max(1, Math.Round(glyph.Width * scale) / scale);
            var cellHeight = Math.Clamp(Math.Ceiling(glyph.Height * scale) / scale, 1, 8192);
            Assert.Equal((int)Math.Floor(1000 / cellWidth), final.Columns);
            Assert.Equal((int)Math.Floor(600 / cellHeight), final.Rows);
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains(
                $"AFTERGRID={final.Columns}x{final.Rows}", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the real ConPTY child did not observe the settled grid");
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void The_real_ConPTY_child_starts_at_the_measured_640_by_240_frame_before_attachment()
    {
        headless.OnUiThread(() =>
        {
            var session = new TerminalSession(Guid.NewGuid(), TerminalTestFolders.Create("initial cell grid"),
                "PowerShell", "Write-Output ('INITIALGRID=' + [Console]::WindowWidth + 'x' + [Console]::WindowHeight); Start-Sleep -Seconds 5");
            try
            {
                var font = session.View.Font!.Resolve();
                var typeface = new Typeface(font.FontFamily, font.FontStyle, font.FontWeight);
                var glyph = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, font.FontSize, Brushes.White);
                var width = Math.Max(1, Math.Round(glyph.Width));
                var height = Math.Clamp(Math.Ceiling(glyph.Height), 1, 8192);
                var expected = $"INITIALGRID={(int)Math.Floor(640 / width)}x{(int)Math.Floor(240 / height)}";
                Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains(expected, StringComparison.Ordinal),
                    TimeSpan.FromSeconds(15)), $"the unattached ConPTY child did not start at {expected}");
            }
            finally { session.Terminate(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-036")] // App/Views/Terminal/TerminalPanel.swift:259-266
    public void Resize_observers_follow_the_current_window_and_are_removed_when_detached()
    {
        WithAttachedHost("resize observers", null, (session, host, window, resize, _) =>
        {
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            Assert.Equal(2, IntMetric(container, "ResizeObserverCount"));
            Assert.Equal(2, RealResizeSubscribers(resize));

            var originalRoot = Assert.IsType<Border>(window.Content);
            originalRoot.Child = null;
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, IntMetric(container, "ResizeObserverCount"));
            Assert.Equal(0, RealResizeSubscribers(resize));
            BeginWin32Resize(resize);
            Assert.True(BoolMetric(container, "IsAutoResizing"));
            EndWin32Resize(resize);

            var secondWindow = new AvaloniaWindow { Width = 640, Height = 240 };
            var secondRoot = new Border { Child = host };
            var secondResize = new WindowLiveResize(secondWindow);
            AppEnvironment.SetWindowLiveResize(secondRoot, secondResize);
            secondWindow.Content = secondRoot;
            secondWindow.Show();
            Settle(secondWindow);
            try
            {
                Assert.Equal(2, IntMetric(container, "ResizeObserverCount"));
                Assert.Equal(2, RealResizeSubscribers(secondResize));
            }
            finally
            {
                secondWindow.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "PR-TERM-036")]
    public void Detaching_during_a_live_resize_cancels_the_queued_frame_sync()
    {
        WithAttachedHost("cancel detached resize", null, (session, host, window, resize, _) =>
        {
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            BeginWin32Resize(resize);
            window.Width = 800;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(BoolMetric(container, "IsResizePending"));
            var before = IntMetric(container, "SyncFrameCount");
            var root = Assert.IsType<Border>(window.Content);
            root.Child = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(BoolMetric(container, "IsResizePending"));
            UiPump.For(TimeSpan.FromMilliseconds(120));
            Assert.Equal(before, IntMetric(container, "SyncFrameCount"));
            EndWin32Resize(resize);
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // App/Views/Terminal/TerminalPanel.swift:191-251; TerminalStore.swift:21-23,47-56
    public void The_terminal_surface_starts_at_the_source_frame_before_its_container_resizes_it()
    {
        headless.OnUiThread(() =>
        {
            var view = new TerminalHostView();
            Assert.Equal(640, view.Width); // TerminalStore.swift:21
            Assert.Equal(240, view.Height);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // App/Views/Terminal/TerminalPanel.swift:191-251; TerminalStore.swift:21-23,47-56
    public void The_session_hosts_a_focusable_font_configured_ConPTY_surface_with_live_output_and_theme_values()
    {
        WithAttachedHost("terminal surface", "Write-Output 'terminal-surface-proof'; Start-Sleep -Seconds 10", (session, host, _, _, runtime) =>
        {
            var view = Assert.IsAssignableFrom<Control>(session.View);
            Assert.True(view.Focusable);
            Assert.Equal(new Size(640, 240), session.View.InitialFrame); // TerminalStore.swift:21
            Assert.Equal(new Size(640, 240), view.Bounds.Size); // TerminalPanel.swift:58-63 immediately hosts the existing frame
            Assert.Equal(TerminalFont.Preferred(), session.View.Font); // TerminalStore.swift:22
            Assert.True(session.View.OptionAsMetaKey); // TerminalStore.swift:23
            session.ApplyAppearance(isDark: true);
            Assert.Equal(new SwiftColor(0.08, 1).Resolve(Avalonia.Styling.ThemeVariant.Dark), session.View.NativeBackgroundColor!.Resolve(Avalonia.Styling.ThemeVariant.Dark));
            Assert.Equal(new SwiftColor(0.9, 1).Resolve(Avalonia.Styling.ThemeVariant.Dark), session.View.NativeForegroundColor!.Resolve(Avalonia.Styling.ThemeVariant.Dark));
            Assert.Same(Chrome.AccentNSColor, session.View.CaretColor);
            Assert.Equal(Chrome.AccentNSColor.Opacity(0.3).Resolve(Avalonia.Styling.ThemeVariant.Dark), session.View.SelectedTextBackgroundColor!.Resolve(Avalonia.Styling.ThemeVariant.Dark));
            session.ApplyAppearance(isDark: false);
            Assert.Equal(new SwiftColor(0.985, 1).Resolve(Avalonia.Styling.ThemeVariant.Light), session.View.NativeBackgroundColor!.Resolve(Avalonia.Styling.ThemeVariant.Light));
            Assert.Equal(new SwiftColor(0.12, 1).Resolve(Avalonia.Styling.ThemeVariant.Light), session.View.NativeForegroundColor!.Resolve(Avalonia.Styling.ThemeVariant.Light));
            Assert.Same(Chrome.AccentNSColor, session.View.CaretColor);
            Assert.Equal(Chrome.AccentNSColor.Opacity(0.3).Resolve(Avalonia.Styling.ThemeVariant.Light), session.View.SelectedTextBackgroundColor!.Resolve(Avalonia.Styling.ThemeVariant.Light));
            Assert.True(view.IsFocused);
            Assert.False(runtime.TerminalWantsFocus); // TerminalPanel.swift:354-364, one focus request per attachment

            Assert.True(UiPump.Until(
                () => AllText(view).Contains("terminal-surface-proof", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the real ConPTY output did not reach the hosted surface");
            Assert.Contains("terminal-surface-proof", session.View.ScrollbackText, StringComparison.Ordinal); // exited scrollback remains on the long-lived view
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            Assert.Same(session.View, container.HostedView);
            Assert.NotNull(LogicalQuery.SelfAndDescendants<TextBlock>(view).SingleOrDefault());
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // state "exited: keeps showing its final scrollback; the tab dims" (TerminalStore.swift:59-63; the view is never rebuilt)
    [Trait("Rule", "R-TEST-4")] // proves that one state of the record
    public void An_exited_shell_keeps_its_final_scrollback_on_the_hosted_surface()
    {
        WithAttachedHost("exited scrollback", "Write-Output 'FINALWORDS'; Start-Sleep -Seconds 30", (session, host, window, _, _) =>
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("FINALWORDS", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)), "the shell did not print its last words");
            var container = LogicalQuery.SelfAndDescendants<TerminalHostContainer>(host).Single();
            var view = session.View;
            session.Terminate();
            Dispatcher.UIThread.RunJobs();
            Settle(window);
            Assert.False(session.IsRunning);
            Assert.Same(view, session.View);
            Assert.Same(view, container.HostedView); // still the hosted surface, not rebuilt or emptied
            Assert.Contains("FINALWORDS", session.View.ScrollbackText, StringComparison.Ordinal);
            Assert.Contains("FINALWORDS", AllText((Control)view), StringComparison.Ordinal); // and still drawn
        }, (session, _, _, _, _) => { });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")]
    public void Hit_tested_terminal_text_and_Enter_reach_the_real_ConPTY_child()
    {
        WithAttachedHost("terminal keyboard", "Write-Output 'INPUTREADY'; "
            + "$line = Read-Host; Write-Output ('RECEIVED=' + $line); Start-Sleep -Seconds 5",
            (session, _, window, _, _) =>
            {
                Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("INPUTREADY", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(15)), "the real ConPTY child did not ask for input");
                window.MouseDown(new Point(30, 30), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(new Point(30, 30), MouseButton.Left, RawInputModifiers.None);
                Assert.True(session.View.IsFocused);
                window.KeyTextInput("terminal-key-proof");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains(
                    "RECEIVED=terminal-key-proof", StringComparison.Ordinal), TimeSpan.FromSeconds(15)),
                    "the real ConPTY child did not receive the typed line");
            }, (session, _, _, _, _) => { });
    }

    // REVIEW (PR-TERM-032): ActiveEscapeListenerCount is the host's own bookkeeping, not the view's subscriptions, so the
    // counting tests above pass with the `KeyDown -=` of DetachEscapeListener removed. The source's coordinator deinit
    // removes the monitor itself (TerminalPanel.swift:202-204): a host that has left the tree must not answer an Escape
    // that a later host of the same long-lived view (:60 .id(selected.id), :242-249 attach) now owns.
    [Fact]
    [Trait("Req", "PR-TERM-032")]
    [Trait("Rule", "R-TEST-4")] // the removal half: the detached host's listener is really gone
    public void REVIEW_A_detached_host_no_longer_answers_Escape_once_a_new_host_holds_the_same_view()
    {
        var oldHostCloses = 0;
        WithAttachedHost("review stale listener", null, (session, host, window, _, runtime) =>
        {
            Assert.True(UiPump.Until(() => TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId),
                TimeSpan.FromSeconds(15)), "the ConPTY shell did not reach its prompt");
            var root = Assert.IsType<Border>(window.Content);
            root.Child = null; // the old host leaves the tree (a tab switch releases it)
            Dispatcher.UIThread.RunJobs();

            var newHostCloses = 0;
            root.Child = new TerminalHost(session, runtime, () => newHostCloses++);
            Settle(window);
            Assert.True(session.View.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(0, oldHostCloses); // :202-204 the old monitor was removed
            Assert.Equal(1, newHostCloses);
        }, (session, _, _, _, _) => { }, () => oldHostCloses++);
    }

    private void WithAttachedHost(
        string label,
        string? command,
        Action<TerminalSession, TerminalHost, AvaloniaWindow, WindowLiveResize, ThreadRuntime> body,
        Action<TerminalSession, TerminalHost, AvaloniaWindow, WindowLiveResize, ThreadRuntime> beforeBody,
        Action? onEscape = null)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = AppModelSamples.Project("Terminal high risk", harness.NewFolder("terminal-high-risk"));
            var thread = AppModelSamples.Thread(project);
            harness.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            var app = harness.MakeModel();
            var runtime = harness.Main.OnMain(() => new ThreadRuntime(thread.Id, app));

            TerminalSession? session = null;
            AvaloniaWindow? window = null;
            try
            {
                harness.Main.OnMain(() =>
                {
                    var currentSession = new TerminalSession(Guid.NewGuid(), TerminalTestFolders.Create(label), "PowerShell", command);
                    session = currentSession;
                    runtime.TerminalWantsFocus = true;
                    var host = new TerminalHost(currentSession, runtime, onEscape ?? (() => runtime.IsTerminalVisible = false));
                    var root = new Border { Child = host };
                    window = new AvaloniaWindow { Content = root, Width = 640, Height = 240 };
                    var resize = new WindowLiveResize(window);
                    AppEnvironment.SetAppModel(root, app);
                    AppEnvironment.SetWindowLiveResize(root, resize);
                    window.Show();
                    Settle(window);
                    beforeBody(currentSession, host, window, resize, runtime);
                    body(currentSession, host, window, resize, runtime);
                });
            }
            finally
            {
                harness.Main.OnMain(() =>
                {
                    window?.Close();
                    session?.Terminate();
                    Dispatcher.UIThread.RunJobs();
                });
            }
        });
    }

    private static void BeginWin32Resize(WindowLiveResize resize)
    {
        var handled = false;
        resize.WndProc(0, 0x0231, 0, 0, ref handled); // WM_ENTERSIZEMOVE
        resize.WndProc(0, 0x0214, 0, 0, ref handled); // WM_SIZING
        Dispatcher.UIThread.RunJobs();
    }

    private static void EndWin32Resize(WindowLiveResize resize)
    {
        var handled = false;
        resize.WndProc(0, 0x0232, 0, 0, ref handled); // WM_EXITSIZEMOVE
        Dispatcher.UIThread.RunJobs();
    }

    private static void Settle(TopLevel window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static string AllText(Control control) =>
        string.Join("\n", LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? ""));

    private static int StaticMetric(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsType<int>(property.GetValue(null));
    }

    private static int IntMetric(object instance, string name) =>
        Assert.IsType<int>(Property(instance, name).GetValue(instance));

    private static bool BoolMetric(object instance, string name) =>
        Assert.IsType<bool>(Property(instance, name).GetValue(instance));

    private static PropertyInfo Property(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property;
    }

    private static FieldInfo Field(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field;
    }

    // The KeyDown subscriptions that a TerminalHost really holds on the session's view, read from Avalonia's own handler
    // table (not from the host's bookkeeping counter): it finds the table by its type, so a changed layout fails loudly.
    private static int RealEscapeSubscriptions(TerminalSession session)
    {
        var table = typeof(Interactive)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType.IsGenericType
                && field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                && field.FieldType.GetGenericArguments()[0] == typeof(RoutedEvent))
            .ToArray();
        Assert.Single(table);
        var handlers = table[0].GetValue(session.View) as System.Collections.IDictionary;
        if (handlers is null || !handlers.Contains(InputElement.KeyDownEvent)) return 0;
        var count = 0;
        foreach (var subscription in (System.Collections.IEnumerable)handlers[InputElement.KeyDownEvent]!)
        {
            var delegates = subscription.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => typeof(Delegate).IsAssignableFrom(field.FieldType))
                .Select(field => field.GetValue(subscription) as Delegate)
                .ToArray();
            Assert.NotEmpty(delegates);
            if (delegates.Any(handler => handler?.Target is TerminalHost)) count++;
        }
        return count;
    }

    private static int RealResizeSubscribers(WindowLiveResize resize)
    {
        var field = typeof(ObservableModel).GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (field.GetValue(resize) as MulticastDelegate)?.GetInvocationList().Length ?? 0;
    }
}
