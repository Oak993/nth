using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// One MCP setup panel view on screen: shown in a window of the panel's width, over a model whose state for the
/// server is set directly (no probe), with the number of times the view asked to be closed counted.
/// </summary>
public sealed class MCPSetupPanelSession : IDisposable
{
    private readonly TestRuntime runtime;

    public MCPSetupPanelSession(TestRuntime runtime, ManualFrameSource frames, AppModel model, MCPConnectionState state, MCPSetupGuide? guide = null, string entryName = "GitLab")
    {
        this.runtime = runtime;
        Frames = frames;
        Model = model;
        Entry = MCPSetupPanelRig.Entry("gitlab", entryName);
        Guide = guide ?? MCPSetupPanelRig.Guide();
        MCPSetupPanelView view = null!;
        Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            MCPSetupPanelRig.SetState(model, Entry.Id, state);
            view = new MCPSetupPanelView(Entry, Guide, model, () => Done++);
            window = MCPSetupPanelRig.Show(view, 400, 600);
        });
        View = view;
        Window = window;
        Pump();
    }

    /// <summary>The frame source the view's springs and spinners run on (bound by the test).</summary>
    public ManualFrameSource Frames { get; }

    public AppModel Model { get; }
    public MCPCatalogEntry Entry { get; }
    public MCPSetupGuide Guide { get; }
    public MCPSetupPanelView View { get; }
    public Avalonia.Controls.Window Window { get; }

    /// <summary>How many times the view's close hook ran.</summary>
    public int Done { get; private set; }

    /// <summary>Sets the server's connection state and lets the view follow it.</summary>
    public void SetState(MCPConnectionState state)
    {
        runtime.OnMain(() => MCPSetupPanelRig.SetState(Model, Entry.Id, state));
        Pump();
    }

    /// <summary>
    /// Presses a button. The press may start a probe on the main actor; it is cancelled before anything pumps the
    /// main actor, so no probe ever runs.
    /// </summary>
    public void Click(Button button)
    {
        runtime.OnMain(() =>
        {
            MCPSetupPanelRig.Click(button);
            MCPSetupPanelRig.CancelProbes(Model);
        });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Moves the manual clock forward and runs what falls due.</summary>
    public void Advance(double milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Moves the clock and the frame source forward together and delivers one frame.</summary>
    public void Frame(double milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Runs a main-actor body (the panel's shared window, the store).</summary>
    public void OnMain(Action body) => runtime.OnMain(body);

    /// <summary>Runs what the main actor and the UI thread have queued. Never call this with a probe in flight.</summary>
    public void Pump()
    {
        runtime.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>What a press at the centre of a node would land on, after the window has drawn its latest layout.</summary>
    public Visual HitAtCentre(Visual node)
    {
        Window.UpdateLayout();
        using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(Window);
        var at = node.TranslatePoint(new Point(node.Bounds.Width / 2, node.Bounds.Height / 2), Window)!.Value;
        return Window.InputHitTest(at) as Visual ?? throw new Xunit.Sdk.XunitException($"nothing was hit at {at}");
    }

    public void Dispose()
    {
        runtime.OnMain(() => MCPSetupPanelRig.CancelProbes(Model));
        Window.Close();
    }
}
