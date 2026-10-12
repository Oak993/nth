using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W14, Tabula's small parts (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.12 look, 10 accessibility,
// 2.4 airspace, 2.6 motion; the approved canvas index.html section 4: .vbar .seg, .sbx, .skel, .toast, .pbtn, .aview;
// ED-NEW-web-w14-1). No Swift counterpart. The colours are the pane tokens' (light and dark from one dynamic set); the code
// colours are the canvas's .code .k/.s/.c in dark and darker hues in light, each at least 4.5:1 on its well. Motion is
// opacity and render transforms only, on interruptible springs, and Reduce Motion turns every move into a short fade.

namespace Marrow.App.Views.Artifacts;

/// <summary>Tabula's colours, sizes and words that more than one part uses.</summary>
public static class TabulaTokens
{
    private static SwiftColor Hex(string hex) => SwiftColor.Hex(hex);

    /// <summary>The preview's well (canvas .aview #101014 in dark; a near-white page in light).</summary>
    public static SwiftColor Well { get; } = SwiftColor.Dynamic(light: Hex("#FBFBFC"), dark: Hex("#101014"));

    /// <summary>The bezel between the well and the card (canvas .aview margin 0 6px 6px).</summary>
    public const double Bezel = 6;

    public static SwiftColor Keyword { get; } = SwiftColor.Dynamic(light: Hex("#6F42C1"), dark: Hex("#C5A5FF"));

    public static SwiftColor StringText { get; } = SwiftColor.Dynamic(light: Hex("#116329"), dark: Hex("#9FE0B6"));

    public static SwiftColor Comment { get; } = SwiftColor.Dynamic(light: Hex("#5E5E66"), dark: Hex("#8E8E96"));

    public static SwiftColor Number { get; } = SwiftColor.Dynamic(light: Hex("#8A4B00"), dark: Hex("#F2C58C"));

    public static SwiftColor Tag { get; } = SwiftColor.Dynamic(light: Hex("#0857B0"), dark: Hex("#8CC8FF"));

    public static SwiftColor CodeText { get; } = SwiftColor.Dynamic(light: Hex("#1F1F24"), dark: Hex("#E3E3E7"));

    public static SwiftColor LineNumber { get; } = SwiftColor.Dynamic(light: Hex("#6B6B73"), dark: Hex("#7E7E86"));

    /// <summary>The version strip's dots and line (canvas .vn .dot, .ln).</summary>
    public static SwiftColor Dot { get; } = SwiftColor.Dynamic(light: SwiftColor.Hex("#000000").Opacity(0.22), dark: SwiftColor.Hex("#FFFFFF").Opacity(0.25));

    public static SwiftColor DotOn { get; } = SwiftColor.Dynamic(light: Hex("#1D1D1F"), dark: Hex("#FFFFFF"));

    public static SwiftColor DotHalo { get; } = SwiftColor.Dynamic(light: SwiftColor.Hex("#000000").Opacity(0.10), dark: SwiftColor.Hex("#FFFFFF").Opacity(0.14));

    public static SwiftColor StripLine { get; } = SwiftColor.Dynamic(light: SwiftColor.Hex("#000000").Opacity(0.12), dark: SwiftColor.Hex("#FFFFFF").Opacity(0.14));

    /// <summary>The calm bar's tint for an error (6.3 "a calm bar").</summary>
    public static SwiftColor ErrorFill { get; } = SwiftColor.Dynamic(light: Hex("#C4281C").Opacity(0.08), dark: Hex("#FF7A7A").Opacity(0.10));

    public static SwiftColor ErrorText { get; } = SwiftColor.Dynamic(light: Hex("#A1251B"), dark: Hex("#FFB4B4"));

    public static SwiftColor InfoFill { get; } = SwiftColor.Dynamic(light: SwiftColor.Hex("#000000").Opacity(0.04), dark: SwiftColor.Hex("#FFFFFF").Opacity(0.06));

    /// <summary>The primary pill (canvas .pbtn.pri: the silver fill with ink text).</summary>
    public static SwiftColor PrimaryFill { get; } = SwiftColor.Dynamic(light: Hex("#1D1D1F"), dark: Hex("#E9E9EE"));

    public static SwiftColor PrimaryText { get; } = SwiftColor.Dynamic(light: Hex("#FFFFFF"), dark: Hex("#141416"));

    public static SwiftColor PillFill { get; } = SwiftColor.Dynamic(light: SwiftColor.Hex("#000000").Opacity(0.06), dark: SwiftColor.Hex("#FFFFFF").Opacity(0.10));

    public static readonly FontFamily Mono = new("fonts:CascadiaMono#Cascadia Mono");

    /// <summary>Code at 12 DIP on a 1.7 line (canvas .code font: 12px/1.7).</summary>
    public const double CodeSize = 12;

    public const double CodeLine = 20;

    /// <summary>The sandbox line's words (canvas section 4).</summary>
    public const string SandboxText = "Sandboxed · no files · no network";

    /// <summary>Reduce Motion, from the view's environment or the system.</summary>
    public static bool ReduceMotion(Control control) =>
        EnvironmentValues.GetAccessibilityReduceMotion(control) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    /// <summary>"6.2 KB", "412 B", "1.3 MB" (the copy toast, canvas section 4).</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB",
    };

    /// <summary>"just now", "5 min ago", "2 h ago", "3 days ago", or the date (the gallery's cards).</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return ((int)span.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture) + " min ago";
        if (span < TimeSpan.FromDays(1)) return ((int)span.TotalHours).ToString(System.Globalization.CultureInfo.InvariantCulture) + " h ago";
        if (span < TimeSpan.FromDays(7)) return ((int)span.TotalDays) switch { 1 => "yesterday", var d => d.ToString(System.Globalization.CultureInfo.InvariantCulture) + " days ago" };
        return at.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>The kind chip's words ("HTML app"); a code artifact names its language.</summary>
    public static string KindName(Artifact artifact) => ArtifactKinds.DisplayName(artifact.Kind, artifact.Language);
}

/// <summary>A segmented choice (canvas .vbar .seg): a tinted strip, the chosen segment filled; arrows move the choice.</summary>
public sealed class TabulaSegmented : Border
{
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    private readonly List<(ToggleButton Button, object Value)> segments = [];
    private object? chosen;

    public TabulaSegmented(string name)
    {
        Padding = new Thickness(2);
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        Background = PaneTokens.HoverFill.Opacity(0.6).Brush(this);
        BorderBrush = PaneTokens.Line.Brush(this);
        Child = row;
        AutomationProperties.SetName(this, name);
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.Group);
        KeyDown += (_, e) =>
        {
            var at = segments.FindIndex(s => Equals(s.Value, chosen));
            var next = e.Key switch
            {
                Avalonia.Input.Key.Left => at - 1,
                Avalonia.Input.Key.Right => at + 1,
                Avalonia.Input.Key.Home => 0,
                Avalonia.Input.Key.End => segments.Count - 1,
                _ => int.MinValue,
            };
            if (next == int.MinValue || segments.Count == 0) return;
            next = Math.Clamp(next, 0, segments.Count - 1);
            e.Handled = true;
            Choose(segments[next].Value, raise: true);
            segments[next].Button.Focus();
        };
    }

    /// <summary>Raised when the user picks a segment.</summary>
    public event Action<object>? Picked;

    public IReadOnlyList<ToggleButton> Buttons => segments.Select(s => s.Button).ToList();

    public object? Chosen => chosen;

    public ToggleButton Add(string text, object value)
    {
        var label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border { Height = 26, Padding = new Thickness(11, 0), CornerRadius = new CornerRadius(8), Child = label, Background = Brushes.Transparent };
        var button = new ToggleButton { Content = face, Padding = default, Template = SegmentTemplate };
        void Look()
        {
            var on = button.IsChecked == true;
            face.Background = on ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent;
            label.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(label);
            label.FontWeight = on ? FontWeight.Medium : FontWeight.Normal;
        }
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) Look();
        };
        button.Click += (_, _) => Choose(value, raise: true);
        Look();
        AutomationProperties.SetName(button, text);
        segments.Add((button, value));
        row.Children.Add(button);
        return button;
    }

    /// <summary>Shows <paramref name="value"/> as chosen (a click on the chosen one keeps it).</summary>
    public void Choose(object value, bool raise = false)
    {
        var changed = !Equals(chosen, value);
        chosen = value;
        foreach (var (button, v) in segments) button.IsChecked = Equals(v, value);
        if (raise && changed) Picked?.Invoke(value);
    }

    public void SetEnabled(object value, bool enabled)
    {
        foreach (var (button, v) in segments)
            if (Equals(v, value)) { button.IsEnabled = enabled; button.Opacity = enabled ? 1 : 0.4; }
    }

    private static readonly FuncControlTemplate<ToggleButton> SegmentTemplate = new((owner, scope) =>
    {
        var content = new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] };
        content.RegisterInNameScope(scope);
        var ring = new Border
        {
            Margin = new Thickness(-2),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            IsVisible = false,
            IsHitTestVisible = false,
            BorderBrush = Marrow.UI.Chrome.Chrome.Accent.Opacity(0.25).Brush(owner),
        };
        AutomationProperties.SetAccessibilityView(ring, AccessibilityView.Raw);
        owner.Classes.CollectionChanged += (_, _) => ring.IsVisible = owner.Classes.Contains(":focus-visible");
        return new Panel { Children = { content, ring } };
    });
}

/// <summary>Small buttons Tabula draws: a glyph and words (canvas .abtn), a pill (canvas .pbtn), a round send button.</summary>
public static class TabulaButtons
{
    /// <summary>A plain text button with an optional glyph: tinted on hover (canvas .abtn).</summary>
    public static Button Text(string text, Action invoke, PaneGlyphKind? glyph = null, string? name = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        PaneGlyph? icon = null;
        if (glyph is { } kind)
        {
            icon = new PaneGlyph(kind, 13) { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);
        }
        var label = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        var face = new Border { Height = 26, Padding = new Thickness(9, 0), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = row };
        var button = PlainControls.Button(face, invoke).KeyboardFocus(8);
        void Paint(bool on)
        {
            face.Background = on ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent;
            label.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(label);
            if (icon is not null) icon.Foreground = label.Foreground;
        }
        button.OnHover(Paint);
        Paint(false);
        AutomationProperties.SetName(button, name ?? text);
        return button;
    }

    /// <summary>A pill (canvas .pbtn, 30 DIP); the primary one is the silver fill.</summary>
    public static Button Pill(string text, Action invoke, bool primary = false, string? name = null)
    {
        var label = new TextBlock { Text = text, FontSize = 12.5, FontWeight = primary ? FontWeight.SemiBold : FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        var face = new Border { Height = 30, Padding = new Thickness(12, 0), CornerRadius = new CornerRadius(15), Child = label };
        var button = PlainControls.Button(face, invoke).KeyboardFocus(15);
        void Paint(bool on)
        {
            face.Background = primary ? TabulaTokens.PrimaryFill.Opacity(on ? 0.9 : 1).Brush(face) : (on ? PaneTokens.HoverFill : TabulaTokens.PillFill).Brush(face);
            label.Foreground = (primary ? TabulaTokens.PrimaryText : PaneTokens.Text1).Brush(label);
        }
        button.OnHover(Paint);
        Paint(false);
        AutomationProperties.SetName(button, name ?? text);
        return button;
    }

    /// <summary>A square glyph button (canvas .hb, 26 DIP).</summary>
    public static Button Glyph(PaneGlyphKind kind, string name, Action invoke, double size = 26)
    {
        var glyph = new PaneGlyph(kind, 14);
        var face = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 3.25), Background = Brushes.Transparent, Child = glyph };
        var button = PlainControls.Button(face, invoke).KeyboardFocus(size / 3.25);
        void Paint(bool on)
        {
            face.Background = on ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent;
            glyph.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(glyph);
        }
        button.OnHover(Paint);
        Paint(false);
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        return button;
    }
}

/// <summary>
/// Drives opacity and a vertical offset of one control on interruptible springs (transform and opacity only). Under Reduce
/// Motion the offset jumps and only a short fade runs. With no frame source bound (a test without frames) it lands at once.
/// </summary>
public sealed class TabulaMotion
{
    private readonly Control target;
    private readonly TranslateTransform offset = new();
    private readonly SplitterSpring opacity;
    private readonly SplitterSpring rise;
    private Action<TimeSpan>? pending;
    private IFrameSource? source;
    private TimeSpan? last;
    private Action? landed;

    public TabulaMotion(Control target, double opacity = 1, double rise = 0)
    {
        this.target = target;
        target.RenderTransform = offset;
        this.opacity = new SplitterSpring(opacity, response: 0.30, damping: 1, epsilon: 0.002);
        this.rise = new SplitterSpring(rise, response: 0.42, damping: 0.86, epsilon: 0.05);
        Apply();
    }

    public double Opacity => opacity.Value;

    public double Rise => rise.Value;

    public bool IsMoving => opacity.IsMoving || rise.IsMoving;

    /// <summary>Moves to <paramref name="toOpacity"/> and <paramref name="toRise"/> from where it is now; <paramref name="done"/> runs on landing.</summary>
    public void To(double toOpacity, double toRise, Action? done = null)
    {
        var reduce = TabulaTokens.ReduceMotion(target);
        landed = done;
        opacity.SetTarget(toOpacity, reduce);
        if (reduce) rise.Snap(toRise);
        else rise.SetTarget(toRise);
        if (!FrameClock.IsBound)
        {
            Snap(toOpacity, toRise);
            return;
        }
        Request();
    }

    public void Snap(double toOpacity, double toRise)
    {
        if (pending is not null) source?.CancelFrame(pending);
        pending = null;
        last = null;
        opacity.Snap(toOpacity);
        rise.Snap(toRise);
        Apply();
        var done = landed;
        landed = null;
        done?.Invoke();
    }

    private void Request()
    {
        if (pending is not null) return;
        source = FrameClock.Source.Current;
        pending = OnFrame;
        source.RequestFrame(pending);
    }

    private void OnFrame(TimeSpan now)
    {
        pending = null;
        var dt = last is { } previous ? Math.Clamp((now - previous).TotalSeconds, 0, 0.034) : 1.0 / 60;
        last = now;
        var moving = opacity.Step(dt) | rise.Step(dt);
        Apply();
        if (moving)
        {
            Request();
            return;
        }
        Snap(opacity.Target, rise.Target);
    }

    private void Apply()
    {
        target.Opacity = Math.Clamp(opacity.Value, 0, 1);
        offset.Y = rise.Value;
    }
}

/// <summary>
/// A short status that fades in with a small rise, stays, and fades (canvas .toast). It sits in the view bar, beside the
/// pane's controls, never over the preview: a web page is its own window and would cover anything drawn over it (2.4).
/// </summary>
public sealed class TabulaStatus : Border
{
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(2.6);

    private readonly TextBlock text = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabulaMotion motion;
    private int serial;

    public TabulaStatus()
    {
        Child = text;
        IsHitTestVisible = false;
        text.Foreground = PaneTokens.Text2.Brush(text);
        motion = new TabulaMotion(this, opacity: 0, rise: 4);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    /// <summary>The words shown last (kept after they fade, for tests and the screen reader).</summary>
    public string? Shown { get; private set; }

    public async void Show(string message)
    {
        Shown = message;
        text.Text = message;
        AutomationProperties.SetName(this, message);
        var mine = ++serial;
        motion.To(1, 0);
        if (!await FrameClock.TrySleep(Hold) || mine != serial) return;
        motion.To(0, 4);
    }
}

/// <summary>A calm bar above the preview (6.3 "This artifact hit an error"): a glyph, words, actions and a close button.</summary>
public sealed class TabulaNotice : Border
{
    private readonly PaneGlyph glyph = new(PaneGlyphKind.Warning, 13) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock title = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };

    public TabulaNotice()
    {
        Margin = new Thickness(TabulaTokens.Bezel, 0, TabulaTokens.Bezel, 6);
        Padding = new Thickness(10, 7, 4, 7);
        CornerRadius = new CornerRadius(10);
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };
        var close = TabulaButtons.Glyph(PaneGlyphKind.Close, "Dismiss", () => Dismissed?.Invoke(), 24);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(glyph);
        Grid.SetColumn(words, 1);
        grid.Children.Add(words);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        Grid.SetColumn(close, 3);
        grid.Children.Add(close);
        Child = grid;
        IsVisible = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    public event Action? Dismissed;

    public string? Title => IsVisible ? title.Text : null;

    public string? Detail => IsVisible ? detail.Text : null;

    public IReadOnlyList<Button> Actions => actions.Children.OfType<Button>().ToList();

    public void Show(string heading, string? body, bool error, params (string Text, Action Invoke)[] buttons)
    {
        title.Text = heading;
        detail.Text = body ?? "";
        detail.IsVisible = !string.IsNullOrEmpty(body);
        var tone = error ? TabulaTokens.ErrorText : PaneTokens.Text2;
        glyph.Kind = error ? PaneGlyphKind.Warning : PaneGlyphKind.Shield;
        glyph.Foreground = tone.Brush(glyph);
        title.Foreground = (error ? TabulaTokens.ErrorText : PaneTokens.Text1).Brush(title);
        detail.Foreground = PaneTokens.Text2.Brush(detail);
        Background = (error ? TabulaTokens.ErrorFill : TabulaTokens.InfoFill).Brush(this);
        actions.Children.Clear();
        foreach (var (text, invoke) in buttons) actions.Children.Add(TabulaButtons.Text(text, invoke));
        AutomationProperties.SetName(this, string.IsNullOrEmpty(body) ? heading : heading + ". " + body);
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}

/// <summary>
/// The skeleton the preview shows until a streaming document has a body (6.4; canvas .skel): a title bar, two lines and two
/// cards, with a slow sheen that Reduce Motion stills.
/// </summary>
public sealed class TabulaSkeleton : Border
{
    private readonly List<Border> bars = [];
    private readonly LinearGradientBrush sheen;
    private Action<TimeSpan>? pending;
    private IFrameSource? source;
    private TimeSpan? start;

    public TabulaSkeleton()
    {
        Padding = new Thickness(22, 26);
        var stack = new StackPanel { Spacing = 14 };
        sheen = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 1) },
            Transform = new TranslateTransform(),
        };
        foreach (var (width, height, radius) in new (double, double, double)[] { (0.40, 22, 7), (0.70, 14, 7), (0.92, 90, 14), (0.92, 120, 14) })
        {
            var bar = new Border { Height = height, CornerRadius = new CornerRadius(radius), HorizontalAlignment = HorizontalAlignment.Left, Tag = width };
            bars.Add(bar);
            stack.Children.Add(bar);
        }
        Child = stack;
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "Writing the artifact");
        SizeChanged += (_, e) =>
        {
            foreach (var bar in bars) bar.Width = Math.Max(0, (e.NewSize.Width - Padding.Left - Padding.Right) * (double)bar.Tag!);
        };
        ActualThemeVariantChanged += (_, _) => Paint();
        AttachedToVisualTree += (_, _) => { Paint(); Run(); };
        DetachedFromVisualTree += (_, _) => Stop();
        Paint();
    }

    /// <summary>The sheen is moving (never under Reduce Motion, never while hidden).</summary>
    public bool IsSheenMoving => pending is not null;

    private void Paint()
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var baseColor = dark ? Color.FromArgb(13, 255, 255, 255) : Color.FromArgb(12, 0, 0, 0);
        var lit = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        sheen.GradientStops[0] = new GradientStop(baseColor, 0);
        sheen.GradientStops[1] = new GradientStop(baseColor, 1);
        if (sheen.GradientStops.Count < 3) sheen.GradientStops.Insert(1, new GradientStop(lit, 0.5));
        else sheen.GradientStops[1] = new GradientStop(lit, 0.5);
        foreach (var bar in bars) bar.Background = sheen;
    }

    private void Run()
    {
        if (TabulaTokens.ReduceMotion(this) || !FrameClock.IsBound || !IsVisible) return;
        Request();
    }

    private void Stop()
    {
        if (pending is not null) source?.CancelFrame(pending);
        pending = null;
        start = null;
    }

    private void Request()
    {
        source = FrameClock.Source.Current;
        pending = OnFrame;
        source.RequestFrame(pending);
    }

    // canvas: @keyframes sk 1.2 s linear, background-position 200 % to -200 %
    private void OnFrame(TimeSpan now)
    {
        pending = null;
        if (!IsVisible || TopLevel.GetTopLevel(this) is null) return;
        start ??= now;
        var phase = ((now - start.Value).TotalSeconds % 1.2) / 1.2;
        if (sheen.Transform is TranslateTransform move) move.X = (1 - 2 * phase) * Math.Max(1, Bounds.Width);
        Request();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;
        if (IsVisible) Run();
        else Stop();
    }
}

/// <summary>
/// One value on an interruptible spring, applied each frame (a halo's scale, a strip's scroll). Under Reduce Motion it moves
/// on the spring's short reduced ease; with no frame source bound it lands at once.
/// </summary>
public sealed class TabulaSpringValue
{
    private readonly SplitterSpring spring;
    private readonly Action<double> apply;
    private Action<TimeSpan>? pending;
    private IFrameSource? source;
    private TimeSpan? last;

    public TabulaSpringValue(double value, Action<double> apply, double response = 0.34, double damping = 0.9, double epsilon = 0.002)
    {
        spring = new SplitterSpring(value, response, damping, epsilon);
        this.apply = apply;
        apply(value);
    }

    public double Value => spring.Value;

    public double Target => spring.Target;

    public bool IsMoving => pending is not null;

    public void To(double target, bool reduceMotion)
    {
        if (Math.Abs(target - spring.Target) < 1e-9 && !spring.IsMoving) return;
        spring.SetTarget(target, reduceMotion);
        if (!FrameClock.IsBound)
        {
            Snap(target);
            return;
        }
        if (pending is not null) return;
        source = FrameClock.Source.Current;
        pending = OnFrame;
        source.RequestFrame(pending);
    }

    public void Snap(double value)
    {
        if (pending is not null) source?.CancelFrame(pending);
        pending = null;
        last = null;
        spring.Snap(value);
        apply(value);
    }

    private void OnFrame(TimeSpan now)
    {
        pending = null;
        var dt = last is { } previous ? Math.Clamp((now - previous).TotalSeconds, 0, 0.034) : 1.0 / 60;
        last = now;
        var moving = spring.Step(dt);
        apply(spring.Value);
        if (!moving)
        {
            Snap(spring.Target);
            return;
        }
        source = FrameClock.Source.Current;
        pending = OnFrame;
        source.RequestFrame(pending);
    }
}
