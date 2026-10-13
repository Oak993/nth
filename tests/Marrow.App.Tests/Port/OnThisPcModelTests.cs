using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a "nth's Settings" and section 8 "Open at sign-in", unit U8
// (ED-NEW-inst-u8-1 and -2). The state and actions behind Settings > General > "On this PC", on FakeShellLinks and
// FakeUserRegistry with a worker and a main actor the test drives step by step: the machine is the state, scans are
// throttled to one per 2 s, a tap acts at once and springs back with a reason when the machine disagrees, taps run in
// order, and no shell or registry call happens on the UI thread. Nothing here touches the real Desktop or HKCU.
[Collection("Headless")]
public sealed class OnThisPcModelTests(HeadlessSession headless)
{
    private const string Root = OnThisPcRig.Root;
    private const string CurrentExe = OnThisPcRig.CurrentExe;
    private const string Run = UninstallEntry.RunKeyPath;
    private const string Approved = SignInEntry.ApprovedKeyPath;
    private const string OursCommand = "\"" + CurrentExe + "\" --at-sign-in";
    private const int AccessDenied = unchecked((int)0x80070005);

    private void OnUi(Action body) => headless.OnUiThread(body);

    // ---- not a Velopack install ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Without_a_Velopack_install_nothing_is_asked_of_the_worker_and_nothing_changes()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig(available: false);

            rig.Model.Refresh(pageOpened: true);
            rig.Model.RequestDesktopShortcut(true);
            rig.Model.RequestSignIn(true);

            Assert.False(rig.Model.IsAvailable);
            Assert.Empty(rig.Work);
            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.False(rig.Model.SignInOn);
            Assert.Equal(0, rig.ShellMade + rig.RegistryMade);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void One_model_per_platform()
    {
        var one = new OnThisPcRig().Platform;
        var other = new OnThisPcRig().Platform;

        Assert.Same(OnThisPcModel.For(one), OnThisPcModel.For(one));
        Assert.NotSame(OnThisPcModel.For(one), OnThisPcModel.For(other));
    }

    // ---- scanning ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Opening_the_page_scans_on_the_worker_and_shows_what_the_machine_has_without_animation()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, OnThisPcRig.Made());
            rig.Registry.Seed(Run, "nth", OursCommand);

            rig.Model.Refresh(pageOpened: true);

            Assert.Single(rig.Work); // asked, not done: the UI thread touched nothing
            Assert.Equal(0, rig.Shell.Reads);
            Assert.Equal(0, rig.ShellMade);
            Assert.False(rig.Model.DesktopShortcutOn); // the last known answer, until the scan answers
            Assert.False(rig.Model.SignInOn);

            rig.RunWorker();
            Assert.False(rig.Model.DesktopShortcutOn); // still the main actor's turn to publish
            rig.RunPosts();

            Assert.True(rig.Model.DesktopShortcutOn);
            Assert.True(rig.Model.SignInOn);
            Assert.True(rig.Model.ChangeIsExternal); // a change from outside: the switch moves without animation
            Assert.Null(rig.Model.DesktopShortcutNote);
            Assert.False(rig.Model.IsScanning);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_scan_that_finds_nothing_leaves_both_off_and_changes_nothing_on_the_machine()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();

            rig.Model.Refresh(pageOpened: true);
            rig.Pump();

            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.False(rig.Model.SignInOn);
            Assert.Equal(0, rig.Shell.Changes);
            Assert.Equal(0, rig.Registry.Writes);
            Assert.Empty(rig.Shell.Notifications);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Scans_run_at_most_once_every_two_seconds_and_never_two_at_once()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();

            rig.Model.Refresh(pageOpened: true);
            rig.Model.Refresh(); // one is already in flight
            rig.Model.Refresh();
            Assert.Single(rig.Work);

            rig.Pump();
            rig.Model.Refresh();
            Assert.Empty(rig.Work); // answered just now

            rig.Clock.Advance(TimeSpan.FromSeconds(1.9));
            rig.Model.Refresh();
            Assert.Empty(rig.Work);

            rig.Clock.Advance(TimeSpan.FromSeconds(0.2));
            rig.Model.Refresh();
            Assert.Single(rig.Work);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Activating_the_window_after_the_shortcut_was_deleted_turns_the_switch_off_in_place()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, OnThisPcRig.Made());
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            Assert.True(rig.Model.DesktopShortcutOn);

            rig.Shell.DeleteLink(FakeShellLinks.DesktopLink); // the user deleted it in Explorer
            rig.Clock.Advance(TimeSpan.FromSeconds(5));
            rig.Model.Refresh(); // the window was activated
            rig.Pump();

            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.True(rig.Model.ChangeIsExternal);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Opening_the_page_clears_an_old_reason_even_when_the_scan_is_throttled()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, new ShellLink(@"C:\Old\nth\nth.exe"));
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            rig.Model.RequestDesktopShortcut(true);
            rig.Pump();
            Assert.Equal(OnThisPcModel.ShortcutNameTaken, rig.Model.DesktopShortcutNote);

            rig.Model.Refresh(pageOpened: true); // inside the 2 s gap: no scan, but the old reason goes

            Assert.Null(rig.Model.DesktopShortcutNote);
            Assert.Empty(rig.Work);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_scan_that_cannot_look_keeps_the_last_known_answer()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, OnThisPcRig.Made());
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            Assert.True(rig.Model.DesktopShortcutOn);

            rig.Shell.Fail(FakeShellLinks.Call.EnumerateLinks, AccessDenied);
            rig.Clock.Advance(TimeSpan.FromSeconds(3));
            rig.Model.Refresh();
            rig.Pump();

            Assert.True(rig.Model.DesktopShortcutOn);
            Assert.Null(rig.Model.DesktopShortcutNote);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_platform_that_cannot_be_made_is_a_quiet_no_scan_and_does_not_stay_stuck()
    {
        OnUi(() =>
        {
            var broken = new OnThisPcPlatform(Root, () => throw new InvalidOperationException("no shell"), () => new FakeUserRegistry());
            var work = new List<Action>();
            var posts = new List<Action>();
            var clock = new ManualTimeProvider();
            var model = new OnThisPcModel(broken, clock, job => { work.Add(job); return Task.CompletedTask; }, posts.Add);

            model.Refresh(pageOpened: true);
            work.ForEach(job => job());
            posts.ForEach(post => post());

            Assert.False(model.IsScanning);
            Assert.False(model.DesktopShortcutOn);
            clock.Advance(TimeSpan.FromSeconds(3));
            model.Refresh();
            Assert.Equal(2, work.Count); // it tries again later
        });
    }

    // ---- the desktop shortcut switch ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_the_desktop_shortcut_on_flips_at_once_and_creates_it_on_the_worker()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();

            rig.Model.RequestDesktopShortcut(true);

            Assert.True(rig.Model.DesktopShortcutOn); // optimistic
            Assert.False(rig.Model.ChangeIsExternal); // the user's own tap: the switch animates
            Assert.Equal(0, rig.Shell.Changes); // nothing yet: the call is the worker's

            rig.Pump();

            Assert.True(rig.Model.DesktopShortcutOn);
            Assert.Null(rig.Model.DesktopShortcutNote);
            Assert.False(rig.Model.ChangeIsExternal);
            Assert.Equal(new[] { FakeShellLinks.DesktopLink }, rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop));
            Assert.True(rig.Shell.Peek(FakeShellLinks.DesktopLink)!.IsMadeByNth);
            Assert.Contains((ShellChange.Created, FakeShellLinks.DesktopLink), rig.Shell.Notifications);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_the_desktop_shortcut_off_removes_only_the_ones_nth_made()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, OnThisPcRig.Made());
            rig.Shell.Seed(FakeShellLinks.OneDriveDesktop + @"\nth - Shortcut.lnk", OnThisPcRig.Users());
            rig.Shell.Seed(FakeShellLinks.OneDriveDesktop + @"\Renamed.lnk", OnThisPcRig.Made());
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            Assert.True(rig.Model.DesktopShortcutOn);

            rig.Model.RequestDesktopShortcut(false);
            Assert.False(rig.Model.DesktopShortcutOn);
            rig.Pump();

            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.Null(rig.Model.DesktopShortcutNote);
            Assert.Equal(new[] { FakeShellLinks.OneDriveDesktop + @"\nth - Shortcut.lnk" }, rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop));
            Assert.Contains((ShellChange.Deleted, FakeShellLinks.DesktopLink), rig.Shell.Notifications);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_foreign_nth_lnk_springs_the_switch_back_with_the_name_taken_reason_and_is_left_untouched()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, new ShellLink(@"C:\Old\nth\nth.exe"));

            rig.Model.RequestDesktopShortcut(true);
            Assert.True(rig.Model.DesktopShortcutOn);
            rig.Pump();

            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.Equal("Another \"nth\" shortcut is already on your desktop.", rig.Model.DesktopShortcutNote);
            Assert.False(rig.Model.ChangeIsExternal); // the spring-back animates
            Assert.Equal(@"C:\Old\nth\nth.exe", rig.Shell.Peek(FakeShellLinks.DesktopLink)!.Target);
            Assert.Equal(0, rig.Shell.Changes);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_users_own_nth_lnk_into_the_root_is_kept_and_the_switch_still_says_it_is_not_nths()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, OnThisPcRig.Users());

            rig.Model.RequestDesktopShortcut(true);
            rig.Pump();

            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.Equal(OnThisPcModel.ShortcutNameTaken, rig.Model.DesktopShortcutNote);
            Assert.Equal("--mine", rig.Shell.Peek(FakeShellLinks.DesktopLink)!.Arguments);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_shell_refusal_springs_the_switch_back_with_the_generic_reason()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Fail(FakeShellLinks.Call.WriteLink, AccessDenied);

            rig.Model.RequestDesktopShortcut(true);
            rig.Pump();

            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.Equal("Windows didn't allow it. Try again.", rig.Model.DesktopShortcutNote);
            Assert.Empty(rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_removal_that_cannot_delete_leaves_the_switch_on_with_the_generic_reason()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, OnThisPcRig.Made());
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            rig.Shell.Fail(FakeShellLinks.Call.DeleteLink, AccessDenied);

            rig.Model.RequestDesktopShortcut(false);
            rig.Pump();

            Assert.True(rig.Model.DesktopShortcutOn);
            Assert.Equal(OnThisPcModel.NotAllowed, rig.Model.DesktopShortcutNote);
            Assert.NotNull(rig.Shell.Peek(FakeShellLinks.DesktopLink));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_next_tap_clears_the_old_reason_at_once()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Fail(FakeShellLinks.Call.WriteLink, AccessDenied, times: 1);
            rig.Model.RequestDesktopShortcut(true);
            rig.Pump();
            Assert.Equal(OnThisPcModel.NotAllowed, rig.Model.DesktopShortcutNote);

            rig.Model.RequestDesktopShortcut(true);
            Assert.Null(rig.Model.DesktopShortcutNote);
            rig.Pump();

            Assert.True(rig.Model.DesktopShortcutOn);
            Assert.Null(rig.Model.DesktopShortcutNote);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Quick_taps_run_in_order_and_only_the_last_one_has_the_last_word()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();

            rig.Model.RequestDesktopShortcut(true);
            rig.Model.RequestDesktopShortcut(false);
            rig.Model.RequestDesktopShortcut(true);
            rig.Model.RequestDesktopShortcut(false);
            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.Single(rig.Work); // one worker, four jobs behind it

            rig.RunWorker();
            Assert.Equal(4, rig.Posts.Count);
            var seen = new List<bool>();
            foreach (var post in rig.Posts.ToArray())
            {
                post();
                seen.Add(rig.Model.DesktopShortcutOn);
            }
            rig.Posts.Clear();

            Assert.All(seen, on => Assert.False(on)); // the switch never jumped to the middle taps' answers
            Assert.Empty(rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop));
            Assert.Null(rig.Model.DesktopShortcutNote);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_scan_that_started_before_a_tap_does_not_overwrite_the_tap()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Model.Refresh(pageOpened: true); // the scan will see no shortcut
            rig.Model.RequestDesktopShortcut(true); // the tap is queued behind it
            rig.RunWorker();
            Assert.Equal(2, rig.Posts.Count);

            rig.Posts[0](); // the scan's answer arrives first
            Assert.True(rig.Model.DesktopShortcutOn);
            rig.Posts[1]();
            Assert.True(rig.Model.DesktopShortcutOn);
            Assert.Single(rig.Shell.LinksIn(FakeShellLinks.OneDriveDesktop));
        });
    }

    // ---- the sign-in switch ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_open_at_sign_in_on_writes_the_run_value_for_the_launch_target()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();

            rig.Model.RequestSignIn(true);
            Assert.True(rig.Model.SignInOn);
            Assert.Equal(0, rig.Registry.Writes);
            rig.Pump();

            Assert.True(rig.Model.SignInOn);
            Assert.Null(rig.Model.SignInNote);
            Assert.Equal(OursCommand, rig.Registry.GetValue(Run, "nth"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_sign_in_entry_follows_Starts_target_and_never_the_stub()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.AddFile(Root + @"\nth.exe"); // the Velopack stub exists

            rig.Model.RequestSignIn(true);
            rig.Pump();

            Assert.Equal(OursCommand, rig.Registry.GetValue(Run, "nth"));
            Assert.DoesNotContain(Root + "\\nth.exe\"", (string)rig.Registry.GetValue(Run, "nth")!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_open_at_sign_in_off_removes_the_value()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Registry.Seed(Run, "nth", OursCommand);
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            Assert.True(rig.Model.SignInOn);

            rig.Model.RequestSignIn(false);
            rig.Pump();

            Assert.False(rig.Model.SignInOn);
            Assert.Null(rig.Registry.GetValue(Run, "nth"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_entry_Windows_turned_off_shows_off_and_one_tap_turns_it_back_on_for_real()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Registry.Seed(Run, "nth", OursCommand);
            rig.Registry.Seed(Approved, "nth", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            Assert.False(rig.Model.SignInOn); // it will not run, so the switch does not say it will

            rig.Model.RequestSignIn(true);
            rig.Pump();

            Assert.True(rig.Model.SignInOn);
            Assert.Null(rig.Registry.GetValue(Approved, "nth"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_value_named_nth_that_is_not_nths_springs_back_with_a_reason_and_stays()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Registry.Seed(Run, "nth", @"C:\Old\nth\nth.exe");

            rig.Model.RequestSignIn(true);
            rig.Pump();

            Assert.False(rig.Model.SignInOn);
            Assert.Equal("Another start-up entry named \"nth\" is already set.", rig.Model.SignInNote);
            Assert.Equal(@"C:\Old\nth\nth.exe", rig.Registry.GetValue(Run, "nth"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_registry_that_refuses_springs_the_sign_in_switch_back_with_the_generic_reason()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig(wrapRegistry: registry => new RefusingUserRegistry(registry));
            rig.Registry.Seed(Run, "nth", OursCommand);
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            Assert.True(rig.Model.SignInOn);

            rig.Model.RequestSignIn(false);
            Assert.False(rig.Model.SignInOn);
            rig.Pump();

            Assert.True(rig.Model.SignInOn);
            Assert.Equal(OnThisPcModel.NotAllowed, rig.Model.SignInNote);
            Assert.Equal(OursCommand, rig.Registry.GetValue(Run, "nth"));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_two_switches_do_not_disturb_each_other()
    {
        OnUi(() =>
        {
            var rig = new OnThisPcRig();
            rig.Shell.Seed(FakeShellLinks.DesktopLink, new ShellLink(@"C:\Old\nth\nth.exe"));

            rig.Model.RequestDesktopShortcut(true); // refused: a foreign nth.lnk
            rig.Model.RequestSignIn(true);
            rig.Pump();

            Assert.False(rig.Model.DesktopShortcutOn);
            Assert.NotNull(rig.Model.DesktopShortcutNote);
            Assert.True(rig.Model.SignInOn);
            Assert.Null(rig.Model.SignInNote);
        });
    }

    // ---- never on the UI thread ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Every_shell_and_registry_call_runs_off_the_UI_thread()
    {
        OnUi(() =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var fakeShell = new FakeShellLinks();
            fakeShell.AddFile(CurrentExe);
            fakeShell.Seed(FakeShellLinks.DesktopLink, OnThisPcRig.Made());
            var shell = new ThreadSpyShellLinks(fakeShell);
            var registry = new FakeUserRegistry();
            registry.SeedKey(Run);
            var spyRegistry = new ThreadSpyUserRegistry(registry);
            var workers = new List<Thread>();
            var posts = new List<Action>();
            // a dedicated thread per burst, like a pool thread but one the waiting test thread cannot run the job on
            var model = new OnThisPcModel(new OnThisPcPlatform(Root, () => shell, () => spyRegistry), new ManualTimeProvider(),
                work => { var worker = new Thread(() => work()) { IsBackground = true }; lock (workers) workers.Add(worker); worker.Start(); return Task.CompletedTask; }, posts.Add);

            model.Refresh(pageOpened: true);
            model.RequestDesktopShortcut(false);
            model.RequestSignIn(true);
            JoinAll(workers);

            Assert.NotEmpty(shell.Threads);
            Assert.NotEmpty(spyRegistry.Threads);
            Assert.DoesNotContain(uiThread, shell.Threads);
            Assert.DoesNotContain(uiThread, spyRegistry.Threads);
            Assert.Empty(fakeShell.LinksIn(FakeShellLinks.OneDriveDesktop)); // and the work still got done
            Assert.Equal(OursCommand, registry.GetValue(Run, "nth"));
        });
    }

    // the worker threads are joined here, in a helper, never inside the test method (xUnit1031)
    private static void JoinAll(List<Thread> workers)
    {
        Thread[] snapshot;
        lock (workers) snapshot = [.. workers];
        foreach (var worker in snapshot) worker.Join();
    }
}
