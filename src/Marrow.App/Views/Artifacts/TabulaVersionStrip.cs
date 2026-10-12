using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W14, Tabula's version strip (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.3 "dots v1…vN with the
// prompt that made each, scrubbable; a 'Diff' toggle compares any two versions"; 10 "keyboard: every control"; the approved
// canvas index.html .vs/.vn: 10 DIP dots on a hairline, the shown one white with a 4 DIP halo, the compared one a ring, the
// one being written pulsing, "v4 · latest" and the prompt under each; ED-NEW-web-w14-1). No Swift counterpart. A click
// shows a version, Shift+click (in Diff) compares with it, a drag scrubs across them and the version under the pointer
// shows when it is let go or rests, and the arrows, Home and End move through them. The halo grows on a spring; Reduce
// Motion keeps only its short ease and stills the pulse.

namespace Marrow.App.Views.Artifacts;

public sealed class TabulaVersionStrip : Border
{
    /// <summary>A scrub that rests this long on a version shows it before the pointer is let go.</summary>
    public static readonly TimeSpan ScrubRest = TimeSpan.FromMilliseconds(220);

    /// <summary>Keys move the shown version after this pause, so holding an arrow does not reload every page on the way.</summary>
    public static readonly TimeSpan KeyRest = TimeSpan.FromMilliseconds(160);

    private const double ScrubSlop = 4;

    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer scroll;
    private readonly TabulaSpringValue offset;
    private readonly List<Node> nodes = [];
    private Artifact? artifact;
    private int shown;
    private int? compare;
    private bool diff;
    private TabulaPendingAsk? pending;
    private IReadOnlyList<TabulaPendingAsk> queued = [];
    private Point? pressAt;
    private bool scrubbing;
    private int? scrubbed;
    private int serial;
    private Action<TimeSpan>? pulse;
    private IFrameSource? pulseSource;
    private TimeSpan? pulseStart;

    public TabulaVersionStrip()
    {
        Padding = new Thickness(14, 10, 14, 8);
        BorderThickness = new Thickness(0, 0, 0, 0.5);
        BorderBrush = PaneTokens.Line.Brush(this);
        scroll = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Child = scroll;
        offset = new TabulaSpringValue(0, x => scroll.Offset = new Vector(x, 0), response: 0.42, damping: 1, epsilon: 0.5);
        Focusable = true;
        AutomationProperties.SetName(this, "Versions");
        SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.List);
        AddHandler(PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => EndScrub(apply: false);
        KeyDown += OnKey;
        DetachedFromVisualTree += (_, _) => StopPulse();
        AttachedToVisualTree += (_, _) => Pulse();
        ActualThemeVariantChanged += (_, _) => Paint(animate: false);
    }

    /// <summary>The user chose a version to show.</summary>
    public event Action<int>? VersionPicked;

    /// <summary>The user chose the version to compare with (Shift+click in Diff).</summary>
    public event Action<int>? ComparePicked;

    /// <summary>The version the strip marks as shown (while scrubbing, the one under the pointer).</summary>
    public int Marked => scrubbed ?? shown;

    public int? Compared => diff ? compare : null;

    public bool IsPulsing => pulse is not null;

    /// <summary>Each version's words as the strip shows them: "v4 · latest" and its caption.</summary>
    public IReadOnlyList<(string Label, string Caption)> Labels => nodes.Select(n => (n.Label.Text ?? "", n.Caption.Text ?? "")).ToList();

    public void Show(Artifact value, int version, int? compareWith, bool comparing, TabulaPendingAsk? asking,
        IReadOnlyList<TabulaPendingAsk>? queuedAsks = null)
    {
        queuedAsks ??= [];
        var rebuild = artifact is null || artifact.Id != value.Id || artifact.Thread != value.Thread || artifact.Versions.Count != value.Versions.Count
            || artifact.Current != value.Current || pending != asking || !queued.SequenceEqual(queuedAsks);
        var animate = artifact is not null && artifact.Id == value.Id && artifact.Thread == value.Thread;
        artifact = value;
        shown = version;
        compare = compareWith;
        diff = comparing;
        pending = asking;
        queued = queuedAsks;
        if (rebuild) Build();
        Paint(animate && !rebuild);
        Pulse();
    }

    /// <summary>What a version says under its number (6.3 "the prompt that made each").</summary>
    public static string Caption(ArtifactVersion version)
    {
        if (version.RestoredFrom is { } restored) return string.Create(CultureInfo.InvariantCulture, $"Restored v{restored}");
        if (version.ForkedFrom is { } fork && string.IsNullOrWhiteSpace(version.Prompt)) return string.Create(CultureInfo.InvariantCulture, $"Copied from v{fork.Version}");
        if (version.Prompt is { } prompt && FirstLine(prompt) is { Length: > 0 } line) return line;
        if (version.Author.IsYou) return "Edited by you";
        if (version.Number == 1) return "First draft";
        return version.Author.Model is { Length: > 0 } model ? "By " + model : "Updated";
    }

    private static string FirstLine(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length > 120 ? line[..120] + "…" : line;
    }

    /// <summary>Shows a version as a click would (Shift compares in Diff).</summary>
    public void Choose(int version, bool shift = false)
    {
        if (artifact?.Version(version) is null) return;
        if (diff && shift)
        {
            if (version != shown) ComparePicked?.Invoke(version);
            return;
        }
        if (version != shown) VersionPicked?.Invoke(version);
    }

    private void Build()
    {
        row.Children.Clear();
        nodes.Clear();
        if (artifact is null) return;
        var latest = artifact.Current;
        foreach (var version in artifact.Versions)
        {
            var label = version.Number == latest && pending is null && queued.Count == 0
                ? string.Create(CultureInfo.InvariantCulture, $"v{version.Number} · latest")
                : string.Create(CultureInfo.InvariantCulture, $"v{version.Number}");
            nodes.Add(Make(version.Number, label, Caption(version), isPending: false, isLast: false));
        }
        if (pending is { } asking)
            nodes.Add(Make(asking.Number > 0 ? asking.Number : latest + 1, string.Create(CultureInfo.InvariantCulture, $"v{(asking.Number > 0 ? asking.Number : latest + 1)}"), FirstLine(asking.Prompt), isPending: true, isLast: true));
        foreach (var request in queued)
        {
            var node = Make(request.Number, string.Create(CultureInfo.InvariantCulture, $"v{request.Number} queued"), FirstLine(request.Prompt), isPending: true, isLast: true);
            node.IsQueued = true;
            nodes.Add(node);
        }
        if (nodes.Count > 0) nodes[^1].Line.IsVisible = false;
        foreach (var node in nodes) row.Children.Add(node.Root);
    }

    private Node Make(int number, string label, string caption, bool isPending, bool isLast)
    {
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
        var halo = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), RenderTransform = new ScaleTransform(0, 0), IsHitTestVisible = false };
        var dotBox = new Panel { Width = 18, Height = 18, Margin = new Thickness(-4, 0, -4, 0), Children = { halo, dot } };
        var line = new Border { Height = 1, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { dotBox } };
        Grid.SetColumn(line, 1);
        head.Children.Add(line);
        var title = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold };
        var words = new TextBlock { Text = caption, FontSize = 11.5, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left };
        var root = new StackPanel { Spacing = 4, MinWidth = 118, Margin = new Thickness(0, 4, 10, 4), Children = { head, title, words }, Background = Brushes.Transparent, Tag = number };
        ToolTip.SetTip(root, caption);
        AutomationProperties.SetName(root, label + ", " + caption);
        root.SetValue(AutomationProperties.ControlTypeOverrideProperty, AutomationControlType.ListItem);
        var node = new Node(number, root, dot, halo, line, title, words, isPending);
        node.Halo = new TabulaSpringValue(0, s => { if (halo.RenderTransform is ScaleTransform t) { t.ScaleX = s; t.ScaleY = s; } }, response: 0.32, damping: 0.82, epsilon: 0.003);
        return node;
    }

    private void Paint(bool animate)
    {
        var reduce = TabulaTokens.ReduceMotion(this);
        var marked = Marked;
        foreach (var node in nodes)
        {
            var on = node.Number == marked && !node.IsPending;
            var compared = diff && compare == node.Number && !on;
            node.Dot.Background = compared ? Brushes.Transparent : (on ? TabulaTokens.DotOn : TabulaTokens.Dot).Brush(node.Dot);
            node.Dot.BorderThickness = compared ? new Thickness(2) : default;
            node.Dot.BorderBrush = compared ? TabulaTokens.DotOn.Opacity(0.6).Brush(node.Dot) : null;
            node.HaloBorder.Background = TabulaTokens.DotHalo.Brush(node.HaloBorder);
            node.Line.Background = TabulaTokens.StripLine.Brush(node.Line);
            node.Label.Foreground = (on ? PaneTokens.Text1 : PaneTokens.Text2).Brush(node.Label);
            node.Caption.Foreground = PaneTokens.Text3.Brush(node.Caption);
            if (animate) node.Halo!.To(on ? 1 : 0, reduce);
            else node.Halo!.Snap(on ? 1 : 0);
            AutomationProperties.SetItemStatus(node.Root, node.IsQueued ? "Queued" : node.IsPending ? "Being written" : on ? "Shown" : compared ? "Compared" : "");
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Reveal(animate && !reduce), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // canvas renderVS: the shown dot's right edge kept in view, 14 DIP in.
    private void Reveal(bool animate)
    {
        if (nodes.FirstOrDefault(n => n.Number == Marked) is not { } node || scroll.Viewport.Width <= 0) return;
        var right = node.Root.Bounds.Right + 14;
        var left = node.Root.Bounds.Left;
        var x = scroll.Offset.X;
        var target = right > x + scroll.Viewport.Width ? right - scroll.Viewport.Width : left < x ? left : x;
        target = Math.Clamp(target, 0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width));
        if (Math.Abs(target - x) < 0.5) return;
        if (animate) { offset.Snap(x); offset.To(target, reduceMotion: false); }
        else offset.Snap(target);
    }

    // ---- the pulse of the version being written (canvas .vn.pend .dot: pulse 1 s ease-in-out infinite) ----

    private void Pulse()
    {
        var wanted = pending is not null && TopLevel.GetTopLevel(this) is not null && !TabulaTokens.ReduceMotion(this) && FrameClock.IsBound;
        if (!wanted)
        {
            StopPulse();
            return;
        }
        if (pulse is not null) return;
        pulseSource = FrameClock.Source.Current;
        pulse = OnPulse;
        pulseSource.RequestFrame(pulse);
    }

    private void StopPulse()
    {
        if (pulse is not null) pulseSource?.CancelFrame(pulse);
        pulse = null;
        pulseStart = null;
        foreach (var node in nodes.Where(n => n.IsPending)) node.Dot.Opacity = 1;
    }

    private void OnPulse(TimeSpan now)
    {
        pulse = null;
        if (pending is null || TopLevel.GetTopLevel(this) is null)
        {
            StopPulse();
            return;
        }
        pulseStart ??= now;
        var phase = (now - pulseStart.Value).TotalSeconds % 1.0;
        var opacity = 0.45 + 0.55 * (0.5 + 0.5 * Math.Cos(phase * 2 * Math.PI));
        foreach (var node in nodes.Where(n => n.IsPending && !n.IsQueued)) node.Dot.Opacity = opacity;
        pulse = OnPulse;
        pulseSource = FrameClock.Source.Current;
        pulseSource.RequestFrame(pulse);
    }

    // ---- pointer: click, Shift+click, scrub ----

    private int? NumberAt(Point inStrip)
    {
        foreach (var node in nodes)
        {
            if (node.IsPending) continue;
            var origin = node.Root.TranslatePoint(new Point(0, 0), this);
            if (origin is not { } at) continue;
            if (inStrip.X >= at.X && inStrip.X < at.X + node.Root.Bounds.Width + 10) return node.Number;
        }
        return null;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressAt = e.GetPosition(this);
        scrubbing = false;
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (pressAt is not { } start) return;
        var at = e.GetPosition(this);
        if (!scrubbing && Math.Abs(at.X - start.X) < ScrubSlop) return;
        scrubbing = true;
        if (NumberAt(at) is not { } number || number == scrubbed) return;
        scrubbed = number;
        Paint(animate: true);
        RestThenApply(number, ScrubRest);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (pressAt is null) return;
        var at = e.GetPosition(this);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var wasScrub = scrubbing;
        EndScrub(apply: false);
        e.Pointer.Capture(null);
        if (NumberAt(at) is { } number) Choose(number, shift && !wasScrub);
        e.Handled = true;
    }

    private void EndScrub(bool apply)
    {
        var number = scrubbed;
        pressAt = null;
        scrubbing = false;
        scrubbed = null;
        serial++;
        if (apply && number is { } chosen) Choose(chosen);
        else Paint(animate: true);
    }

    private async void RestThenApply(int number, TimeSpan rest)
    {
        var mine = ++serial;
        if (!await FrameClock.TrySleep(rest) || mine != serial) return;
        if (scrubbed == number) Choose(number);
    }

    // ---- keys ----

    private int keyed;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (artifact is null || artifact.Versions.Count == 0) return;
        var numbers = artifact.Versions.Select(v => v.Number).ToList();
        var at = numbers.IndexOf(Marked);
        var next = e.Key switch
        {
            Key.Left => at - 1,
            Key.Right => at + 1,
            Key.Home => 0,
            Key.End => numbers.Count - 1,
            _ => int.MinValue,
        };
        if (next == int.MinValue) return;
        e.Handled = true;
        next = Math.Clamp(next, 0, numbers.Count - 1);
        if (numbers[next] == Marked) return;
        scrubbed = numbers[next];
        Paint(animate: true);
        var mine = ++keyed;
        var number = numbers[next];
        _ = Rest();
        async Task Rest()
        {
            if (!await FrameClock.TrySleep(KeyRest) || mine != keyed) return;
            scrubbed = null;
            Choose(number);
            Paint(animate: true);
        }
    }

    private sealed class Node(int number, StackPanel root, Border dot, Border halo, Border line, TextBlock label, TextBlock caption, bool isPending)
    {
        public int Number { get; } = number;
        public StackPanel Root { get; } = root;
        public Border Dot { get; } = dot;
        public Border HaloBorder { get; } = halo;
        public Border Line { get; } = line;
        public TextBlock Label { get; } = label;
        public TextBlock Caption { get; } = caption;
        public bool IsPending { get; } = isPending;
        public bool IsQueued { get; set; }
        public TabulaSpringValue? Halo { get; set; }
    }
}
