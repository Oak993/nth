using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Cards;

[Collection("Headless")]
public sealed class LiveCardFollowUpTests(HeadlessSession headless)
{
    [Fact]
    public void An_Ask_answer_cannot_hide_the_follow_up_switch_or_wait_card()
    {
        var rig = new AccountsRig(); var first = rig.Connect(ProviderKind.Claude); var next = rig.ConnectAnother(ProviderKind.Claude);
        var thread = Guid.NewGuid(); var port = new FollowUpPort();
        port.Current = new(thread, HandshakeEventKind.DecisionRequested, HandshakePhase.Idle, HandshakePhase.Asking,
            first.Id.ToString("D"), next.Id.ToString("D"), HandshakeReason.HardLimit, DateTimeOffset.UtcNow,
            Request: new(thread, [DecisionKind.OnLimit], true, first.Id.ToString("D"), next.Id.ToString("D"), null));
        MultiAccountHarness.Run(headless, rig, () =>
        {
            var panel = new StackPanel();
            MultiAccountCardRows.Append(panel, thread, port,
                () => ThreadAccountRows.Build(rig.Accounts, ProviderKind.Claude, rig.IsConnected, _ => null), 0);
            return panel;
        }, h =>
        {
            Assert.True(h.Press("limit-action-ask"));
            h.Until(() => h.Words().Any(text => text.StartsWith("Switch to ", StringComparison.Ordinal)));
            Assert.Contains("Wait", h.Words());
            Assert.DoesNotContain("Ask me each time", h.Words());
        });
    }
    private sealed class FollowUpPort : ICardPort
    {
        internal HandshakeEvent Current = null!;
        public event EventHandler<HandshakeEvent>? Changed;
        public HandshakeEvent? State(Guid thread) => Current;
        public Task AnswerAsync(Guid thread, IReadOnlyList<Choice> choices, RememberScope scope, CancellationToken ct = default)
        {
            Assert.Equal(DecisionValues.Ask, Assert.Single(choices).Value);
            Current = Current with { Request = Current.Request! with { FirstTime = false } };
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
    }
}
