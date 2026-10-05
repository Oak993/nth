// PORT-ONLY: first-release feature unit SK-18 (OD-0118 addendum). STUB: red until implemented.
using Marrow.Core.Port.Skills.Import;

namespace Marrow.Services.Port.Skills.Import;

public sealed record WatchTarget(string Folder, bool Deep, IReadOnlyList<string>? Names);

public interface IFolderWatch
{
    IDisposable Watch(IReadOnlyList<WatchTarget> targets, Action changed);
}

public sealed record Detection(int Count, IReadOnlyList<ForeignApp> Apps, IReadOnlyList<string> Keys)
{
    public bool Visible => Count > 0;
}

public static class ForeignWatchTargets
{
    public static IReadOnlyList<WatchTarget> For(string home) => throw new NotImplementedException();
}

public sealed class ImportDetector : IDisposable
{
    public ImportDetector(Func<ForeignScan> scan, ImportedSkillsLibrary library, string home, IFolderWatch? watch = null, string? stateFile = null, TimeProvider? clock = null) => throw new NotImplementedException();
    public event Action? Changed;
    public Detection Current => throw new NotImplementedException();
    public bool Watching => throw new NotImplementedException();
    public void Refresh() => throw new NotImplementedException();
    public Task RefreshAsync() => throw new NotImplementedException();
    public void Start() => throw new NotImplementedException();
    public void Stop() => throw new NotImplementedException();
    public void Dismiss() => throw new NotImplementedException();
    public void Dispose() { }
}

public sealed class SystemFolderWatch : IFolderWatch
{
    public IDisposable Watch(IReadOnlyList<WatchTarget> targets, Action changed) => throw new NotImplementedException();
}
