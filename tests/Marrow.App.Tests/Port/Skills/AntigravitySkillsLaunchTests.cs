using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Install;

namespace Marrow.App.Tests.Port.Skills;

// SK-17 / OD-0099 Q-SKILLS-8 A: exercise the actual ThreadRuntime factory with offline sessions and real fake-protected leases.
[Trait("Decision", "ED-326"), Trait("Rule", "R-TEST-4")]
public sealed class AntigravitySkillsLaunchTests
{
    private static IDisposable UseProductionPreparation()
    {
        var type = typeof(ThreadRuntime).Assembly.GetType("Marrow.App.Port.Skills.AntigravitySkills")!;
        var boundary = (Ambient<Func<string, string, Task<Generation>>>)type.GetProperty("Prepare")!.GetValue(null)!;
        var prepare = type.GetMethod("PrepareOwned", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, string, Task<Generation>>>();
        return boundary.Override(prepare);
    }
    [Fact]
    public void The_production_preparation_connects_the_bound_data_root_host_to_the_runtime_factory()
    {
        using var rig = new Sk17LaunchRig();
        using var host = SkillsHost.Open(rig.RuntimeRig.H.Root, new SkillsHostOptions
        {
            Catalogue = new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-05", Items = [] },
            Protection = rig.Files.Protection, InstallLock = new WindowsInstallLock(),
        });
        using var preparation = UseProductionPreparation();
        try
        {
            var session = Assert.IsType<Sk17FakeSession>(rig.Ensure());
            var generation = session.Configuration.SkillGeneration;
            Assert.NotNull(generation);
            Assert.StartsWith(host.Paths.Views, generation.Folder, StringComparison.Ordinal);
            Assert.True(generation.Verify());
            Assert.Equal("agy", Path.GetFileName(Path.GetDirectoryName(generation.Folder)));
            rig.Stop();
            Assert.False(generation.Verify());
        }
        finally { host.Dispose(); rig.Files.Trees.Unlock(host.Paths.Views); }
    }
    [Fact]
    public void An_unavailable_host_refuses_the_launch_with_a_provider_error_before_any_child_starts()
    {
        using var rig = new Sk17LaunchRig();
        using var preparation = UseProductionPreparation();
        var error = Assert.Throws<ProviderError>(() => rig.Ensure());
        Assert.Equal("The skills host is unavailable for this run.", Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase).Message);
        Assert.Empty(rig.Sessions);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("prior-chat")]
    public void Every_new_or_resumed_Antigravity_launch_receives_a_verified_owned_generation(string? resume)
    {
        using var rig = new Sk17LaunchRig(resume: resume);
        var session = Assert.IsType<Sk17FakeSession>(rig.Ensure());
        Assert.Same(Assert.Single(rig.Leases), session.Configuration.SkillGeneration);
        Assert.True(session.Configuration.SkillGeneration!.Verify());
        Assert.Equal(resume, session.Configuration.ResumeID);
        Assert.Equal(rig.RuntimeRig.Directory, Assert.Single(rig.PreparedDirectories));
        Assert.Equal(1, session.Starts);
    }
    [Fact]
    public void Unreadable_switches_fail_with_localized_provider_text_and_are_preserved()
    {
        using var rig = new Sk17LaunchRig();
        using var host = SkillsHost.Open(rig.RuntimeRig.H.Root, new SkillsHostOptions
        {
            Catalogue = new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-05", Items = [] },
            Protection = rig.Files.Protection, InstallLock = new WindowsInstallLock(),
        });
        Directory.CreateDirectory(host.Paths.Skills);
        File.WriteAllText(host.Paths.EnabledFile, "{ broken fixture");
        using var preparation = UseProductionPreparation();
        var error = Assert.Throws<ProviderError>(() => rig.Ensure());
        var failure = Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        Assert.Equal(failure.Message, SwiftErrors.LocalizedDescription(error));
        Assert.Equal("{ broken fixture", File.ReadAllText(host.Paths.EnabledFile));
        Assert.Empty(rig.Sessions);
    }
    [Fact]
    public void Reuse_keeps_the_same_lease_and_stop_releases_it_before_a_fresh_launch()
    {
        using var rig = new Sk17LaunchRig();
        var first = rig.Ensure();
        Assert.Same(first, rig.Ensure());
        var lease = Assert.Single(rig.Leases);
        Assert.True(lease.Verify());
        rig.Stop();
        Assert.False(lease.Verify());
        Assert.NotSame(first, rig.Ensure());
        Assert.Equal(2, rig.Leases.Count);
        Assert.True(rig.Leases[1].Verify());
    }
    [Fact]
    public void Failed_start_releases_its_lease_and_allows_the_same_chat_to_start_again()
    {
        using var rig = new Sk17LaunchRig();
        rig.StartFailure = new IOException("offline launch failure");
        var error = Assert.Throws<ProviderError>(() => rig.Ensure());
        Assert.Equal("offline launch failure", Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase).Message);
        Assert.False(Assert.Single(rig.Leases).Verify());
        Assert.Equal(1, Assert.Single(rig.Sessions).Stops);
        rig.StartFailure = null;
        Assert.NotNull(rig.Ensure());
        Assert.True(rig.Leases[1].Verify());
    }
    [Fact]
    public void Constructor_failure_releases_the_prepared_generation()
    {
        using var rig = new Sk17LaunchRig();
        rig.ConstructionFailure = new InvalidOperationException("offline constructor failure");
        Assert.Throws<InvalidOperationException>(() => rig.Ensure());
        Assert.False(Assert.Single(rig.Leases).Verify());
        Assert.Empty(rig.Sessions);
    }
    [Fact]
    public void A_failed_resume_releases_its_lease_before_the_new_conversation_retry()
    {
        using var rig = new Sk17LaunchRig(resume: "lost-chat");
        rig.StartFailure = new ProviderError(new ProviderError.Case.Failed("no conversation found"));
        rig.StartFailureOnce = true;
        var session = Assert.IsType<Sk17FakeSession>(rig.Ensure());
        Assert.Equal(2, rig.Leases.Count);
        Assert.False(rig.Leases[0].Verify());
        Assert.True(rig.Leases[1].Verify());
        Assert.Same(rig.Leases[1], session.Configuration.SkillGeneration);
        Assert.Null(session.Configuration.ResumeID);
        Assert.Equal(1, rig.Sessions[0].Stops);
    }
    [Fact]
    public void A_reused_session_refuses_a_generation_damaged_between_turns()
    {
        using var rig = new Sk17LaunchRig();
        _ = rig.Ensure();
        var lease = Assert.Single(rig.Leases);
        rig.Files.Trees.Unlock(lease.Folder);
        File.AppendAllText(Path.Combine(Assert.Single(lease.Skills).Folder, "SKILL.md"), "damage");
        var error = Assert.Throws<ProviderError>(() => rig.Ensure());
        Assert.Equal("The skill generation failed verification before the next turn.", Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase).Message);
        Assert.Single(rig.Sessions);
    }
    [Fact]
    public void Stop_keeps_the_lease_until_the_child_exits_and_the_next_launch_waits_for_that_exit()
    {
        using var rig = new Sk17LaunchRig();
        var first = Assert.IsType<Sk17FakeSession>(rig.Ensure());
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.SkillReadersExited = exited.Task;
        var lease = Assert.Single(rig.Leases);
        rig.Stop();
        Assert.True(lease.Verify());
        var next = rig.BeginEnsure();
        Assert.False(next.IsCompleted);
        Assert.Single(rig.Leases);
        exited.SetResult();
        Assert.NotSame(first, rig.RuntimeRig.H.Main.Loop.Run(() => next, AppModelHarness.Bound));
        Assert.False(lease.Verify());
        Assert.Equal(2, rig.Leases.Count);
    }
    [Fact]
    public void Stop_during_preparation_discards_the_late_generation_without_starting_a_child()
    {
        using var rig = new Sk17LaunchRig();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.PreparationGate = gate;
        var start = rig.BeginEnsure();
        rig.RuntimeRig.H.Main.Loop.RunUntil(() => rig.Leases.Count == 1, AppModelHarness.Bound);
        rig.Stop();
        gate.SetResult();
        Assert.Throws<CancellationError>(() => rig.RuntimeRig.H.Main.Loop.Run(() => start, AppModelHarness.Bound));
        Assert.Empty(rig.Sessions);
        Assert.False(Assert.Single(rig.Leases).Verify());
    }
    [Theory]
    [InlineData(null)]
    [InlineData("prior-cursor-chat")]
    public void Devin_new_and_resumed_launches_never_receive_an_automatic_generation(string? resume)
    {
        using var rig = new Sk17LaunchRig(ProviderKind.Devin, resume);
        var session = Assert.IsType<Sk17FakeSession>(rig.Ensure());
        Assert.Null(session.Configuration.SkillGeneration);
        Assert.Empty(rig.Leases);
        Assert.Empty(rig.PreparedDirectories);
        Assert.Equal(resume, session.Configuration.ResumeID);
    }
}
