using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

// A notification channel that shows nothing: it records the notices it was sent and the ones it was asked to take back (AS-9).
internal sealed class FakeNotificationChannel : NotificationChannel
{
    public override string Name => "Fake";

    public List<DecisionNotice> Sent { get; } = [];

    public List<Guid> Withdrawn { get; } = [];

    /// <summary>Whether the channel says it took the notice.</summary>
    public bool Takes { get; set; } = true;

    /// <summary>When set, Send and Withdraw throw it.</summary>
    public Exception? Throws { get; set; }

    public override Task<bool> Send(DecisionNotice notice, CancellationToken cancellationToken = default)
    {
        if (Throws is { } error) throw error;
        Sent.Add(notice);
        return Task.FromResult(Takes);
    }

    public override Task Withdraw(Guid target)
    {
        if (Throws is { } error) throw error;
        Withdrawn.Add(target);
        return Task.CompletedTask;
    }
}
