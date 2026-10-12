using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 12.1a, 12.3, 15.4; ED-NEW-dextra-as5-3, -5, -6): the rule every launch and every turn of a
// chat goes through. A mission chat is sealed (the ask mode and the seal) on every provider nth can seal and refused on
// every other, whatever mode its record or its caller asks for, and it runs in its own agent job; a chat with no mission
// launches exactly as before. No mission descendant ever starts in Full access or a bypass mode.
[Trait("Decision", "ED-NEW-dextra-as5-6")]
[Trait("Feature", "AS-5")]
public sealed class MissionLaunchTests
{
    private static readonly Guid Mission = Guid.Parse("80000000-0000-4000-8000-000000000008");

    private static SessionConfiguration Configuration(ProviderKind provider, RuntimeMode mode) => new()
    {
        Provider = provider,
        Executable = null,
        WorkingDirectory = SwiftURL.Create(new Uri(Path.GetTempPath()).AbsoluteUri)!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = mode,
        InteractionMode = InteractionMode.Build,
    };

    private static ChatThread Chat(ProviderKind provider, RuntimeMode mode, Guid? mission) =>
        MissionRig.NewChat(() => new ChatThread(Guid.NewGuid(), provider, null, null, mode) { MissionID = mission });

    public static TheoryData<ProviderKind, RuntimeMode> EveryProviderAndMode()
    {
        var data = new TheoryData<ProviderKind, RuntimeMode>();
        foreach (var provider in Enum.GetValues<ProviderKind>())
            foreach (var mode in Enum.GetValues<RuntimeMode>())
                data.Add(provider, mode);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryProviderAndMode))]
    public void A_mission_chat_is_sealed_or_refused_on_every_provider_and_never_starts_in_a_bypass_mode(ProviderKind provider, RuntimeMode mode)
    {
        var jobs = new AgentJobs(new FakeAgentJobFactory());
        var thread = Chat(provider, mode, Mission);
        var launch = new SealedLaunch(Mission) { Connections = new(["calendar"]) };
        if (!SealedProviderConfig.CanSeal(provider))
        {
            var refused = Assert.Throws<ProviderError>(() => MissionLaunch.Apply(thread, Configuration(provider, mode), _ => launch, jobs.For));
            Assert.Contains("cannot run mission work yet", refused.Message, StringComparison.Ordinal);
            Assert.Empty(jobs.Threads);
            return;
        }
        var sealedConfiguration = MissionLaunch.Apply(thread, Configuration(provider, mode), _ => launch, jobs.For);
        Assert.Equal(SealedProviderConfig.CappedMode, sealedConfiguration.RuntimeMode);
        Assert.False(SealedProviderConfig.Bypasses(sealedConfiguration.RuntimeMode));
        Assert.NotEqual(RuntimeMode.FullAccess, sealedConfiguration.RuntimeMode);
        Assert.Equal(launch, sealedConfiguration.Sealed);
        Assert.Equal([thread.Id], jobs.Threads);
        Assert.Equal(SealedProviderConfig.CappedMode, MissionLaunch.TurnMode(thread));
    }

    [Theory]
    [MemberData(nameof(EveryProviderAndMode))]
    public void A_chat_with_no_mission_launches_as_before(ProviderKind provider, RuntimeMode mode)
    {
        var jobs = new AgentJobs(new FakeAgentJobFactory());
        var thread = Chat(provider, mode, null);
        var configuration = Configuration(provider, mode);
        var sealedCalls = 0;
        Assert.Same(configuration, MissionLaunch.Apply(thread, configuration, _ => { sealedCalls++; return null; }, jobs.For));
        Assert.Equal(0, sealedCalls);
        Assert.Empty(jobs.Threads);
        Assert.Equal(mode, MissionLaunch.TurnMode(thread));
        Assert.False(MissionLaunch.IsMissionChat(thread));
    }

    [Fact]
    public void A_seal_for_another_mission_is_not_used_and_the_chat_gets_a_bare_seal()
    {
        var jobs = new AgentJobs(new FakeAgentJobFactory());
        var thread = Chat(ProviderKind.Claude, RuntimeMode.FullAccess, Mission);
        var other = new SealedLaunch(Guid.NewGuid()) { Connections = new(["mail"]) };
        var sealedConfiguration = MissionLaunch.Apply(thread, Configuration(ProviderKind.Claude, RuntimeMode.FullAccess), _ => other, jobs.For);
        Assert.Equal(new SealedLaunch(Mission), sealedConfiguration.Sealed);
        var bare = MissionLaunch.Apply(thread, Configuration(ProviderKind.Claude, RuntimeMode.FullAccess), _ => null, jobs.For);
        Assert.Equal(new SealedLaunch(Mission), bare.Sealed);
        Assert.Empty(bare.Sealed!.Connections);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-12")]
    public void A_mission_chat_the_controller_refuses_starts_nothing_and_gets_no_job()
    {
        var factory = new FakeAgentJobFactory();
        var jobs = new AgentJobs(factory);
        var thread = Chat(ProviderKind.Claude, RuntimeMode.Supervised, Mission);
        var refused = Assert.Throws<ProviderError>(() => MissionLaunch.Apply(thread, Configuration(ProviderKind.Claude, RuntimeMode.Supervised), _ => null, jobs.For,
            _ => AssistantController.StoppedLaunch));
        Assert.Contains(AssistantController.StoppedLaunch, refused.Message, StringComparison.Ordinal);
        Assert.Empty(factory.Made);
        var plain = Chat(ProviderKind.Claude, RuntimeMode.FullAccess, null);
        var configuration = Configuration(ProviderKind.Claude, RuntimeMode.FullAccess);
        Assert.Same(configuration, MissionLaunch.Apply(plain, configuration, _ => null, jobs.For, _ => AssistantController.StoppedLaunch));
        var allowed = MissionLaunch.Apply(thread, Configuration(ProviderKind.Claude, RuntimeMode.Supervised), _ => null, jobs.For, _ => null);
        Assert.NotNull(allowed.Sealed);
        Assert.Single(factory.Made);
    }

    [Fact]
    public void A_mission_chat_whose_job_cannot_be_made_does_not_start()
    {
        var factory = new FakeAgentJobFactory { Fails = new InvalidOperationException("no job") };
        var jobs = new AgentJobs(factory);
        var thread = Chat(ProviderKind.Codex, RuntimeMode.Supervised, Mission);
        var refused = Assert.Throws<ProviderError>(() => MissionLaunch.Apply(thread, Configuration(ProviderKind.Codex, RuntimeMode.Supervised), _ => null, jobs.For));
        Assert.Contains("could not give this mission chat a job of its own", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_agent_job_is_handed_to_the_session()
    {
        var jobs = new AgentJobs(WindowsAgentJobFactory.Instance);
        var thread = Chat(ProviderKind.Claude, RuntimeMode.Supervised, Mission);
        try
        {
            var sealedConfiguration = MissionLaunch.Apply(thread, Configuration(ProviderKind.Claude, RuntimeMode.Supervised), _ => null, jobs.For);
            Assert.NotNull(sealedConfiguration.AgentJob);
            Assert.Same(jobs.Existing(thread.Id)!.Native, sealedConfiguration.AgentJob);
            Assert.False(sealedConfiguration.AgentJob!.IsClosed);
            // Stop terminated it: the next launch (on Resume) gets a fresh job, never the ended one.
            jobs.Terminate(thread.Id);
            Assert.True(sealedConfiguration.AgentJob.IsClosed);
            var again = MissionLaunch.Apply(thread, Configuration(ProviderKind.Claude, RuntimeMode.Supervised), _ => null, jobs.For);
            Assert.NotSame(sealedConfiguration.AgentJob, again.AgentJob);
            Assert.False(again.AgentJob!.IsClosed);
        }
        finally
        {
            jobs.End(thread.Id);
        }
    }
}
