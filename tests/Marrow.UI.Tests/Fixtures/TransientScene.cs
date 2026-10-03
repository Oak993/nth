using Avalonia;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// ED-253 (round 4): transients shown over windows for one test (WindowTransients), each recording its name when the
/// window's Escape dismisses it; disposing hides every one it showed.
/// </summary>
public sealed class TransientScene : IDisposable
{
    private readonly List<object> keys = [];

    public List<string> Dismissed { get; } = [];

    public void Show(object key, Visual owner, string name)
    {
        keys.Add(key);
        WindowTransients.Show(key, owner, () => Dismissed.Add(name));
    }

    public void Dispose()
    {
        foreach (var key in keys) WindowTransients.Hide(key);
    }
}
