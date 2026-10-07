using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Cards;

// PORT-ONLY: MA-3b-13, other service :4101 and earlier account :4698, account names emphasized as <b>.
[Collection("Headless")]
public sealed class ReturnAndServiceCardTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Earlier_account_title_emphasizes_both_names_and_stay_answers_once()
    {
        var f = FirstTimeCardTests.Make(false, DecisionKind.EarlierAccount); f.Cards.Raise(f.State with { Reason = HandshakeReason.EarlierAccountBack });
        MultiAccountHarness.Run(headless, f.Rig, () => { var body = new StackPanel(); MultiAccountCardRows.Append(body, f.Accounts.ThreadId, f.Cards, () => f.Accounts.Accounts, 12); return body; }, h =>
        {
            var title = h.All().OfType<TextBlock>().Single(t => t.Text == "Claude Max 2 has reset · you are on Claude Max 1");
            Assert.NotNull(title.Inlines);
            Assert.Equal(new[] { "Claude Max 2", "Claude Max 1" }, title.Inlines.OfType<Run>().Where(r => r.FontWeight == FontWeight.SemiBold).Select(r => r.Text));
            Assert.True(h.Press("limit-action-stay")); Assert.False(h.Press("limit-action-stay")); Assert.Equal("stay", Assert.Single(Assert.Single(f.Cards.Answers).Choices).Value);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Other_service_warns_before_moving_and_wait_is_the_primary_choice()
    {
        var f = FirstTimeCardTests.Make(false, DecisionKind.OtherService); var account = f.Rig.Connect(ProviderKind.Codex); f.Rig.ConnectAnother(ProviderKind.Codex); var codex = new FakeThreadAccountPort(f.Rig, ProviderKind.Codex);
        var request = f.State.Request! with { IncomingAccountId = account.Id.ToString("D") }; f.Cards.Raise(f.State with { Request = request });
        MultiAccountCardRows? host = null;
        MultiAccountHarness.Run(headless, f.Rig, () => { var body = new StackPanel(); host = MultiAccountCardRows.Append(body, f.Accounts.ThreadId, f.Cards, () => f.Accounts.Accounts.Concat(codex.Accounts).ToArray(), 12, new MultiAccountCardContext(null, null, TimeZoneInfo.Utc)); return body; }, h =>
        {
            Assert.Contains("All your Claude accounts are out. The first is back at 3:40 PM.", h.Words()); Assert.Contains("Codex 1 is another service: it will see this conversation.", h.Words());
            Assert.Empty(f.Cards.Answers); Assert.Contains("primary", host!.Card!.Actions[0].Classes); Assert.DoesNotContain("primary", host.Card.Actions[1].Classes);
            Assert.True(h.Press("limit-action-switch")); Assert.Equal("switch", Assert.Single(Assert.Single(f.Cards.Answers).Choices).Value);
        });
    }
}
