using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Assistant;

// The agent job registry: one job per chat, made on first use, replaced once ended, held through Stop until the agent's
// processes are gone, and let go when the mission ends.
[Trait("Decision", "ED-NEW-dextra-as5-5")]
[Trait("Feature", "AS-5")]
public sealed class AgentJobsTests
{
    [Fact]
    public void A_chat_keeps_one_job_until_it_settles_or_ends()
    {
        var factory = new FakeAgentJobFactory { EndOnTerminate = false };
        var jobs = new AgentJobs(factory);
        var thread = Guid.NewGuid();
        Assert.Null(jobs.Existing(thread));
        Assert.True(jobs.Settled(thread));
        var job = jobs.For(thread);
        Assert.Same(job, jobs.For(thread));
        Assert.Single(factory.Made);

        jobs.Terminate(thread);
        Assert.Equal(1, factory.Made[0].Terminations);
        Assert.False(jobs.Settled(thread));
        Assert.Same(job, jobs.Existing(thread));
        factory.Made[0].LiveProcesses = 0;
        Assert.True(jobs.Settled(thread));
        Assert.True(factory.Made[0].Disposed);
        Assert.Null(jobs.Existing(thread));

        var next = jobs.For(thread);
        Assert.NotSame(job, next);
        jobs.End(thread);
        Assert.Equal(1, factory.Made[1].Terminations);
        Assert.True(factory.Made[1].Disposed);
        Assert.Empty(jobs.Threads);
        jobs.End(thread);
    }
}
