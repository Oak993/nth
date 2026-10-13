namespace Marrow.Core.Support;

/// <summary>PORT-ONLY (PLAN 3/5/8; ED-NEW-inst-c7-10): one monotonic deadline, entry allowance and depth bound shared by an entire cleanup invocation.</summary>
internal sealed class SweepBudget
{
    private readonly SweepLimits limits;
    private readonly TimeProvider clock;
    private readonly long started;
    private int entries;
    private bool stopped;
    private readonly List<string> roots = [];

    public SweepBudget(SweepLimits? limits = null, TimeProvider? clock = null)
    {
        this.limits = limits ?? SweepLimits.Normal;
        this.clock = clock ?? TimeProvider.System;
        ArgumentOutOfRangeException.ThrowIfLessThan(this.limits.Duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(this.limits.MaxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(this.limits.MaxDepth);
        started = this.clock.GetTimestamp();
    }

    public void AddRoot(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
    }

    private int PathDepth(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var depth = 0;
        foreach (var root in roots)
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                depth = Math.Max(depth, Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar).Length);
        return depth;
    }
    public TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0, (limits.Duration - clock.GetElapsedTime(started)).Ticks));

    public void Check(string path, int depth = 0)
    {
        if (stopped || Math.Max(depth, PathDepth(path)) > limits.MaxDepth || clock.GetElapsedTime(started) >= limits.Duration)
        {
            stopped = true;
            throw new SweepLimitException(path);
        }
    }

    public void Visit(string path, int depth = 0)
    {
        Check(path, depth);
        if (entries >= limits.MaxEntries) { stopped = true; throw new SweepLimitException(path); }
        entries++;
    }

    public T Io<T>(string path, Func<T> operation, int depth = 0)
    {
        Check(path, depth);
        var value = operation();
        try { Check(path, depth); return value; }
        catch { if (value is IDisposable disposable) disposable.Dispose(); throw; }
    }

    public void Io(string path, Action operation, int depth = 0)
    {
        Check(path, depth);
        operation();
        Check(path, depth);
    }

    public void Guard(string path) => SetupFolders.RequireNoLinks(path, () => Check(path));
}
