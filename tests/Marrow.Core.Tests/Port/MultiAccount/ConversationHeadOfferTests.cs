using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class ConversationHeadOfferTests
{
    public static TheoryData<ProviderKind> Unverified => new()
    {
        ProviderKind.Codex, ProviderKind.Cursor, ProviderKind.Opencode, ProviderKind.OpencodeGo,
        ProviderKind.Grok, ProviderKind.Devin, ProviderKind.Antigravity, ProviderKind.Copilot,
        ProviderKind.Commandcode, (ProviderKind)999,
    };

    [Theory]
    [MemberData(nameof(Unverified))]
    public async Task Unverified_heads_are_absent_and_cannot_be_selected_or_sent(ProviderKind service)
    {
        var source = new HeadOfferAccountPort(ProviderKind.Claude, Row(ProviderKind.Claude));
        var other = new HeadOfferAccountPort(service, Row(service));
        var opinions = new HeadOfferOpinionsPort();
        using var model = new AskOthersModel(Guid.NewGuid(), source, [other], opinions,
            new OtherServiceConsentStore(new InMemorySettingsStore()));
        Assert.Empty(model.Rows);
        Assert.False(model.Shown);
        model.Toggle(other.Accounts[0].Id);
        model.ToggleAll();
        Assert.Empty(model.Picked);
        Assert.Equal(AskSendResult.NoSelection, await model.SendAsync("offline question"));
        Assert.Empty(opinions.Sends);
    }

    [Fact]
    public void All_includes_only_verified_heads_with_room_and_keeps_normal_account_rows()
    {
        var active = Row(ProviderKind.Claude);
        var rows = Enum.GetValues<ProviderKind>().Select(service => Row(service)).ToArray();
        var source = new HeadOfferAccountPort(ProviderKind.Claude, active);
        var all = new HeadOfferAccountPort(ProviderKind.Claude, rows);
        var disabled = Row(ProviderKind.Pi) with { Enabled = false };
        var noRoom = Row(ProviderKind.Deepseek);
        using var model = new AskOthersModel(Guid.NewGuid(), source,
            [all, new HeadOfferAccountPort(ProviderKind.Pi, disabled), new HeadOfferAccountPort(ProviderKind.Deepseek, noRoom)],
            new HeadOfferOpinionsPort(), new OtherServiceConsentStore(new InMemorySettingsStore()), hasRoom: a => a.Id != noRoom.Id);
        var verified = rows.Where(a => a.Service is ProviderKind.Claude or ProviderKind.Pi or ProviderKind.Deepseek
            or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local).Select(a => a.Id).ToArray();
        Assert.Equal(verified.Append(noRoom.Id), model.Rows.Select(r => r.Account.Id));
        model.ToggleAll();
        Assert.Equal(verified, model.Picked);
        Assert.Equal(Enum.GetValues<ProviderKind>().Length, all.Accounts.Count);
        Assert.Equal("Everyone with room", model.AllDetail);
        Assert.Equal("Ask others for this message", model.ButtonTitle);
    }

    [Theory]
    [MemberData(nameof(Unverified))]
    public void An_unverified_active_private_answer_and_combine_head_cannot_offer_ask_others(ProviderKind service)
    {
        var source = new HeadOfferAccountPort(service, Row(service));
        using var model = new AskOthersModel(Guid.NewGuid(), source,
            [new HeadOfferAccountPort(ProviderKind.Claude, Row(ProviderKind.Claude))], new HeadOfferOpinionsPort(),
            new OtherServiceConsentStore(new InMemorySettingsStore()));
        Assert.Empty(model.Rows);
        Assert.False(model.Shown);
    }

    internal static ThreadAccount Row(ProviderKind service) => new(Guid.NewGuid(), service, 1, "offline account", false,
        "A", null, true, null, new AccountDefaults());
}
