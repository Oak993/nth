using Marrow.Core.Accounts.Keys;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts.Keys;

/// <summary>Three keys on one service, an injected clock, no network.</summary>
public sealed class ApiKeyRig
{
    public const string Service = "openrouter";
    public static readonly Guid Thread = new("00000000-0000-0000-0000-0000000000a6");
    public static readonly Guid Other = new("00000000-0000-0000-0000-0000000000b7");

    public ManualTimeProvider Clock { get; }
    public TimeProvider Time { get; }
    public InMemoryKeyMovePolicy Moves { get; } = new();
    public InMemoryApiKeyStore Store { get; } = new();
    public InMemoryApiKeyCredits Credits { get; } = new();
    public InMemoryApiKeySpend Spend { get; } = new();
    public ApiKeyPool Pool { get; }
    public List<ApiKeyEvent> Events { get; } = [];

    public ApiKeyRig(int keys = 3, bool autoMove = true, TimeZoneInfo? zone = null, DateTimeOffset? start = null)
    {
        Clock = start is { } at ? new ManualTimeProvider(at) : new ManualTimeProvider();
        Time = zone is null ? Clock : new ZonedTimeProvider(Clock, zone);
        // OD-0116: moving is off until the user chooses; the tests of the continuation turn it on as the user would.
        Moves.Default = autoMove ? KeyMoveSetting.Allow : KeyMoveSetting.Unset;
        foreach (var id in new[] { "a", "b", "c" }.Take(keys))
            Store.Add(new ApiKeyEntry(id, Service, $"Key {id.ToUpperInvariant()}"), $"sk-{id}");
        Pool = new ApiKeyPool(Service, Store, Credits, Spend, Time, moves: Moves);
        Pool.Changed += (_, e) => Events.Add(e);
    }

    public IApiKeyRotation Rotation(Guid? thread = null) => Pool.ForThread(thread ?? Thread);

    public string? Pick(IApiKeyRotation rotation) => rotation.Current()?.Id;
}
