using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/DroppyCodeApp.swift (B3 52bc792), :7-19 init(), the launch prologue:
//   DCInstallNotificationGuard(); signal(SIGPIPE, SIG_IGN); WebsiteCaptures.prepare(); LegacyMigration.run();
//   HangWatchdog.start(); SwitchLatency.start(), synchronously, in that order, before the scene body (:21-25) and
//   before applicationDidFinishLaunching, with nothing else first.
// The port keeps one of the six as a call (RunPrologue): PR-LAUNCH-001 and PR-WIN-002 windows_path say steps 1, 2 and 3 are
// platform boundaries (an AppKit exception net, SIGPIPE, the capture subsystem), step 4 is not applicable (OD-0008 A5) and
// step 6 is development-only instrumentation (ED-005); HangWatchdog.start() (:17) is the step that runs.
// HangWatchdog.swift:42-60: the first start() latches, takes the time, and starts ONE thread that loops "sleep 0.5 s, ping the
// main queue, check"; a second start() returns without a second thread.
// The watchdog is a process-wide thread with no stop, so these tests run the prologue through PrologueWatchdogRig, which starts
// it once under a manual clock and a manual main queue (never the real ones).
public sealed class DroppyCodePrologueTests
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromMilliseconds(500); // HangWatchdog.swift:52 Thread.sleep(forTimeInterval: 0.5)

    // The step list of the Swift prologue the port does not carry: a call to any of these from RunPrologue would be the port
    // doing something the records say it must not (nothing is installed, ignored, prepared, migrated or timed).
    private static readonly string[] NotCarriedStepOwners = ["SwitchLatency", "LegacyMigration", "WebsiteCaptures", "DCNotificationGuard", "NotificationGuard"];

    [Fact]
    [Trait("Req", "PR-LAUNCH-014")] // App/DroppyCodeApp.swift:16-17; Core/Support/HangWatchdog.swift:42-60
    [Trait("Req", "PR-LAUNCH-001")] // App/DroppyCodeApp.swift:7-19 step 5, the one step the port runs
    [Trait("Req", "PR-WIN-002")] // App/DroppyCodeApp.swift:7-19 step 5
    public void The_prologue_starts_the_watchdog_which_puts_one_block_on_the_main_queue_every_500_ms_and_never_stops()
    {
        var rig = PrologueWatchdogRig.Instance;

        // Started by the prologue: a thread is waiting out its first sleep, and nothing has been posted yet (sleep, then ping).
        Assert.True(rig.WatchdogIsSleeping);
        Assert.Equal(0, rig.Loop.Pending);

        for (var round = 0; round < 3; round++) // "while true": the cadence goes on, it is not a one-off
        {
            Assert.Equal(0, rig.Advance(PingInterval - TimeSpan.FromMilliseconds(1), expectPing: false)); // 499 ms: not yet
            Assert.Equal(1, rig.Advance(TimeSpan.FromMilliseconds(1), expectPing: true)); // 500 ms: one block on the main queue
        }
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-014")] // Core/Support/HangWatchdog.swift:43-48 the started latch: a later start() returns
    public void A_later_prologue_run_starts_no_second_watchdog()
    {
        var rig = PrologueWatchdogRig.Instance;
        Assert.True(rig.WatchdogIsSleeping);

        using (rig.Apply()) // were a second thread started, it must run on the manual clock too, never the real one
        {
            DroppyCodeApp.RunPrologue();
            DroppyCodeApp.RunPrologue();
        }

        Assert.True(rig.WaitForSleepers(1)); // still one thread waiting on the clock
        Thread.Sleep(100);
        Assert.Equal(1, rig.Time.ActiveTimers);
        Assert.Equal(1, rig.Advance(PingInterval, expectPing: true)); // one block per half second, not one per start
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-014")] // Core/Support/HangWatchdog.swift:50-59 thread.name = "hang-watchdog", never keeps the app alive
    public void The_watchdog_is_one_background_thread_named_hang_watchdog_and_a_later_start_keeps_it()
    {
        var rig = PrologueWatchdogRig.Instance;
        Assert.True(rig.WatchdogIsSleeping);
        var first = WatchdogThread();

        using (rig.Apply()) DroppyCodeApp.RunPrologue();

        Assert.NotNull(first);
        Assert.Equal("hang-watchdog", first.Name);
        Assert.True(first.IsAlive);
        Assert.True(first.IsBackground); // :50-58 has no quit path; the thread ends with the process (PR-LAUNCH-021)
        Assert.Same(first, WatchdogThread());
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-001")] // App/DroppyCodeApp.swift:7-19, :8-9 and :13 comments: nothing else runs in the prologue
    [Trait("Req", "PR-WIN-002")] // App/DroppyCodeApp.swift:7-19
    [Trait("Req", "PR-LAUNCH-002")] // App/DroppyCodeApp.swift:10: DCInstallNotificationGuard is not ported (port_impacts.md:384 entry 188)
    [Trait("Req", "PR-LAUNCH-003")] // App/DroppyCodeApp.swift:12: signal(SIGPIPE, SIG_IGN) has no Windows analogue (PA-0004)
    [Trait("Req", "PR-LAUNCH-022")] // App/DroppyCodeApp.swift:18: SwitchLatency.start() installs nothing in a shipped build (ED-005)
    public void The_prologue_makes_exactly_the_one_watchdog_call_and_none_of_the_steps_the_port_does_not_carry()
    {
        var members = CompiledLaunchOrder.Members(typeof(DroppyCodeApp).GetMethod(nameof(DroppyCodeApp.RunPrologue))!);

        var calls = members.OfType<MethodBase>().ToArray();
        var call = Assert.Single(calls); // not two, not zero: the ported step, alone
        Assert.Equal(typeof(HangWatchdog), call.DeclaringType);
        Assert.Equal(nameof(HangWatchdog.Start), call.Name);
        Assert.DoesNotContain(members, member => NotCarriedStepOwners.Any(owner => member.DeclaringType?.Name.Contains(owner, StringComparison.Ordinal) == true));
        Assert.DoesNotContain(members, member => member is FieldInfo); // no setting, library or other state is touched
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-001")] // App/DroppyCodeApp.swift:7-19 runs before the scene (:21-25) and before applicationDidFinishLaunching
    [Trait("Req", "PR-WIN-002")] // App/DroppyCodeApp.swift:13 "Before anything reads settings or the library", before any window
    public void The_entry_point_runs_the_prologue_before_any_window_exists_and_before_launch_begins()
    {
        var members = CompiledLaunchOrder.Members(typeof(Program).GetMethod(nameof(Program.Main))!).ToList();
        int At(Predicate<MemberInfo> match) => members.FindIndex(match);
        var prologue = At(member => member.DeclaringType == typeof(DroppyCodeApp) && member.Name == nameof(DroppyCodeApp.RunPrologue));
        var bind = At(member => member.DeclaringType == typeof(Marrow.Core.Port.MainActor) && member.Name == nameof(Marrow.Core.Port.MainActor.Bind));
        var window = At(member => member is ConstructorInfo && member.DeclaringType == typeof(Avalonia.Controls.Window));
        var startup = At(member => member.Name == "add_Startup");
        var start = At(member => member.DeclaringType?.Name == "ClassicDesktopStyleApplicationLifetime" && member.Name == "Start");

        Assert.True(prologue >= 0, "the entry point never runs the prologue");
        Assert.True(bind >= 0 && bind < prologue, "the watchdog's first ping needs the main actor bound"); // HangWatchdog.swift:62-69
        Assert.True(window > prologue, "a window is made before the prologue"); // the frame clock's top level
        Assert.True(startup > prologue, "launch (the lifetime's Startup, finishLaunching) is hooked before the prologue");
        Assert.True(start > prologue, "the run starts before the prologue");
        // Constructing WindowManager.shared makes the AppModel, which reads the library (WindowManager.swift:10-13): not before.
        Assert.DoesNotContain(members.Take(prologue), member => member.DeclaringType == typeof(Marrow.App.Window.WindowManager));
        Assert.DoesNotContain(members.Take(prologue), member => member.DeclaringType == typeof(AppDelegate) && member.Name == nameof(AppDelegate.ApplicationDidFinishLaunching));
    }

    // The runner of HangWatchdog's one process-wide watchdog, made on first use (HangWatchdog.cs). Nothing public observes the
    // thread's name or kind, so it is read by reflection (ED-119).
    private static Thread? WatchdogThread()
    {
        var shared = typeof(HangWatchdog).GetField("shared", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var runner = shared.GetType().GetProperty("Value")!.GetValue(shared)!;
        return (Thread?)runner.GetType().GetProperty("Thread", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runner);
    }
}
