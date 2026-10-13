namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-17: stands in for the team's change event: hands the subscriber's callback to the test and notes when the subscription ends.</summary>
internal sealed class TeamWatch(Action<Action> capture)
{
    public bool Disposed { get; private set; }

    public IDisposable Subscribe(Action handler)
    {
        capture(handler);
        return new CallbackDisposable(() => Disposed = true);
    }
}
