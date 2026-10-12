using Avalonia.Controls;
using Marrow.App.Port.PcControl.Workspace;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: the Remote Desktop control for WorkspaceView tests (unit P18, ED-NEW-pcws-7): a plain Avalonia control, no
// ActiveX, no connection. Its states run through what the test lists (connecting, then connected by default).
internal sealed class FakeRdpControl : IRdpControl
{
    private int read;

    public Control View { get; } = new Border { Focusable = true };

    public string? NotReady { get; set; }

    public string? Refusal { get; set; }

    /// <summary>The states State answers in turn; the last one stays.</summary>
    public List<int> States { get; } = [2, 1];

    public List<string> Programs { get; } = [];

    public List<bool> Input { get; } = [];

    public int Disconnects { get; private set; }

    public bool Disposed { get; private set; }

    public int State => States[Math.Min(read++, States.Count - 1)];

    public string? Prepare() => NotReady;

    public string? Connect(string startProgram)
    {
        Programs.Add(startProgram);
        return Refusal;
    }

    public void Disconnect() => Disconnects++;

    public void TakeInput(bool on) => Input.Add(on);

    public void Dispose() => Disposed = true;
}
