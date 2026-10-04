using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Port.FirstRun;
using Marrow.App.Views;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// GR-2: the real RootView (no projects, so its detail pane is WelcomeView) in a 960 x 680 window with the Getting ready
/// sheet shown over it, on a frame clock and a clock the test moves by hand. Build it on the main actor of an AppModelHarness.
/// </summary>
public sealed class GettingReadyRootRig : IDisposable
{
    private readonly IDisposable frameScope;
    private readonly IDisposable clockScope;

    public GettingReadyRootRig(AppModel model, bool reduceMotion = false, bool show = true)
    {
        frameScope = FrameClock.Source.Override(Frames);
        clockScope = SwiftClock.Provider.Override(Time);
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        Root = new RootView();
        host.Child = Root;
        Window = new AvaloniaWindow { Width = 960, Height = 680, Content = host };
        if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(Window, true);
        Window.Show();
        Settle();
        Plan = GettingReadyRig.W1Plan();
        Sheet = new GettingReadySheet(Plan);
        if (show) Root.ShowGettingReady(Sheet);
        Settle();
    }

    public ManualFrameSource Frames { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    public RootView Root { get; }

    public AvaloniaWindow Window { get; }

    public IReadOnlyList<FirstRunStepInfo> Plan { get; }

    public GettingReadySheet Sheet { get; }

    public void Settle()
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    public void Frame(double seconds)
    {
        var by = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        Time.Advance(by);
        Frames.Advance(by);
        Dispatcher.UIThread.RunJobs();
    }

    public void Run(double seconds, Action<double>? sample = null)
    {
        var count = (int)Math.Round(seconds * GettingReadyRig.Hz);
        for (var index = 1; index <= count; index++)
        {
            Frame(1 / GettingReadyRig.Hz);
            sample?.Invoke(index / GettingReadyRig.Hz);
        }
    }

    /// <summary>Every step done, as a run reports them.</summary>
    public void AllDone()
    {
        for (var index = 0; index < Plan.Count; index++)
        {
            Sheet.Report(GettingReadyRig.Event(Plan[index], index, Plan.Count, FirstRunStepState.Running, 0));
            Sheet.Report(GettingReadyRig.Event(Plan[index], index, Plan.Count, FirstRunStepState.Done, Plan[index].Parts));
        }
        Run(0.6);
    }

    /// <summary>The blur the detail pane is drawn with, as a Gaussian standard deviation in pixels (0: none).</summary>
    public double PaneBlurSigma => Root.DetailPane.Effect is IBlurEffect blur ? 0.288675 * blur.Radius + 0.5 : 0;

    public void Dispose()
    {
        Window.Close();
        clockScope.Dispose();
        frameScope.Dispose();
    }
}
