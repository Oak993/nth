using Marrow.App.Runtime;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-perf-idle-5: whether a window of the app can be seen, driven by the test (the real one needs Win32 windows).
public sealed class FakeAppAttention : AppAttention
{
    private Action? changed;

    public bool IsSeen { get; set; } = true;

    public override bool Seen => IsSeen;

    public override event Action? Changed
    {
        add => changed += value;
        remove => changed -= value;
    }

    public void Raise() => changed?.Invoke();
}
