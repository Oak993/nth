using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Install;

namespace Marrow.App.Tests.Port.Skills;

[Trait("Decision", "ED-326"), Trait("Rule", "R-TEST-4")]
public sealed class CursorSkillsLaunchTests
{
    [Fact]
    public void The_MCP_relay_entry_precedes_all_desktop_and_data_startup_without_running_the_app()
    {
        var calls = MethodCalls.Of(typeof(Marrow.App.Program).GetMethod(nameof(Marrow.App.Program.Main))!);
        var names = calls.Select(c => c.Name).ToArray();
        var relay = Array.IndexOf(names, "RunClient");
        Assert.True(relay >= 0);
        Assert.True(relay < Array.IndexOf(names, "RunVelopackHook"));
        Assert.True(relay < Array.IndexOf(names, "TryAcquireSingleInstanceLock"));
        Assert.True(relay < Array.IndexOf(names, "SetProcessDefault"));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("prior-cursor-chat")]
    public void Cursor_new_and_resumed_runtime_sessions_receive_verified_owned_generations(string? resume)
    {
        using var rig = new Sk17LaunchRig(ProviderKind.Cursor, resume);
        var session = Assert.IsType<Sk17FakeSession>(rig.Ensure());
        Assert.Same(Assert.Single(rig.Leases), session.Configuration.SkillGeneration);
        Assert.True(session.Configuration.SkillGeneration!.Verify());
        Assert.Equal("cursor", Path.GetFileName(Path.GetDirectoryName(session.Configuration.SkillGeneration.Folder)));
        Assert.Equal(resume, session.Configuration.ResumeID);
        Assert.NotNull(typeof(Marrow.Services.Providers.SessionConfiguration).GetProperty("SkillMcpExecutable")!.GetValue(session.Configuration));
        rig.Stop();
        Assert.False(rig.Leases[0].Verify());
    }
    [Fact]
    public void Cursor_stop_during_preparation_cannot_launch_a_late_child()
    {
        using var rig = new Sk17LaunchRig(ProviderKind.Cursor);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.PreparationGate = gate;
        var start = rig.BeginEnsure();
        rig.RuntimeRig.H.Main.Loop.RunUntil(() => rig.Leases.Count == 1, AppModelHarness.Bound);
        rig.Stop(); gate.SetResult();
        Assert.Throws<CancellationError>(() => rig.RuntimeRig.H.Main.Loop.Run(() => start, AppModelHarness.Bound));
        Assert.Empty(rig.Sessions);
        Assert.False(Assert.Single(rig.Leases).Verify());
    }
    [Fact]
    public void Cursor_keeps_the_lease_until_the_actual_child_exit_before_replacement()
    {
        using var rig = new Sk17LaunchRig(ProviderKind.Cursor);
        var first = Assert.IsType<Sk17FakeSession>(rig.Ensure());
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.SkillReadersExited = exited.Task;
        rig.Stop();
        Assert.True(Assert.Single(rig.Leases).Verify());
        var next = rig.BeginEnsure(); Assert.False(next.IsCompleted);
        exited.SetResult();
        Assert.NotSame(first, rig.RuntimeRig.H.Main.Loop.Run(() => next, AppModelHarness.Bound));
        Assert.False(rig.Leases[0].Verify());
    }
    [Fact]
    public void Cursor_default_preparation_uses_the_bound_host()
    {
        using var rig = new Sk17LaunchRig(ProviderKind.Cursor);
        using var host = SkillsHost.Open(rig.RuntimeRig.H.Root, new SkillsHostOptions
        {
            Catalogue = new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-05", Items = [] },
            Protection = rig.Files.Protection, InstallLock = new WindowsInstallLock(),
        });
        var type = typeof(ThreadRuntime).Assembly.GetType("Marrow.App.Port.Skills.CursorSkills")!;
        var boundary = (Ambient<Func<string, string, Task<Generation>>>)type.GetProperty("Prepare")!.GetValue(null)!;
        using var preparation = boundary.Override(type.GetMethod("PrepareOwned", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Func<string, string, Task<Generation>>>());
        try
        {
            var generation = Assert.IsType<Sk17FakeSession>(rig.Ensure()).Configuration.SkillGeneration;
            Assert.NotNull(generation);
            Assert.StartsWith(host.Paths.Views, generation.Folder, StringComparison.Ordinal);
            Assert.Equal("cursor", Path.GetFileName(Path.GetDirectoryName(generation.Folder)));
            Assert.True(generation.Verify());
            rig.Stop(); Assert.False(generation.Verify());
        }
        finally { host.Dispose(); rig.Files.Trees.Unlock(host.Paths.Views); }
    }
}
