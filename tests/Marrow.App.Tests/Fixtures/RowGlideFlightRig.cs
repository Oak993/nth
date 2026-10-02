using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Views.Sidebar;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Rig for the flight tests of DroppyCode/App/Views/Sidebar/RowGlide.swift:139-243: one glide launched on its own animator,
/// drawn by a RowGlideLayer laid over a white pane in a shown 400 x 300 window. Time moves only through the runtime's
/// manual clock and the manual frame source (<see cref="Step"/>), and the frame is read back from the headless renderer.
/// Helper functions only; every test body lives in a test class.
/// </summary>
public sealed class RowGlideFlightRig : IDisposable
{
    public static readonly Guid Thread = Guid.Parse("00000000-0000-0000-0000-0000000000B7");

    private readonly TestRuntime runtime;
    private readonly IDisposable frameScope;

    public RowGlideFlightRig(TestRuntime runtime, RowGlideAnimator.Glide.Direction direction, Rect from, Rect fallback, Rect bounds,
        IImage departure, IImage arrival, double departureFill, double arrivalFill)
    {
        this.runtime = runtime;
        frameScope = FrameClock.Source.Override(Frames);
        Animator = new RowGlideAnimator(new FakeGeniePlatform());
        Layer = new RowGlideLayer(Animator);
        Under = new Border { Background = Brushes.White };
        var host = new Grid();
        host.Children.Add(Under);
        host.Children.Add(Layer);
        Window = new AvaloniaWindow { Content = host, Width = 400, Height = 300, Background = Brushes.White, RequestedThemeVariant = ThemeVariant.Light };
        Window.Show();
        runtime.RunUntilIdle();
        Window.UpdateLayout();
        runtime.OnMain(() => Animator.Launch(Thread, direction, from, fallback, bounds, departure, arrival, departureFill, arrivalFill));
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public ManualFrameSource Frames { get; } = new();

    public RowGlideAnimator Animator { get; }

    public RowGlideLayer Layer { get; }

    public Border Under { get; }

    public AvaloniaWindow Window { get; }

    public RowGlideView View => Layer.Children.OfType<RowGlideView>().Single();

    /// <summary>Moves the clock and the display by <paramref name="seconds"/>: the next frame is delivered and drawn.</summary>
    public void Step(double seconds)
    {
        runtime.Advance(TimeSpan.FromSeconds(seconds));
        Frames.Advance(TimeSpan.FromSeconds(seconds));
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A copy of the frame as drawn now.</summary>
    public RowGlideSnapshot Snapshot()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = Window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        return new RowGlideSnapshot(frame);
    }

    public void Dispose()
    {
        Window.Close();
        frameScope.Dispose();
    }
}
