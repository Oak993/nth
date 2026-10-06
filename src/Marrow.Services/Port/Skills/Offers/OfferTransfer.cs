// PORT-ONLY: SK-12; one shared confirmed transfer, and listeners that can leave without cancelling its owner.
using Marrow.Core.Port.Skills.Catalogue;
namespace Marrow.Services.Port.Skills.Offers;
internal sealed class OfferTransfer(IReadOnlyList<CatalogueItem> items)
{
    public IReadOnlyList<CatalogueItem> Items { get; } = items;
    public TaskCompletionSource<IReadOnlyDictionary<string, string>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object gate = new();
    private readonly List<Action<OfferProgress>> listeners = [];
    private OfferProgress? last;
    public Action Listen(Action<OfferProgress> listener)
    {
        lock (gate) { listeners.Add(listener); if (last is { } value) listener(value); }
        return () => { lock (gate) listeners.Remove(listener); };
    }
    public void Report(OfferProgress value)
    {
        Action<OfferProgress>[] observers;
        lock (gate) { last = value; observers = listeners.ToArray(); }
        foreach (var observer in observers) observer(value);
    }
}
