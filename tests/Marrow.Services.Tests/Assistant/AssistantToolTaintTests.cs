using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.4 "Taint follows text"; ED-NEW-dextra-as7-7): a tool result that carries untrusted
// material taints the chat before the model reads it, after its audit line; a clean one does not; and a result whose
// taint cannot be recorded is withheld.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-7")]
public sealed class AssistantToolTaintTests
{
    private static readonly Taint Mail = Taint.From(ContentOrigin.Email, "a reply from raj@partner.example");

    private static (DextraFakeHost Host, AssistantToolRunner Runner) Rig(Taint taint)
    {
        var host = new DextraFakeHost { Answer = call => AssistantToolResult.Done("what " + call.Tool.Name + " found") with { Taint = taint } };
        var thread = host.Add();
        using var bound = AssistantMcpBridge.Host.Override(host);
        var registration = AssistantMcpBridge.Register(BuiltInServerFixtures.Config(Marrow.Core.Models.ProviderKind.Claude, thread))!;
        return (host, new AssistantToolRunner(registration, host.RecordingPolicy()));
    }

    private static Task<AssistantToolResult> Recall(AssistantToolRunner runner) =>
        runner.Call("recall", AssistantToolCatalogFixture.Minimal("recall"), AssistantRoute.Mcp, CancellationToken.None);

    [Fact]
    public async Task A_tainted_result_taints_the_chat_after_its_line_and_before_it_is_returned()
    {
        var (host, runner) = Rig(Mail);
        var result = await Recall(runner);
        Assert.False(result.IsError, result.Text);
        Assert.Equal("what recall found", result.Text);
        Assert.Equal([Mail], host.Absorbed);
        var ran = host.Log.IndexOf("run:recall");
        var logged = host.Log.FindIndex(l => l.StartsWith("audit:nth-dextra/recall:done", StringComparison.Ordinal));
        var absorbed = host.Log.IndexOf("absorb:1");
        Assert.True(ran >= 0 && logged > ran && absorbed > logged, string.Join(" | ", host.Log));
    }

    [Fact]
    public async Task A_clean_result_taints_nothing()
    {
        var (host, runner) = Rig(Taint.Clean);
        Assert.False((await Recall(runner)).IsError);
        Assert.Empty(host.Absorbed);
    }

    [Fact]
    public async Task A_result_whose_taint_cannot_be_recorded_is_withheld()
    {
        var (host, runner) = Rig(Mail);
        host.AbsorbThrows = true;
        var result = await Recall(runner);
        Assert.True(result.IsError);
        Assert.Equal("Not shown: nth could not mark this result as untrusted content.", result.Text);
        Assert.DoesNotContain("found", result.Text, StringComparison.Ordinal);
        Assert.Contains(host.Audits, a => a.Result == "withheld: its taint could not be recorded");
    }

    [Fact]
    public void Without_a_controller_nothing_absorbs()
    {
        var none = new NoAssistantToolHost();
        Assert.Throws<InvalidOperationException>(() => none.Absorb(new AssistantChat(Guid.NewGuid(), "dextra", null, AssistantChatRole.Home,
            Marrow.Core.Models.ProviderKind.Claude, "model-a"), Mail));
    }
}
