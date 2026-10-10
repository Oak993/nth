// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime). No Swift twin. What Ignis learns of each provider's request rhythm:
// per provider and TTL bucket (the main chat, the helpers at every depth), the last requests' idle gaps, cached prefix and
// writes by TTL, each tagged with its model. No message content. The app keeps it at Storage.Root/ignis/gaps-v1.json
// (Services IgnisGapStore); this in-memory copy serves tests and an unbound app.
using Marrow.Core.Models;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Port.Ignis;

public interface IIgnisGapHistory
{
    /// <summary>Adds one request; the oldest falls out past <see cref="IgnisGapMemory.Window"/> per provider and bucket.</summary>
    void Add(ProviderKind provider, IgnisCacheBucket bucket, IgnisRequestSample sample);

    /// <summary>The kept requests, oldest first.</summary>
    IReadOnlyList<IgnisRequestSample> Samples(ProviderKind provider, IgnisCacheBucket bucket);
}

public sealed class IgnisGapMemory : IIgnisGapHistory
{
    /// <summary>ENGINEERING DECISION: a small rolling window, the last 200 requests per provider and bucket.</summary>
    public const int Window = 200;

    private readonly Lock gate = new();
    private readonly Dictionary<(ProviderKind, IgnisCacheBucket), List<IgnisRequestSample>> samples = [];

    public void Add(ProviderKind provider, IgnisCacheBucket bucket, IgnisRequestSample sample)
    {
        lock (gate)
        {
            if (!samples.TryGetValue((provider, bucket), out var list)) samples[(provider, bucket)] = list = [];
            list.Add(sample);
            if (list.Count > Window) list.RemoveRange(0, list.Count - Window);
        }
    }

    public IReadOnlyList<IgnisRequestSample> Samples(ProviderKind provider, IgnisCacheBucket bucket)
    {
        lock (gate) return samples.TryGetValue((provider, bucket), out var list) ? [.. list] : [];
    }

    /// <summary>Every kept list (for a store that writes them out).</summary>
    public IReadOnlyList<(ProviderKind Provider, IgnisCacheBucket Bucket, IReadOnlyList<IgnisRequestSample> Samples)> All()
    {
        lock (gate) return [.. samples.Select(pair => (pair.Key.Item1, pair.Key.Item2, (IReadOnlyList<IgnisRequestSample>)[.. pair.Value]))];
    }
}
