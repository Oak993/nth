namespace Marrow.Core.Port;

// PORT-ONLY (no Swift twin). The seam behind a Swift global, static or `.shared` singleton that a test
// must replace. Call sites keep Swift's static shape (`UserDefaults.standard`, `Date()`), and a test
// overrides the value for its own async flow only, so parallel xUnit test classes never share one.
// ED-065, ED-068, ED-079. Source: draft C (proto-C Port/Ambient.cs), unchanged in behaviour.
public sealed class Ambient<T> where T : class
{
    private readonly AsyncLocal<T?> scoped = new();
    private Lazy<T> processDefault;

    public Ambient(T processDefault) => this.processDefault = new(processDefault);

    /// <summary>
    /// For a Swift <c>static let shared = X()</c> whose initializer has side effects: it runs lazily, once,
    /// thread-safely, on first access, as a Swift global does. A test that overrides the value never runs it.
    /// </summary>
    public Ambient(Func<T> processDefault) =>
        this.processDefault = new(processDefault, LazyThreadSafetyMode.ExecutionAndPublication);

    public T Current => scoped.Value ?? Volatile.Read(ref processDefault).Value;

    /// <summary>For tests: whether the process default has been created.</summary>
    public bool IsDefaultCreated => Volatile.Read(ref processDefault).IsValueCreated;

    /// <summary>App start-up, and headless UI sessions (their dispatcher thread has no test flow).</summary>
    public void SetProcessDefault(T value) => Volatile.Write(ref processDefault, new Lazy<T>(value));

    /// <summary>Replaces the value for the calling async flow until the result is disposed.</summary>
    public IDisposable Override(T value)
    {
        var previous = scoped.Value;
        scoped.Value = value;
        return new Restorer(() => scoped.Value = previous);
    }

    private sealed class Restorer(Action restore) : IDisposable
    {
        private Action? pending = restore;
        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
