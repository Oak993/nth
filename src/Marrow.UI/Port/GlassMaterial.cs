// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :153-184, :218-259;
// App/Views/Composer/ComposerView.swift:23; App/Views/Chat/ChatView.swift:1120-1124; App/Views/Diff/DiffInspector.swift:25.
// PORT-ONLY: OD-0005 / ADR-0002 Glass (b), Skia leases as demonstrated in porting/probes/p1-glass.
// Optical values are an ED proposal in glass-material.report.md, UNVERIFIED under OD-0001.
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
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
            owner.DetachedFromVisualTree += (_, _) => { RemoveMaterial(); UnbindInput(); Unwatch(); requested = false; };
            owner.LayoutUpdated += (_, _) => Queue();
            owner.PropertyChanged += (_, _) => Queue();
            owner.ActualThemeVariantChanged += (_, _) => Queue();
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
            }
            var index = parent.Children.IndexOf(visual);
            if (index < 0 || material is null) return;
            var existing = parent.Children.IndexOf(material);
            if (existing != index - 1)
            {
                if (existing >= 0) parent.Children.Remove(material);
                parent.Children.Insert(parent.Children.IndexOf(visual), material);
            }
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

        private static Region? RegionFor(Control control, Matrix toOwner, double opacity)
        {
            var e = SwiftGlass.GetEffect(control);
            if (e is null || SwiftGlass.GetUsesDesktopBackdrop(control) || opacity <= 0 || control.Bounds.Width <= 0 || control.Bounds.Height <= 0 || !toOwner.TryInvert(out _)) return null;
            var tint = e.TintColor?.Opacity(e.TintOpacity ?? 1).Resolve(control.ActualThemeVariant) ?? Colors.Transparent;
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
            return new Region(control.Bounds.Size, e.Shape, corners, tint, drawnTint, PlainControls.IsDark(control.ActualThemeVariant),
                e.IsInteractive && a.hovered, e.IsInteractive && a.pressed, a.pointer, toOwner, opacity);
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
            if (material is not null) parent?.Children.Remove(material);
            Unwatch();
            parent = null;
            material = null;
            previous = null;
        }
    }

    private sealed record Region(Size Size, GlassShape Shape, CornerRadius Corners, Color Tint, bool DrawnTint,
        bool Dark, bool Hovered, bool Pressed, Point Pointer, Matrix Transform, double Opacity);
    private sealed record State(Region[] Regions, double Spacing);

    private sealed class Handler : CompositionCustomVisualHandler
    {
        private State? state;
        public override void OnMessage(object message)
        {
            if (message is not State next) return;
            state = next;
            Invalidate(); // Event-driven, no animation-frame registration or idle timer.
        }
        public override Rect GetRenderBounds() => state is { Regions.Length: > 0 } s ? UnionBounds(s).Inflate(s.Spacing / 2 + 1) : default;
        public override void OnRender(ImmediateDrawingContext context)
        {
            if (state is not { Regions.Length: > 0 } s) return;
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null) return;
            using var lease = feature.Lease();
            Draw(lease, s);
        }
    }

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
        source.Append("""
            float d = -sceneDistance(xy);
            float coverage = clamp(d + 0.5, 0.0, 1.0);
            if (coverage <= 0.0) return half4(0.0);
            // P1 finite-difference inward normal follows curved edges and the merged neck.
            float2 normal = float2(-sceneDistance(xy + float2(1.0, 0.0)) + sceneDistance(xy - float2(1.0, 0.0)),
                                  -sceneDistance(xy + float2(0.0, 1.0)) + sceneDistance(xy - float2(0.0, 1.0)));
            float2 n = normalize(normal + float2(0.000001));
            // The refracting band and its bend scale with the shape, capped at the panel values: on a small lens (the
            // effort knob, 28 pt) a fixed 46 pt band made the whole disc an edge that bent in the dark surroundings,
            // where the reference video shows a light disc with a thin refracting rim (2-chat-use.mov 35.5 s, 43.9 s).
            // A band of 3.5 % of the smaller side plus blur is the same idea in rizzytoday/liquid-glass (MIT,
            // technique only). ENGINEERING DECISION, UNVERIFIED fractions (35 % band, 20 % bend).
            float smaller = min(extent.x, extent.y);
            float band = min(46.0, max(smaller * 0.35, 1.0));
            float lens = pow(clamp(1.0 - d / band, 0.0, 1.0), 2.2);
            float disp = lens * min(26.0, smaller * 0.2);
            half4 r = backdrop.eval(xy - n * disp * 1.10);
            half4 g = backdrop.eval(xy - n * disp);
            half4 b = backdrop.eval(xy - n * disp * 0.90);
            half3 col = half3(r.r, g.g, b.b);
            col = mix(col, half3(frost.rgb), half(frost.a));
            // Avoid double tinting existing overlays; tint the merged neck outside those overlays.
            if (ptr.w < 0.5 || raw > 0.0) col = mix(col, half3(tint.rgb), half(tint.a));
            float rim = exp(-pow(d - 1.5, 2.0) / 6.0);
            float light = pow(clamp(-n.x * 0.7 - n.y * 0.7, 0.0, 1.0), 1.5);
            col += half3(half(rim * light * 0.55));
            // The inner shade sits 5 pt in on a panel; on a small lens it keeps to the same share of the band.
            float shadeAt = min(5.0, band * 0.3);
            float inner = exp(-pow(d - shadeAt, 2.0) / (2.0 * shadeAt * shadeAt + 0.5));
            float shade = pow(clamp(n.x * 0.7 + n.y * 0.7, 0.0, 1.0), 1.5);
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

    private static void Draw(ISkiaSharpApiLease lease, State state)
    {
        var canvas = lease.SkCanvas;
        var bounds = UnionBounds(state).Inflate(state.Spacing / 2 + 1);
        var local = new SKRect((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom);
        var transform = canvas.TotalMatrix;
        var scale = MathF.Max(MathF.Sqrt(transform.ScaleX * transform.ScaleX + transform.SkewY * transform.SkewY),
            MathF.Sqrt(transform.ScaleY * transform.ScaleY + transform.SkewX * transform.SkewX));
        if (scale <= 0) return;
        var sampled = transform.MapRect(local);
        sampled.Inflate(66 * scale, 66 * scale); // 3 sigma (36), max P1 displacement (28.6), antialias margin.
        var crop = SKRectI.Ceiling(sampled);
        // Immutable app-owned pixels: no screen capture or visual-tree replay.
        using var snapshot = lease.SkSurface?.Snapshot();
        var info = new SKImageInfo(Math.Max(1, crop.Width), Math.Max(1, crop.Height), SKColorType.Rgba8888, SKAlphaType.Premul);
        // Keep native rendering on the lease's GPU; the software path also runs in headless tests (P1).
        using var surface = lease.GrContext is { } gpu ? SKSurface.Create(gpu, false, info) : SKSurface.Create(info);
        if (surface is null) return;
        surface.Canvas.Clear(state.Regions[0].Dark ? new SKColor(30, 30, 32) : new SKColor(245, 245, 247));
        if (snapshot is not null)
        {
            // 12 pt on a panel; a small lens takes a blur in proportion (15 % of its smaller side), so the colour under
            // it is not washed out by what lies around it (the effort knob over its fill and glow; see the shader note).
            var smaller = state.Regions.Max(r => Math.Min(r.Size.Width, r.Size.Height));
            var radius = (float)Math.Min(12, Math.Max(1, smaller * 0.15)) * scale;
            using var blur = SKImageFilter.CreateBlur(radius, radius, SKShaderTileMode.Clamp);
            using var blurPaint = new SKPaint { ImageFilter = blur };
            surface.Canvas.DrawImage(snapshot, -crop.Left, -crop.Top, blurPaint);
        }
        using var image = surface.Snapshot();
        var sourceTransform = transform;
        sourceTransform.TransX -= crop.Left;
        sourceTransform.TransY -= crop.Top;
        if (!sourceTransform.TryInvert(out var shaderTransform)) return;
        using var backdrop = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Linear), shaderTransform);
        var effect = Effect(state.Regions.Length);
        var uniforms = new SKRuntimeEffectUniforms(effect) { ["merge"] = (float)(2 * state.Spacing) };
        for (var i = 0; i < state.Regions.Length; i++)
        {
            var r = state.Regions[i];
            var inverse = r.Transform.Invert();
            uniforms[$"transform{i}"] = new[] { (float)inverse.M11, (float)inverse.M12, (float)inverse.M21, (float)inverse.M22 };
            uniforms[$"translate{i}"] = new[] { (float)inverse.M31, (float)inverse.M32 };
            uniforms[$"size{i}"] = new[] { (float)r.Size.Width, (float)r.Size.Height };
            uniforms[$"corners{i}"] = new[] { (float)r.Corners.TopLeft, (float)r.Corners.TopRight, (float)r.Corners.BottomRight, (float)r.Corners.BottomLeft };
            uniforms[$"tint{i}"] = new[] { r.Tint.R / 255f, r.Tint.G / 255f, r.Tint.B / 255f, r.Tint.A / 255f };
            uniforms[$"frost{i}"] = r.Dark ? new[] { 30 / 255f, 30 / 255f, 32 / 255f, 0.18f } : new[] { 245 / 255f, 245 / 255f, 247 / 255f, 0.18f };
            var distanceScale = Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22 - r.Transform.M12 * r.Transform.M21));
            uniforms[$"flags{i}"] = new[] { (float)r.Shape.Kind, r.Shape.Style == RoundedCornerStyle.Continuous ? (r.Shape.CircularInBand ? 2f : 1f) : 0f, (float)distanceScale, (float)r.Opacity };
            uniforms[$"pointer{i}"] = new[] { (float)r.Pointer.X, (float)r.Pointer.Y, r.Pressed ? 0.12f : r.Hovered ? 0.06f : 0f, r.DrawnTint ? 1f : 0f };
        }
        var children = new SKRuntimeEffectChildren(effect) { ["backdrop"] = backdrop };
        using var shader = effect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader, Color = SKColors.White.WithAlpha((byte)Math.Clamp(lease.CurrentOpacity * 255, 0, 255)) };
        canvas.DrawRect(local, paint); // One SDF clips, refracts, lights and merges the same shape.
    }
}
