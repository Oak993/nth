// PORT-ONLY: Tactus, the chat's strip (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.7). No Swift twin. A 32 DIP glass row above
// the composer of each chat with a working or paused Tactus stream: "Tactus. Using Excel and Outlook", a chip per app (its
// icon, its state dot, its name, its x: stop path 7), the asks badge, and Pause or Resume for this chat. It opens by growing to its
// height under a clip and closes the same way (no fade); Reduce Motion: at once. It reads the running chat mirror, so it
// shows nothing while Tactus is off.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port.PcControl;
using Marrow.App.Runtime;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Views.Chat;

public sealed class TactusStrip : Decorator
{
    public const double StripHeight = 32;
    public const double OpenSeconds = 0.25;

    private readonly string thread;
    private readonly Func<PcChatFeed?> source;
    private readonly Func<bool> reduceMotion;
    private readonly Border shape;
    private readonly TextBlock line = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
    private readonly StackPanel chips = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock asks = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontWeight = FontWeight.SemiBold };
    private readonly Button action;
    private PcChatFeed? feed;
    private int generation;

    public TactusStrip(ThreadRuntime runtime)
        : this(runtime.ThreadID.ToString(), () => TactusChatBridge.Current?.Feed)
    {
    }

    public TactusStrip(string thread, Func<PcChatFeed?> source, Func<bool>? reduceMotion = null)
    {
        this.thread = thread;
        this.source = source;
        this.reduceMotion = reduceMotion ?? (() => Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion);
        action = new Button { Padding = new Thickness(10, 2), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        action.Click += (_, _) => Act();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { asks, action } };
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 0, 6, 0) };
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { line, chips } });
        shape = new Border
        {
            Child = row,
            Height = StripHeight,
            CornerRadius = new CornerRadius(StripHeight / 2),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        SwiftGlass.SetEffect(shape, GlassEffect.Regular(GlassShape.RoundedRectangle(StripHeight / 2, RoundedCornerStyle.Continuous)));
        Child = shape;
        ClipToBounds = true;
        Height = 0;
        IsVisible = false;
        AutomationProperties.SetName(this, "Tactus");
        AttachedToVisualTree += (_, _) =>
        {
            TactusChatBridge.CurrentChanged += Rebind;
            Rebind();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            TactusChatBridge.CurrentChanged -= Rebind;
            Bind(null);
        };
    }

    /// <summary>What the strip shows now (null: closed).</summary>
    public PcChatStrip? Shown { get; private set; }

    /// <summary>The line, the chips and the button as drawn (for checks).</summary>
    public string Line => line.Text ?? "";

    public IReadOnlyList<Button> AppButtons => [.. chips.Children.OfType<StackPanel>().SelectMany(c => c.Children.OfType<Button>())];

    public Button Action => action;

    public string AsksText => asks.Text ?? "";

    /// <summary>Reads the chat's strip again (on the UI thread).</summary>
    public void Refresh()
    {
        var strip = feed?.Strip(thread);
        var was = Shown;
        Shown = strip;
        if (strip is null)
        {
            if (was is not null) Close();
            return;
        }
        line.Text = strip.Line;
        line.Foreground = Chrome.PrimaryText.Brush(this);
        asks.Text = strip.Asks switch { 0 => "", 1 => "1 ask", _ => strip.Asks + " asks" };
        asks.Foreground = Chrome.Accent.Brush(this);
        asks.IsVisible = strip.Asks > 0;
        action.Content = strip.Action;
        AutomationProperties.SetName(action, strip.Action + " Tactus in this chat");
        Chips(strip);
        if (was is null) Open();
    }

    private void Rebind() => Bind(source());

    private void Bind(PcChatFeed? next)
    {
        if (ReferenceEquals(next, feed)) return;
        if (feed is not null) feed.Changed -= Changed;
        feed = next;
        if (feed is not null) feed.Changed += Changed;
        Refresh();
    }

    private void Changed() => Dispatcher.UIThread.Post(Refresh);

    private void Act()
    {
        if (feed is null || Shown is not { } strip) return;
        if (strip.Paused) feed.Resume(thread);
        else feed.Pause(thread);
        Refresh();
    }

    private void Chips(PcChatStrip strip)
    {
        chips.Children.Clear();
        foreach (var app in strip.Apps)
        {
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (app.State switch { PcStripAppState.Working => Chrome.Accent, PcStripAppState.Pausing => Chrome.Orange, _ => Chrome.Gray }).Brush(this),
            };
            var name = new TextBlock { Text = app.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Chrome.SecondaryText.Brush(this) };
            var stop = new Button { Content = "×", Padding = new Thickness(4, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = app.Key };
            AutomationProperties.SetName(stop, "Stop " + app.Name);
            var key = app.Key;
            stop.Click += (_, _) => feed?.StopApp(key);
            // the app's icon (16 DIP; ED-NEW-pcctl-46), its state dot, its name and its x
            chips.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { PcAppIcons.Tile(app.Name, app.Key, 16), dot, name, stop } });
        }
    }

    // Opens by growing to its height under the clip (no fade).
    private void Open()
    {
        generation++;
        IsVisible = true;
        Grow(StripHeight);
    }

    // Closes by shrinking under the clip, then leaves the layout.
    private void Close()
    {
        var mine = ++generation;
        Grow(0);
        if (reduceMotion())
        {
            IsVisible = false;
            return;
        }
        DispatcherTimer.RunOnce(() =>
        {
            if (mine == generation) IsVisible = false;
        }, TimeSpan.FromSeconds(OpenSeconds));
    }

    private void Grow(double to)
    {
        Transitions = reduceMotion() ? null : [new DoubleTransition { Property = HeightProperty, Duration = TimeSpan.FromSeconds(OpenSeconds), Easing = new CubicEaseOut() }];
        Height = to;
    }
}
