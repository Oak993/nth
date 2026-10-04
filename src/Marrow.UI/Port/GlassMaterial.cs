// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184, :218-259;
// App/Views/Composer/ComposerView.swift:23; App/Views/Chat/ChatView.swift:1120-1124; App/Views/Diff/DiffInspector.swift:25.
// PORT-ONLY: OD-0005 / ADR-0002 Glass (b), Skia leases as demonstrated in porting/probes/p1-glass.
// Optical values are an ED proposal in glass-material.report.md, UNVERIFIED under OD-0001.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using SkiaSharp;

namespace Marrow.UI.Port;

internal static class GlassMaterial
{
    private static readonly ConditionalWeakTable<Control, Attachment> attachments = new();
    public static void Attach(Control control) => AttachmentFor(control).Queue();
    private static Attachment AttachmentFor(Control control) => attachments.GetValue(control, static c => new Attachment(c));
    private static Control? ContainerOf(Control control) => control.GetVisualAncestors().OfType<Control>()
        .FirstOrDefault(c => SwiftGlass.GetContainerSpacing(c) is not null);

    private sealed class Attachment
    {
        private readonly Control owner;
        private CompositionCustomVisual? material;
        private CompositionContainerVisual? parent;
        private Control? input;
        private State? previous;
        private bool queued, requested, hovered, pressed;
        private Point pointer;
        // A container's regions carry each piece's opacity and place as they were at its last synchronisation, and a piece fades or
        // rises with the opacity and render transform of any visual between it and the container (a soft appear moves only those,
        // with no layout pass). The container therefore watches those visuals (and the pieces) and synchronises on the next
        // composition update after any of them changes, in the frame the change was made. The delegates are made once, so a change
        // costs no allocation; the watched set is rebuilt only when it differs.
        private readonly Dictionary<Visual, Transform?> watched = new();
        private readonly HashSet<Visual> wanted = new();
        private readonly List<Visual> unwanted = new();
        private readonly EventHandler<AvaloniaPropertyChangedEventArgs> watchedChanged;
        private readonly EventHandler transformChanged;
        private readonly Action posted, synchronize;

        internal Control Owner => owner;

        public Attachment(Control owner)
        {
            this.owner = owner;
            watchedChanged = WatchedChanged;
            transformChanged = (_, _) => RequestSynchronize();
            synchronize = Synchronize;
            posted = () =>
            {
                queued = false;
                ElementComposition.GetElementVisual(this.owner)?.Compositor.RequestCompositionUpdate(synchronize);
            };
            owner.AttachedToVisualTree += (_, _) => { requested = false; Queue(); };
            owner.DetachedFromVisualTree += (_, _) => { RemoveMaterial(); UnbindInput(); Unwatch(); StopTurn(); requested = false; };
            owner.LayoutUpdated += (_, _) => Queue();
            owner.PropertyChanged += (_, _) => Queue();
            owner.ActualThemeVariantChanged += (_, _) => Queue();
        }

        // The window is redrawn whole once, a frame after an eased turnover of its glass has ended. The turnover is drawn by partial redraws, the glass's
        // own and those of the colours that ease beside it, and where they overlap the antialiased edge of a vector icon near a piece (the Models page's
        // chevron, the Mcp page's server icon) is left a few levels from what a whole drawing gives (LookCardTurnoverTests: 4 and 44 channels, past the
        // 2 allowed; with the kept backdrop (Backdrop) and the material's full render bounds, 5 of 18 pages without it); one whole redraw after the last frame
        // of the ease settles the picture to the whole drawing. Measured, not traced into Avalonia's dirty-region merging. The window counts the pieces still turning; when the last one ends or leaves the tree (its run stopped, GlassMaterialTurnoverRepaintTests),
        // the repaint comes a frame later, unless another turn has begun by then (a turnover that a later one interrupts does not repaint; the later one does).
        // Nothing is drawn extra for a change made at once.
        private sealed class WindowTurns { public int Turning; public bool Owed; }
        private static readonly ConditionalWeakTable<TopLevel, WindowTurns> windowTurns = new();
        // Begins a piece's eased turn in its window; the action returned ends it (once, whether the run reached its end or was stopped).
        private static Action BeginTurn(Control control)
        {
            if (TopLevel.GetTopLevel(control) is not { } top) return static () => { };
            var turns = windowTurns.GetOrCreateValue(top);
            turns.Turning++;
            turns.Owed = true;
            var ended = false;
            return () =>
            {
                if (ended) return;
                ended = true;
                if (--turns.Turning == 0) FrameClock.Source.Current.RequestFrame(_ => RepaintWhole(top, turns));
            };
        }
        private static void RepaintWhole(TopLevel top, WindowTurns turns)
        {
            if (turns.Turning != 0 || !turns.Owed) return;
            turns.Owed = false;
            foreach (var visual in top.GetVisualDescendants()) visual.InvalidateVisual();
        }

        public void Queue()
        {
            if (queued) return;
            queued = true;
            Dispatcher.UIThread.Post(posted, DispatcherPriority.Loaded);
        }

        // Synchronise at the next composition update, without a dispatcher hop: for a change that is already on the render path.
        private void RequestSynchronize()
        {
            if (requested || ElementComposition.GetElementVisual(owner)?.Compositor is not { } compositor) return;
            requested = true;
            compositor.RequestCompositionUpdate(synchronize);
        }

        private void WatchedChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            var property = e.Property; // IsVisible is not watched: it invalidates layout, and the layout pass that follows synchronises (the visibility tests prove it)
            if (property != Visual.OpacityProperty && property != Visual.RenderTransformProperty && property != Visual.RenderTransformOriginProperty) return;
            if (property == Visual.RenderTransformProperty && sender is Visual visual && watched.TryGetValue(visual, out var old))
            {
                if (old is not null) old.Changed -= transformChanged;
                var next = visual.RenderTransform as Transform;
                if (next is not null) next.Changed += transformChanged;
                watched[visual] = next;
            }
            RequestSynchronize();
        }

        // The visuals from each glass piece (itself included) up to, not including, the container are the ones whose opacity or
        // transform the piece is drawn with. Called with `wanted` filled; subscribes the new ones and drops the others.
        private void Rewatch()
        {
            var same = watched.Count == wanted.Count;
            if (same) foreach (var visual in wanted) if (!watched.ContainsKey(visual)) { same = false; break; }
            if (same) return;
            foreach (var visual in watched.Keys) if (!wanted.Contains(visual)) unwanted.Add(visual);
            foreach (var visual in unwanted) Unwatch(visual);
            unwanted.Clear();
            foreach (var visual in wanted)
            {
                if (watched.ContainsKey(visual)) continue;
                var transform = visual.RenderTransform as Transform;
                if (transform is not null) transform.Changed += transformChanged;
                visual.PropertyChanged += watchedChanged;
                watched[visual] = transform;
            }
        }
        private void Unwatch(Visual visual)
        {
            if (!watched.Remove(visual, out var transform)) return;
            if (transform is not null) transform.Changed -= transformChanged;
            visual.PropertyChanged -= watchedChanged;
        }
        private void Unwatch()
        {
            unwanted.AddRange(watched.Keys);
            foreach (var visual in unwanted) Unwatch(visual);
            unwanted.Clear();
        }

        // Puts the material directly before the owner's visual in the parent's composition children. A material not yet in the parent reads -1, which
        // is also "just before" a control at index 0, so only a material that is present and directly before the owner stays where it is (a control
        // that is its parent's first child got no material at all).
        private void Place(CompositionVisual visual)
        {
            if (material is null || parent is null) return;
            var index = parent.Children.IndexOf(visual);
            if (index < 0) return;
            var existing = parent.Children.IndexOf(material);
            if (existing >= 0 && existing == index - 1) return;
            if (existing >= 0) parent.Children.Remove(material);
            parent.Children.Insert(parent.Children.IndexOf(visual), material);
        }

        // Put back after Avalonia re-synchronised the parent's composition children (see WindowHook).
        internal void Reinsert()
        {
            if (material is not null && parent is not null && ElementComposition.GetElementVisual(owner) is { } visual) Place(visual);
        }

        private void Synchronize()
        {
            requested = false;
            var spacing = SwiftGlass.GetContainerSpacing(owner);
            var ownEffect = SwiftGlass.GetEffect(owner);
            BindInput(ownEffect?.IsInteractive == true);
            if (spacing is null && ContainerOf(owner) is { } group)
            {
                RemoveMaterial();
                AttachmentFor(group).Queue();
                return;
            }
            var visual = ElementComposition.GetElementVisual(owner);
            var nextParent = owner.GetVisualParent() is { } p ? ElementComposition.GetElementVisual(p) as CompositionContainerVisual : null;
            if ((spacing is null && ownEffect is null) || visual is null || nextParent is null || SwiftGlass.GetUsesDesktopBackdrop(owner))
            {
                RemoveMaterial();
                return;
            }
            if (parent != nextParent)
            {
                RemoveMaterial();
                parent = nextParent;
                material = visual.Compositor.CreateCustomVisual(new Handler());
                // A custom visual clips to its Size unless told not to, and that clip also cut its render bounds back to the owner's: Avalonia then
                // redrew only the owner's box when the material appeared or changed, not the margin it reads (SampledMargin, Backdrop).
                material.ClipToBounds = false;
            }
            var index = parent.Children.IndexOf(visual);
            if (index < 0 || material is null) return;
            Place(visual);
            Hook(this);
            // Preserve source control type, layout, tint, hairline and foreground; draw immediately before it.
            material.Offset = visual.Offset;
            material.Translation = visual.Translation;
            material.Size = visual.Size;
            material.AnchorPoint = visual.AnchorPoint;
            material.CenterPoint = visual.CenterPoint;
            material.Scale = visual.Scale;
            material.RotationAngle = visual.RotationAngle;
            material.Orientation = visual.Orientation;
            material.Opacity = visual.Opacity;
            material.Visible = visual.Visible;
            material.OpacityMask = visual.OpacityMask;

            var regions = new List<Region>();
            wanted.Clear();
            if (spacing is null)
            {
                if (RegionFor(owner, LocalTransform(owner), 1) is { } r) regions.Add(r);
            }
            else
            {
                foreach (var child in owner.GetVisualDescendants().OfType<Control>())
                {
                    if (SwiftGlass.GetEffect(child) is null || ContainerOf(child) != owner) continue;
                    for (Visual? node = child; node is not null && node != owner; node = node.GetVisualParent()) wanted.Add(node);
                    var toOwner = child.TransformToVisual(owner);
                    if (toOwner is null) continue;
                    var opacity = 1.0;
                    for (Visual? node = child; node is not null && node != owner; node = node.GetVisualParent())
                        opacity *= node.IsVisible ? node.Opacity : 0;
                    if (RegionFor(child, toOwner.Value * LocalTransform(owner), opacity) is { } r) regions.Add(r);
                }
            }
            Rewatch();
            var state = new State(regions.ToArray(), spacing ?? 0);
            if (previous is null || previous.Spacing != state.Spacing || !previous.Regions.SequenceEqual(state.Regions))
            {
                previous = state;
                material.SendHandlerMessage(state);
            }
        }

        private static Matrix LocalTransform(Control control)
        {
            if (control.RenderTransform is not { } transform) return Matrix.Identity;
            var origin = control.RenderTransformOrigin.ToPixels(control.Bounds.Size);
            return Matrix.CreateTranslation(-origin.X, -origin.Y) * transform.Value * Matrix.CreateTranslation(origin.X, origin.Y);
        }

        private Region? RegionFor(Control control, Matrix toOwner, double opacity)
        {
            var e = SwiftGlass.GetEffect(control);
            if (e is null || SwiftGlass.GetUsesDesktopBackdrop(control) || opacity <= 0 || control.Bounds.Width <= 0 || control.Bounds.Height <= 0 || !toOwner.TryInvert(out _)) return null;
            var resolved = e.TintColor?.Opacity(e.TintOpacity ?? 1).Resolve(control.ActualThemeVariant) ?? Colors.Transparent;
            // A shape's fill and corners are the Border's or, for a SwiftUI shape with its own curve, the SwiftShapeBorder's (ED-181).
            var (fill, ownCorners) = control switch
            {
                Border border => (border.Background, (CornerRadius?)SwiftShapes.StatedCornerRadius(border)), // the stated radius: the shader draws the corner (OD-0102) itself
                SwiftShapeBorder shape => (shape.Background, shape.CornerRadius),
                _ => ((IBrush?)null, null),
            };
            var drawnTint = fill is ISolidColorBrush background && background.Color.A > 0 && e.TintColor is not null;
            var corners = ownCorners ?? new CornerRadius(e.Shape.CornerRadius ?? 0);
            if (e.Shape.Kind == GlassShapeKind.RoundedRectangle && corners == default) corners = new CornerRadius(e.Shape.CornerRadius ?? 0);
            var a = AttachmentFor(control);
            var (darkness, tint) = a.Appearance(control, this, e, resolved);
            return new Region(control.Bounds.Size, e.Shape, corners, tint, drawnTint, darkness,
                e.IsInteractive && a.hovered, e.IsInteractive && a.pressed, a.pointer, toOwner, opacity);
        }

        // OD-0076 / ED-254 (SPEC 6.5): in a window that turns over, a piece's veil colour (its darkness, 0 light to 1 dark) and its resolved tint ease to the new
        // appearance on AppearanceTurn's curve and clock, as every other colour does, and are what the click frame already drew until then (ED-320).
        // The piece holds what it is drawn with; a change of its effect's own tint (not of what the appearance resolves it to) is made at once, as is a change in a
        // tree that does not turn over. `drawing` is the attachment whose material draws the piece: it is synchronised on each frame of the turn.
        private bool appearanceKnown;
        private double darkShown, darkTarget;
        private Color tintShown, tintTarget;
        private SwiftColor? tintSource;
        private double? tintOpacity;
        private Attachment? drawing;
        private Action stopTurn = static () => { };

        private void StopTurn()
        {
            stopTurn();
            stopTurn = static () => { };
        }

        private (double Darkness, Color Tint) Appearance(Control control, Attachment drawing, GlassEffect effect, Color resolved)
        {
            this.drawing = drawing;
            var darkTo = PlainControls.IsDark(control.ActualThemeVariant) ? 1.0 : 0.0;
            var sameTint = appearanceKnown && ReferenceEquals(effect.TintColor, tintSource) && effect.TintOpacity == tintOpacity;
            tintSource = effect.TintColor;
            tintOpacity = effect.TintOpacity;
            if (!appearanceKnown || (darkTo == darkTarget && resolved == tintTarget))
            {
                if (!appearanceKnown) { darkShown = darkTo; tintShown = resolved; }
                (appearanceKnown, darkTarget, tintTarget) = (true, darkTo, resolved);
                return (darkShown, tintShown);
            }
            StopTurn();
            var (darkFrom, tintFrom) = (darkShown, tintShown);
            (darkTarget, tintTarget) = (darkTo, resolved);
            if (!sameTint) tintFrom = tintShown = resolved; // the effect itself changed: not the appearance
            if (darkFrom == darkTo && tintFrom == resolved)
            {
                (darkShown, tintShown) = (darkTo, resolved);
                return (darkShown, tintShown);
            }
            var endTurn = AppearanceTurn.Eases(control) ? BeginTurn(control) : static () => { };
            var stopRun = AppearanceTurn.Run(control, progress =>
            {
                (darkShown, tintShown) = progress >= 1 ? (darkTo, resolved) : (darkFrom + (darkTo - darkFrom) * progress, tintFrom == resolved ? resolved : progress <= 0 ? tintFrom : SwiftColor.PremultipliedMix(tintFrom, resolved, progress));
                this.drawing?.RequestSynchronize();
                if (progress >= 1) endTurn();
            });
            stopTurn = () => { stopRun(); endTurn(); };
            return (darkShown, tintShown);
        }

        private void BindInput(bool interactive)
        {
            var next = interactive ? owner.IsHitTestVisible ? owner : owner.GetVisualParent() as Control : null;
            if (next == input) return;
            UnbindInput();
            input = next;
            if (input is null) return;
            input.PointerEntered += Enter;
            input.PointerMoved += Move;
            input.PointerExited += Exit;
            input.AddHandler(InputElement.PointerPressedEvent, Press, RoutingStrategies.Tunnel, handledEventsToo: true);
            input.AddHandler(InputElement.PointerReleasedEvent, Release, RoutingStrategies.Tunnel, handledEventsToo: true);
            input.PointerCaptureLost += CaptureLost;
        }
        private void Enter(object? sender, PointerEventArgs e) { hovered = true; pointer = e.GetPosition(owner); Queue(); }
        private void Move(object? sender, PointerEventArgs e) { pointer = e.GetPosition(owner); Queue(); }
        private void Exit(object? sender, PointerEventArgs e) { hovered = false; pressed = false; Queue(); }
        private void Press(object? sender, PointerPressedEventArgs e) { pressed = e.GetCurrentPoint(input).Properties.IsLeftButtonPressed; pointer = e.GetPosition(owner); Queue(); }
        private void Release(object? sender, PointerReleasedEventArgs e) { pressed = false; Queue(); }
        private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) { pressed = false; Queue(); }
        private void UnbindInput()
        {
            if (input is null) return;
            input.PointerEntered -= Enter;
            input.PointerMoved -= Move;
            input.PointerExited -= Exit;
            input.RemoveHandler(InputElement.PointerPressedEvent, Press);
            input.RemoveHandler(InputElement.PointerReleasedEvent, Release);
            input.PointerCaptureLost -= CaptureLost;
            input = null;
            hovered = pressed = false;
        }
        private void RemoveMaterial()
        {
            material?.SendHandlerMessage(Released.Message);
            if (material is not null) parent?.Children.Remove(material);
            Unhook(this);
            Unwatch();
            parent = null;
            material = null;
            previous = null;
        }
    }

    // Avalonia 12.1.2 (CompositingRenderer.UpdateCore, Visual.SynchronizeCompositionChildVisuals) re-checks the composition children of every visual that is
    // dirty at a render update, even for a change that is only to how it draws (a new Background, a hover, a colour ease): if their count differs from the
    // visual children's it clears them and adds back only the controls' own visuals. A parent that holds a material always has one more, so each such
    // update dropped every material in it, and nothing put it back until the owner's next layout pass. The point is not open to a subclass (an internal
    // virtual of Visual; the one extension, ElementComposition.SetElementChildVisual, adds a child after all the others, above the owner and its content),
    // so each window's renderer is given a step after its update, in the same composition update and before the frame is committed, that puts every material
    // of the window back directly before its owner. The pinned package version is what makes the private member safe to name (Directory.Packages.props);
    // if it is not there the materials keep to the layout pass as before.
    private sealed class WindowHook
    {
        public readonly HashSet<Attachment> Materials = [];
        private readonly List<Attachment> pass = [];
        public void Repair()
        {
            if (Materials.Count == 0) return;
            pass.AddRange(Materials);
            foreach (var material in pass) material.Reinsert();
            pass.Clear();
        }
    }
    private static readonly ConditionalWeakTable<TopLevel, WindowHook> windows = new();
    private static readonly ConditionalWeakTable<Attachment, TopLevel> hooked = new();

    private static void Hook(Attachment attachment)
    {
        if (hooked.TryGetValue(attachment, out _) || TopLevel.GetTopLevel(attachment.Owner) is not { } top) return;
        if (!windows.TryGetValue(top, out var hook))
        {
            hook = new WindowHook();
            windows.Add(top, hook);
            Install(top, hook);
        }
        hook.Materials.Add(attachment);
        hooked.Add(attachment, top);
    }

    private static void Unhook(Attachment attachment)
    {
        if (!hooked.TryGetValue(attachment, out var top)) return;
        hooked.Remove(attachment);
        if (windows.TryGetValue(top, out var hook)) hook.Materials.Remove(attachment);
    }

    private static void Install(TopLevel top, WindowHook hook)
    {
        try
        {
            var renderer = typeof(TopLevel).GetProperty("Renderer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(top);
            var update = renderer?.GetType().GetField("_update", BindingFlags.Instance | BindingFlags.NonPublic);
            if (renderer is null || update?.GetValue(renderer) is not Action original) return;
            update.SetValue(renderer, new Action(() => { original(); hook.Repair(); }));
            // An update already queued holds the old delegate: one more step, after it, in the next composition update.
            if (ElementComposition.GetElementVisual(top)?.Compositor is { } compositor) compositor.RequestCompositionUpdate(hook.Repair);
        }
        catch (Exception e) when (e is ArgumentException or MemberAccessException or TargetInvocationException or InvalidCastException or FieldAccessException) { }
    }

    private sealed record Region(Size Size, GlassShape Shape, CornerRadius Corners, Color Tint, bool DrawnTint,
        double Darkness, bool Hovered, bool Pressed, Point Pointer, Matrix Transform, double Opacity);
    private sealed record State(Region[] Regions, double Spacing);

    private sealed class Handler : CompositionCustomVisualHandler
    {
        private State? state;
        private readonly Backdrop backdrop = new();
        private Reach? reach;
        public override void OnMessage(object message)
        {
            if (message is Released) { backdrop.Release(); reach?.Remove(this); reach = null; return; }
            if (message is not State next) return;
            state = next;
            Invalidate(); // Event-driven, no animation-frame registration or idle timer.
        }
        public override Rect GetRenderBounds() => state is { Regions.Length: > 0 } s ? UnionBounds(s).Inflate(s.Spacing / 2 + 1 + SampledMargin) : default;
        public override void OnRender(ImmediateDrawingContext context)
        {
            if (state is not { Regions.Length: > 0 } s) return;
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            DrawsForTests++;
            Backdrop.DisposeReleased();
            var target = Frame.Target(this);
            var next = target is null ? null : Frame.ReachOf(target);
            if (next != reach) { reach?.Remove(this); reach = next; }
            var cost = FrameCostForTests;
            var watch = cost is null ? null : Wait(lease);
            if (Draw(lease, s, backdrop, target is null ? null : Frame.Dirty(target)) is { } drawn) reach?.Set(this, drawn);
            if (cost is not null && watch is not null) { Wait(lease); cost(s.Regions[0].Size, watch.Elapsed); }
        }

        // The GPU's queued work done (flushed and waited on), then a clock started: so the time to the next wait is the material's alone.
        private static Stopwatch Wait(ISkiaSharpApiLease lease)
        {
            lease.GrContext?.Flush(true, true);
            return Stopwatch.StartNew();
        }
    }

    /// <summary>The one-time log of the same-frame reach switching off (ResizeStretchLog writes once, ED-101's pattern). The process default writes to
    /// System.Diagnostics.Trace; tests replace it.</summary>
    internal static Ambient<ResizeStretchLog> ReachLog { get; } = new(() => new ResizeStretchLog(message => Trace.WriteLine(message)));

    // Test-only readback (InternalsVisibleTo): how many times any material has drawn, on the render thread.
    internal static int DrawsForTests;

    // Test-only (InternalsVisibleTo), ED-NEW-glass-g1's frame cost: while set, each draw waits for the GPU's work before and after itself and reports its
    // first piece's size and how long the draw took, the GPU's part included. Called on the render thread.
    internal static volatile Action<Size, TimeSpan>? FrameCostForTests;

    // Sent before a material leaves its parent: its handler gives up the backdrop it keeps (disposed at the next render, where the GPU context is current).
    private sealed class Released { public static readonly Released Message = new(); }

    // How far past its shape the material reads the pixels under it: the blur's 3 sigma (36), the refraction's largest displacement (Optics.MaxBend plus
    // Optics.MaxRing, the red channel's at the edge: 50; ED-NEW-glass-g1, it was 28.6) and the antialiased edge (Draw).
    // It is also the material's render bounds: whenever the material appears, moves or changes, Avalonia redraws all it reads, so the backdrop it keeps is
    // taken whole there (Backdrop; GlassMaterialPartialRedrawTests, the label beside a piece that appears).
    private const double SampledMargin = 3 * 12 + Optics.MaxBend + Optics.MaxRing + 2;

    private static Rect UnionBounds(State s)
    {
        var bounds = new Rect(s.Regions[0].Size).TransformToAABB(s.Regions[0].Transform);
        foreach (var r in s.Regions.Skip(1)) bounds = bounds.Union(new Rect(r.Size).TransformToAABB(r.Transform));
        return bounds;
    }

    // The shape's signed distance (negative inside). A `.continuous` rounded rectangle is the circular corner's distance plus the
    // offset of Apple's curve from the circle (ContinuousCorner.ShaderSource, ED-181): the zero level lies on the path
    // SwiftShapes draws (to 2e-5 r) and the field is the plain circle's wherever the curve is not drawn (a capsule, `.circular`, and the composer
    // pill, which the video measures circular in ED-275 (2)'s band); a radius between ContinuousCorner.LargestRadius and half the shorter side
    // is Apple's corner at LargestRadius (OD-0102). The C# mirror is ContinuousCorner.Distance.
    internal static readonly string ShaderHeader = string.Concat(
        "uniform shader backdrop;\nuniform float merge;\n",
        ContinuousCorner.ShaderSource(),
        """

        float shapeDistance(float2 xy, float2 size, float4 corners, float kind, float continuous) {
            float2 c = size * 0.5;
            float2 p = xy - c;
            if (kind > 1.5) return length(p) - min(c.x, c.y);
            float radius = kind > 0.5 ? min(c.x, c.y) :
                (p.y < 0.0 ? (p.x < 0.0 ? corners.x : corners.y) : (p.x < 0.0 ? corners.w : corners.z));
            radius = min(radius, min(c.x, c.y));
            // OD-0102: a continuous corner that does not fit at its radius yet is under half the shorter side (ED-275 (2)'s band) is Apple's corner at the
            // largest radius that fits (ContinuousCorner.LargestRadius), unless the shape is measured circular there (continuous == 2: the composer pill).
            float largest = min(c.x, c.y) / EXTENT;
            if (continuous > 0.5 && continuous < 1.5 && kind < 0.5 && radius > largest && radius < min(c.x, c.y) - 0.000001) radius = largest;
            float2 q = abs(p) - c + radius;
            float2 qc = max(q, 0.0);
            float cd = length(qc) + min(max(q.x, q.y), 0.0) - radius;
            return continuous > 0.5 ? continuousCorner(cd, c, p, radius) : cd;
        }
        float smoothUnion(float a, float b) {
            if (merge <= 0.0) return min(a, b);
            float h = max(merge - abs(a - b), 0.0) / merge;
            return min(a, b) - h * h * merge * 0.25;
        }
        """.Replace("EXTENT", ContinuousCorner.Extent.ToString("R", CultureInfo.InvariantCulture), StringComparison.Ordinal));
    private static readonly ConcurrentDictionary<int, Lazy<SKRuntimeEffect>> effects = new();

    private static SKRuntimeEffect Effect(int count) => effects.GetOrAdd(count, static n => new Lazy<SKRuntimeEffect>(() =>
    {
        var source = new StringBuilder(ShaderHeader).Append("\nuniform float stage;\nuniform float interior;\n");
        for (var i = 0; i < n; i++) source.Append(CultureInfo.InvariantCulture, $$"""

            uniform float4 transform{{i}};
            uniform float2 translate{{i}};
            uniform float2 size{{i}};
            uniform float4 corners{{i}};
            uniform float4 tint{{i}};
            uniform float4 film{{i}};
            uniform float4 optics{{i}};
            uniform float4 flags{{i}};
            uniform float4 pointer{{i}};
            float2 local{{i}}(float2 xy) {
                return float2(xy.x * transform{{i}}.x + xy.y * transform{{i}}.z,
                              xy.x * transform{{i}}.y + xy.y * transform{{i}}.w) + translate{{i}};
            }
            float distance{{i}}(float2 xy) {
                return shapeDistance(local{{i}}(xy), size{{i}}, corners{{i}}, flags{{i}}.x, flags{{i}}.y) * flags{{i}}.z;
            }
            """);
        source.Append("\nfloat sceneDistance(float2 xy) { float d = distance0(xy);\n");
        for (var i = 1; i < n; i++) source.Append(CultureInfo.InvariantCulture, $"d = smoothUnion(d, distance{i}(xy));\n");
        source.Append("return d; }\n");
        source.Append(BodySource);
        source.Append("half4 main(float2 xy) { float4 tint = tint0; float4 film = film0; float4 ptr = pointer0; float2 at = local0(xy); float4 flags = flags0; float2 extent = size0; float4 optics = optics0;\n");
        // ED-320 16: the inside of a lone rounded rectangle (Draw's Inside) is drawn with `interior` set: what the shader computes past Optics.Deep,
        // without the shape's distance. It is the same program and the same arithmetic as the whole shader's deep branch below, so the two are
        // byte-identical (GlassMaterialReachReviewTests). A container's union never has one (Inside is null): its pointer is the nearest piece's.
        if (n == 1) source.Append("""
            float2 delta = at - ptr.xy;
            float highlight = exp(-dot(delta, delta) / (2.0 * 48.0 * 48.0)) * ptr.z;
            if (interior > 0.5) return inside(backdrop.eval(xy), at, extent, film, tint, ptr.w < 0.5, highlight, flags.w);
            float raw = distance0(xy);

            """);
        else source.Append("float raw = distance0(xy);\n");
        for (var i = 1; i < n; i++) source.Append(CultureInfo.InvariantCulture, $"if (distance{i}(xy) < raw) {{ raw = distance{i}(xy); tint = tint{i}; film = film{i}; ptr = pointer{i}; at = local{i}(xy); flags = flags{i}; extent = size{i}; optics = optics{i}; }}\n");
        if (n > 1) source.Append("float2 delta = at - ptr.xy;\nfloat highlight = exp(-dot(delta, delta) / (2.0 * 48.0 * 48.0)) * ptr.z;\n");
        // With one shape the scene's distance is the shape's own (no union): it is not evaluated twice.
        source.Append(n == 1 ? "float d = -raw;\n" : "float d = -sceneDistance(xy);\n");
        source.Append("""
            // Past the contact line outside the shape the material leaves the pixel as it is (both stages add nothing).
            if (d <= -1.25) return half4(0.0);
            float coverage = clamp(d + 0.5, 0.0, 1.0);
            // ED-320 16 / ED-NEW-glass-g1: past the bend's support, the ring's, the inner light's and the rim's (Optics.Deep in C#) the
            // lens bends nothing and lights nothing: the inside of a shape reads one texel and skips the normal's four distances.
            float deep = max(max(optics.z + LG_BENDSUPPORT * optics.y, LG_GLOWREACH), LG_RINGREACH);
            float2 n = float2(0.0, 0.0);
            if (d < deep) {
                // P1 finite-difference inward normal follows curved edges and the merged neck.
                float2 normal = float2(-sceneDistance(xy + float2(1.0, 0.0)) + sceneDistance(xy - float2(1.0, 0.0)),
                                      -sceneDistance(xy + float2(0.0, 1.0)) + sceneDistance(xy - float2(0.0, 1.0)));
                n = normalize(normal + float2(0.000001));
            }
            // The contact line just outside the edge, dark where the edge faces sideways (the Mac frames' 1 px dark line at a capsule's
            // ends), and dashersw/liquid-glass-js's soft shadow in miniature.
            float shade = d < 0.25 ? clamp(1.0 - abs(d + 0.5) / 0.75, 0.0, 1.0) * (LG_SHADEBASE + LG_SHADESIDE * n.x * n.x) : 0.0;
            // What the pixel becomes is mix(under, value, amount): stage 0 (BlendMode.DstOut) takes `amount` of the pixel under away,
            // stage 1 (BlendMode.Plus) adds value * amount. The glass is translucent: its value keeps the alpha of what lies under it.
            float amount = flags.w * (1.0 - (1.0 - coverage) * (1.0 - shade));
            if (stage < 0.5) return half4(0.0, 0.0, 0.0, half(amount));
            half4 outside = half4(0.0, 0.0, 0.0, half(flags.w * (1.0 - coverage) * shade));
            if (coverage <= 0.0) return outside;
            // Avoid double tinting existing overlays; tint the merged neck outside those overlays.
            bool tinted = ptr.w < 0.5 || raw > 0.0;
            if (d >= deep) return inside(backdrop.eval(xy), at, extent, film, tint, tinted, highlight, flags.w);
            // rizzytoday/liquid-glass's displacement map (buildDisplacementMap): a full bend at the edge that falls off as the
            // complement of the blurred neutral centre (a Gaussian edge, here its logistic form), with the per-channel scales of its
            // chromatic aberration (scale -180 + 0 / 10 / 20: red 1, green 0.944, blue 0.889); plus dashersw/liquid-glass-js's rim
            // ring (rimIntensity * exp(-rimDistance * d), rimDistance 0.8). Sampled outward along the inward normal.
            float dd = max(d, 0.0);
            float lens = (1.0 + exp(-LG_LOGISTIC * optics.z / optics.y)) / (1.0 + exp(LG_LOGISTIC * (dd - optics.z) / optics.y));
            lens = max(lens - LG_BENDFLOOR, 0.0) / (1.0 - LG_BENDFLOOR);
            float disp = optics.x * lens + optics.w * max(exp(-0.8 * dd) - LG_RINGFLOOR, 0.0) / (1.0 - LG_RINGFLOOR);
            half4 r = backdrop.eval(xy - n * disp);
            half4 g = backdrop.eval(xy - n * (disp * LG_GREEN));
            half4 b = backdrop.eval(xy - n * (disp * LG_BLUE));
            // Each read is lifted on its own, so the fringe is the bend's (not amplified by the saturation); deep inside the three are one.
            half4 col = filmed(half3(lift(r, film.w).r, lift(g, film.w).g, lift(b, film.w).b), g.a, at, extent, film);
            if (tinted) col = over(col, half3(tint.rgb), half(tint.a));
            // The specular rim: a bright line on the edge's first point, all around and strongest where the edge faces up or down,
            // the top brightest (the Mac frames: +33 to +41 levels on the composer pill and the title capsules); the light inner edge
            // under it.
            float rim = clamp(1.5 - d, 0.0, 1.0);
            float facing = n.y * n.y;
            float spec = rim * (LG_RIMBASE + (1.0 - LG_RIMBASE) * facing) * (n.y > 0.0 ? LG_RIMTOP : LG_RIMBOTTOM);
            float glow = d < LG_GLOWREACH ? LG_GLOW * pow(clamp(1.0 - (d - 1.0) / (LG_GLOWREACH - 1.0), 0.0, 1.0), 3.0) * (0.3 + 0.7 * facing) * (1.0 - rim) : 0.0;
            col = over(col, half3(1.0), half(clamp(spec + glow + highlight, 0.0, 1.0)));
            return col * half(coverage * flags.w) + outside;
            }
            """);
        return SKRuntimeEffect.CreateShader(Constants(source.ToString()), out var error)
            ?? throw new InvalidOperationException("Glass shader did not compile: " + error);
    })).Value;

    // The glass body, the same at the edge and inside. `under` is the blurred backdrop, premultiplied,
    // with whatever alpha the pixels under the glass have (the Acrylic window's transparent or scrim pixels show through: nothing is
    // filled in under them). Its colour takes a saturation lift (Apple's vibrancy; rizzytoday/liquid-glass's `saturate()`), then
    // dashersw/liquid-glass-js's tint gradient over it: a grey from film.x at the top to film.y at the bottom, at film.z
    // (Container.setupShader: topTint 1.0, bottomTint 0.7, tintOpacity 0.2), as a translucent layer: over a transparent pixel it is
    // that light film alone, never black.
    private const string BodySource = """
        half4 unpremultiply(half4 c) { return c.a > 0.002 ? half4(c.rgb / c.a, c.a) : half4(0.0); }
        half4 over(half4 below, half3 colour, half alpha) { return half4(colour * alpha + below.rgb * (1.0 - alpha), alpha + below.a * (1.0 - alpha)); }
        half3 lift(half4 under, float saturation) {
            half4 c = unpremultiply(under);
            half luma = dot(c.rgb, half3(0.2126, 0.7152, 0.0722));
            return clamp(luma + (c.rgb - luma) * half(saturation), 0.0, 1.0);
        }
        half4 filmed(half3 lifted, half alpha, float2 at, float2 extent, float4 film) {
            half grey = half(mix(film.x, film.y, clamp(at.y / max(extent.y, 1.0), 0.0, 1.0)));
            return over(half4(lifted * alpha, alpha), half3(grey), half(film.z));
        }
        half4 body(half4 under, float2 at, float2 extent, float4 film) { return filmed(lift(under, film.w), under.a, at, extent, film); }
        // The inside of a shape, past Optics.Deep: the body, the tint, the pointer's highlight (the deep branch and the `interior` drawing).
        half4 inside(half4 texel, float2 at, float2 extent, float4 film, float4 tint, bool tinted, float highlight, float amount) {
            half4 col = body(texel, at, extent, film);
            if (tinted) col = over(col, half3(tint.rgb), half(tint.a));
            return over(col, half3(1.0), half(clamp(highlight, 0.0, 1.0))) * half(amount);
        }

        """;

    // The optical constants, written into the shaders' source once (they are the same for every piece).
    private static string Constants(string source)
    {
        string F(double value) => value.ToString("0.0###########", CultureInfo.InvariantCulture);
        return source.Replace("LG_BENDSUPPORT", F(Optics.BendSupport), StringComparison.Ordinal)
            .Replace("LG_BENDFLOOR", F(Optics.BendFloor), StringComparison.Ordinal)
            .Replace("LG_RINGFLOOR", F(Optics.RingFloor), StringComparison.Ordinal)
            .Replace("LG_RINGREACH", F(Optics.RingReach), StringComparison.Ordinal)
            .Replace("LG_GLOWREACH", F(Optics.GlowReach), StringComparison.Ordinal)
            .Replace("LG_LOGISTIC", F(Optics.Logistic), StringComparison.Ordinal)
            .Replace("LG_GREEN", F(Optics.Green), StringComparison.Ordinal)
            .Replace("LG_BLUE", F(Optics.Blue), StringComparison.Ordinal)
            .Replace("LG_SHADEBASE", F(Optics.ShadeBase), StringComparison.Ordinal)
            .Replace("LG_SHADESIDE", F(Optics.ShadeSide), StringComparison.Ordinal)
            .Replace("LG_RIMBASE", F(Optics.RimBase), StringComparison.Ordinal)
            .Replace("LG_RIMTOP", F(Optics.RimTop), StringComparison.Ordinal)
            .Replace("LG_RIMBOTTOM", F(Optics.RimBottom), StringComparison.Ordinal)
            .Replace("LG_GLOW", F(Optics.Glow), StringComparison.Ordinal);
    }

    // The pixels under a material as they are when it draws: what a whole drawing of the window has drawn before it (ED-320 11). The surface
    // holds them only inside the region Avalonia redraws in this frame; outside it the surface is the last finished frame, the glass itself and what lies
    // over it, so a redraw of a control over the glass blurred the glass's own output into its frost (8 to 11 levels, GlassMaterialPartialRedrawReviewTests).
    // Outside that region nothing under the material has changed (whatever changes adds its bounds to the region), so the material keeps what it took the
    // last time and takes new pixels only inside the region. With no known region (another dirty-rect tracker, another surface) it takes them all, as before.
    // ED-320 16 (frame cost): the kept pixels live in one surface the material keeps, and only the region's pixels are copied out of the
    // window's surface (a whole snapshot of it, written to right after, made Skia copy the whole window every frame); the blur's own surface is kept too.
    // Both are made again when their size, the window's surface or the GPU context changes, and given up when the material leaves (Released).
    private sealed class Backdrop
    {
        private static readonly ConcurrentQueue<IDisposable> released = new();
        private SKSurface? kept, blurred;
        private SKRectI area;
        private IntPtr surface, context, blurredContext; // blurredContext: the GPU context the blur's own surface was made on
        private SKSizeI size, keptSize, blurredSize;
        private bool valid;

        public static void DisposeReleased()
        {
            while (released.TryDequeue(out var old)) old.Dispose();
        }

        public void Release()
        {
            if (kept is not null) released.Enqueue(kept);
            if (blurred is not null) released.Enqueue(blurred);
            (kept, blurred, valid) = (null, null, false);
        }

        private static SKSurface? Create(ISkiaSharpApiLease lease, int width, int height)
        {
            var info = new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Rgba8888, SKAlphaType.Premul);
            // Keep native rendering on the lease's GPU; the software path also runs in headless tests (P1).
            return lease.GrContext is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
        }

        // The surface the blur is drawn into, the crop's size, kept from frame to frame (what it held is drawn over where it is read).
        public SKSurface? Blurred(ISkiaSharpApiLease lease, SKRectI crop)
        {
            var wanted = new SKSizeI(Math.Max(1, crop.Width), Math.Max(1, crop.Height));
            var gpu = lease.GrContext?.Handle ?? IntPtr.Zero;
            if (blurred is not null && blurredSize == wanted && gpu == blurredContext) return blurred;
            if (blurred is not null) released.Enqueue(blurred);
            (blurredSize, blurredContext) = (wanted, gpu);
            return blurred = Create(lease, wanted.Width, wanted.Height);
        }

        // Brings the kept pixels up to this frame and returns the surface that holds them and where it lies in the window's surface.
        public (SKSurface Surface, SKRectI Area)? Take(ISkiaSharpApiLease lease, SKRectI crop, SKRectI? dirty)
        {
            if (lease.SkSurface is not { } source) return null;
            var handle = source.Handle;
            var gpu = lease.GrContext?.Handle ?? IntPtr.Zero;
            // Measured every frame: a new window surface (a resize, another scale) can be given a freed one's address. The snapshot is released at once,
            // before anything is drawn, so Skia copies nothing.
            SKSizeI whole;
            using (var probe = source.Snapshot()) whole = new SKSizeI(probe.Width, probe.Height);
            var at = SKRectI.Intersect(crop, new SKRectI(0, 0, whole.Width, whole.Height));
            if (at.IsEmpty) return null;
            var same = valid && dirty is not null && handle == surface && gpu == context && size == whole && kept is not null;
            using var copy = new SKPaint { BlendMode = SKBlendMode.Src };
            if (same && at == area)
            {
                var fresh = SKRectI.Intersect(dirty!.Value, at);
                if (!fresh.IsEmpty)
                {
                    using var piece = source.Snapshot(fresh);
                    if (piece is not null) kept!.Canvas.DrawImage(piece, fresh.Left - at.Left, fresh.Top - at.Top, copy);
                }
            }
            else
            {
                using var under = source.Snapshot(at);
                using var old = same ? kept!.Snapshot() : null; // the piece moved or grew: what it kept still holds where it was not redrawn
                var oldArea = area;
                if (kept is null || keptSize != new SKSizeI(at.Width, at.Height) || gpu != context)
                {
                    if (kept is not null) released.Enqueue(kept);
                    kept = Create(lease, at.Width, at.Height);
                    keptSize = new SKSizeI(at.Width, at.Height);
                }
                if (kept is null || under is null) { valid = false; return null; }
                var canvas = kept.Canvas;
                canvas.DrawImage(under, 0, 0, copy);
                if (old is not null)
                {
                    var keptAt = new SKRect(oldArea.Left - at.Left, oldArea.Top - at.Top, oldArea.Right - at.Left, oldArea.Bottom - at.Top);
                    canvas.Save();
                    canvas.ClipRect(keptAt);
                    var fresh = SKRectI.Intersect(dirty!.Value, at);
                    if (!fresh.IsEmpty) canvas.ClipRect(new SKRect(fresh.Left - at.Left, fresh.Top - at.Top, fresh.Right - at.Left, fresh.Bottom - at.Top), SKClipOperation.Difference);
                    canvas.DrawImage(old, keptAt.Left, keptAt.Top, copy);
                    canvas.Restore();
                }
            }
            (area, surface, context, size) = (at, handle, gpu, whole);
            valid = dirty is not null; // nothing to keep without a known region: the next frame takes the surface whole again
            return (kept!, at);
        }
    }

    // The frame being drawn, read from the handler's host in Avalonia 12.1.2 (`_host`, ServerCompositionVisual.Root, the ServerCompositionTarget): the region it
    // redraws, in the window surface's pixels (DirtyRects, a SingleDirtyRectTracker unless region clipping is turned on: it is not, no CompositionOptions are set;
    // its CombinedRect is the frame's clip, which ServerCompositionTarget.RenderRootToContextWithClip clears and draws everything that meets), and the
    // window's Reach. Private and internal members of the pinned package (Directory.Packages.props); null when one is not found or the tracker is another kind:
    // the material then takes the surface whole and adds no reach.
    private static class Frame
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo? host = typeof(CompositionCustomVisualHandler).GetField("_host", Members);
        private static readonly Type? events = typeof(CompositionCustomVisualHandler).Assembly.GetType("Avalonia.Rendering.Composition.ICompositionTargetDebugEvents");
        private static PropertyInfo? root, rects, combined, debugEvents;
        private static MethodInfo? addRect;
        private static readonly ConditionalWeakTable<object, Reach> reaches = new();

        public static object? Target(CompositionCustomVisualHandler handler)
        {
            try
            {
                if (host?.GetValue(handler) is not { } visual) return null;
                root ??= visual.GetType().GetProperty("Root", Members);
                return root?.GetValue(visual);
            }
            catch (Exception e) when (Expected(e)) { return null; }
        }

        private static object? Tracker(object target)
        {
            rects ??= target.GetType().GetProperty("DirtyRects", Members);
            return rects?.GetValue(target) is { } tracker && tracker.GetType().Name == "SingleDirtyRectTracker" ? tracker : null;
        }

        public static SKRectI? Dirty(object target)
        {
            try
            {
                if (Tracker(target) is not { } tracker) return null;
                combined ??= tracker.GetType().GetProperty("CombinedRect", Members);
                return combined?.GetValue(tracker) is { } rect ? ToRect(rect) : null;
            }
            catch (Exception e) when (Expected(e)) { return null; }
        }

        // The window's Reach, made and set as the target's debug events the first time (only when nothing else has set them).
        public static Reach? ReachOf(object target)
        {
            if (reaches.TryGetValue(target, out var known)) return known;
            if (refused) return null; // a failure to set the reach up is not tried again on every frame
            try
            {
                if (events is null || Tracker(target) is not { } tracker) return null;
                debugEvents ??= target.GetType().GetProperty("DebugEvents", Members);
                addRect ??= tracker.GetType().GetMethod("AddRect", Members);
                if (debugEvents is null || addRect is null || debugEvents.GetValue(target) is not null) return null;
                var reach = (Reach)DispatchProxy.Create(events, typeof(Reach));
                reach.Bind(tracker, Adder(tracker.GetType(), addRect));
                debugEvents.SetValue(target, reach);
                reaches.Add(target, reach);
                return reach;
            }
            catch (Exception e) // any failure, the adder's compilation included: no reach, the frames are drawn as in item 11
            {
                refused = true;
                Off("the reach could not be set up: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        private static bool refused;

        public static void Off(string reason) =>
            ReachLog.Current.Once("GlassMaterial: the same-frame reach is off (" + reason + "); a change under the glass redraws only its own rect, its frost around it a frame late.");

        // Avalonia's LtrbRect (a struct with public fields and internal constructors) read from its box, and the tracker's AddRect called with one,
        // through lambdas compiled once (a compiled lambda may reach internal members): nothing is boxed and no argument array is made per rect.
        private static Func<object, SKRectI>? reader;
        private static Type? readerType;

        public static SKRectI ToRect(object rect)
        {
            if (readerType != rect.GetType()) (reader, readerType) = (Reader(rect.GetType()), rect.GetType());
            return reader!(rect);
        }

        private static Func<object, SKRectI> Reader(Type ltrb)
        {
            var boxed = Expression.Parameter(typeof(object));
            var value = Expression.Unbox(boxed, ltrb);
            var round = typeof(Math).GetMethod(nameof(Math.Round), [typeof(double)])!;
            Expression Side(string name) => Expression.Convert(Expression.Call(round, Expression.Field(value, name)), typeof(int));
            var make = typeof(SKRectI).GetConstructor([typeof(int), typeof(int), typeof(int), typeof(int)])!;
            return Expression.Lambda<Func<object, SKRectI>>(Expression.New(make, Side("Left"), Side("Top"), Side("Right"), Side("Bottom")), boxed).Compile();
        }

        public static Action<object, SKRectI> Adder(Type tracker, MethodInfo addRect)
        {
            var ltrb = addRect.GetParameters()[0].ParameterType;
            var target = Expression.Parameter(typeof(object));
            var rect = Expression.Parameter(typeof(SKRectI));
            MemberBinding Side(string name) => Expression.Bind(ltrb.GetField(name)!, Expression.Convert(Expression.Property(rect, name), typeof(double)));
            var value = Expression.MemberInit(Expression.New(ltrb), Side("Left"), Side("Top"), Side("Right"), Side("Bottom"));
            return Expression.Lambda<Action<object, SKRectI>>(Expression.Call(Expression.Convert(target, tracker), addRect, value), target, rect).Compile();
        }

        private static bool Expected(Exception e) =>
            e is ArgumentException or MemberAccessException or TargetInvocationException or InvalidCastException or FieldAccessException or NullReferenceException;
    }

    // ED-320 15. A change under a material spreads, through its blur and refraction, to the glass up to SampledMargin around it, but Avalonia
    // redraws only the change's own dirty rect and clips the frame to it: the glass just outside kept the last frame's frost (a seam, 86 to 90 levels,
    // GlassMaterialKeptBackdropReviewTests). Avalonia passes every rect a frame collects to the target's debug events (DebugEventsDirtyRectCollectorProxy,
    // while they are set), during the update and before the frame's region is final; the window's Reach is those events, and for each rect that meets a
    // material's reach it adds that reach, inside the material's own output, to the same frame's region. What it adds goes straight to the tracker, not
    // through the events, so it is never answered again: no frame is asked for, and a scene that does not change draws nothing (the settle test). A rect
    // that meets several materials is grown for each, and what one adds is grown for the others (a material over another's output), each once a rect.
    // Runs on the render thread, as the frame's update does. Public only because DispatchProxy derives from it.
    public class Reach : DispatchProxy
    {
        private object? tracker;
        private Action<object, SKRectI>? addRect;
        private readonly Dictionary<object, Spread> materials = new();
        private readonly List<SKRectI> queue = new();
        private readonly HashSet<object> grown = new();
        private readonly List<KeyValuePair<object, Spread>> pass = new();
        private int rendered, visited;

        internal void Bind(object dirtyRects, Action<object, SKRectI> add) => (tracker, addRect) = (dirtyRects, add);
        internal void Set(object material, Spread spread) => materials[material] = spread;
        internal void Remove(object material) => materials.Remove(material);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "RectInvalidated" when args is [{ } rect]:
                    // Anything thrown here would go through the proxy into Avalonia's update on the render thread: a refused call, a rect the reader cannot be
                    // compiled for. The reach gives up for good, says why once, and the frame is drawn as before.
                    try { Grow(rect); }
                    catch (Exception e)
                    {
                        tracker = null;
                        Frame.Off("a rect could not be grown: " + e.GetType().Name + ": " + e.Message);
                    }
                    return null;
                case "get_RenderedVisuals": return rendered;
                case "set_RenderedVisuals" when args is [int count]: rendered = count; return null;
                case "get_VisitedVisuals": return visited;
                case "set_VisitedVisuals" when args is [int count]: visited = count; return null;
                default: return null;
            }
        }

        private void Grow(object rect)
        {
            if (materials.Count == 0 || tracker is null || addRect is null) return;
            queue.Clear();
            grown.Clear();
            queue.Add(Frame.ToRect(rect));
            pass.AddRange(materials);
            for (var i = 0; i < queue.Count; i++)
            {
                var changed = queue[i];
                foreach (var (material, spread) in pass)
                {
                    if (grown.Contains(material)) continue;
                    var reach = spread.Of(changed);
                    var add = SKRectI.Intersect(reach, spread.Output);
                    if (add.IsEmpty || changed.Contains(add)) continue;
                    grown.Add(material);
                    addRect(tracker, add);
                    queue.Add(add);
                }
            }
            pass.Clear();
        }
    }

    // ED-NEW-glass-g1: the film over the glass body (BodySource), between the light appearance's and the dark one's: darkness 0 is light, 1 is dark.
    // x and y are the grey at the top and at the bottom, z its opacity, w the saturation of the colour under it. The dark ends are
    // dashersw/liquid-glass-js's tint gradient (topTint 1.0, bottomTint 0.7) at 0.17 rather than its 0.2: over the Mac frames' dark backdrop that
    // gives what they show (the composer pill over about 57: 91 under the top rim, 84 at the middle, 77 at the bottom, against 93, 85 and 76; the
    // disabled small glass button over (52,59,60): (80,86,87) against (75,84,85)); at 0.2 the middle was 89. The light ends are a whiter,
    // stronger frost (no light-appearance frame was measured). The saturation is the lift of the Mac glass's colour (outside G-B 10, inside 12
    // after the film).
    internal static float[] Film(double darkness)
    {
        var t = Math.Clamp(darkness, 0, 1);
        static float Mix(double light, double dark, double t) => (float)(light + (dark - light) * t);
        return [Mix(FilmLight[0], FilmDark[0], t), Mix(FilmLight[1], FilmDark[1], t), Mix(FilmLight[2], FilmDark[2], t), Mix(FilmLight[3], FilmDark[3], t)];
    }
    internal static readonly double[] FilmDark = [1.0, 0.70, 0.17, 1.4];
    internal static readonly double[] FilmLight = [1.0, 0.88, 0.30, 1.25];

    // ED-NEW-glass-g1: a piece's refraction, by its size (the shader's `optics`: x the bend at the edge, y the fall-off's width, z the border
    // before it falls, w the rim ring at the edge, all in the piece's units).
    //   - rizzytoday/liquid-glass (MIT, (c) 2026 Riz Roze), core/liquid-glass.ts buildDisplacementMap: the map is the full displacement (scale -180,
    //     half of it at the edge: 90 px) under a neutral grey centre blurred by 11 px and inset by 3.5 % of the smaller side, so the bend is
    //     full at the edge and falls off as a Gaussian edge does (its complement) over about the blur. On its 52 px pill that is a bend of about
    //     the pill's height at the edge over a 14 px band. Here the fall-off is 21 % of the smaller side up to 11 (its 11 on 52), the border 3.5 %
    //     up to 3, and the bend 55 % of the smaller side up to 40 (a small piece takes less again, down to half that under 60 pt, so the
    //     effort knob, 28 pt, keeps the colour under it: GlassSmallLensTests). The per-channel scales of its chromatic aberration (0 / 10 / 20 on
    //     180) are the fringe: green bends 0.944 and blue 0.889 of red, about 4 to 6 pt from red to blue at a panel's edge.
    //   - dashersw/liquid-glass-js (MIT, (c) 2025 Armagan Amcalar), container.js setupShader: the rim ring, rimIntensity * exp(-rimDistance * d)
    //     with rimDistance 0.8 per pixel, a sharp extra bend on the edge's first pixels; here 15 % of the smaller side up to 10 pt.
    internal readonly record struct Optics(double Bend, double Sigma, double Border, double Ring)
    {
        internal const double MaxBend = 40, MaxRing = 10;
        internal const double Logistic = 1.702; // the logistic form of the normal distribution (within 0.0095 of it)
        internal const double BendFloor = 0.01; // the bend's last 1 % is dropped, so it ends: at BendSupport fall-off widths past the border
        internal const double BendSupport = 3.12; // ln(199) / 1.702, where the normalised logistic tail reaches the floor
        internal const double RingFloor = 0.001;
        internal static readonly double RingReach = Math.Log(1 / RingFloor) / 0.8; // where exp(-0.8 d) reaches the floor: 8.63
        internal const double GlowReach = 9; // the light inner edge ends 9 pt in
        internal const double Green = 1 - 10 / 180.0, Blue = 1 - 20 / 180.0;
        internal const double ShadeBase = 0.08, ShadeSide = 0.6; // the contact line outside the edge: everywhere, and where the edge faces sideways
        internal const double RimBase = 0.2, RimTop = 0.22, RimBottom = 0.18; // the rim's white over the body: all around, then by how much it faces up or down
        internal const double Glow = 0.05;

        public static Optics For(Size size)
        {
            var smaller = Math.Max(1, Math.Min(size.Width, size.Height));
            var small = Math.Clamp(smaller / 60, 0.5, 1);
            return new Optics(Math.Min(MaxBend, 0.55 * smaller * small), Math.Clamp(0.21 * smaller, 1, 11), Math.Min(3, 0.035 * smaller), Math.Min(MaxRing, 0.15 * smaller));
        }

        /// <summary>How far in from the edge anything but the body is drawn: past it the shader reads one texel.</summary>
        public double Deep => Math.Max(Math.Max(Border + BendSupport * Sigma, GlowReach), RingReach);

        /// <summary>The largest displacement, the red channel's at the edge.</summary>
        public double Reach => Bend + Ring;

        public float[] Uniform => [(float)Bend, (float)Sigma, (float)Border, (float)Ring];
    }

    // How far a change under a material reaches into what it draws, in the surface's pixels: through the blur (3 sigma) everywhere, and through the
    // refraction as well (SampledMargin in all) within the refracting band at the shape's edge. Inner is the part of the output no band can reach into
    // (empty for a container's union or a turned shape): a change whose blur and refraction reach stays inside it spreads by the blur alone.
    internal readonly record struct Spread(SKRectI Output, int Margin, SKRectI Inner, int Blur, int Bend)
    {
        public SKRectI Of(SKRectI changed)
        {
            var wide = Grow(changed, Blur + Bend);
            return Grow(changed, !Inner.IsEmpty && Inner.Contains(wide) ? Blur : Math.Max(Margin, Blur + Bend));
        }
        // Clamped to int: a huge or infinite rect (an unbounded dirty rect) must not overflow the checked arithmetic (Directory.Build.props).
        private static SKRectI Grow(SKRectI r, int by) => new(Clamp((long)r.Left - by), Clamp((long)r.Top - by), Clamp((long)r.Right + by), Clamp((long)r.Bottom + by));
        private static int Clamp(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    // Draws the material; returns what it covers in the surface's pixels and how far a change under it reaches, for the window's Reach.
    // ED-NEW-glass-g1: the glass is translucent (it keeps the alpha of the pixels under it), so it cannot be drawn over them: the sharp pixels would show
    // through it. Each pixel becomes mix(under, value, amount) in two draws of the one shader: stage 0 with BlendMode.DstOut takes `amount` of the pixel away,
    // stage 1 with BlendMode.Plus adds value * amount. The second is the material; the first evaluates only the shape's distance and its edge.
    private static Spread? Draw(ISkiaSharpApiLease lease, State state, Backdrop kept, SKRectI? dirty)
    {
        var canvas = lease.SkCanvas;
        var bounds = UnionBounds(state).Inflate(state.Spacing / 2 + 2); // the contact line reaches 1.25 past the shape
        var local = new SKRect((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom);
        var transform = canvas.TotalMatrix;
        var scale = MathF.Max(MathF.Sqrt(transform.ScaleX * transform.ScaleX + transform.SkewY * transform.SkewY),
            MathF.Sqrt(transform.ScaleY * transform.ScaleY + transform.SkewX * transform.SkewX));
        if (scale <= 0) return null;
        var sampled = transform.MapRect(local);
        var output = SKRectI.Ceiling(sampled);
        sampled.Inflate((float)SampledMargin * scale, (float)SampledMargin * scale); // 3 sigma (36), the largest displacement (50), antialias margin.
        var crop = SKRectI.Ceiling(sampled);
        // Immutable app-owned pixels: no screen capture or visual-tree replay.
        var under = kept.Take(lease, crop, dirty);
        var surface = kept.Blurred(lease, crop);
        if (surface is null) return null;
        // The blur is read only where the shader draws in this frame (the canvas's clip on the shapes) and as far as the refraction displaces from
        // there (the largest bend and ring, the red channel's, and the linear filter's pixel): only that much is cleared and blurred (ED-320 16).
        var reads = SKRectI.Intersect(canvas.DeviceClipBounds, output);
        var smaller = state.Regions.Max(r => Math.Min(r.Size.Width, r.Size.Height));
        // 12 pt on a panel; a small lens takes a blur in proportion (15 % of its smaller side), so the colour under
        // it is not washed out by what lies around it (the effort knob over its fill and glow; see the shader note).
        var radius = (float)Math.Min(12, Math.Max(1, smaller * 0.15)) * scale;
        var reach = state.Regions.Max(r => Optics.For(r.Size).Reach * Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22 - r.Transform.M12 * r.Transform.M21)));
        var spread = SpreadOf(state, output, scale, radius);
        if (reads.IsEmpty) return spread;
        var shift = (int)Math.Ceiling(reach * scale) + 2;
        var blurredCanvas = surface.Canvas;
        blurredCanvas.Save();
        blurredCanvas.ClipRect(new SKRect(reads.Left - shift - crop.Left, reads.Top - shift - crop.Top, reads.Right + shift - crop.Left, reads.Bottom + shift - crop.Top));
        // ED-NEW-glass-g1: cleared to nothing, not to a veil: where the window's pixels are transparent (the Acrylic) or translucent (its scrim), the
        // glass keeps them so (it was an opaque #1E1E20 there, near-black glass over the Acrylic).
        blurredCanvas.Clear(SKColors.Transparent);
        if (under is { } taken)
        {
            using var blur = SKImageFilter.CreateBlur(radius, radius, SKShaderTileMode.Clamp);
            using var blurPaint = new SKPaint { ImageFilter = blur };
            using var pixels = taken.Surface.Snapshot(); // released before the next frame writes to the kept surface, so Skia copies nothing
            blurredCanvas.DrawImage(pixels, taken.Area.Left - crop.Left, taken.Area.Top - crop.Top, blurPaint);
        }
        blurredCanvas.Restore();
        using var image = surface.Snapshot();
        var sourceTransform = transform;
        sourceTransform.TransX -= crop.Left;
        sourceTransform.TransY -= crop.Top;
        if (!sourceTransform.TryInvert(out var shaderTransform)) return null;
        using var backdrop = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Linear), shaderTransform);
        var effect = Effect(state.Regions.Length);
        var uniforms = new SKRuntimeEffectUniforms(effect) { ["merge"] = (float)(2 * state.Spacing), ["stage"] = 0f, ["interior"] = 0f };
        for (var i = 0; i < state.Regions.Length; i++)
        {
            var r = state.Regions[i];
            var inverse = r.Transform.Invert();
            var values = new (string Name, float[] Value)[]
            {
                ($"transform{i}", [(float)inverse.M11, (float)inverse.M12, (float)inverse.M21, (float)inverse.M22]),
                ($"translate{i}", [(float)inverse.M31, (float)inverse.M32]),
                ($"size{i}", [(float)r.Size.Width, (float)r.Size.Height]),
                ($"corners{i}", [(float)r.Corners.TopLeft, (float)r.Corners.TopRight, (float)r.Corners.BottomRight, (float)r.Corners.BottomLeft]),
                ($"tint{i}", [r.Tint.R / 255f, r.Tint.G / 255f, r.Tint.B / 255f, r.Tint.A / 255f]),
                ($"film{i}", Film(r.Darkness)),
                ($"optics{i}", Optics.For(r.Size).Uniform),
                ($"flags{i}", [(float)r.Shape.Kind, r.Shape.Style == RoundedCornerStyle.Continuous ? (r.Shape.CircularInBand ? 2f : 1f) : 0f,
                    (float)Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22 - r.Transform.M12 * r.Transform.M21)), (float)r.Opacity]),
                ($"pointer{i}", [(float)r.Pointer.X, (float)r.Pointer.Y, r.Pressed ? 0.12f : r.Hovered ? 0.06f : 0f, r.DrawnTint ? 1f : 0f]),
            };
            foreach (var (name, value) in values) uniforms[name] = value;
        }
        var children = new SKRuntimeEffectChildren(effect) { ["backdrop"] = backdrop };
        using var maskShader = effect.ToShader(uniforms, children);
        uniforms["stage"] = 1f;
        using var shader = effect.ToShader(uniforms, children);
        var faded = SKColors.White.WithAlpha((byte)Math.Clamp(lease.CurrentOpacity * 255, 0, 255));
        using var mask = new SKPaint { Shader = maskShader, Color = faded, BlendMode = SKBlendMode.DstOut };
        using var paint = new SKPaint { Shader = shader, Color = faded, BlendMode = SKBlendMode.Plus };
        if (Inside(state) is not { } inside)
        {
            canvas.DrawRect(local, mask); // One SDF clips, refracts, lights and merges the same shape.
            canvas.DrawRect(local, paint);
            return spread;
        }
        // ED-320 16: the inside of one rounded rectangle (Inside) is drawn by the same shader with `interior` set, which computes there what its deep
        // branch does without the shape's distance; the shader draws the rest. The two clips are the same rectangle, not antialiased, so each pixel
        // is drawn once.
        uniforms["interior"] = 1f;
        using var innerShader = effect.ToShader(uniforms, children);
        using var innerPaint = new SKPaint { Shader = innerShader, Color = faded, BlendMode = SKBlendMode.Plus };
        // Inside, the amount is the piece's opacity alone (its coverage is whole and no contact line reaches there).
        using var innerMask = new SKPaint { Color = SKColors.Black.WithAlpha((byte)Math.Clamp(Math.Round(lease.CurrentOpacity * state.Regions[0].Opacity * 255), 0, 255)), BlendMode = SKBlendMode.DstOut };
        canvas.Save();
        canvas.ClipRect(inside);
        canvas.DrawRect(inside, innerMask);
        canvas.DrawRect(inside, innerPaint);
        canvas.Restore();
        canvas.Save();
        canvas.ClipRect(inside, SKClipOperation.Difference);
        canvas.DrawRect(local, mask);
        canvas.DrawRect(local, paint);
        canvas.Restore();
        return spread;
    }

    // The inside of a lone rounded rectangle, in the material's coordinates: past 1.6 corner radii from every side (Apple's corner curve lies within them,
    // ContinuousCorner.Extent) and deeper than Optics.Deep (the shader's own test, half a unit further in): there the shader reads one texel and its
    // bend, ring, rim, inner light and contact line add nothing. Null for a container's union, a turned or a non-rectangular shape, or one too small to have one.
    private static SKRect? Inside(State state)
    {
        if (state.Regions is not [{ Shape.Kind: GlassShapeKind.RoundedRectangle, Transform: { M12: 0, M21: 0 } } r]) return null;
        var distanceScale = Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22));
        if (distanceScale <= 0) return null;
        var deep = Optics.For(r.Size).Deep / distanceScale;
        var corners = Math.Max(Math.Max(r.Corners.TopLeft, r.Corners.TopRight), Math.Max(r.Corners.BottomRight, r.Corners.BottomLeft));
        var inset = Math.Max(1.6 * corners, deep) + 0.5;
        if (r.Size.Width <= 2 * inset || r.Size.Height <= 2 * inset) return null;
        var box = new Rect(inset, inset, r.Size.Width - 2 * inset, r.Size.Height - 2 * inset).TransformToAABB(r.Transform);
        return new SKRect((float)box.Left, (float)box.Top, (float)box.Right, (float)box.Bottom);
    }

    private static Spread SpreadOf(State state, SKRectI output, float scale, float radius)
    {
        var margin = (int)Math.Ceiling(SampledMargin * scale);
        var blur = (int)Math.Ceiling(3 * radius) + 2; // the blur's 3 sigma (radius is the sigma) and the linear filter's pixel
        var bend = (int)Math.Ceiling((Optics.MaxBend + Optics.MaxRing) * scale) + 2; // the refraction's largest displacement, the red channel's at the edge
        var inner = SKRectI.Empty;
        if (state.Regions is [{ Transform: { M12: 0, M21: 0 } } only])
        {
            // The band ends Optics.Deep in from the edge, at most 37.4 pt (3 + 3.12 * 11): 48 holds it with room. A corner's curve, Apple's included
            // (ContinuousCorner, under 1.6 times its radius), lies inside the box the corners' radii take from it.
            var corners = Math.Max(Math.Max(only.Corners.TopLeft, only.Corners.TopRight), Math.Max(only.Corners.BottomRight, only.Corners.BottomLeft));
            if (only.Shape.Kind != GlassShapeKind.RoundedRectangle) corners = Math.Min(only.Size.Width, only.Size.Height) / 2;
            var inset = (int)Math.Ceiling((48 + 1.6 * corners) * scale * Math.Max(Math.Abs(only.Transform.M11), Math.Abs(only.Transform.M22)));
            inner = new SKRectI(output.Left + inset, output.Top + inset, output.Right - inset, output.Bottom - inset);
            if (inner.Width <= 0 || inner.Height <= 0) inner = SKRectI.Empty;
        }
        return new Spread(output, margin, inner, blur, bend);
    }
}
