using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (Dextra AS-20): a checkout page on a local host as a fill path would hold it. The test sets what the page is at
/// each read (a redirect after approval is a second snapshot), and the page records, in order, the agents being held and let
/// go, each field it was given (what the shop's field now holds), each field emptied and read back, a discard and the submit.
/// Holds nest, as a fill path's must: the agents stay held until the last hold is let go. A field can refuse the card, take
/// part of it and then fail, or refuse to empty (a masked or script-controlled input); the page can refuse to close.
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

    /// <summary>What each field was last given, kept after it is emptied (what went in).</summary>
    public Dictionary<string, string> Took { get; } = [];

    public HashSet<string> RefuseFields { get; } = [];

    /// <summary>Fields that take the first half of the value and then report failure.</summary>
    public HashSet<string> PartialFields { get; } = [];

    /// <summary>Fields that put their value back when emptied.</summary>
    public HashSet<string> StickyFields { get; } = [];

    public bool DiscardFails { get; set; }

    public bool Discarded { get; private set; }

    /// <summary>Runs as each field is filled, after the field took the value (a test stops a payment mid-fill).</summary>
    public Action<string>? Filling { get; set; }

    public CheckoutOutcome Outcome { get; set; } = CheckoutOutcome.Paid;

    public Exception? SubmitThrows { get; set; }

    public bool AgentsHeld => holds > 0;

    /// <summary>Runs as the order is placed, before its outcome (a test reads what was written down by then).</summary>
    public Action? Submitting { get; set; }

    private int holds;

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
        if (PartialFields.Contains(fieldId))
        {
            Filled[fieldId] = new string(value.Span[..(value.Length / 2)]);
            return Task.FromResult(false);
        }
        Filled[fieldId] = new string(value.Span);
        Took[fieldId] = Filled[fieldId];
        Filling?.Invoke(fieldId);
        return Task.FromResult(true);
    }

    public Task<bool> Empty(string fieldId, CancellationToken cancellationToken)
    {
        Events.Add("empty " + fieldId + (AgentsHeld ? " (held)" : ""));
        if (StickyFields.Contains(fieldId)) return Task.FromResult(false);
        if (Filled.ContainsKey(fieldId)) Filled[fieldId] = "";
        return Task.FromResult(true);
    }

    public Task<bool> Discard(CancellationToken cancellationToken)
    {
        Events.Add("discard" + (AgentsHeld ? " (held)" : ""));
        if (DiscardFails) return Task.FromResult(false);
        Filled.Clear();
        Discarded = true;
        return Task.FromResult(true);
    }

    public IDisposable HoldAgents()
    {
        Events.Add("hold");
        holds++;
        return new Release(this);
    }

    public Task<CheckoutOutcome> Submit(CancellationToken cancellationToken)
    {
        Events.Add("submit" + (AgentsHeld ? " (held)" : ""));
        Submitting?.Invoke();
        if (SubmitThrows is { } error) throw error;
        return Task.FromResult(Outcome);
    }

    public static CheckoutSnapshot Page1(string shop, decimal total, params (string Id, string Autocomplete, string? Frame, string? Action, bool Visible)[] fields) =>
        new(shop, new ValueList<CheckoutField>(fields.Select(f => new CheckoutField(f.Id, f.Frame ?? shop, f.Action, f.Autocomplete, f.Visible)))) { ShownTotal = total, ShownCurrency = "USD" };

    private sealed class Release(FakeCheckoutPage page) : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            page.holds--;
            page.Events.Add("release");
        }
    }
}
