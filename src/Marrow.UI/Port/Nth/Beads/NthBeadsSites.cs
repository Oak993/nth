// PORT-ONLY: Calculi WU6 (ED-NEW-calculi-wu6-1; DESIGN.md sections 6, 7, 9 and 12). The small sites of the working beads: the sidebar's thread
// rows, the Hydra chip, head footers and send badge, the helper capsule, the background tasks row, the Tactus pill and the "N running tasks"
// chip. Each site is a place with a fixed size, family and rate (the section 9 table); a slot holds the beads at that size in a box of the
// n box's own size, so a row lays out as its old spinner did, and the beads' envelope (1.05 x 1.23 of the box) overhangs into the row's padding
// and is never clipped. The UI follows its own state: the slot begins and ends with the host, and nothing waits for the beads.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Port.Nth.Beads;

/// <summary>Where a small mark of the working beads lives (section 9).</summary>
public enum NthBeadsPlace : byte
{
    /// <summary>A sidebar thread row, helper row or ghost: 12 DIP, 30 Hz, quiet.</summary>
    SidebarRow,
    /// <summary>The Hydra header chip: 12 DIP, 30 Hz, quiet Tripus.</summary>
    HydraChip,
    /// <summary>A Hydra head's footer: 12 DIP, 30 Hz, quiet Tripus.</summary>
    HydraHeadFooter,
    /// <summary>The Hydra send badge: 12 DIP, 30 Hz, quiet Tripus, then it morphs to the count as before.</summary>
    HydraBadge,
    /// <summary>The helper capsule (<c>SubagentWorkingBox</c>): 14 DIP, 60 Hz, the thinking family.</summary>
    HelperCapsule,
    /// <summary>The background tasks pane row: 16 DIP, 30 Hz, quiet Pulsus.</summary>
    BackgroundRow,
    /// <summary>The Tactus status pill: 14 DIP, 60 Hz, Tactus.</summary>
    TactusPill,
    /// <summary>A pill or chip saying "N running tasks": 12 DIP, 30 Hz, quiet Tripus.</summary>
    TasksChip,
}

/// <summary>A place's size, rate and family: the n box in DIP, whether it runs the quiet family, the 30 Hz ambient rate or the full rate, and the state it holds.</summary>
public readonly record struct NthBeadsSiteSpec(double Size, bool Quiet, bool Ambient, bool FullRate, NthBeadsState State);

/// <summary>The places of the small sites and the beads made for them.</summary>
public static class NthBeadsSites
{
    /// <summary>The accessible name of a working mark (MiniSpinner's, PR-THEME-171).</summary>
    public const string Name = "Working";

    /// <summary>The section 9 table, one row a place.</summary>
    public static NthBeadsSiteSpec SpecOf(NthBeadsPlace place) => place switch
    {
        NthBeadsPlace.SidebarRow => new(12, true, true, false, NthBeadsState.Tool),
        NthBeadsPlace.HydraChip or NthBeadsPlace.HydraHeadFooter or NthBeadsPlace.HydraBadge or NthBeadsPlace.TasksChip => new(12, true, true, false, NthBeadsState.Hydra),
        NthBeadsPlace.HelperCapsule => new(14, false, false, true, NthBeadsState.Thinking),
        NthBeadsPlace.BackgroundRow => new(16, true, true, false, NthBeadsState.Background),
        NthBeadsPlace.TactusPill => new(14, false, false, true, NthBeadsState.Tactus),
        _ => throw new ArgumentOutOfRangeException(nameof(place), place, null),
    };

    /// <summary>The beads for a place, set up but not yet entered (<paramref name="state"/> overrides the place's own).</summary>
    public static NthBeadsControl Create(NthBeadsPlace place, NthBeadsState? state = null)
    {
        var spec = SpecOf(place);
        var beads = new NthBeadsControl
        {
            GlyphSize = spec.Size, Quiet = spec.Quiet, Ambient = spec.Ambient, FullRate = spec.FullRate,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        beads.SetState(state ?? spec.State);
        return beads;
    }
}

/// <summary>
/// A small site's beads in a box of the n box's size. It lays out like the spinner it replaces (a 12 or 14 DIP square) and puts the beads'
/// envelope, centred as section 9 gives it, over that box. It is hit-testable (a row's tooltip sits on it) and named "Working" for assistive technology.
/// </summary>
public sealed class NthBeadsSlot : Panel
{
    /// <summary>The beads (the same control for the slot's whole life).</summary>
    public NthBeadsControl Beads { get; }

    public NthBeadsPlace Place { get; }

    /// <summary>The seed's source: the same key gives the same pulse direction and tempo; different keys differ (each row on its own phase).</summary>
    public string Key { get; }

    public NthBeadsSlot(NthBeadsPlace place, string key, NthBeadsState? state = null, bool running = true)
    {
        Place = place;
        Key = key;
        var spec = NthBeadsSites.SpecOf(place);
        Width = spec.Size; Height = spec.Size;
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Center;
        Background = Brushes.Transparent;
        Beads = NthBeadsSites.Create(place, state);
        Children.Add(Beads);
        AutomationProperties.SetName(this, NthBeadsSites.Name);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Content);
        if (running) Begin();
    }

    /// <summary>Whether a turn of the beads is on: entered and not yet leaving.</summary>
    public bool IsRunning => Beads.Current.Turn > 0 && Beads.Current.Exit == NthBeadsExit.None;

    /// <summary>The host is showing work: the beads are born at the n's dot and settle on their hub (Ortus). Does nothing while they already run.</summary>
    public void Begin()
    {
        if (!IsRunning) Beads.Enter(NthBeadsEntrance.Ortus, Key);
    }

    /// <summary>The host stopped showing work: the beads leave (Occasus) beside whatever the host shows now. Does nothing when they do not run.</summary>
    public void End()
    {
        if (IsRunning) Beads.Finish();
    }

    /// <summary>The hub the beads hold follows the state (a state change travels through the hubs).</summary>
    public void SetState(NthBeadsState state) => Beads.SetState(state);

    protected override Size MeasureOverride(Size availableSize)
    {
        Beads.Measure(Size.Infinity);
        return new Size(Width, Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var box = Beads.DesiredSize;
        var u = NthBeadsPainter.Unit(Beads.GlyphSize);
        // the envelope's centre is 0.04 units below the n box's centre (section 9)
        var centreY = ((EnvelopeY0 + EnvelopeY1) / 2 - BoxCentreY) * u;
        var centreX = ((EnvelopeX0 + EnvelopeX1) / 2 - BoxCentreX) * u;
        Beads.Arrange(new Rect((finalSize.Width - box.Width) / 2 + centreX, (finalSize.Height - box.Height) / 2 + centreY, box.Width, box.Height));
        return finalSize;
    }
}

/// <summary>
/// The beads as a picture: a ghost carried as a snapshot (a row that glides or genies) holds no composition visual, so it draws the three
/// beads of a hub in the flat mark colour with ordinary rendering, the pose the moving beads hold between pulses.
/// </summary>
public sealed class NthBeadsStill : Control
{
    /// <summary>The n box in DIP.</summary>
    public static readonly StyledProperty<double> GlyphSizeProperty = AvaloniaProperty.Register<NthBeadsStill, double>(nameof(GlyphSize), 12);

    /// <summary>The hub whose pose is drawn.</summary>
    public static readonly StyledProperty<NthBeadsHub> HubProperty = AvaloniaProperty.Register<NthBeadsStill, NthBeadsHub>(nameof(Hub), NthBeadsHub.Pedes);

    static NthBeadsStill()
    {
        AffectsRender<NthBeadsStill>(HubProperty, GlyphSizeProperty);
        AffectsMeasure<NthBeadsStill>(GlyphSizeProperty);
    }

    public NthBeadsStill()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public double GlyphSize { get => GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public NthBeadsHub Hub { get => GetValue(HubProperty); set => SetValue(HubProperty, value); }

    /// <summary>The picture's box: the n box, as the slot lays it out.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(GlyphSize, GlyphSize);

    /// <summary>The bead centres in DIP from the box's top-left, and the radius (the small-size rule).</summary>
    public static (Point[] Centres, double Radius) Pose(NthBeadsHub hub, double size)
    {
        var u = NthBeadsPainter.Unit(size);
        var centres = new Point[3];
        for (var i = 0; i < 3; i++)
        {
            var p = HubPoint(hub, i);
            centres[i] = new Point(size / 2 + (p.X - BoxCentreX) * u, size / 2 + (p.Y - BoxCentreY) * u);
        }
        return (centres, BeadRadius * NthBeadsPainter.RadiusFactor(size) * u);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = GlyphSize;
        if (size <= 0 || !HasPose(Hub)) return;
        var ink = NthBeadsInk.For(PlainControls.IsDark(ActualThemeVariant));
        var brush = new SolidColorBrush(Color.FromArgb(ink.Flat.Alpha, ink.Flat.Red, ink.Flat.Green, ink.Flat.Blue));
        var (centres, radius) = Pose(Hub, size);
        foreach (var c in centres) context.DrawEllipse(brush, null, c, radius, radius);
    }
}
