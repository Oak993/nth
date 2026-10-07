// PORT-ONLY: multi-account step 3a (OD-0115 composer=A, OD-0119). Settings > Customization > "Pill in the composer": the section under "Composer" with the twelve styles as live cards (multi-account.html #czPick).
// It writes the one style setting every capsule follows. The glow previews follow the "Usage left" scrubber, which runs slowly by itself from 100 percent down to the Handshake point (4 percent) and starts again, unless it was touched in the last four seconds
// or Reduce Motion is on.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal sealed class PillCapsuleSection : Decorator
{
    private readonly CapsuleStyleSetting setting;
    private readonly PillStyleCards cards;
    private bool echoing;
    private Action<TimeSpan>? frame;
    private bool onScreen;
    private ContinuousInstant due;
    private ContinuousInstant idleUntil;
    private enum Leg { Running, Resting, Waiting }
    private Leg leg = Leg.Running;

    public PillCapsuleSection(CapsuleStyleSetting setting)
    {
        this.setting = setting;
        cards = new PillStyleCards(setting.Value);
        var rows = new CzRowStack();
        var title = new TextBlock { Text = "Pill in the composer", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(title);
        title.Foreground = ChromeTokens.PrimaryText.Brush(title);
        var detail = new TextBlock { Text = "How a thread that runs on a pill shows it. Default: Merged capsule.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(detail);
        detail.Foreground = ChromeTokens.SecondaryText.Brush(detail);
        var texts = new StackPanel { Spacing = 2, Margin = new Thickness(2, 0, 2, 10), Children = { title, detail } };
        rows.Add(new Border { Padding = new Thickness(12, 10, 12, 12), Child = new StackPanel { Children = { texts, cards } } });
        Child = new CzSection("Composer", rows, open: true);
        cards.SelectionChanged += id => { if (!echoing) setting.Value = id; };
        cards.Scrubber.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) ShowUsage(); };
        cards.Scrubber.Touched += Touch;
        // the run goes only while the scrubber is in view (the page: an IntersectionObserver at 30 percent)
        cards.Scrubber.EffectiveViewportChanged += (_, e) =>
        {
            var area = cards.Scrubber.Bounds.Width * cards.Scrubber.Bounds.Height;
            var seen = area > 0 && Area(e.EffectiveViewport.Intersect(new Rect(cards.Scrubber.Bounds.Size))) >= .3 * area;
            if (seen == onScreen) return;
            onScreen = seen;
            if (seen) Start(); else Stop();
        };
        AttachedToVisualTree += (_, _) =>
        {
            setting.Changed += OnChanged;
            OnChanged();
            ShowUsage();
            Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            setting.Changed -= OnChanged;
            Stop();
        };
        PropertyChanged += (_, e) => { if (e.Property == EnvironmentValues.AccessibilityReduceMotionProperty) { Stop(); Start(); } };
    }

    internal string Picked => cards.SelectedId;

    internal IReadOnlyList<PillCard> Cards => cards.Cards;

    internal IReadOnlyList<CzRadioButton> Radios => cards.Buttons;

    internal IReadOnlyList<AccountCapsule> Previews => cards.Previews;

    internal UsageScrubber Scrubber => cards.Scrubber;

    internal PillStyleCards Group => cards;

    private void OnChanged()
    {
        echoing = true;
        try { cards.Show(setting.Value); }
        finally { echoing = false; }
    }

    // ---- the preview follows the scrubber: only the glow styles show it (the page: pv holds the two glow previews)

    private void ShowUsage()
    {
        var left = cards.Scrubber.Value;
        foreach (var preview in cards.GlowPreviews) preview.Apply(PillPreview.Demo(1 - left / 100), animate: true);
    }

    // ---- the slow run

    private bool Still => CapsuleMotion.Reduce(this);

    private static double Area(Rect rect) => rect.Width * rect.Height;

    private void Touch() => idleUntil = SwiftClock.Now + TimeSpan.FromSeconds(4);

    private void Start()
    {
        if (frame is not null || !onScreen || Still || !FrameClock.IsBound || VisualRoot is null) return;
        leg = Leg.Running;
        due = SwiftClock.Now;
        frame = Tick;
        FrameClock.Source.Current.RequestFrame(frame);
    }

    private void Stop()
    {
        if (frame is { } pending) FrameClock.Source.Current.CancelFrame(pending);
        frame = null;
    }

    private void Tick(TimeSpan _)
    {
        frame = null;
        var now = SwiftClock.Now;
        if (now >= due)
        {
            switch (leg)
            {
                case Leg.Running:
                    if (now < idleUntil || cards.Scrubber.IsHeld) due = now + TimeSpan.FromMilliseconds(400);
                    else if (cards.Scrubber.Value > 4)
                    {
                        cards.Scrubber.Set(cards.Scrubber.Value - 1);
                        due = now + TimeSpan.FromMilliseconds(cards.Scrubber.Value > 60 ? 90 : 150);
                    }
                    else { leg = Leg.Resting; due = now + TimeSpan.FromMilliseconds(1800); }
                    break;
                case Leg.Resting:
                    cards.Scrubber.Set(100);
                    leg = Leg.Waiting;
                    due = now + TimeSpan.FromMilliseconds(1400);
                    break;
                default:
                    leg = Leg.Running;
                    break;
            }
        }
        if (Still || !onScreen || VisualRoot is null) return;
        frame = Tick;
        FrameClock.Source.Current.RequestFrame(frame);
    }
}
