// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184, :218-259;
// App/Views/Composer/ComposerView.swift:23; App/Views/Chat/ChatView.swift:1120-1124; App/Views/Diff/DiffInspector.swift:25.
// PORT-ONLY: OD-0005 / ADR-0002 Glass (b), Skia leases as demonstrated in porting/probes/p1-glass.
// Optical values are an ED proposal in glass-material.report.md, UNVERIFIED under OD-0001.
using System.Collections.Concurrent;
using System.Globalization;
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
        // appearance on AppearanceTurn's curve and clock, as every other colour does, and are what the click frame already drew until then (ED-NEW-glass-firstchild).
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
            if (Draw(lease, s, backdrop, target is null ? null : Frame.Dirty(target)) is { } drawn) reach?.Set(this, drawn);
        }
    }

    // Test-only readback (InternalsVisibleTo): how many times any material has drawn, on the render thread.
    internal static int DrawsForTests;

    // Sent before a material leaves its parent: its handler gives up the backdrop it keeps (disposed at the next render, where the GPU context is current).
    private sealed class Released { public static readonly Released Message = new(); }

    // How far past its shape the material reads the pixels under it: the blur's 3 sigma, the refraction's largest displacement and the antialiased edge (Draw).
    // It is also the material's render bounds: whenever the material appears, moves or changes, Avalonia redraws all it reads, so the backdrop it keeps is
    // taken whole there (Backdrop; GlassMaterialPartialRedrawTests, the label beside a piece that appears).
    private const double SampledMargin = 66;

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
        var source = new StringBuilder(ShaderHeader);
        for (var i = 0; i < n; i++) source.Append(CultureInfo.InvariantCulture, $$"""

            uniform float4 transform{{i}};
            uniform float2 translate{{i}};
            uniform float2 size{{i}};
            uniform float4 corners{{i}};
            uniform float4 tint{{i}};
            uniform float4 frost{{i}};
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
        source.Append("return d; }\nhalf4 main(float2 xy) { float raw = distance0(xy); float4 tint = tint0; float4 frost = frost0; float4 ptr = pointer0; float2 at = local0(xy); float4 flags = flags0; float2 extent = size0;\n");
        for (var i = 1; i < n; i++) source.Append(CultureInfo.InvariantCulture, $"if (distance{i}(xy) < raw) {{ raw = distance{i}(xy); tint = tint{i}; frost = frost{i}; ptr = pointer{i}; at = local{i}(xy); flags = flags{i}; extent = size{i}; }}\n");
        // With one shape the scene's distance is the shape's own (no union): it is not evaluated twice.
        source.Append(n == 1 ? "float d = -raw;\n" : "float d = -sceneDistance(xy);\n");
        source.Append("""
            float coverage = clamp(d + 0.5, 0.0, 1.0);
            if (coverage <= 0.0) return half4(0.0);
            // The refracting band and its bend scale with the shape, capped at the panel values: on a small lens (the
            // effort knob, 28 pt) a fixed 46 pt band made the whole disc an edge that bent in the dark surroundings,
            // where the reference video shows a light disc with a thin refracting rim (2-chat-use.mov 35.5 s, 43.9 s).
            // A band of 3.5 % of the smaller side plus blur is the same idea in rizzytoday/liquid-glass (MIT,
            // technique only). ENGINEERING DECISION, UNVERIFIED fractions (35 % band, 20 % bend).
            float smaller = min(extent.x, extent.y);
            float band = min(46.0, max(smaller * 0.35, 1.0));
            // The inner shade sits 5 pt in on a panel; on a small lens it keeps to the same share of the band.
            float shadeAt = min(5.0, band * 0.3);
            half3 col;
            float rim = 0.0, light = 0.0, inner = 0.0, shade = 0.0;
            // ED-NEW-glass-firstchild 16: past the band the lens bends nothing (the three reads are one), and past where the rim light and the inner
            // shade fall under 1e-7 of a level the normal changes nothing either: the inside of a shape skips the normal's four distances.
            if (d >= band && d > 11.5 && d > shadeAt + 4.0 * sqrt(2.0 * shadeAt * shadeAt + 0.5)) {
                col = half3(backdrop.eval(xy).rgb);
            } else {
                // P1 finite-difference inward normal follows curved edges and the merged neck.
                float2 normal = float2(-sceneDistance(xy + float2(1.0, 0.0)) + sceneDistance(xy - float2(1.0, 0.0)),
                                      -sceneDistance(xy + float2(0.0, 1.0)) + sceneDistance(xy - float2(0.0, 1.0)));
                float2 n = normalize(normal + float2(0.000001));
                float lens = pow(clamp(1.0 - d / band, 0.0, 1.0), 2.2);
                float disp = lens * min(26.0, smaller * 0.2);
                half4 r = backdrop.eval(xy - n * disp * 1.10);
                half4 g = backdrop.eval(xy - n * disp);
                half4 b = backdrop.eval(xy - n * disp * 0.90);
                col = half3(r.r, g.g, b.b);
                rim = exp(-pow(d - 1.5, 2.0) / 6.0);
                light = pow(clamp(-n.x * 0.7 - n.y * 0.7, 0.0, 1.0), 1.5);
                inner = exp(-pow(d - shadeAt, 2.0) / (2.0 * shadeAt * shadeAt + 0.5));
                shade = pow(clamp(n.x * 0.7 + n.y * 0.7, 0.0, 1.0), 1.5);
            }
            col = mix(col, half3(frost.rgb), half(frost.a));
            // Avoid double tinting existing overlays; tint the merged neck outside those overlays.
            if (ptr.w < 0.5 || raw > 0.0) col = mix(col, half3(tint.rgb), half(tint.a));
            col += half3(half(rim * light * 0.55));
            col *= half(1.0 - inner * shade * 0.12);
            float2 delta = at - ptr.xy;
            float highlight = exp(-dot(delta, delta) / (2.0 * 48.0 * 48.0)) * ptr.z;
            col += half3(half(highlight));
            half alpha = half(coverage * flags.w);
            return half4(clamp(col, 0.0, 1.0) * alpha, alpha);
            }
            """);
        return SKRuntimeEffect.CreateShader(source.ToString(), out var error)
            ?? throw new InvalidOperationException("Glass shader did not compile: " + error);
    })).Value;

    // The pixels under a material as they are when it draws: what a whole drawing of the window has drawn before it (ED-NEW-glass-firstchild 11). The surface
    // holds them only inside the region Avalonia redraws in this frame; outside it the surface is the last finished frame, the glass itself and what lies
    // over it, so a redraw of a control over the glass blurred the glass's own output into its frost (8 to 11 levels, GlassMaterialPartialRedrawReviewTests).
    // Outside that region nothing under the material has changed (whatever changes adds its bounds to the region), so the material keeps what it took the
    // last time and takes new pixels only inside the region. With no known region (another dirty-rect tracker, another surface) it takes them all, as before.
    // ED-NEW-glass-firstchild 16 (frame cost): the kept pixels live in one surface the material keeps, and only the region's pixels are copied out of the
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
        private static FieldInfo? left, top, right, bottom;
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
            try
            {
                if (events is null || Tracker(target) is not { } tracker) return null;
                debugEvents ??= target.GetType().GetProperty("DebugEvents", Members);
                addRect ??= tracker.GetType().GetMethod("AddRect", Members);
                if (debugEvents is null || addRect is null || debugEvents.GetValue(target) is not null) return null;
                var reach = (Reach)DispatchProxy.Create(events, typeof(Reach));
                reach.Bind(tracker, addRect);
                debugEvents.SetValue(target, reach);
                reaches.Add(target, reach);
                return reach;
            }
            catch (Exception e) when (Expected(e) || e is NotSupportedException or InvalidOperationException) { return null; }
        }

        public static SKRectI ToRect(object rect)
        {
            var type = rect.GetType();
            (left, top, right, bottom) = (left ?? type.GetField("Left"), top ?? type.GetField("Top"), right ?? type.GetField("Right"), bottom ?? type.GetField("Bottom"));
            return new SKRectI((int)Math.Round((double)left!.GetValue(rect)!), (int)Math.Round((double)top!.GetValue(rect)!),
                (int)Math.Round((double)right!.GetValue(rect)!), (int)Math.Round((double)bottom!.GetValue(rect)!));
        }

        public static object ToLtrb(Type type, SKRectI rect)
        {
            var boxed = Activator.CreateInstance(type)!;
            left!.SetValue(boxed, (double)rect.Left);
            top!.SetValue(boxed, (double)rect.Top);
            right!.SetValue(boxed, (double)rect.Right);
            bottom!.SetValue(boxed, (double)rect.Bottom);
            return boxed;
        }

        private static bool Expected(Exception e) =>
            e is ArgumentException or MemberAccessException or TargetInvocationException or InvalidCastException or FieldAccessException or NullReferenceException;
    }

    // ED-NEW-glass-firstchild 15. A change under a material spreads, through its blur and refraction, to the glass up to SampledMargin around it, but Avalonia
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
        private MethodInfo? addRect;
        private readonly Dictionary<object, Spread> materials = new();
        private readonly List<SKRectI> queue = new();
        private readonly HashSet<object> grown = new();
        private readonly List<KeyValuePair<object, Spread>> pass = new();
        private int rendered, visited;

        internal void Bind(object dirtyRects, MethodInfo add) => (tracker, addRect) = (dirtyRects, add);
        internal void Set(object material, Spread spread) => materials[material] = spread;
        internal void Remove(object material) => materials.Remove(material);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "RectInvalidated" when args is [{ } rect]: Grow(rect); return null;
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
                    addRect.Invoke(tracker, [Frame.ToLtrb(rect.GetType(), add)]);
                    queue.Add(add);
                }
            }
            pass.Clear();
        }
    }

    // The veil's channel between the light appearance's and the dark one's (a whole level, as the two ends were): darkness 0 is light, 1 is dark.
    private static byte Veil(double darkness, int dark, int light) => (byte)Math.Round(light + (dark - light) * Math.Clamp(darkness, 0, 1));

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
        private static SKRectI Grow(SKRectI r, int by) => new(r.Left - by, r.Top - by, r.Right + by, r.Bottom + by);
    }

    // Draws the material; returns what it covers in the surface's pixels and how far a change under it reaches, for the window's Reach.
    private static Spread? Draw(ISkiaSharpApiLease lease, State state, Backdrop kept, SKRectI? dirty)
    {
        var canvas = lease.SkCanvas;
        var bounds = UnionBounds(state).Inflate(state.Spacing / 2 + 1);
        var local = new SKRect((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom);
        var transform = canvas.TotalMatrix;
        var scale = MathF.Max(MathF.Sqrt(transform.ScaleX * transform.ScaleX + transform.SkewY * transform.SkewY),
            MathF.Sqrt(transform.ScaleY * transform.ScaleY + transform.SkewX * transform.SkewX));
        if (scale <= 0) return null;
        var sampled = transform.MapRect(local);
        var output = SKRectI.Ceiling(sampled);
        sampled.Inflate((float)SampledMargin * scale, (float)SampledMargin * scale); // 3 sigma (36), max P1 displacement (28.6), antialias margin.
        var crop = SKRectI.Ceiling(sampled);
        // Immutable app-owned pixels: no screen capture or visual-tree replay.
        var under = kept.Take(lease, crop, dirty);
        var surface = kept.Blurred(lease, crop);
        if (surface is null) return null;
        // The blur is read only where the shader draws in this frame (the canvas's clip on the shapes) and as far as the refraction displaces from
        // there (26 pt times 1.1, the red channel's, and the linear filter's pixel): only that much is cleared and blurred (ED-NEW-glass-firstchild 16).
        var reads = SKRectI.Intersect(canvas.DeviceClipBounds, output);
        var smaller = state.Regions.Max(r => Math.Min(r.Size.Width, r.Size.Height));
        // 12 pt on a panel; a small lens takes a blur in proportion (15 % of its smaller side), so the colour under
        // it is not washed out by what lies around it (the effort knob over its fill and glow; see the shader note).
        var radius = (float)Math.Min(12, Math.Max(1, smaller * 0.15)) * scale;
        var spread = SpreadOf(state, output, scale, radius);
        if (reads.IsEmpty) return spread;
        var shift = (int)Math.Ceiling(28.6 * scale) + 2;
        var blurredCanvas = surface.Canvas;
        blurredCanvas.Save();
        blurredCanvas.ClipRect(new SKRect(reads.Left - shift - crop.Left, reads.Top - shift - crop.Top, reads.Right + shift - crop.Left, reads.Bottom + shift - crop.Top));
        blurredCanvas.Clear(new SKColor(Veil(state.Regions[0].Darkness, 30, 245), Veil(state.Regions[0].Darkness, 30, 245), Veil(state.Regions[0].Darkness, 32, 247)));
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
        var uniforms = new SKRuntimeEffectUniforms(effect) { ["merge"] = (float)(2 * state.Spacing) };
        var first = new List<(string Name, float[] Value)>(6); // region 0's values, for Inner
        for (var i = 0; i < state.Regions.Length; i++)
        {
            var r = state.Regions[i];
            var inverse = r.Transform.Invert();
            uniforms[$"transform{i}"] = new[] { (float)inverse.M11, (float)inverse.M12, (float)inverse.M21, (float)inverse.M22 };
            uniforms[$"translate{i}"] = new[] { (float)inverse.M31, (float)inverse.M32 };
            uniforms[$"size{i}"] = new[] { (float)r.Size.Width, (float)r.Size.Height };
            uniforms[$"corners{i}"] = new[] { (float)r.Corners.TopLeft, (float)r.Corners.TopRight, (float)r.Corners.BottomRight, (float)r.Corners.BottomLeft };
            uniforms[$"tint{i}"] = new[] { r.Tint.R / 255f, r.Tint.G / 255f, r.Tint.B / 255f, r.Tint.A / 255f };
            uniforms[$"frost{i}"] = new[] { Veil(r.Darkness, 30, 245) / 255f, Veil(r.Darkness, 30, 245) / 255f, Veil(r.Darkness, 32, 247) / 255f, 0.18f };
            var distanceScale = Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22 - r.Transform.M12 * r.Transform.M21));
            uniforms[$"flags{i}"] = new[] { (float)r.Shape.Kind, r.Shape.Style == RoundedCornerStyle.Continuous ? (r.Shape.CircularInBand ? 2f : 1f) : 0f, (float)distanceScale, (float)r.Opacity };
            uniforms[$"pointer{i}"] = new[] { (float)r.Pointer.X, (float)r.Pointer.Y, r.Pressed ? 0.12f : r.Hovered ? 0.06f : 0f, r.DrawnTint ? 1f : 0f };
            if (i == 0)
            {
                first.Add(("transform0", new[] { (float)inverse.M11, (float)inverse.M12, (float)inverse.M21, (float)inverse.M22 }));
                first.Add(("translate0", new[] { (float)inverse.M31, (float)inverse.M32 }));
                first.Add(("tint0", new[] { r.Tint.R / 255f, r.Tint.G / 255f, r.Tint.B / 255f, r.Tint.A / 255f }));
                first.Add(("frost0", new[] { Veil(r.Darkness, 30, 245) / 255f, Veil(r.Darkness, 30, 245) / 255f, Veil(r.Darkness, 32, 247) / 255f, 0.18f }));
                first.Add(("flags0", new[] { (float)r.Shape.Kind, r.Shape.Style == RoundedCornerStyle.Continuous ? (r.Shape.CircularInBand ? 2f : 1f) : 0f, (float)distanceScale, (float)r.Opacity }));
                first.Add(("pointer0", new[] { (float)r.Pointer.X, (float)r.Pointer.Y, r.Pressed ? 0.12f : r.Hovered ? 0.06f : 0f, r.DrawnTint ? 1f : 0f }));
            }
        }
        var children = new SKRuntimeEffectChildren(effect) { ["backdrop"] = backdrop };
        using var shader = effect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader, Color = SKColors.White.WithAlpha((byte)Math.Clamp(lease.CurrentOpacity * 255, 0, 255)) };
        if (Inside(state) is not { } inside)
        {
            canvas.DrawRect(local, paint); // One SDF clips, refracts, lights and merges the same shape.
            return spread;
        }
        // ED-NEW-glass-firstchild 16: the inside of one rounded rectangle (Inside) is drawn by Inner, which computes there what the whole shader does;
        // the shader draws the rest. The two clips are the same rectangle, not antialiased, so each pixel is drawn once.
        var innerEffect = state.Regions[0] is { Hovered: false, Pressed: false } ? Unlit.Value : Inner.Value;
        var innerUniforms = new SKRuntimeEffectUniforms(innerEffect);
        foreach (var (name, value) in first) if (innerEffect.Uniforms.Contains(name)) innerUniforms[name] = value;
        using var innerShader = innerEffect.ToShader(innerUniforms, new SKRuntimeEffectChildren(innerEffect) { ["backdrop"] = backdrop });
        using var innerPaint = new SKPaint { Shader = innerShader, Color = paint.Color };
        canvas.Save();
        canvas.ClipRect(inside);
        canvas.DrawRect(inside, innerPaint);
        canvas.Restore();
        canvas.Save();
        canvas.ClipRect(inside, SKClipOperation.Difference);
        canvas.DrawRect(local, paint);
        canvas.Restore();
        return spread;
    }

    // The inside of a lone rounded rectangle, in the material's coordinates: past 1.6 corner radii from every side (Apple's corner curve lies within them,
    // ContinuousCorner.Extent) and deeper than the band, 11.5 and the inner shade's reach (the shader's own test, half a unit further in): there the shader
    // reads one texel and its rim and shade add nothing. Null for a container's union, a turned or a non-rectangular shape, or one too small to have one.
    private static SKRect? Inside(State state)
    {
        if (state.Regions is not [{ Shape.Kind: GlassShapeKind.RoundedRectangle, Transform: { M12: 0, M21: 0 } } r]) return null;
        var distanceScale = Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22));
        if (distanceScale <= 0) return null;
        var band = Math.Min(46.0, Math.Max(Math.Min(r.Size.Width, r.Size.Height) * 0.35, 1.0));
        var shadeAt = Math.Min(5.0, band * 0.3);
        var deep = Math.Max(Math.Max(band, 11.5), shadeAt + 4.0 * Math.Sqrt(2.0 * shadeAt * shadeAt + 0.5)) / distanceScale;
        var corners = Math.Max(Math.Max(r.Corners.TopLeft, r.Corners.TopRight), Math.Max(r.Corners.BottomRight, r.Corners.BottomLeft));
        var inset = Math.Max(1.6 * corners, deep) + 0.5;
        if (r.Size.Width <= 2 * inset || r.Size.Height <= 2 * inset) return null;
        var box = new Rect(inset, inset, r.Size.Width - 2 * inset, r.Size.Height - 2 * inset).TransformToAABB(r.Transform);
        return new SKRect((float)box.Left, (float)box.Top, (float)box.Right, (float)box.Bottom);
    }

    // The shader's own sum inside a shape (the branch of Effect past the band, with coverage 1 and the shape's own tint): one texel, the frost, the tint,
    // the pointer's highlight; Unlit is the same with no pointer on the piece (its highlight is 0 times a positive number: nothing).
    private static readonly Lazy<SKRuntimeEffect> Unlit = new(() => SKRuntimeEffect.CreateShader("""
        uniform shader backdrop;
        uniform float4 tint0;
        uniform float4 frost0;
        uniform float4 flags0;
        uniform float4 pointer0;
        half4 main(float2 xy) {
            half3 col = half3(backdrop.eval(xy).rgb);
            col = mix(col, half3(frost0.rgb), half(frost0.a));
            if (pointer0.w < 0.5) col = mix(col, half3(tint0.rgb), half(tint0.a));
            half alpha = half(1.0 * flags0.w);
            return half4(clamp(col, 0.0, 1.0) * alpha, alpha);
        }
        """, out var error) ?? throw new InvalidOperationException("Glass inside shader did not compile: " + error));
    private static readonly Lazy<SKRuntimeEffect> Inner = new(() => SKRuntimeEffect.CreateShader("""
        uniform shader backdrop;
        uniform float4 transform0;
        uniform float2 translate0;
        uniform float4 tint0;
        uniform float4 frost0;
        uniform float4 flags0;
        uniform float4 pointer0;
        half4 main(float2 xy) {
            half3 col = half3(backdrop.eval(xy).rgb);
            col = mix(col, half3(frost0.rgb), half(frost0.a));
            if (pointer0.w < 0.5) col = mix(col, half3(tint0.rgb), half(tint0.a));
            float2 at = float2(xy.x * transform0.x + xy.y * transform0.z, xy.x * transform0.y + xy.y * transform0.w) + translate0;
            float2 delta = at - pointer0.xy;
            float highlight = exp(-dot(delta, delta) / (2.0 * 48.0 * 48.0)) * pointer0.z;
            col += half3(half(highlight));
            half alpha = half(1.0 * flags0.w);
            return half4(clamp(col, 0.0, 1.0) * alpha, alpha);
        }
        """, out var error) ?? throw new InvalidOperationException("Glass inside shader did not compile: " + error));

    private static Spread SpreadOf(State state, SKRectI output, float scale, float radius)
    {
        var margin = (int)Math.Ceiling(SampledMargin * scale);
        var blur = (int)Math.Ceiling(3 * radius) + 2; // the blur's 3 sigma (radius is the sigma) and the linear filter's pixel
        var bend = (int)Math.Ceiling(28.6 * scale) + 2; // the refraction's largest displacement, 26 pt times 1.1
        var inner = SKRectI.Empty;
        if (state.Regions is [{ Transform: { M12: 0, M21: 0 } } only])
        {
            // The band is at most 46 pt in from the edge; a corner's curve, Apple's included (ContinuousCorner, under 1.6 times its radius), lies inside
            // the box the corners' radii take from it.
            var corners = Math.Max(Math.Max(only.Corners.TopLeft, only.Corners.TopRight), Math.Max(only.Corners.BottomRight, only.Corners.BottomLeft));
            if (only.Shape.Kind != GlassShapeKind.RoundedRectangle) corners = Math.Min(only.Size.Width, only.Size.Height) / 2;
            var inset = (int)Math.Ceiling((48 + 1.6 * corners) * scale * Math.Max(Math.Abs(only.Transform.M11), Math.Abs(only.Transform.M22)));
            inner = new SKRectI(output.Left + inset, output.Top + inset, output.Right - inset, output.Bottom - inset);
            if (inner.Width <= 0 || inner.Height <= 0) inner = SKRectI.Empty;
        }
        return new Spread(output, margin, inner, blur, bend);
    }
}
