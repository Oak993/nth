// PORT-ONLY CZ-6 (customization-SPEC CZ-3.5; OWNER OD-0089 amendment, OD-0076): what makes the Customization page's pinned block sticky. It finds the page's ancestor ScrollViewer on attach and follows its offset `y` (it never changes
// SettingsView's scroll machinery), and on every step it changes four things, none of which lays a section out again (CZ-3.5.7):
//   - the block's transform: its visual top is clamp(y, 0, H_c - h_pin) from the sticky container's top, so it stays at the viewer's top and lets go when the container's last section ends (CZ-3.5.1, CZ-3.5.3);
//   - the compaction `col` = min(92, y), exactly for a step of a wheel or a drag and eased (tau 0.06 s, on the frame clock) for a jump; it sets the stage's height and the conversation text (CZ-3.5.2);
//   - a clip rectangle on every section of the sticky container, from the block's bottom edge down, so what scrolls under the block is neither drawn nor hit (CZ-3.5.4);
//   - the hairline's state, shown while y is past 4 (CZ-3.5.5).
// Keyboard focus is kept clear of the block (CZ-3.5.6): a request to bring a control into view (the viewer's own answer to Tab, which scrolls at once, the control left under the block's edge) is answered here instead: the page scrolls,
// over 0.3 s ease, until the control's top is 8 below the block's bottom. A pointer focus does not scroll. The arithmetic is Core's StickyStage; this class is the frame clock, the viewer and the controls.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class StickyStageController
{
    // a clip never starts further than this above a section's top: nothing a section draws outside its bounds (a focus ring, a shadow) reaches past it
    private const double ClipMargin = 64;
    private const double ClipExtent = 100000;

    private readonly Control page;
    private readonly Panel sticky;
    private readonly CustomizationStage stage;
    private readonly CzMotion motion;
    private readonly Animation focusCurve = CzMotion.Ease(StickyStage.FocusSeconds);
    private readonly Action<TimeSpan> frame;
    private readonly Dictionary<Control, double> clipped = [];
    private ScrollViewer? scroll;
    private Compaction compaction = new(0, false);
    private ContinuousInstant lastFrame;
    private IFrameSource? frames;
    private bool waiting;
    private bool applying;
    private double pinTop;

    // the focus scroll in flight
    private bool focusing;
    private double focusFrom, focusTo, focusApplied;
    private ContinuousInstant focusStart;

    // the last keyboard focus the viewer reported: its target and how it came (Tab, arrows or a pointer)
    private object? focusTarget;
    private NavigationMethod focusMethod;

    internal StickyStageController(Control page, Panel sticky, CustomizationStage stage)
    {
        this.page = page;
        this.sticky = sticky;
        this.stage = stage;
        motion = new CzMotion(stage);
        frame = Tick;
        stage.ZIndex = StageZIndex;
        page.AddHandler(InputElement.GotFocusEvent, (_, e) => { focusTarget = e.Source; focusMethod = e.NavigationMethod; }, RoutingStrategies.Bubble, handledEventsToo: true);
        page.AddHandler(Control.RequestBringIntoViewEvent, OnBringIntoView);
        page.AttachedToVisualTree += (_, _) => Attach();
        page.DetachedFromVisualTree += (_, _) => Detach();
        page.LayoutUpdated += (_, _) => Apply();
        if (TopLevel.GetTopLevel(page) is not null) Attach();
    }

    /// <summary>CZ-3.5.1: the block is the highest in the page.</summary>
    internal const int StageZIndex = 1000;

    /// <summary>The compaction applied now (CZ-3.5.2).</summary>
    internal double Compaction => compaction.Value;

    /// <summary>Whether a jump of the compaction is being eased.</summary>
    internal bool Easing => compaction.Easing;

    /// <summary>Whether the page is being scrolled to a focused control.</summary>
    internal bool FocusScrolling => focusing;

    private double Offset => scroll?.Offset.Y ?? 0;

    private void Attach()
    {
        var found = page.FindAncestorOfType<ScrollViewer>();
        if (found == scroll) return;
        Detach();
        scroll = found;
        if (scroll is null) return;
        scroll.ScrollChanged += OnScrollChanged;
        scroll.PropertyChanged += OnScrollProperty;
        // a page put into a viewer that is already scrolled takes the compaction for that offset at once (it was never shown at another)
        compaction = new Compaction(StickyStage.Target(Offset), false);
        shownOffset = Offset;
        Apply();
    }

    private void Detach()
    {
        Stop();
        focusing = false;
        if (scroll is not null)
        {
            scroll.ScrollChanged -= OnScrollChanged;
            scroll.PropertyChanged -= OnScrollProperty;
        }
        scroll = null;
    }

    private void OnScrollProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty) Scrolled();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => Scrolled();

    private double shownOffset = double.NaN;

    // the viewer's offset changed: CZ-3.5.2 takes a step the compaction can follow exactly at once and starts easing a jump
    private void Scrolled()
    {
        var y = Offset;
        if (y == shownOffset) return;
        shownOffset = y;
        // the user's own scroll during a focus scroll takes it over
        if (focusing && Math.Abs(y - focusApplied) > 0.5) focusing = false;
        var before = compaction;
        compaction = StickyStage.Scrolled(compaction, y, motion.Still);
        if (compaction.Easing && !before.Easing) lastFrame = SwiftClock.Now;
        Apply();
        Wake();
    }

    // everything the offset and the layout say, applied to the block, the sections and the hairline; idempotent
    private void Apply()
    {
        if (scroll is null || applying || stage.Bounds.Height <= 0) return;
        applying = true;
        try
        {
            var y = Offset;
            stage.Compact(compaction.Value);
            var block = StickyStage.BlockHeight(stage.Bounds.Height, compaction.Value);
            pinTop = StickyStage.PinTop(y - ContentTop(sticky), sticky.Bounds.Height, block);
            stage.PinTo(pinTop);
            stage.ShowHairline(StickyStage.HairlineShown(y));
            Clip(pinTop + block);
        }
        finally { applying = false; }
    }

    // where a visual's top is in the viewer's content (from its top, whatever the offset): the arranged positions up to the viewer's content, and that content's own margin. Layout only, so it is right
    // in the same turn the offset changes, before the viewer has arranged its content at the new one. The container's own is 0 in the Settings window, where the page takes the 60 above it back.
    private double ContentTop(Visual visual)
    {
        var content = scroll?.Content as Control;
        var y = 0.0;
        for (var v = visual; v is not null && v != content; v = v.GetVisualParent()) y += v.Bounds.Y;
        return y + (content?.Margin.Top ?? 0);
    }

    // CZ-3.5.4: from the block's bottom edge (in the container's coordinates) down, every section is clipped: its own rectangle, in its own coordinates
    private void Clip(double blockBottom)
    {
        foreach (var child in sticky.Children)
        {
            if (child is not Control section || section == stage) continue;
            var height = section.Bounds.Height;
            if (height <= 0 && !clipped.ContainsKey(section)) continue;
            var edge = Math.Clamp(blockBottom - section.Bounds.Y, -ClipMargin, height + ClipMargin);
            if (clipped.TryGetValue(section, out var shown) ? shown == edge : edge <= -ClipMargin) continue;
            clipped[section] = edge;
            section.Clip = edge <= -ClipMargin ? null : new RectangleGeometry(new Rect(-ClipExtent / 2, edge, ClipExtent, ClipExtent));
        }
    }

    private void Wake()
    {
        if (waiting || scroll is null || (!compaction.Easing && !focusing)) return;
        frames = FrameClock.Source.Current;
        lastFrame = SwiftClock.Now;
        waiting = true;
        frames.RequestFrame(frame);
    }

    private void Stop()
    {
        if (waiting) frames?.CancelFrame(frame);
        waiting = false;
    }

    private void Tick(TimeSpan _)
    {
        waiting = false;
        if (scroll is null) return;
        var now = SwiftClock.Now;
        var seconds = Math.Max(0, (now - lastFrame).TotalSeconds);
        lastFrame = now;
        if (focusing) StepFocus(now);
        if (compaction.Easing)
        {
            compaction = StickyStage.Frame(compaction, Offset, seconds, motion.Still);
            Apply();
        }
        Wake();
    }

    // CZ-3.5.6: the offset on its 0.3 s ease; the viewer reports each step, and the compaction follows it by CZ-3.5.2
    private void StepFocus(ContinuousInstant now)
    {
        var span = TimeSpan.FromSeconds(focusCurve.Duration);
        var complete = (now - focusStart).Ticks + TimeSpan.TicksPerMillisecond >= span.Ticks; // a span is complete 1 ms early at most (ED-117 (2))
        var y = complete ? focusTo : focusFrom + (focusTo - focusFrom) * focusCurve.Progress((now - focusStart).TotalSeconds);
        SetOffset(y);
        if (complete) focusing = false;
    }

    private void SetOffset(double y)
    {
        y = Math.Clamp(y, 0, MaxOffset());
        focusApplied = y; // before the viewer reports it, so it is not taken for the user's
        scroll!.Offset = new Vector(scroll.Offset.X, y);
    }

    // the viewer asks that a control be brought into view (its answer to a focus that came by Tab or the arrows)
    private void OnBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (scroll is null || e.TargetObject is not Control target || target == page || !page.IsVisualAncestorOf(target)) return;
        e.Handled = true; // the viewer's own scroll is immediate and leaves the control under the block's edge: this page answers
        // CZ-3.5.6: a pointer focus does not scroll
        if (ReferenceEquals(focusTarget, target) && focusMethod == NavigationMethod.Pointer) return;
        var rect = e.TargetRect;
        var viewport = scroll.Viewport.Height;
        var y = Offset;
        var containerTop = ContentTop(sticky);
        var rest = stage.Bounds.Height;
        var block = StickyStage.BlockHeight(rest, compaction.Value);
        double wanted;
        if (ReferenceEquals(stage, target) || stage.IsVisualAncestorOf(target))
        {
            // a control of the block itself lies where the block is, so it needs the page only once the block has been carried off with it (and then only to the point it is let go at)
            var blockTop = containerTop + pinTop - y;
            if (blockTop >= -0.5 && blockTop + block <= viewport + 0.5) return;
            wanted = containerTop + Math.Max(0, sticky.Bounds.Height - StickyStage.BlockHeight(rest, StickyStage.MaxCompaction));
        }
        else
        {
            var contentTop = ContentTop(target) + rect.Y;
            var top = contentTop - y;
            var blockBottom = containerTop + pinTop + block - y;
            if (!StickyStage.FocusNeedsScroll(top, top + rect.Height, blockBottom, viewport)) return;
            wanted = containerTop + StickyStage.FocusOffset(contentTop - containerTop, rest, MaxOffset() - containerTop);
        }
        ScrollTo(wanted);
    }

    private double MaxOffset() => Math.Max(0, scroll!.Extent.Height - scroll.Viewport.Height);

    // 0.3 s ease from where the page is; at once under reduce motion (the compaction is then at its target at once too)
    private void ScrollTo(double y)
    {
        y = Math.Clamp(y, 0, MaxOffset());
        if (Math.Abs(y - Offset) < 0.5) return;
        if (motion.Still)
        {
            focusing = false;
            SetOffset(y);
            return;
        }
        focusFrom = Offset;
        focusTo = y;
        focusStart = SwiftClock.Now;
        focusApplied = Offset;
        focusing = true;
        Wake();
    }
}
