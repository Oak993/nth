// PORT-ONLY: ED-207. A test's view of what Shell.run (Shell.swift:31-108, Shell.cs Shell.Run) was asked to run.
namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY (ED-207). Every argument of one <c>Shell.Run</c> call, exactly as its call site passed it:
/// <see cref="Arguments"/> is a copy taken at the call, the rest are the call's own references.
/// </summary>
public sealed record ShellInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    string? Directory,
    IReadOnlyDictionary<string, string>? Environment,
    byte[]? Input,
    double Timeout,
    int? OutputLimit);

/// <summary>
/// PORT-ONLY (ED-207). An observer of <c>Shell.Run</c> calls for one async flow, so a test can assert a call site's
/// literal argv and timeout budget (PR-GIT-003's per-site table, PR-GIT-031's argv order) while the real process
/// still runs. The call is reported when <c>Shell.Run</c> is entered, before the process starts, in call order;
/// nothing the call site passed is changed and nothing is replaced. With no observer in the flow (the app, and every
/// test that installs none) nothing is reported. Not an <see cref="Ambient{T}"/>: that seam replaces a Swift static,
/// and this one observes a call that has no Swift counterpart to replace, so it has no process default.
/// </summary>
public static class ShellObserver
{
    private static readonly AsyncLocal<Action<ShellInvocation>?> current = new();

    /// <summary>The calling flow's observer, or null.</summary>
    internal static Action<ShellInvocation>? Current => current.Value;

    /// <summary>
    /// Reports every <c>Shell.Run</c> call made by the calling async flow, and by the flows it starts, to
    /// <paramref name="observer"/> until the result is disposed; disposing restores the observer that was there before.
    /// </summary>
    public static IDisposable Observe(Action<ShellInvocation> observer)
    {
        var previous = current.Value;
        current.Value = observer;
        return new Restorer(() => current.Value = previous);
    }

    private sealed class Restorer(Action restore) : IDisposable
    {
        private Action? pending = restore;
        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
