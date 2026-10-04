using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;

namespace Marrow.App.Port;

/// <summary>
/// The engine the running app updates itself through (UE-1). One Ambient, as the other process-wide services: production's is Velopack over
/// <see cref="MarrowReleaseFeed.Url"/>, made on first use; a test overrides it with a fake.
/// </summary>
public static class SelfUpdateEngine
{
    private static readonly Ambient<ISelfUpdateEngine> Running = new(() => VelopackSelfUpdateEngine.ForRepository(MarrowReleaseFeed.Url));

    public static ISelfUpdateEngine Shared => Running.Current;

    /// <summary>For tests only: replaces <see cref="Shared"/> for the calling async flow.</summary>
    public static IDisposable OverrideShared(ISelfUpdateEngine engine) => Running.Override(engine);
}
