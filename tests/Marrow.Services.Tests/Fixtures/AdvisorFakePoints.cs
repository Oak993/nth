using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.Services.Tests.Fixtures;

// ED-NEW-ignisadvisor-2: Ignis points that know one model at fixed prices (USD per million tokens: input 2, output 10,
// cache read 0.2), and nothing else. No feed is ever checked.
public sealed class AdvisorFakePoints(ProviderKind provider, string model) : IIgnisPoints
{
    public event Action? Changed { add { } remove { } }
    public IgnisPointsFile Current => throw new NotSupportedException("The advisor tests only price models.");
    public IgnisPointsOrigin Origin => IgnisPointsOrigin.Bundled;
    public DateTimeOffset? CheckedAt => null;

    public IgnisModelPoints? Find(ProviderKind provider1, string? model1) => provider1 == provider && model1 == model
        ? new IgnisModelPoints(SwiftEnum<ProviderKind>.RawValue(provider), new ValueList<string>([model]), ValueList<string>.Empty, model, 200_000,
            new IgnisPrices(2m, 10m, 0.2m, null, null, ValueList<IgnisPriceStep>.Empty), new IgnisCachePoints(null, null, false), null, null, null, "https://example.test/prices", "2026-10-11")
        : null;

    public Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken) => throw new NotSupportedException("No feed in the advisor tests.");
}
