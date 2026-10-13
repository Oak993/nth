// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 4.2 "Keyboard": the rule for every button on an irreversible card; the canvas's kHint
// and hold handlers, work/design/assistant/index.html lines 3026-3060, and .btn.irr / .khint CSS, lines 564-568;
// ED-NEW-dextra-as8-6). No Swift twin.
// A button that acts only on a pointer click, or on holding Space for one second while a fill sweeps across it (releasing early
// cancels). Enter, a short Space and an automation Invoke (a screen reader's default action) do nothing but flash the hint
// "Enter does nothing here. Click it, or hold Space for one second.", so a stray keypress can never send, pay, delete, install,
// commit, merge or discard. It never takes focus by itself: the card never focuses it, and focus never lands on it after a card
// above it closes.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class HoldToActButton : Button
{
    public const string EnterHint = "Enter does nothing here. Click it, or hold Space for one second.";
    public const string ShortHint = "Hold Space for a full second to confirm, or click it.";
    public static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(1);

    private readonly Border fill = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransform = new ScaleTransform(0, 1), RenderTransformOrigin = new RelativePoint(0, .5, RelativeUnit.Relative) };
    private readonly Popup hintPopup;
    private readonly LiveStatusText hintText = new();
    private ITimer? hold;
    private ITimer? hintOff;
    private bool pointerClick;
    private bool held;
    private bool hintShown;

    /// <summary>
    /// How the hint shows and hides: the popup itself (a small window over the button, as the canvas's fixed .khint). Headless
    /// tests cannot open a popup (the reason for R-VIEW-9's seam), so they record here instead.
    /// </summary>
    public static Ambient<Action<Popup, bool>> HintPresenter { get; } = new(static (popup, open) => popup.IsOpen = open);

    protected override Type StyleKeyOverride => typeof(Button);

    public HoldToActButton(Control face, string name, double radius = 13)
    {
        ArgumentNullException.ThrowIfNull(face);
        fill.CornerRadius = new CornerRadius(radius);
        fill.Background = Chrome.PrimaryText.Opacity(.24).Brush(fill);
        AutomationProperties.SetAccessibilityView(fill, AccessibilityView.Raw);
        Padding = default;
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetHelpText(this, EnterHint);
        hintText.TextWrapping = TextWrapping.Wrap;
        SwiftFont.System(12).Apply(hintText);
        hintText.MaxWidth = 260;
        var hintBox = new Border { Padding = new Thickness(10, 7), Child = hintText };
        hintBox.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        hintBox.Background = Chrome.PrimaryText.Brush(hintBox);
        hintText.Foreground = SwiftColor.Dynamic(new SwiftColor(red: .97, green: .97, blue: .98), new SwiftColor(red: .08, green: .08, blue: .09)).Brush(hintText);
        hintPopup = new Popup
        {
            Child = hintBox,
            PlacementTarget = this,
            Placement = PlacementMode.Top,
            VerticalOffset = -6,
            IsLightDismissEnabled = false,
            Focusable = false,
            IsHitTestVisible = false,
        };
        Content = new Panel { Children = { new Panel { ClipToBounds = true, Children = { face, fill } }, hintPopup } };
        LostFocus += (_, _) => Cancel(false);
    }

    /// <summary>The hint shown now, or null (tests).</summary>
    public string? Hint => hintShown ? hintText.Text : null;

    /// <summary>Whether a Space hold is under way.</summary>
    public bool IsHolding => hold is not null;

    /// <summary>Raised when the button acts: a pointer click, or a full one-second hold of Space.</summary>
    public event Action? Acted;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Flash(EnterHint);
            return;
        }
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (hold is not null || !IsEffectivelyEnabled) return;
            Sweep(true);
            hold = SwiftClock.Provider.Current.CreateTimer(_ => Dispatcher.UIThread.Post(Complete), null, HoldFor, Timeout.InfiniteTimeSpan);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (hold is not null) { Cancel(true); }
            return;
        }
        if (e.Key == Key.Enter) { e.Handled = true; return; }
        base.OnKeyUp(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        pointerClick = e.InitialPressMouseButton == MouseButton.Left && IsPointerOver;
        base.OnPointerReleased(e);
        pointerClick = false;
    }

    protected override void OnClick()
    {
        // only a pointer click or a completed hold acts; Enter, a tap of Space or an automation Invoke shows the hint
        if (!pointerClick && !held)
        {
            Flash(EnterHint);
            return;
        }
        held = false;
        base.OnClick();
        Acted?.Invoke();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private void Complete()
    {
        if (hold is null) return;
        hold.Dispose();
        hold = null;
        Sweep(false, still: true);
        held = true;
        OnClick();
    }

    private void Cancel(bool hint)
    {
        if (hold is null) return;
        hold.Dispose();
        hold = null;
        Sweep(false);
        if (hint) Flash(ShortHint);
    }

    private void Sweep(bool on, bool still = false)
    {
        if (fill.RenderTransform is not ScaleTransform scale) return;
        var quiet = still || TaskLook.Still(this);
        scale.Transitions = quiet && !on
            ? null
            : [new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = on ? HoldFor : TimeSpan.FromMilliseconds(200), Easing = on ? new Avalonia.Animation.Easings.LinearEasing() : new Avalonia.Animation.Easings.SplineEasing(.32, .72, 0, 1) }];
        scale.ScaleX = on ? 1 : 0;
    }

    private void Flash(string text)
    {
        hintText.Text = text;
        ShowHint(true);
        hintOff?.Dispose();
        hintOff = SwiftClock.Provider.Current.CreateTimer(_ => Dispatcher.UIThread.Post(() => ShowHint(false)), null, TimeSpan.FromMilliseconds(1800), Timeout.InfiniteTimeSpan);
    }

    private void ShowHint(bool open)
    {
        if (hintShown == open) return;
        hintShown = open;
        HintPresenter.Current(hintPopup, open);
    }

    // the screen reader's Invoke goes through OnClick, so it only shows the hint
    private sealed class Peer(HoldToActButton owner) : ButtonAutomationPeer(owner)
    {
        protected override string? GetHelpTextCore() => EnterHint;
    }
}
