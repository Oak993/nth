using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Port.Skills;

// SK-17 / OD-0099 Q-SKILLS-8 A: exercise the actual ThreadRuntime factory with offline sessions and real fake-protected leases.
[Trait("Decision", "ED-326"), Trait("Rule", "R-TEST-4")]
public sealed class AntigravitySkillsLaunchTests
{
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
        Assert.Throws<IOException>(() => rig.Ensure());
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
        rig.StartFailure = new IOException("no conversation found");
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
        Assert.Throws<IOException>(() => rig.Ensure());
        Assert.Single(rig.Sessions);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("prior-cursor-chat")]
    public void Cursor_new_and_resumed_launches_never_receive_an_automatic_generation(string? resume)
    {
        using var rig = new Sk17LaunchRig(ProviderKind.Cursor, resume);
        var session = Assert.IsType<Sk17FakeSession>(rig.Ensure());
        Assert.Null(session.Configuration.SkillGeneration);
        Assert.Empty(rig.Leases);
        Assert.Empty(rig.PreparedDirectories);
        Assert.Equal(resume, session.Configuration.ResumeID);
    }
}
