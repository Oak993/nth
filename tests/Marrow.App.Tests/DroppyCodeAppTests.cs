using System;
using System.Reflection;
using Xunit;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/DroppyCodeApp.swift (B2 1fbf81e), :1-27.
public sealed class DroppyCodeAppTests
{
    private static string LockName() => "Local\\Marrow.Tests.SingleInstance." + Guid.NewGuid().ToString("N");

    [Fact]
    [Trait("Decision", "ED-002")] // App/DroppyCodeApp.swift:1-27, engineering_decisions.md ED-002
    public void The_first_instance_to_ask_acquires_the_lock()
    {
        var name = LockName();
        var acquired = DroppyCodeApp.TryAcquireSingleInstanceLock(name, out var handle);

        try
        {
            Assert.True(acquired);
            Assert.NotNull(handle);
        }
        finally
        {
            handle?.Dispose();
        }
    }

    [Fact]
    [Trait("Decision", "ED-002")] // App/DroppyCodeApp.swift:1-27, engineering_decisions.md ED-002
    public void A_second_instance_asking_for_the_same_lock_is_refused_while_the_first_holds_it()
    {
        var name = LockName();
        Assert.True(DroppyCodeApp.TryAcquireSingleInstanceLock(name, out var first));

        try
        {
            var second = DroppyCodeApp.TryAcquireSingleInstanceLock(name, out var secondHandle);

            Assert.False(second);
            Assert.Null(secondHandle);
        }
        finally
        {
            first?.Dispose();
        }
    }

    [Fact]
    [Trait("Decision", "ED-002")] // App/DroppyCodeApp.swift:1-27, engineering_decisions.md ED-002
    public void Releasing_the_lock_lets_a_later_instance_acquire_it()
    {
        var name = LockName();
        Assert.True(DroppyCodeApp.TryAcquireSingleInstanceLock(name, out var first));
        first?.Dispose();

        var acquired = DroppyCodeApp.TryAcquireSingleInstanceLock(name, out var second);

        try
        {
            Assert.True(acquired);
            Assert.NotNull(second);
        }
        finally
        {
            second?.Dispose();
        }
    }

    [Fact]
    [Trait("Decision", "ED-002")] // App/DroppyCodeApp.swift:1-27, engineering_decisions.md ED-002
    public void Two_different_lock_names_never_contend()
    {
        Assert.True(DroppyCodeApp.TryAcquireSingleInstanceLock(LockName(), out var first));
        Assert.True(DroppyCodeApp.TryAcquireSingleInstanceLock(LockName(), out var second));

        first?.Dispose();
        second?.Dispose();
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-113")] // project.yml:46; App/DroppyCodeApp.swift:3
    public void The_assembly_carries_the_exact_copyright_string()
    {
        var copyright = typeof(DroppyCodeApp).Assembly
            .GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        Assert.Equal(
            "Copyright © 2026 Jordy Spruit (Droppy). Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app. GNU AGPL v3.0 with additional attribution terms.",
            copyright);
    }

    // No dedicated test for RunPrologue() itself: it is exactly `HangWatchdog.Start()`, and Start() spawns a real,
    // never-ending background thread that pings MainActor every 0.5s with no stop() (PR-LAUNCH-014's own text says
    // so) - calling it for real inside a test starts a thread that outlives the test and can crash a LATER,
    // unrelated test in the same xUnit process the moment it runs without a bound MainActor (this crashed the
    // whole test host here: "MainActor is not bound" from HangWatchdog.Runner.Ping(), aborting the run). Confirmed
    // by tests/Marrow.Core.Tests/Support/HangWatchdogTests.cs, which deliberately never calls the real Start()
    // either, only its internal State/formatting/report logic. RunPrologue's one-line body is verified by reading
    // DroppyCodeApp.cs instead.
}

// STOP App.DroppyCodeApp #1 (helper Claude, 2026-09-25)
// Kind: missing dependency
// Swift: DroppyCode/App/DroppyCodeApp.swift:21-25 (B2 1fbf81e); DroppyCode/App/AppDelegate.swift:1-87
// Requirements: PR-LAUNCH-004, PR-LAUNCH-072 (the reopen path), PR-WIN-001
// What is unclear or missing: this packet covers DroppyCodeApp.swift only. AppDelegate.swift (a separate, not yet
// started unit) owns applicationShouldHandleReopen, the actual second-launch reactivation ED-002 asks for; and
// App.Window.WindowManager (Shared, ShowMain) is still MARROW_STUB, so even a ported AppDelegate could not front a
// real window yet. Program.cs's Main() calls WindowManager.Shared.ShowMain() directly, correctly wired but
// currently throwing, which is the intended, visible signal of exactly what remains.
// Readings or options: (a) invent a reopen mechanism (a named pipe, a window message) ahead of AppDelegate's own
// unit -> guesses at behavior that file, not this one, specifies; (b) leave the second-launch path as "acquire
// fails, exit 0" -> safe (never contends for the store root) but does not re-front the running instance.
// Needed to proceed: App.AppDelegate ported (owns applicationShouldHandleReopen) and App.Window.WindowManager
// ported (owns ShowMain(), NSApp.activate()-equivalent).
// Done so far: the six-step prologue (minus the three platform-boundary steps and the two not-ported/inert ones)
// and the single-instance lock are implemented and tested; the scene's only real effect (constructing WindowManager
// and showing the main window) is wired to the real, still-throwing callee rather than stubbed out.
// Boot merge (2026-09-26): both dependencies named above are in impl/b2 (App.AppDelegate, App.Window.WindowManager),
// and the Boot unit wrote the ED-002 reopen path: a second launch sets Port/ReopenSignal.cs's signal and exits, and the
// running instance runs AppDelegate.swift:36-39 through Port/AppShell.cs Reopen (tests: Port/ReopenSignalTests.cs,
// Port/AppShellSecondTests.cs, PR-LAUNCH-072, marked IMPLEMENTED at that merge). PR-LAUNCH-004 and PR-WIN-001 are not
// re-examined here.