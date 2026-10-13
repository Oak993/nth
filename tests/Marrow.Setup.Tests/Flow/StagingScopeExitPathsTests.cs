using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Flow;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers": the staging folder is deleted on every exit path the setup
// controls: success, Close on any screen, the Problem screen, and an unhandled exception through a last-chance handler), unit U9
// (ED-NEW-inst-u9-1). The window's own paths are Dispose (the window closed on any screen), Release (Ready or Problem showing,
// tested with a session in StagingScopeSessionTests) and the two process-wide handlers, called here directly: a real unhandled
// exception would end the test host. Temporary folders only.
public sealed class StagingScopeExitPathsTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    private StagingScope Staged(out string folder, bool lastChance = false)
    {
        var scope = StagingScope.Create(area.LocalAppData, area.Temp, "1.0.0", lastChance);
        folder = scope.Ensure();
        area.Put(Path.Combine(folder, InstallOptions.OptionsFileName), "{}");
        area.Put(Path.Combine(folder, SetupFolders.EngineFileName));
        return scope;
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Closing_the_window_on_any_screen_deletes_the_staging_folder()
    {
        var scope = Staged(out var folder);

        scope.Dispose();

        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Dispose_twice_and_use_after_it_are_harmless_but_a_dead_scope_stages_nothing_new()
    {
        var scope = Staged(out var folder);

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(0, scope.Release().Removed);
        Assert.Throws<ObjectDisposedException>(() => scope.Ensure());
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unhandled_exception_deletes_the_staging_folder()
    {
        using var scope = Staged(out var folder);

        scope.OnUnhandledException(this, new UnhandledExceptionEventArgs(new InvalidOperationException("boom"), isTerminating: true));

        Assert.False(Directory.Exists(folder));
        Assert.False(scope.IsHeld);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_process_ending_by_any_other_way_deletes_the_staging_folder()
    {
        using var scope = Staged(out var folder);

        scope.OnProcessExit(this, EventArgs.Empty);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_last_chance_handlers_leave_a_folder_a_running_engine_still_uses_for_the_next_sweep()
    {
        using var scope = Staged(out var folder);
        var running = area.Hold(Path.Combine(folder, SetupFolders.EngineFileName));

        scope.OnUnhandledException(this, new UnhandledExceptionEventArgs(new InvalidOperationException("boom"), isTerminating: true));

        Assert.Equal("{}", File.ReadAllText(Path.Combine(folder, InstallOptions.OptionsFileName)));
        area.Release(running);
        Assert.True(SetupSweeper.Sweep(area.LocalAppData, area.Temp).Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_last_chance_handlers_do_not_wait_for_a_lock_another_thread_holds()
    {
        using var scope = Staged(out var folder);
        using var holding = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var gate = typeof(StagingScope).GetField("gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scope)!;
        var holder = new Thread(() =>
        {
            lock (gate)
            {
                holding.Set();
                done.Wait(TimeSpan.FromSeconds(10));
            }
        });
        holder.Start();
        Assert.True(holding.Wait(TimeSpan.FromSeconds(5)));
        var started = DateTime.UtcNow;

        scope.OnProcessExit(this, EventArgs.Empty);

        var waited = DateTime.UtcNow - started;
        done.Set();
        holder.Join();
        Assert.True(waited < TimeSpan.FromSeconds(5), $"the handler waited {waited.TotalSeconds:0.0} s");
        Assert.True(Directory.Exists(folder), "it gave up rather than hang the end of the process; the next sweep takes the folder");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Hooking_the_process_handlers_and_disposing_leaves_nothing_behind()
    {
        var scope = Staged(out var folder, lastChance: true);

        scope.Dispose();

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Release_from_many_threads_at_once_is_safe()
    {
        using var scope = Staged(out var folder);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(scope.Release)));

        Assert.Equal(1, results.Sum(r => r.Removed));
        Assert.All(results, r => Assert.True(r.Clean));
        Assert.False(Directory.Exists(folder));
    }
}
