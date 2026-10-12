using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.NthUi;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

/// <summary>
/// ED-NEW-web-w16a-1: a live view in a headless window, driven by a frame source and a clock the test advances by hand, so
/// every arrival, spring and fade is read at exact times. The window's background is the thread's (light or dark).
/// </summary>
public sealed class NthUiRig : IDisposable
{
    public const double Frame = 1 / 60.0;

    public static readonly Color LightThread = Color.FromRgb(0xF5, 0xF5, 0xF7);

    public static readonly Color DarkThread = Color.FromRgb(0x1E, 0x1E, 0x21);

    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly IDisposable source;
    private readonly IDisposable clock;
    private readonly IDisposable calm;

    public NthUiRig(NthUiView? view = null, double width = 560, ThemeVariant? theme = null, bool reduceMotion = false)
    {
        source = FrameClock.Source.Override(Frames);
        clock = SwiftClock.Provider.Override(Time);
        calm = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: reduceMotion));
        View = view ?? new NthUiView(culture: English);
        var dark = theme == ThemeVariant.Dark;
        View.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        // The overlay window (HeadlessPopupHost): a select's or a date's menu opens in its overlay layer, as headless has no
        // native popups. The thread's background is the content's, since that template draws no window background.
        Window = HeadlessPopupHost.OverlayWindow(new Border { Padding = new Thickness(16), Background = new SolidColorBrush(dark ? DarkThread : LightThread), Child = View });
        Window.Width = width;
        Window.Height = 2400;
        Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Window.Show();
        Pump();
    }

    public ManualFrameSource Frames { get; } = new();

    public ManualTimeProvider Time { get; } = new(DateTimeOffset.UnixEpoch);

    public Window Window { get; }

    public NthUiView View { get; }

    /// <summary>Runs queued work and a layout pass.</summary>
    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Moves the clock and delivers one frame.</summary>
    public void Tick(double seconds = Frame)
    {
        Time.Advance(TimeSpan.FromSeconds(seconds));
        Frames.Advance(TimeSpan.FromSeconds(seconds));
        Pump();
    }

    /// <summary>Delivers frames for <paramref name="seconds"/>, calling <paramref name="each"/> after every one.</summary>
    public void Run(double seconds, Action? each = null)
    {
        var frames = (int)Math.Round(seconds / Frame);
        for (var i = 0; i < frames; i++)
        {
            Tick();
            each?.Invoke();
        }
    }

    /// <summary>Delivers frames until nothing moves (at most <paramref name="limit"/> seconds).</summary>
    public void Settle(double limit = 3, Action? each = null)
    {
        for (var t = 0.0; t < limit && (View.IsMoving || Frames.Pending > 0); t += Frame)
        {
            Tick();
            each?.Invoke();
        }
        Pump();
    }

    /// <summary>Appends <paramref name="text"/> in pieces of <paramref name="chunk"/> characters, a frame after each.</summary>
    public void Stream(string text, int chunk, Action? each = null)
    {
        for (var i = 0; i < text.Length; i += chunk)
        {
            View.Append(text.Substring(i, Math.Min(chunk, text.Length - i)));
            Pump();
            Tick();
            each?.Invoke();
        }
    }

    /// <summary>A node's layout rectangle in the view's space (render transforms such as the arrival rise left out).</summary>
    public Rect Layout(Visual visual)
    {
        var x = 0.0;
        var y = 0.0;
        for (Visual? v = visual; v is not null && !ReferenceEquals(v, View); v = v.GetVisualParent())
        {
            x += v.Bounds.X;
            y += v.Bounds.Y;
        }
        return new Rect(x, y, visual.Bounds.Width, visual.Bounds.Height);
    }

    /// <summary>The slots attached and shown in the view now.</summary>
    public IReadOnlyList<NthUiSlot> Slots() =>
        View.GetVisualDescendants().OfType<NthUiSlot>().Where(s => s.IsEffectivelyVisible).ToList();

    /// <summary>The texts the reader can see, in order.</summary>
    public IReadOnlyList<string> Texts() =>
        View.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && Opaque(t)).Select(t => t.Text!).ToList();

    private bool Opaque(Visual visual)
    {
        for (Visual? v = visual; v is not null && !ReferenceEquals(v, View); v = v.GetVisualParent())
        {
            if (v.Opacity < .01) return false;
        }
        return true;
    }

    /// <summary>The visible texts under the view of the first node of <paramref name="kind"/>.</summary>
    public IReadOnlyList<string> TextsOf(string kind)
    {
        var control = Control(kind);
        return control.GetVisualDescendants().Prepend(control).OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();
    }

    public NthUiNode Node(string kind, int index = 0) => View.Document.Nodes.Where(n => n.Kind == kind && !n.Degraded).ElementAt(index);

    public NthUiNodeView ViewOf(string kind, int index = 0) => View.ViewFor(Node(kind, index).Key) ?? throw new Xunit.Sdk.XunitException($"no view for {kind}");

    public T ViewOf<T>(int index = 0) where T : NthUiNodeView =>
        View.Document.Nodes.Select(n => View.ViewFor(n.Key)).OfType<T>().Distinct().ElementAt(index);

    public Control Control(string kind, int index = 0) => ViewOf(kind, index).Control;

    /// <summary>Presses a button (or toggle) the way a pointer click does.</summary>
    public void Click(Control control)
    {
        if (control is Avalonia.Controls.Primitives.ToggleButton toggle) toggle.IsChecked = !(toggle.IsChecked ?? false);
        control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    /// <summary>Sends a key to <paramref name="control"/>.</summary>
    public void Key(Control control, Key key)
    {
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Pump();
    }

    /// <summary>The first button or toggle under <paramref name="root"/> named <paramref name="name"/>.</summary>
    public static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => Avalonia.Automation.AutomationProperties.GetName(c) == name)
        ?? throw new Xunit.Sdk.XunitException($"no {typeof(T).Name} named {name}");

    public RenderedPixels Capture() => RenderedPixels.Capture(Window);

    public void Dispose()
    {
        Window.Close();
        calm.Dispose();
        clock.Dispose();
        source.Dispose();
    }
}
