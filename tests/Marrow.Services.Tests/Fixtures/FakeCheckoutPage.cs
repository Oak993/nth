using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (Dextra AS-20): a checkout page on a local host as a fill path would hold it. The test sets what the page is at
/// each read (a redirect after approval is a second snapshot), and the page records, in order, the agents being held and let
/// go, each field it was given (what the shop's field now holds) and the submit.
/// </summary>
public sealed class FakeCheckoutPage : ICheckoutPage
{
    private readonly Queue<CheckoutSnapshot> next = new();

    public FakeCheckoutPage(CheckoutSnapshot page) => Page = page;

    /// <summary>The page every read returns once the queued ones are used.</summary>
    public CheckoutSnapshot Page { get; set; }

    public List<string> Events { get; } = [];

    /// <summary>What each field holds after the fill, by field id.</summary>
    public Dictionary<string, string> Filled { get; } = [];

    /// <summary>The memory each fill was handed, kept to prove it is zero once the fill returns.</summary>
    public List<ReadOnlyMemory<char>> Handed { get; } = [];

    public HashSet<string> RefuseFields { get; } = [];

    public CheckoutOutcome Outcome { get; set; } = CheckoutOutcome.Paid;

    public Exception? SubmitThrows { get; set; }

    public bool AgentsHeld { get; private set; }

    public FakeCheckoutPage Then(CheckoutSnapshot snapshot)
    {
        next.Enqueue(snapshot);
        return this;
    }

    public Task<CheckoutSnapshot> Snapshot(CancellationToken cancellationToken)
    {
        Events.Add("snapshot");
        return Task.FromResult(next.Count > 0 ? next.Dequeue() : Page);
    }

    public Task<bool> Fill(string fieldId, ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        Events.Add("fill " + fieldId + (AgentsHeld ? " (held)" : ""));
        Handed.Add(value);
        if (RefuseFields.Contains(fieldId)) return Task.FromResult(false);
        Filled[fieldId] = new string(value.Span);
        return Task.FromResult(true);
    }

    public IDisposable HoldAgents()
    {
        Events.Add("hold");
        AgentsHeld = true;
        return new Release(this);
    }

    public Task<CheckoutOutcome> Submit(CancellationToken cancellationToken)
    {
        Events.Add("submit");
        if (SubmitThrows is { } error) throw error;
        return Task.FromResult(Outcome);
    }

    public static CheckoutSnapshot Page1(string shop, decimal total, params (string Id, string Autocomplete, string? Frame, string? Action, bool Visible)[] fields) =>
        new(shop, new ValueList<CheckoutField>(fields.Select(f => new CheckoutField(f.Id, f.Frame ?? shop, f.Action, f.Autocomplete, f.Visible)))) { ShownTotal = total };

    private sealed class Release(FakeCheckoutPage page) : IDisposable
    {
        public void Dispose()
        {
            if (!page.AgentsHeld) return;
            page.AgentsHeld = false;
            page.Events.Add("release");
        }
    }
}
