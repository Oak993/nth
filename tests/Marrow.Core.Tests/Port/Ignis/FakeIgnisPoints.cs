using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.Core.Tests.Port.Ignis;

// Ignis U0 (OD-0134, ED-NEW-ignis-contract): a fake that proves the contract compiles for the units that implement it.
internal sealed class FakeIgnisPoints : IIgnisPoints
{
    public event Action? Changed { add { } remove { } }
    public IgnisPointsFile Current { get; } = new(IgnisPointsFile.CurrentSchemaVersion, "2026.10.10", new ValueList<IgnisModelPoints>([]));
    public IgnisPointsOrigin Origin => IgnisPointsOrigin.Bundled;
    public DateTimeOffset? CheckedAt => null;
    public IgnisModelPoints? Find(ProviderKind provider, string? model) => null;
    public Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken) => Task.FromResult(new IgnisPointsCheck(Current.Version, null, false));
}
