// PORT-ONLY: first-release feature unit UE-5 (BUILD-PLAN.md 2.4 UE-5, OD-0100 place D). The glass pill above Settings in the sidebar. Design: update-everything/src/ue-page.css (.vD .spill) and the D shots.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Updates;

/// <summary>
/// The update pill (OD-0100 pick: place D). A glass capsule above the Settings rows, present only while something is out of date, running, failed or waiting for a restart
/// (<see cref="UpdatePillPhase"/>); its lead mark is a dot (updates waiting), the run's progress ring, the restart mark, the done check or the warning mark; its words are the U1 set. A press opens
/// <see cref="UpdatePopoverContent"/> on a popover above it. SOURCE FACT (design page, D): 34 pt high, 17 pt corner, 12.5 pt medium words, an 18 pt lead 8 pt from the words, 10 pt
/// before the lead and 14 pt after the words. It arrives and leaves by easing its height (the sidebar's list moves with it), opacity, 8 pt of rise and a 0.94 scale (OD-0076); a
/// change of words crossfades and the capsule's width eases (the design page's `width .32s`). ENGINEERING DECISIONS (ED-344): 6 pt between the pill and the row below it;
/// the capsule is the app's chrome glass (<see cref="Chrome.ChromeGlassCapsule"/>, the GlassMaterial of ED-320); the same easing curves as the page are not reproduced where the app has its own
/// (Chrome.PanelSlide for the arrival and the card's size).
/// </summary>
public sealed class UpdatePill : Decorator
{
    public const double PillHeight = 34;
    public const double GapBelow = 6;

    private sealed class ViewState : MainActorModel
    {
        private bool presented;

        public bool Presented { get => Read(presented); set => Set(ref presented, value); }
    }

    private sealed class RevealSlot : Decorator
    {
        public double Reveal { get; private set; }

        // Never IsVisible = false: an invisible control is not measured, and a parent is not told when it turns visible again. A pill that is away has no height, is clipped
        // to nothing and takes neither the pointer nor the keyboard.
        public void SetReveal(double value, Control press)
        {
            Reveal = value;
            ClipToBounds = value < 1;
            press.IsHitTestVisible = value > 0;
            press.Focusable = value > 0;
            AutomationProperties.SetAccessibilityView(this, value > 0 ? AccessibilityView.Content : AccessibilityView.Raw);
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child is not { } child) return default;
            child.Measure(availableSize);
            return new Size(child.DesiredSize.Width, (child.DesiredSize.Height + GapBelow) * Reveal);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(default, Child.DesiredSize));
            return finalSize;
        }
    }

    private sealed class LeadMark
    {
        public required Control View { get; init; }

        public required AnimatedScalar On { get; init; }
    }

    private readonly UpdateEverythingService service;
    private readonly ViewState state = new();
    private readonly RevealSlot slot = new();
    private readonly AnimatedScalar reveal;
    private readonly Panel face;
    private readonly Button button;
    private readonly OpacityCrossfade label = new();
    private readonly SizeSlot labelSlot;
    private readonly UpdateRing ring = new();
    private readonly Dictionary<UpdatePillPhase, LeadMark> marks = [];
    private readonly Popup popup;
    private UpdatePillState? last;
    private bool primed;

    /// <param name="service">The service whose state the pill shows; the shared one when none is given.</param>
    /// <param name="nested">True when the pill is inside a popover itself (the floating sidebar): its own popover is built after that one has opened.</param>
    public UpdatePill(UpdateEverythingService? service = null, bool nested = false)
    {
        this.service = service ?? UpdateEverythingService.Shared;

        // The lead: four marks stacked in an 18 pt square, the one for the phase on show.
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dot.Fill = Chrome.Accent.Brush(dot);
        var restart = SwiftImage.SystemName("arrow.clockwise").Font(SwiftFont.System(12, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.Accent);
        var warning = SwiftImage.SystemName("exclamationmark.triangle.fill").Font(SwiftFont.System(13, SwiftFont.Weight.Medium)).ForegroundStyle(Chrome.Orange);
        var done = SwiftImage.SystemName("checkmark.circle.fill").Font(SwiftFont.System(13, SwiftFont.Weight.Medium)).ForegroundStyle(SwiftColor.Rgb(0x34C759));
        var lead = new Grid { Width = UpdateRing.Size, Height = UpdateRing.Size, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (phase, view) in new (UpdatePillPhase, Control)[] { (UpdatePillPhase.Available, dot), (UpdatePillPhase.Running, ring), (UpdatePillPhase.Restart, restart), (UpdatePillPhase.Failed, warning), (UpdatePillPhase.Done, done) })
        {
            var captured = view;
            view.HorizontalAlignment = HorizontalAlignment.Center;
            view.VerticalAlignment = VerticalAlignment.Center;
            view.RenderTransformOrigin = RelativePoint.Center;
            lead.Children.Add(view);
            marks[phase] = new LeadMark { View = view, On = new AnimatedScalar(view, 0, value => { captured.Opacity = value; var scale = 0.5 + 0.5 * value; captured.RenderTransform = new ScaleTransform(scale, scale); }) };
        }

        labelSlot = new SizeSlot(label) { VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = PillHeight,
            Margin = new Thickness(10, 0, 14, 0),
            Children = { lead, labelSlot },
        };
        button = PlainControls.Button(row, () => state.Presented = !state.Presented).KeyboardFocus();
        face = (Panel)button.ChromeGlassCapsule();
        face.HorizontalAlignment = HorizontalAlignment.Left;
        face.VerticalAlignment = VerticalAlignment.Top;
        face.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);

        var binding = new Binding<bool>(() => state.Presented, value => state.Presented = value);
        popup = PlainControls.Popover(button, binding, () => new UpdatePopoverContent(this.service), PlacementMode.TopEdgeAlignedLeft, buildBeforeOpen: !nested);

        slot.Child = new Panel { Children = { face, popup } };
        Child = slot;
        reveal = new AnimatedScalar(slot, 0, value =>
        {
            slot.SetReveal(value, button);
            face.Opacity = value;
            face.RenderTransform = new TransformGroup { Children = { new ScaleTransform(0.94 + 0.06 * value, 0.94 + 0.06 * value), new TranslateTransform(0, (1 - value) * 8) } };
        });

        this.Track(() => this.service.State, Apply);
        // The done phase stays until it has been seen: closing the popover after it is the user's acknowledgement (the page's "later").
        var wasPresented = false;
        this.Track(() => state.Presented, presented =>
        {
            if (wasPresented && !presented && last is { Phase: UpdatePillPhase.Done }) this.service.AcknowledgeDone();
            wasPresented = presented;
        });
    }

    /// <summary>The button the pill is, for a press.</summary>
    internal Button Button => button;

    internal Control Slot => slot;

    /// <summary>The run's ring.</summary>
    internal UpdateRing Ring => ring;

    /// <summary>The words on show.</summary>
    internal string? LabelText => (label.Current as TextBlock)?.Text;

    /// <summary>0 while the pill is away, 1 when it is whole; moves as the pill arrives or leaves.</summary>
    internal double Revealed => reveal.Value;

    /// <summary>Whether the pill is on its way in or in.</summary>
    internal bool IsShown => last is { Phase: not UpdatePillPhase.Hidden };

    /// <summary>The popover the pill opens; its content is built when it shows.</summary>
    internal Popup Popover => popup;

    /// <summary>The popover's content as it would be built now.</summary>
    internal UpdatePopoverContent BuildPopover() => new(service);

    /// <summary>The opacity of each lead mark (1 on show, 0 off).</summary>
    internal double LeadOpacity(UpdatePillPhase phase) => marks.TryGetValue(phase, out var mark) ? mark.View.Opacity : 0;

    internal bool IsPresented => state.Presented;

    private void Apply(UpdatePillState next)
    {
        var animation = primed ? Chrome.PanelSlide : null;
        var quick = primed ? Animation.EaseOut(0.22) : null;
        primed = true;
        if (next.Phase == UpdatePillPhase.Hidden)
        {
            if (last is { Phase: not UpdatePillPhase.Hidden } && state.Presented) state.Presented = false;
            last = next;
            reveal.Set(0, animation);
            return;
        }
        var wasShown = IsShown;
        last = next;
        if (wasShown) labelSlot.Note(quick);
        Transaction.WithAnimation(quick, () =>
        {
            if (LabelText != next.PillText)
            {
                var text = new TextBlock { Text = next.PillText, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
                SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(text);
                text.Foreground = Chrome.PrimaryText.Brush(text);
                label.Show(text);
            }
        });
        foreach (var (phase, mark) in marks) mark.On.Set(phase == next.Phase ? 1 : 0, quick);
        if (next.Phase == UpdatePillPhase.Running) ring.Set(next.Fraction, quick); // the ring springs; it keeps its arc while the mark of the next phase takes over
        AutomationProperties.SetName(button, next.PillText);
        reveal.Set(1, animation);
    }
}
