using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.3, 12.5; ED-NEW-dextra-as10-8): a call from the chat of a dry run's mission. The host says whether
// the call is a read (it runs) or not (the model is told it was not run, and the call is written down as "dry run: not run"). The
// host is asked after the policy and before the decision is logged, a host that cannot say refuses the call, and a log that cannot
// be written still stops it.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-8")]
public sealed class AssistantToolRunnerDryTests
{
    private const string Told = "Dry run: nothing was done. This action was not run.";

    private static (DextraFakeHost Host, AssistantToolRunner Runner) Rig()
    {
        var host = new DextraFakeHost();
        var thread = host.Add(AssistantChatRole.Mission, Guid.NewGuid());
        using var bound = AssistantMcpBridge.Host.Override(host);
        var registration = AssistantMcpBridge.Register(BuiltInServerFixtures.Config(ProviderKind.Claude, thread))!;
        return (host, new AssistantToolRunner(registration, host.RecordingPolicy()));
    }

    private static Task<AssistantToolResult> Call(AssistantToolRunner runner, string name) =>
        runner.Call(name, AssistantToolCatalogFixture.Minimal(name), AssistantRoute.Mcp, CancellationToken.None);

    [Fact]
    public async Task A_call_the_host_answers_as_a_dry_run_is_not_run_and_the_model_is_told_so()
    {
        var (host, runner) = Rig();
        host.Dry = _ => Told;
        var result = await Call(runner, "remember");
        Assert.False(result.IsError);
        Assert.Equal(Told, result.Text);
        Assert.Empty(host.Runs);
        Assert.Empty(host.Asks);
        Assert.Empty(host.Offers);
        Assert.Equal("dry run: not run", Assert.Single(host.Audits).Result);
        Assert.Equal(["policy:remember", "dry:remember", "audit:nth-dextra/remember:dry run: not run"], host.Log);
    }

    [Fact]
    public async Task A_call_the_host_lets_through_is_decided_logged_and_run_as_usual()
    {
        var (host, runner) = Rig();
        host.Dry = _ => null; // a read: real
        var result = await Call(runner, "recall");
        Assert.False(result.IsError);
        Assert.Equal("ran recall", result.Text);
        Assert.Single(host.Runs);
        Assert.Equal(2, host.Audits.Count);
        Assert.DoesNotContain(host.Audits, a => a.Result == "dry run: not run");
    }

    [Fact]
    public async Task A_host_that_cannot_say_whether_the_call_is_a_read_refuses_it()
    {
        var (host, runner) = Rig();
        host.Dry = _ => throw new InvalidOperationException("the mission store is busy");
        var result = await Call(runner, "recall");
        Assert.Contains("could not tell whether this call may run", result.Text, StringComparison.Ordinal);
        Assert.Empty(host.Runs);
        Assert.Equal("dry run: not run", Assert.Single(host.Audits).Result);
    }

    [Fact]
    public async Task A_dry_call_whose_line_cannot_be_written_is_stopped_all_the_same()
    {
        var (host, runner) = Rig();
        host.Dry = _ => Told;
        host.AuditThrows = true;
        var result = await Call(runner, "remember");
        Assert.True(result.IsError);
        Assert.Equal("Not run: the activity log could not be written.", result.Text);
        Assert.Empty(host.Runs);
    }
}
