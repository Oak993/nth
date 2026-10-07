using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class ConversationHeadSelectionTests
{
    [Fact]
    public void Production_host_offers_only_verified_targets_but_keeps_an_unverified_primary_as_check_input()
    {
        using var runtime = new TestRuntime();
        var rows = Enum.GetValues<ProviderKind>().Select(service =>
            new ConversationHeadAccount(Guid.NewGuid(), service, "offline account", true, true)).ToArray();
        var active = rows.Single(a => a.Service == ProviderKind.Codex).Id;
        var prompt = new HeadSelectionNeverPrompt();
        var host = new AccountConversationHeadHost(null!, _ => active, _ => 1, _ => rows,
            (_, _) => throw new InvalidOperationException("No context fetch is allowed."),
            (_, _) => throw new InvalidOperationException("No session launch is allowed."),
            new ConversationTransferConsent(runtime.Defaults, prompt), (_, _, _, _, _) => throw new InvalidOperationException());
        runtime.Loop.Run(() =>
        {
            var offered = host.Accounts(Guid.NewGuid());
            Assert.Equal(rows.Where(a => a.Id == active || a.Service is ProviderKind.Claude or ProviderKind.Pi
                or ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local), offered);
            Assert.Contains(offered, a => a.Id == active);
            Assert.DoesNotContain(offered.Where(a => a.Id != active), a => a.Service is ProviderKind.Codex or ProviderKind.Cursor
                or ProviderKind.Opencode or ProviderKind.OpencodeGo or ProviderKind.Grok or ProviderKind.Devin
                or ProviderKind.Antigravity or ProviderKind.Copilot or ProviderKind.Commandcode);
            return Task.CompletedTask;
        });
        Assert.Equal(0, prompt.Calls);
    }
}
