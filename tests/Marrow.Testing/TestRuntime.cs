using Marrow.Core.Port;

namespace Marrow.Testing;

/// <summary>
/// One deterministic runtime per test (ED-080): a main loop, a manual clock and an in-memory settings store,
/// installed as MainActor, SwiftClock and UserDefaults.Standard for the current async flow only (xUnit runs
/// test classes in parallel). <see cref="Advance"/> runs the loop to idle after every timer, so a debounce
/// that re-arms itself on main is followed exactly. Source: draft B (proto-B TestRuntime), plus the settings seam.
/// </summary>
public sealed class TestRuntime : IDisposable
{
    private readonly IDisposable mainScope;
    private readonly IDisposable clockScope;
    private readonly IDisposable defaultsScope;

    public TestRuntime()
    {
        mainScope = MainActor.Executor.Override(Loop);
        clockScope = SwiftClock.Provider.Override(Time);
        defaultsScope = UserDefaults.Standard.Override(Defaults);
    }

    public ManualMainLoop Loop { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    public InMemorySettingsStore Defaults { get; } = new();

    public int RunUntilIdle() => Loop.RunUntilIdle();

    /// <summary>Runs <paramref name="body"/> on the main loop now, then everything it posted; its failures propagate.</summary>
    public void OnMain(Action body)
    {
        Loop.Enqueue(body);
        Loop.RunUntilIdle();
    }

    /// <summary>Runs <paramref name="body"/> on the main loop now, then everything it posted, and returns its result.</summary>
    public T OnMain<T>(Func<T> body)
    {
        T result = default!;
        Loop.Enqueue(() => result = body());
        Loop.RunUntilIdle();
        return result;
    }

    public void Advance(TimeSpan by) => Time.Advance(by, () => Loop.RunUntilIdle());

    public void AdvanceMilliseconds(double milliseconds) => Advance(TimeSpan.FromMilliseconds(milliseconds));

    public void Dispose()
    {
        defaultsScope.Dispose();
        clockScope.Dispose();
        mainScope.Dispose();
    }
}

/// <summary>A UserDefaults store in memory that records every write, for tests (ED-079).</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    public List<string> Writes { get; } = [];

    public object? Object(string key) => values.GetValueOrDefault(key);

    public void Set(string key, object? value)
    {
        Writes.Add(key);
        if (value is null) values.Remove(key);
        else values[key] = value;
    }

    public void RemoveObject(string key)
    {
        Writes.Add(key);
        values.Remove(key);
    }
}
