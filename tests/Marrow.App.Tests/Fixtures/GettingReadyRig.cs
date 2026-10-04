using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.FirstRun;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// GR-2: a Getting ready sheet standing in the detail pane of the fixture's 960 x 680 window (710 wide beside the 250 sidebar),
/// on a frame clock and a clock the test moves by hand (ED-117: frames are the only wake-ups, spans are measured on SwiftClock).
/// </summary>
public sealed class GettingReadyRig : IDisposable
{
    public const double PaneWidth = 710, PaneHeight = 680, Hz = 60;

    private readonly IDisposable frameScope;
    private readonly IDisposable clockScope;

    public GettingReadyRig(IReadOnlyList<FirstRunStepInfo>? plan = null, bool reduceMotion = false)
    {
        frameScope = FrameClock.Source.Override(Frames);
        clockScope = SwiftClock.Provider.Override(Time);
        Plan = plan ?? W1Plan();
        Sheet = new GettingReadySheet(Plan) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        Host = new Panel { Width = PaneWidth, Height = PaneHeight, Children = { Sheet } };
        Window = new AvaloniaWindow { Width = PaneWidth, Height = PaneHeight, Content = Host };
        if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(Window, true);
        Window.Show();
        Settle();
    }

    public ManualFrameSource Frames { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    public IReadOnlyList<FirstRunStepInfo> Plan { get; }

    public GettingReadySheet Sheet { get; }

    public Panel Host { get; }

    public AvaloniaWindow Window { get; }

    /// <summary>W1's six steps as a run of today's registry plans them: the graphics card's read, the knob's four programs,
    /// the composer light's three, the liquid, the strike and the voice hardware check.</summary>
    public static IReadOnlyList<FirstRunStepInfo> W1Plan() =>
    [
        new(FirstRunSteps.Graphics, 1), new(FirstRunSteps.Knob, 4), new(FirstRunSteps.Lights, 3),
        new(FirstRunSteps.Liquid, 1), new(FirstRunSteps.Strike, 1), new(FirstRunSteps.Voice, 1),
    ];

    public void Settle()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>One frame <paramref name="seconds"/> after the last.</summary>
    public void Frame(double seconds)
    {
        var by = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        Time.Advance(by);
        Frames.Advance(by);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Frames at <see cref="Hz"/> for <paramref name="seconds"/>, <paramref name="sample"/> after each one (the time
    /// since the start of this call, in seconds, is its argument).</summary>
    public void Run(double seconds, Action<double>? sample = null)
    {
        var count = (int)Math.Round(seconds * Hz);
        for (var index = 1; index <= count; index++)
        {
            Frame(1 / Hz);
            sample?.Invoke(index / Hz);
        }
    }

    /// <summary>The control's rectangle in the pane's own coordinates.</summary>
    public Rect Rect(Control control)
    {
        var origin = control.TranslatePoint(new Point(0, 0), Host) ?? throw new InvalidOperationException("The control is not in the pane.");
        return new Rect(origin, control.Bounds.Size);
    }

    public static FirstRunProgress Event(FirstRunStepInfo info, int index, int count, FirstRunStepState state, int done) =>
        new(info.Step, index, count, state, done, info.Parts, TimeSpan.Zero);

    public void Dispose()
    {
        Window.Close();
        clockScope.Dispose();
        frameScope.Dispose();
    }
}
