using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Port.Ignis;

// ED-NEW-kindlehover-1: KindleCapsuleHoverTests' capsule in a shown headless window, with its frame clock, motion and presenter driven by hand.
internal sealed class KindleCapsuleRig : IDisposable
{
    private readonly IDisposable frameScope, motionScope, presenterScope;
    public TestRuntime Runtime { get; } = new();
    public ManualFrameSource Frames { get; } = new();
    public RecordingPresenter Presenter { get; } = new();
    public KindleFakeController Controller { get; }
    public KindleCapsule Capsule { get; }
    public AvaloniaWindow Window { get; }

    public KindleCapsuleRig(Func<TestRuntime, IgnisChatStatus> status)
    {
        frameScope = FrameClock.Source.Override(Frames);
        motionScope = ChromeTokens.ReduceMotionPlatform.Override(new RenderingGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        presenterScope = Presentation.Presenter.Override(Presenter);
        Controller = new KindleFakeController(new IgnisSettings(Runtime.Defaults)) { Current = status(Runtime) };
        Capsule = Runtime.OnMain(() => new KindleCapsule(Controller, KindleFakeController.Chat));
        Window = Runtime.OnMain(() =>
        {
            var window = new AvaloniaWindow
            {
                Content = new Border { Child = Capsule, Margin = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom },
                Width = 600, Height = 400,
            };
            window.Show();
            return window;
        });
        Render();
    }

    public void Render()
    {
        for (var i = 0; i < 4; i++)
        {
            Window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Moves the clock and the frame clock together and delivers one frame.</summary>
    public void Step(double milliseconds)
    {
        Runtime.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Runtime.RunUntilIdle();
    }

    /// <summary>The ring's centre (the glyph itself takes no hits: what is under it must).</summary>
    public Point Ring => Capsule.Glyph.TranslatePoint(new Point(KindleGlyph.RingSize / 2, Capsule.Glyph.Bounds.Height / 2), Window)!.Value;

    /// <summary>The empty padding after the words, inside the capsule's right end.</summary>
    public Point Padding => Capsule.Button.TranslatePoint(new Point(Capsule.Button.Bounds.Width - 5, Capsule.Button.Bounds.Height / 2), Window)!.Value;

    /// <summary>Well away from the capsule.</summary>
    public Point Away => new(560, 20);

    // pointer input reaches the view's model, so it is delivered on the main loop as the app's input is
    public void Move(Point point) => Runtime.OnMain(() => Window.MouseMove(point));
    public void Down(Point point) => Runtime.OnMain(() => Window.MouseDown(point, MouseButton.Left));
    public void Up(Point point) => Runtime.OnMain(() => Window.MouseUp(point, MouseButton.Left));
    public bool Presented => Runtime.OnMain(() => Capsule.IsPresented);

    public Color Resolve(SwiftColor color) => color.Resolve(Capsule.ActualThemeVariant);

    public void Dispose()
    {
        Runtime.OnMain(Window.Close);
        presenterScope.Dispose();
        motionScope.Dispose();
        frameScope.Dispose();
        Runtime.Dispose();
    }
}
