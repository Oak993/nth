// PORT-ONLY (OD-0074, ED-249). The Settings window's open, variant 2 "Rise" of the locked canvas
// E:/Droppy/work/design/settings-open-rise-2-locked.dc.html (exact spec E:/Droppy/work/design/settings-rise/SPEC.md).
// The source orders the window front with the system's own animation (WindowManager.swift:57-60); the owner ruled the
// port opens it with Rise instead.
//
// Hand-over (ED-236's readiness signal, probe P14): the window is cloaked (DWMWA_CLOAK) before it is shown and shown
// without activation. DWM composes a cloaked window, Acrylic included, and the first frame displayed after the uncloak is
// already blurred and tinted (P14: 0 tinted-over-sharp samples of 47, cloaked for 0, 300 or 1200 ms), so the plain tint
// of a first show (~225 ms on a user-opened Settings window, ED-236 UNRESOLVED (b)) is never on screen. The window is
// uncloaked at the first frame at which the hidden starting state was already drawn by the frame before (the same
// controls hidden two frames running), so the first displayed frame is the start of the wave, never a stale settled frame
// of a previous open. That frame is glass-ready, G; the window is then activated (ED-236's order: uncloak, then activate).
// Where the window cannot be cloaked (no Win32 window, DWM refuses) nothing is hidden and nothing rises: the window is
// shown and activated at once, as before.
//
// The wave, from G, on the frame clock (FrameClock.Source, ED-117; no timer): the window's glass (WindowBackdrop: the
// app's glass base, tint and hairline; the detail sheet's wash and tint) and the first card's surface fade 0 to 1 over
// 80 ms; each item rises 10 px with its opacity 0 to 1 over 140 ms from G + 12 ms x its index, one cubic-bezier
// (0.2, 0.8, 0.2, 1) driving both (opacity = 1 - y / 10). Two columns rise together, top to bottom: the sidebar (its
// search field 0, then each page row 1 to 10) and the page (1 the chrome row when the page has controls in it, then the
// page's blocks: a section heading, each row of the first card with the divider below it, every later card whole).
// ENGINEERING DECISION (the canvas shows the Appearance page only): a page with more than ten such blocks starts the
// ones past index 10 with index 10, so the wave never ends after G + 260 ms; on the Appearance page every index is the
// canvas's. The caption buttons are not in the wave (they show whole at G, as the canvas's do). The values are set at
// Avalonia's Animation priority over the controls' own (a row's own opacity, such as a disabled row's 0.5, is multiplied,
// never replaced) and removed when the wave ends, at G + 260 ms. Nothing is made hit-test invisible: the window takes
// input from the moment it is displayed. Reduce motion (ED-084): the same hidden show and reveal, with nothing animated.
// Every open (the window going from hidden to shown) plays it; bringing an open window forward does not.
//
// RR-37: the native Acrylic sprite now follows the glass curve. The common WinUI root owns the
// locked close fade/scale (SettingsNativePresentation, SettingsClose); native pixels still need Windows evidence.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

/// <summary>PORT-ONLY (OD-0074): the timing of Rise, in seconds from glass-ready (SPEC.md sections 2 and 3).</summary>
public static class SettingsRiseTiming
{
    /// <summary>The glass and the first card's surface fade in over this long.</summary>
    public const double GlassFade = 0.080;

    /// <summary>One item's rise.</summary>
    public const double ItemDuration = 0.140;

    /// <summary>The start of item i is i times this.</summary>
    public const double Stagger = 0.012;

    /// <summary>An item starts this many DIPs below its place.</summary>
    public const double Offset = 10;

    /// <summary>The highest start index (the sidebar's last page row on the canvas).</summary>
    public const int LastIndex = 10;

    /// <summary>The whole open: the last item settles at G + 260 ms.</summary>
    public const double Duration = Stagger * LastIndex + ItemDuration;

    /// <summary><c>cubic-bezier(0.2, 0.8, 0.2, 1)</c>.</summary>
    public static double Ease(double x) => x <= 0 ? 0 : x >= 1 ? 1 : CometEasing.Bezier(x, 0.2, 0.8, 0.2, 1).Value;

    /// <summary>The glass's opacity <paramref name="t"/> seconds after G (0 before G).</summary>
    public static double Glass(double t) => Ease(t / GlassFade);

    /// <summary>Item <paramref name="index"/>'s progress (its opacity) <paramref name="t"/> seconds after G; it sits
    /// <see cref="Offset"/> x (1 - progress) below its place.</summary>
    public static double Item(int index, double t) => Ease((t - Stagger * Math.Min(index, LastIndex)) / ItemDuration);
}

/// <summary>PORT-ONLY (OD-0074, ED-249): what Rise moves in one Settings view, gathered at a frame.</summary>
internal sealed class SettingsRiseLayout
{
    /// <summary>The glass layers: they fade with the glass.</summary>
    public List<Control> Glass { get; } = [];

    /// <summary>The first card of the page: its surface fades with the glass, its rows rise inside it.</summary>
    public ChromeCard? FirstCard { get; set; }

    /// <summary>The sidebar column, top to bottom; the list index is the start index.</summary>
    public List<Control[]> Sidebar { get; } = [];

    /// <summary>The page column, top to bottom; the list index plus one is the start index.</summary>
    public List<Control[]> Page { get; } = [];

    /// <summary>Every rising item with its start index.</summary>
    public IEnumerable<(int Index, Control[] Parts)> Items =>
        Sidebar.Select((parts, i) => (i, parts)).Concat(Page.Select((parts, i) => (i + 1, parts)));

    private IEnumerable<Control> All =>
        Glass.Concat(FirstCard is null ? [] : [FirstCard]).Concat(Sidebar.SelectMany(p => p)).Concat(Page.SelectMany(p => p));

    /// <summary>The same controls in the same places.</summary>
    public bool SameAs(SettingsRiseLayout other) =>
        Sidebar.Count == other.Sidebar.Count && Page.Count == other.Page.Count && All.SequenceEqual(other.All);

    /// <summary>Gathers <paramref name="view"/>'s parts as they are now.</summary>
    public static SettingsRiseLayout Gather(SettingsView view)
    {
        var layout = new SettingsRiseLayout();
        layout.Glass.Add(view.RiseBackdrop);
        if (TopLevel.GetTopLevel(view) is AvaloniaWindow { Content: SettingsShell shell })
            layout.Glass.Add(shell.Shadow);
        if (view.RiseSheet is { } sheet)
            foreach (var child in sheet.Children)
                if (child is Shape) layout.Glass.Add(child); // DetailSheetModifier's wash and tint (Chrome.swift:195-196)
        layout.Sidebar.Add([view.RiseSearch]);
        foreach (var row in view.RiseSidebarRows) layout.Sidebar.Add([row]);
        if (view.RiseChrome is { } chrome) layout.Page.Add([chrome]);
        if (view.RisePage is { } page)
        {
            ChromeCard? first = null;
            Walk(page, layout.Page, ref first);
            layout.FirstCard = first;
        }
        return layout;
    }

    // The page's blocks, top to bottom: bare vertical stacks and bare wrappers are looked through; a card is one block,
    // except the first, whose rows are; anything else is one block.
    private static void Walk(Control node, List<Control[]> items, ref ChromeCard? first)
    {
        switch (node)
        {
            case ChromeCard card when first is null:
                first = card;
                var rows = CardRows(card);
                if (rows.Count == 0) items.Add([card]);
                else items.AddRange(rows);
                return;
            case ChromeCard card:
                items.Add([card]);
                return;
            case StackPanel stack when IsBareStack(stack):
                foreach (var child in stack.Children) Walk(child, items, ref first);
                return;
            case Border { Background: null, BorderBrush: null, Child: { } inner } border when border.BoxShadow.Count == 0:
                Walk(inner, items, ref first);
                return;
            case Decorator { Child: { } inner } decorator when decorator is not Border && !DrawsItself(decorator):
                Walk(inner!, items, ref first); // The Child pattern above already excludes null (WL-1 build unblock).
                return;
            default:
                items.Add([node]);
                return;
        }
    }

    // The rows of a card (ChromeCard: Border > StackPanel > content), each with the divider below it.
    private static List<Control[]> CardRows(ChromeCard card)
    {
        Control? node = (card.Child as Decorator)?.Child;
        while (node is StackPanel { Children.Count: 1 } single && IsBareStack(single)) node = single.Children[0];
        var children = node is StackPanel list && IsBareStack(list) ? [.. list.Children] : node is null ? new List<Control>() : [node];
        var groups = new List<List<Control>>();
        foreach (var child in children)
        {
            if (child is ChromeRowDivider && groups.Count > 0) groups[^1].Add(child);
            else groups.Add([child]);
        }
        return groups.Select(group => group.ToArray()).ToList();
    }

    private static bool IsBareStack(StackPanel stack) => stack.Orientation == Orientation.Vertical && stack.Background is null;

    private static bool DrawsItself(Control control) =>
        control.GetType().GetMethod(nameof(Visual.Render), [typeof(DrawingContext)])?.DeclaringType is { } type
        && type != typeof(Visual) && type != typeof(Control) && type != typeof(Decorator);
}

/// <summary>PORT-ONLY (OD-0074, ED-249): one open of the Settings window (see the file note).</summary>
internal sealed class SettingsRise
{
    // The reveal waits for the hidden state to hold over two frames; a page that never settles is revealed after this many.
    private const int MaxHiddenFrames = 6;

    private readonly AvaloniaWindow window;
    private readonly SettingsView view;
    private readonly Func<AvaloniaWindow, bool, bool> cloak;
    private readonly Action activate;
    private bool reduceMotion;
    private readonly IFrameSource frames;
    private readonly Action<TimeSpan> frame;
    private readonly Dictionary<Control, Driven> driven = new(ReferenceEqualityComparer.Instance);
    private SettingsRiseLayout? hidden;
    private int hiddenFrames;
    private ContinuousInstant glassReady;
    private bool cloaked;
    private bool requested;
    private SettingsNativePresentation? native;
    private bool? fullOpacity;

    private SettingsRise(AvaloniaWindow window, SettingsView view, Func<AvaloniaWindow, bool, bool> cloak, Action activate)
    {
        this.window = window;
        this.view = view;
        this.cloak = cloak;
        this.activate = activate;
        frames = FrameClock.Source.Current;
        frame = OnFrame;
    }

    /// <summary>The window is still cloaked, waiting for its reveal.</summary>
    public bool Waiting => cloaked;

    /// <summary>The open is over (settled, or never started).</summary>
    public bool Done { get; private set; }

    /// <summary>The parts of the current wave, for tests; null before the first frame and after the end.</summary>
    internal SettingsRiseLayout? Layout { get; private set; }

    /// <summary>
    /// Opens <paramref name="window"/> (hidden) with Rise. <paramref name="show"/> shows it (and restores it from
    /// minimised); <paramref name="activate"/> activates it. Returns null when the window could not be cloaked: it has then
    /// been shown and activated at once, with nothing animated.
    /// </summary>
    public static SettingsRise? Open(AvaloniaWindow window, SettingsView view, Func<AvaloniaWindow, bool, bool> cloak, Action show, Action activate)
    {
        if (!cloak(window, true))
        {
            show();
            activate();
            return null;
        }
        var rise = new SettingsRise(window, view, cloak, activate) { cloaked = true, fullOpacity = RenderOptions.GetRequiresFullOpacityHandling(window) };
        RenderOptions.SetRequiresFullOpacityHandling(window, true);
        var activated = window.ShowActivated;
        window.ShowActivated = false; // activated at the reveal (ED-236's order)
        try { show(); }
        finally { window.ShowActivated = activated; }
        window.PropertyChanged += rise.OnWindowChanged;
        rise.Request();
        return rise;
    }

    /// <summary>Ends the open at once: everything settled, the window uncloaked, no more frames.</summary>
    public void Stop()
    {
        if (Done) return;
        Done = true;
        window.PropertyChanged -= OnWindowChanged;
        if (requested)
        {
            requested = false;
            frames.CancelFrame(frame);
        }
        Release();
        native?.Dispose();
        native = null;
        RenderOptions.SetRequiresFullOpacityHandling(window, fullOpacity);
        if (cloaked)
        {
            cloaked = false; // never leave the window hidden
            cloak(window, false);
        }
    }

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && !window.IsVisible) Stop(); // hidden during the open
    }

    private void Request()
    {
        requested = true;
        frames.RequestFrame(frame);
    }

    private void OnFrame(TimeSpan _)
    {
        requested = false;
        if (Done) return;
        if (cloaked)
        {
            // ED-084: read at the frame, since the window takes the system setting when it opens (SystemReduceMotion), which
            // a first open does inside its show.
            reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(view);
            var layout = SettingsRiseLayout.Gather(view);
            if (reduceMotion) Release();
            else Hide(layout);
            var attached = false;
            if (native is null && SettingsNativePresentation.Attach.Current(window) is { } presentation)
            { native = presentation; attached = true; }
            native?.Glass(reduceMotion ? 1 : 0);
            hiddenFrames++;
            var held = hidden is { } before && before.SameAs(layout);
            hidden = layout;
            // Native readiness is independent of a stable managed layout. Never uncloak before
            // its Acrylic sprite exists, nor on the same frame that first sets its hidden alpha.
            if ((native is null && SettingsNativePresentation.RequiresSurface.Current(window))
                || attached || (!held && hiddenFrames < MaxHiddenFrames))
            {
                Request();
                return;
            }
            if (native is null) SettingsNativePresentation.ReportUnavailable(window);
            // G: the hidden state was drawn by the previous frame; the first frame displayed is the wave's start.
            cloaked = false;
            cloak(window, false);
            glassReady = SwiftClock.Now;
            activate();
            if (reduceMotion)
            {
                Stop();
                return;
            }
            Layout = layout;
            Apply(layout, 0);
            Request();
            return;
        }
        var t = (SwiftClock.Now - glassReady).TotalSeconds;
        // ED-117 (2): a span is complete 1 ms early at most.
        if (t + 0.001 >= SettingsRiseTiming.Duration)
        {
            Stop();
            return;
        }
        Apply(Layout!, t);
        Request();
    }

    // Before G: the glass and the first card at 0, every item at 0 and 10 below its place.
    private void Hide(SettingsRiseLayout layout) => Apply(layout, double.NegativeInfinity);

    private void Apply(SettingsRiseLayout layout, double t)
    {
        var glass = SettingsRiseTiming.Glass(t);
        native?.Glass(glass);
        foreach (var layer in layout.Glass) For(layer).Set(glass, null);
        if (layout.FirstCard is { } card) For(card).Set(glass, null);
        foreach (var (index, parts) in layout.Items)
        {
            var progress = SettingsRiseTiming.Item(index, t);
            foreach (var part in parts) For(part).Set(progress, SettingsRiseTiming.Offset * (1 - progress));
        }
        // A control rebuilt away since the last frame keeps nothing of the wave.
        foreach (var stale in driven.Keys.Where(control => !InLayout(layout, control)).ToList())
        {
            driven[stale].Release();
            driven.Remove(stale);
        }
    }

    private static bool InLayout(SettingsRiseLayout layout, Control control) =>
        layout.Glass.Contains(control) || ReferenceEquals(layout.FirstCard, control) || layout.Items.Any(item => item.Parts.Contains(control));

    private Driven For(Control control)
    {
        if (!driven.TryGetValue(control, out var value)) driven[control] = value = new Driven(control);
        return value;
    }

    private void Release()
    {
        foreach (var value in driven.Values) value.Release();
        driven.Clear();
        Layout = null;
    }

    // One control's opacity and offset, at Animation priority over its own values.
    private sealed class Driven(Control control)
    {
        private readonly Feed<double> opacity = new();
        private readonly Feed<ITransform?> transform = new();
        private IDisposable? opacityBinding;
        private IDisposable? transformBinding;

        public void Set(double factor, double? offset)
        {
            var own = control.GetBaseValue(Visual.OpacityProperty) is { HasValue: true } set ? set.Value : 1;
            opacity.Push(own * factor);
            opacityBinding ??= control.Bind(Visual.OpacityProperty, opacity, BindingPriority.Animation);
            if (offset is not { } y) return;
            var translate = new TranslateTransform(0, y);
            var ownTransform = control.GetBaseValue(Visual.RenderTransformProperty) is { HasValue: true } setTransform ? setTransform.Value : null;
            transform.Push(ownTransform is Transform given ? new TransformGroup { Children = { given, translate } } : translate);
            transformBinding ??= control.Bind(Visual.RenderTransformProperty, transform, BindingPriority.Animation);
        }

        public void Release()
        {
            opacityBinding?.Dispose();
            transformBinding?.Dispose();
            opacityBinding = null;
            transformBinding = null;
        }
    }

    // A value source an Avalonia binding follows.
    internal sealed class Feed<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> observers = [];
        private T value = default!;
        private bool has;

        public void Push(T next)
        {
            value = next;
            has = true;
            foreach (var observer in observers.ToArray()) observer.OnNext(next);
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            observers.Add(observer);
            if (has) observer.OnNext(value);
            return new Subscription(() => observers.Remove(observer));
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
