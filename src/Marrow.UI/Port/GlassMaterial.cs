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

internal static partial class GlassMaterial
{
    private static readonly ConditionalWeakTable<Control, Attachment> attachments = new();
    public static void Attach(Control control) => AttachmentFor(control).Queue();
    private static Attachment AttachmentFor(Control control) => attachments.GetValue(control, static c => new Attachment(c));
    // A piece that stands alone (ED-NEW-voicefix-ui-1) has no container: it draws its own material, and no container gathers it.
    private static Control? ContainerOf(Control control) => SwiftGlass.GetStandsAlone(control) ? null : control.GetVisualAncestors().OfType<Control>()
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
            owner.AttachedToVisualTree += (_, _) => { if (InteractionProbe.On) InteractionProbe.Count(ProbeName(owner)); requested = false; Queue(); };
            owner.DetachedFromVisualTree += (_, _) => { RemoveMaterial(); UnbindInput(); Unwatch(); StopTurn(); requested = false; };
            owner.LayoutUpdated += (_, _) =>
            {
                var indifferent = LayoutIndifferent(out var why);
                if (InteractionProbe.On) InteractionProbe.Count(why);
                if (!indifferent) Queue();
            };
            owner.PropertyChanged += (_, e) =>
            {
                if (InteractionProbe.On) InteractionProbe.Count("glass.queue.property." + e.Property.Name);
                Queue();
            };
            owner.ActualThemeVariantChanged += (_, _) => { InteractionProbe.Count("glass.queue.theme"); Queue(); };
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

        // ED-NEW-stutterfix-4: a layout pass anywhere in a window synchronised each of its pieces (a keystroke in the composer did so for each of the
        // main window's ~44: 1,936 synchronisations for 43 keys), and a lone piece's synchronisation, when nothing it is drawn with had changed, computed
        // the same state and sent nothing. A lone piece (one drawing its own material, in no container) now skips a layout pass when what it was last
        // synchronised with that changes without telling it is as it was: its render transform's value (changed in place), its fill's colour (changed
        // in place), its tint as resolved (the accent), its having no container, and its material's copy of its composition visual. The rest of what it is
        // drawn with raises its PropertyChanged or ActualThemeVariantChanged (bounds, opacity, visibility, transform replaced, effect, fill replaced,
        // corners), each of which queues it as before; the window's renderer step (WindowHook) puts its material back after Avalonia's re-synchronisation.
        // A window without that step, a container, and a piece drawn by a container keep to every layout pass. What is drawn, and when, is unchanged.
        private (Matrix Local, Color? Fill, Color Tint)? alone;

        private (Matrix Local, Color? Fill, Color Tint) Unannounced()
        {
            var fill = owner switch { Border border => border.Background, SwiftShapeBorder shape => shape.Background, _ => null } as ISolidColorBrush;
            var e = SwiftGlass.GetEffect(owner);
            var tint = e?.TintColor?.Opacity(e.TintOpacity ?? 1).Resolve(owner.ActualThemeVariant) ?? Colors.Transparent;
            return (LocalTransform(owner), fill?.Color, tint);
        }

        // `why` names the outcome for InteractionProbe (constant strings).
        private bool LayoutIndifferent(out string why)
        {
            why = "glass.layout.queue.not-lone";
            if (alone is not { } kept || material is not { } m || parent is null || ElementComposition.GetElementVisual(owner) is not { } v) return false;
            why = "glass.layout.queue.no-step";
            if (TopLevel.GetTopLevel(owner) is not { } top || !windows.TryGetValue(top, out var hook) || !hook.Installed) return false;
            why = "glass.layout.queue.container";
            if (SwiftGlass.GetContainerSpacing(owner) is not null || ContainerOf(owner) is not null) return false;
            why = "glass.layout.queue.visual";
            if (m.Offset != v.Offset || m.Translation != v.Translation || m.Size != v.Size || m.AnchorPoint != v.AnchorPoint || m.CenterPoint != v.CenterPoint
                || m.Scale != v.Scale || m.RotationAngle != v.RotationAngle || m.Orientation != v.Orientation || m.Opacity != v.Opacity || m.Visible != v.Visible
                || !ReferenceEquals(m.OpacityMask, v.OpacityMask)) return false;
            var same = Unannounced() == kept;
            why = same ? "glass.layout.skip" : "glass.layout.queue.unannounced";
            return same;
        }

        public void Queue()
        {
            // ED-356 (the first frame): until a piece has synchronised once, its window's renderer step (WindowHook) synchronises it right
            // after the renderer's own update, in the composition update that draws the window's first frame: the Loaded post below lands a frame
            // or more later (the knob-cache unit measured the glass 34 to 53 ms after a window was shown: pop-in).
            if (!synchronizedOnce && TopLevel.GetTopLevel(owner) is { } top) HookOf(top).Pending.Add(this);
            if (queued) return;
            queued = true;
            InteractionProbe.Count("glass.queue");
            Dispatcher.UIThread.Post(posted, DispatcherPriority.Loaded);
        }

        private bool synchronizedOnce;
        // ED-NEW-stutterfix-1: the name a piece is counted under when it is attached (InteractionProbe on only): its type and its first ancestors'.
        private static string ProbeName(Control control)
        {
            var name = new StringBuilder("glass.attached.");
            Visual? visual = control;
            for (var i = 0; i < 4 && visual is not null; i++, visual = visual.GetVisualParent()) name.Append(visual.GetType().Name).Append('<');
            return name.ToString();
        }

        // Run by the window's renderer step (WindowHook.Repair), after the renderer's update and before the frame is committed.
        internal void SynchronizeFirst(WindowHook hook)
        {
            if (synchronizedOnce || !owner.IsAttachedToVisualTree()) return;
            Synchronize();
            if (!synchronizedOnce) hook.Later.Add(this); // its visuals are made by a later update: tried at the next step
            TraceForTests?.Invoke(synchronizedOnce ? "first synchronised in the renderer step" : "first synchronisation waits: no composition visual yet");
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
            using var probe = InteractionProbe.Begin("glass.sync");
            SynchronizedForTests?.Invoke(owner);
            requested = false;
            var spacing = SwiftGlass.GetContainerSpacing(owner);
            var ownEffect = SwiftGlass.GetEffect(owner);
            alone = null;
            BindInput(ownEffect?.IsInteractive == true);
            if (spacing is null && ContainerOf(owner) is { } group)
            {
                RemoveMaterial();
                synchronizedOnce = true; // the container draws it
                InteractionProbe.Count("glass.queue.by-piece");
                AttachmentFor(group).Queue();
                return;
            }
            var visual = ElementComposition.GetElementVisual(owner);
            var nextParent = owner.GetVisualParent() is { } p ? ElementComposition.GetElementVisual(p) as CompositionContainerVisual : null;
            if ((spacing is null && ownEffect is null) || visual is null || nextParent is null || SwiftGlass.GetUsesDesktopBackdrop(owner))
            {
                // Settled when there is nothing to draw; a piece whose composition visuals are not made yet is tried again at the next step.
                synchronizedOnce = visual is not null && nextParent is not null;
                RemoveMaterial();
                return;
            }
            Handler? fresh = null;
            if (parent != nextParent)
            {
                RemoveMaterial();
                parent = nextParent;
                material = visual.Compositor.CreateCustomVisual(fresh = new Handler());
                // A custom visual clips to its Size unless told not to, and that clip also cut its render bounds back to the owner's: Avalonia then
                // redrew only the owner's box when the material appeared or changed, not the margin it reads (SampledMargin, Backdrop).
                material.ClipToBounds = false;
            }
            var index = parent.Children.IndexOf(visual);
            if (index < 0 || material is null) return;
            synchronizedOnce = true;
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
                alone = Unannounced();
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
                // ED-356 (the first frame): a material made in this update holds its state before the batch that makes it is committed, so
                // the frame that first draws it already has its bounds and its regions (a message is taken a frame later for a new visual).
                fresh?.Prime(state);
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
            // ED-NEW-glasslook-3: a glass inside another glass (a control on the composer) takes the Inner surface's look; a quiet look draws nothing
            // OD-0147: a surface set beside the effect (SwiftGlass.SurfaceProperty) when the effect names none
            var look = GlassLooks.Of(e.Surface is null && SwiftGlass.GetSurface(control) is { } beside ? e.OnSurface(beside) : e, Nested(control));
            // OD-0147: a piece that casts its own shadow draws none of the canvas's past its edge
            if (look.Canvas is { } canvas && SwiftGlass.GetCastsOwnShadow(control)) look = GlassLook.Of(canvas.Kind, canvas.Tuning with { Shadow = 0 });
            if (look.Quiet) return null;
            var a = AttachmentFor(control);
            var (darkness, tint) = a.Appearance(control, this, e, resolved);
            return new Region(control.Bounds.Size, e.Shape, corners, tint, drawnTint, darkness,
                e.IsInteractive && a.hovered, e.IsInteractive && a.pressed, a.pointer, toOwner, opacity, e.NoRefraction, e.NoContactLine, look,
                e.IsInteractive ? a.hoverShown : 0, e.IsInteractive ? a.pressShown : 0, e.OwnBackdrop);
        }

        // Whether a glass piece lies inside another glass piece (a visual ancestor with an effect of its own).
        private static bool Nested(Control control) => SwiftGlass.IsInsideGlass(control);

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
        private void Enter(object? sender, PointerEventArgs e) { hovered = true; pointer = e.GetPosition(owner); Queue(); Ease(); }
        private void Move(object? sender, PointerEventArgs e) { pointer = e.GetPosition(owner); Queue(); }
        private void Exit(object? sender, PointerEventArgs e) { hovered = false; pressed = false; Queue(); Ease(); }
        private void Press(object? sender, PointerPressedEventArgs e) { pressed = e.GetCurrentPoint(input).Properties.IsLeftButtonPressed; pointer = e.GetPosition(owner); Queue(); Ease(); }
        private void Release(object? sender, PointerReleasedEventArgs e) { pressed = false; Queue(); Ease(); }
        private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) { pressed = false; Queue(); Ease(); }

        // OD-0147: the approved material's hover and press glide in and out (the canvas's springs: about 0.1 s for the hover and 0.06 s for the
        // press, stiffer under reduced motion), one display frame at a time on the frame clock (ED-117: frames are the only wake-ups, spans are
        // measured on SwiftClock); the swell and the glow follow them, so nothing snaps. With no frame source bound (a test that drives none) they
        // are taken at once.
        internal double hoverShown, pressShown;
        private ContinuousInstant easedAt;
        private IFrameSource? easeFrames;
        private void Ease()
        {
            if (easeFrames is not null) return;
            if (!FrameClock.IsBound || TopLevel.GetTopLevel(owner) is null)
            {
                (hoverShown, pressShown) = (hovered ? 1 : 0, pressed ? 1 : 0);
                return;
            }
            easedAt = SwiftClock.Now;
            easeFrames = FrameClock.Source.Current;
            easeFrames.RequestFrame(Eased);
        }
        private void Eased(TimeSpan _)
        {
            var now = SwiftClock.Now;
            var dt = Math.Clamp((now - easedAt).TotalSeconds, 0, 0.1);
            easedAt = now;
            var reduced = GlassLooks.Shown.Motion < 0.5;
            static double Toward(double shown, double target, double dt, double tau)
            {
                var next = target + (shown - target) * Math.Exp(-dt / tau);
                return Math.Abs(next - target) < 0.004 ? target : next;
            }
            var (hoverTo, pressTo) = (hovered ? 1.0 : 0.0, pressed ? 1.0 : 0.0);
            hoverShown = Toward(hoverShown, hoverTo, dt, reduced ? 0.03 : 0.1);
            pressShown = Toward(pressShown, pressTo, dt, reduced ? 0.02 : 0.06);
            Queue();
            var frames = easeFrames;
            if (hoverShown == hoverTo && pressShown == pressTo || frames is null || TopLevel.GetTopLevel(owner) is null) { easeFrames = null; return; }
            frames.RequestFrame(Eased);
        }
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
            hoverShown = pressShown = 0;
            easeFrames?.CancelFrame(Eased);
            easeFrames = null;
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
        /// <summary>Pieces not yet synchronised (Attachment.Queue): synchronised here first, so a window's first frame already has its glass. A piece in
        /// a container queues the container, which is taken in the same step (a few rounds at most: a container of containers is not made).</summary>
        public readonly HashSet<Attachment> Pending = [];
        public readonly List<Attachment> Later = [];
        private readonly List<Attachment> pass = [];
        /// <summary>Whether the step runs after the window's renderer update (Install found the member it names).</summary>
        public bool Installed;
        public void Repair()
        {
            if (Pending.Count > 0) TraceForTests?.Invoke($"renderer step with {Pending.Count} pending");
            for (var round = 0; round < 4 && Pending.Count > 0; round++)
            {
                pass.AddRange(Pending);
                Pending.Clear();
                foreach (var piece in pass) piece.SynchronizeFirst(this);
                pass.Clear();
            }
            foreach (var piece in Later) Pending.Add(piece);
            Later.Clear();
            if (Materials.Count == 0) return;
            pass.AddRange(Materials);
            foreach (var material in pass) material.Reinsert();
            pass.Clear();
        }
    }
    private static readonly ConditionalWeakTable<TopLevel, WindowHook> windows = new();

    /// <summary>ED-NEW-glasslook-2: the user's tuning changed (GlassLooks.Tuning): every piece with a material in every window synchronises again, so its
    /// regions carry the new look's uniforms (no layout pass, no new program).</summary>
    internal static void LookChanged()
    {
        var hooks = new List<WindowHook>();
        foreach (var (_, hook) in windows) hooks.Add(hook);
        // only a running UI has hooked windows: with none, nothing touches the dispatcher (a settings object made off the UI, as in a test)
        if (hooks.Count > 0)
        {
            void Queue()
            {
                foreach (var hook in hooks)
                    foreach (var material in hook.Materials.ToArray()) material.Queue();
            }
            if (Dispatcher.UIThread.CheckAccess()) Queue();
            else Dispatcher.UIThread.Post(Queue);
        }
        LookChangedForTests?.Invoke();
    }

    /// <summary>Called after a look change has queued every piece (tests).</summary>
    internal static Action? LookChangedForTests;
    private static readonly ConditionalWeakTable<Attachment, TopLevel> hooked = new();

    private static WindowHook HookOf(TopLevel top)
    {
        if (windows.TryGetValue(top, out var hook)) return hook;
        hook = new WindowHook();
        windows.Add(top, hook);
        Install(top, hook);
        return hook;
    }

    private static void Hook(Attachment attachment)
    {
        if (hooked.TryGetValue(attachment, out _) || TopLevel.GetTopLevel(attachment.Owner) is not { } top) return;
        HookOf(top).Materials.Add(attachment);
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
            update.SetValue(renderer, new Action(() => { TraceForTests?.Invoke("renderer update"); original(); hook.Repair(); }));
            hook.Installed = true;
            // An update already queued holds the old delegate: one more step, after it, in the next composition update.
            if (ElementComposition.GetElementVisual(top)?.Compositor is { } compositor) compositor.RequestCompositionUpdate(() => { TraceForTests?.Invoke("extra step"); hook.Repair(); });
            TraceForTests?.Invoke("hook installed");
        }
        catch (Exception e) when (e is ArgumentException or MemberAccessException or TargetInvocationException or InvalidCastException or FieldAccessException) { }
    }

    private sealed record Region(Size Size, GlassShape Shape, CornerRadius Corners, Color Tint, bool DrawnTint,
        double Darkness, bool Hovered, bool Pressed, Point Pointer, Matrix Transform, double Opacity, bool NoRefraction = false, bool NoContactLine = false,
        GlassLook? Look = null, double HoverAmount = 0, double PressAmount = 0, bool OwnBackdrop = false)
    {
        /// <summary>ED-NEW-glasslook-1: the look it is drawn with (GlassLooks), the release's when none is named.</summary>
        public GlassLook Drawn => Look ?? GlassLook.Release;
        /// <summary>Its refraction, by its size and look (OD-0147: the approved material's bend, fringe and depth: past its deep the shader reads one texel).</summary>
        public Optics Lens => Drawn.Canvas is { } canvas ? CanvasOptics(canvas, Smaller) : Optics.For(Size, Drawn);
        /// <summary>Its smaller side in the owner's units.</summary>
        public double Smaller => Math.Min(Size.Width, Size.Height) * Math.Sqrt(Math.Abs(Transform.M11 * Transform.M22 - Transform.M12 * Transform.M21));
        /// <summary>ED-NEW-glasslook-3: how far its look's soft shadow reaches past its edge (its blur and its drop), 0 with none.</summary>
        public double DepthReach => Drawn.Canvas is { } canvas ? canvas.Reach(Smaller)
            : Drawn.DepthValue > 0 ? GlassLook.DepthRadius(Smaller) + GlassLook.DepthOffset(Smaller) : 0;
    }
    private sealed record State(Region[] Regions, double Spacing);

    private sealed class Handler : CompositionCustomVisualHandler
    {
        private State? state;
        private readonly Backdrop backdrop = new();
        // Called on the UI thread for a handler whose visual is made in the same update, before that batch is committed (the render thread has not
        // seen the handler yet).
        public void Prime(State first) => state = first;
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
            using var probe = InteractionProbe.Begin("glass.draw");
            using var lease = feature.Lease();
            DrawsForTests++;
            if (DrawsForTests == 1) TraceForTests?.Invoke("first draw (render thread)");
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

    // ED-NEW-stutterfix-1 (InteractionProbe, diagnostics only; render thread): give the window a handler draws in the reach that reports its frames,
    // as a material's first draw does, and name that window's tracker.
    internal static void ObserveFrames(CompositionCustomVisualHandler handler)
    {
        if (Frame.Target(handler) is { } target) Frame.ReachOf(target);
    }

    internal static object? TrackerOf(CompositionCustomVisualHandler handler) =>
        Frame.Target(handler) is { } target ? Frame.ReachOf(target)?.Tracker : null;

    // Test-only (InternalsVisibleTo), ED-356's frame cost: while set, each draw waits for the GPU's work before and after itself and reports its
    // first piece's size and how long the draw took, the GPU's part included. Called on the render thread.
    internal static volatile Action<Size, TimeSpan>? FrameCostForTests;

    // Test-only (InternalsVisibleTo): the first-frame steps (a piece's first synchronisation, the first draw), for the native first-frame test.
    internal static volatile Action<string>? TraceForTests;
    /// <summary>Called with a piece's owner each time it synchronises (tests).</summary>
    internal static Action<Control>? SynchronizedForTests;

    // Sent before a material leaves its parent: its handler gives up the backdrop it keeps (disposed at the next render, where the GPU context is current).
    private sealed class Released { public static readonly Released Message = new(); }

    // How far past its shape the material reads the pixels under it: the blur's 3 sigma (36), the refraction's largest displacement (Optics.MaxBend plus
    // Optics.MaxRing, the red channel's at the edge: 50; the unlit band reads 72 inward) and the antialiased edge (Draw).
    // It is also the material's render bounds: whenever the material appears, moves or changes, Avalonia redraws all it reads, so the backdrop it keeps is
    // taken whole there (Backdrop; GlassMaterialPartialRedrawTests, the label beside a piece that appears).
    private const double UnlitBandSigmas = 6, MaxReadDisplacement = UnlitBandSigmas * 12;
    private const double SampledMargin = 3 * 12 + MaxReadDisplacement + 2;

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

    // OD-0108 (ED-NEW-fix-knobcache, on unit/fix-knobcache: GlassWarmup, GpuWarmup's "glass"): the programs the material draws with, for the GPU
    // warm-up, made by the material's own builders, so a change to the shader is warmed as it is drawn. The same API as that unit's, for its merge.
    // ED-356: one program per shape count (a container merges its pieces in one smoothUnion program), 1 to WarmShapes; the inside of a lone
    // piece is the same program (its `interior` uniform), and so are both draws of a pixel (the `stage` uniform: DstOut, then Plus), so there are no
    // separate inside programs to warm any more. A warm-up that mirrors the draws must draw each program twice, with BlendMode.DstOut and with
    // BlendMode.Plus, and the blur as a Clamp image shader under the blur filter (Draw).
    internal const int WarmShapes = 8;

    internal static IReadOnlyList<SKRuntimeEffect> WarmupPrograms() => [.. Enumerable.Range(1, WarmShapes).Select(Effect)];

    /// <summary>The program for <paramref name="count"/> shapes (the warm-up's), made as a draw makes it.</summary>
    internal static SKRuntimeEffect WarmupProgram(int count) => Effect(count);

    /// <summary>Raised the first time a draw asks for the program of a shape count in this process (render thread; the warm-up records it).</summary>
    internal static event Action<int>? ShapeCountDrawn;
    private static readonly ConcurrentDictionary<int, bool> drawnCounts = new();

    /// <summary>The backdrop blur's sigma (device px) for a piece whose smaller side is <paramref name="smaller"/> pt, at <paramref name="scale"/>:
    /// 12 pt on a panel, 15 % of the smaller side on a small lens (Draw).</summary>
    internal static float BlurSigma(double smaller, float scale) => (float)Math.Min(12, Math.Max(1, smaller * 0.15)) * scale;

    /// <summary>ED-NEW-glasslook-2 (tests): how many programs the material has built in this process (a look change builds none).</summary>
    internal static int ProgramsBuiltForTests;

    private static SKRuntimeEffect Effect(int count) => effects.GetOrAdd(count, static n => new Lazy<SKRuntimeEffect>(() =>
    {
        Interlocked.Increment(ref ProgramsBuiltForTests);
        var source = new StringBuilder(ShaderHeader).Append("\nuniform shader sharp;\nuniform float stage;\nuniform float interior;\nuniform float4 window;\n");
        for (var i = 0; i < n; i++) source.Append(CultureInfo.InvariantCulture, $$"""

            uniform float4 transform{{i}};
            uniform float2 translate{{i}};
            uniform float2 size{{i}};
            uniform float4 corners{{i}};
            uniform float4 tint{{i}};
            uniform float4 film{{i}};
            uniform float4 law{{i}};
            uniform float4 optics{{i}};
            uniform float4 flags{{i}};
            uniform float4 pointer{{i}};
            uniform float4 look{{i}};
            uniform float4 finish{{i}};
            uniform float4 aura{{i}};
            uniform float plain{{i}};
            uniform float bare{{i}};
            uniform float4 cvA{{i}};
            uniform float4 cvB{{i}};
            uniform float4 cvC{{i}};
            uniform float4 cvD{{i}};
            uniform float4 cvE{{i}};
            uniform float4 cvF{{i}};
            uniform float4 cvG{{i}};
            uniform float4 cvH{{i}};
            uniform float4 cvI{{i}};
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
        source.Append(BodySource).Append(CanvasSource);
        source.Append("half4 main(float2 xy) { float4 tint = tint0; float4 film = film0; float4 law = law0; float4 ptr = pointer0; float2 at = local0(xy); float4 flags = flags0; float2 extent = size0; float4 optics = optics0; float4 look = look0; float4 finish = finish0; float4 aura = aura0; float plain = plain0; float bare = bare0; float4 cvA = cvA0; float4 cvB = cvB0; float4 cvC = cvC0; float4 cvD = cvD0; float4 cvE = cvE0; float4 cvF = cvF0; float4 cvG = cvG0; float4 cvH = cvH0; float4 cvI = cvI0;\n");
        // ED-320 16: the inside of a lone rounded rectangle (Draw's Inside) is drawn with `interior` set: what the shader computes past Optics.Deep,
        // without the shape's distance. It is the same program and the same arithmetic as the whole shader's deep branch below, so the two are
        // byte-identical (GlassMaterialReachReviewTests). A container's union never has one (Inside is null): its pointer is the nearest piece's.
        if (n == 1) source.Append("""
            float2 delta = at - ptr.xy;
            float highlight = exp(-dot(delta, delta) / (2.0 * 48.0 * 48.0)) * ptr.z;
            if (interior > 0.5) return cvA.x > 0.5 ? cvInterior(xy, at, extent, ptr.xy, flags.w, tint, ptr.w < 0.5, cvB, cvC, cvD, cvF, cvG, cvH, cvI)
                : inside(backdrop.eval(xy), at, extent, film, law, tint, ptr.w < 0.5, highlight, flags.w, aura.x);
            float raw = distance0(xy);

            """);
        else source.Append("float raw = distance0(xy);\n");
        for (var i = 1; i < n; i++) source.Append(CultureInfo.InvariantCulture, $"if (distance{i}(xy) < raw) {{ raw = distance{i}(xy); tint = tint{i}; film = film{i}; law = law{i}; ptr = pointer{i}; at = local{i}(xy); flags = flags{i}; extent = size{i}; optics = optics{i}; look = look{i}; finish = finish{i}; aura = aura{i}; plain = plain{i}; bare = bare{i}; cvA = cvA{i}; cvB = cvB{i}; cvC = cvC{i}; cvD = cvD{i}; cvE = cvE{i}; cvF = cvF{i}; cvG = cvG{i}; cvH = cvH{i}; cvI = cvI{i}; }}\n");
        if (n > 1) source.Append("float2 delta = at - ptr.xy;\nfloat highlight = exp(-dot(delta, delta) / (2.0 * 48.0 * 48.0)) * ptr.z;\n");
        // With one shape the scene's distance is the shape's own (no union): it is not evaluated twice.
        source.Append(n == 1 ? "float d = -raw;\n" : "float d = -sceneDistance(xy);\n");
        // OD-0147: a piece in the approved material draws it, and nothing below.
        source.Append("if (cvA.x > 0.5) return cvGlass(xy, d, at, extent, ptr.xy, flags.w, tint, ptr.w < 0.5 || raw > 0.0, cvA, cvB, cvC, cvD, cvE, cvF, cvG, cvH, cvI);\n");
        source.Append("""
            // ED-NEW-glasslook-3: the look's depth (aura.y; 0 in the release look): a soft, wide, faint shadow under the glass, the shape blurred over
            // aura.z and dropped aura.w, darkening what lies just outside it (the glass itself covers the rest).
            float soft = 0.0;
            if (aura.y > 0.0 && d < 0.5) {
                float dropped = -SHADOWDISTANCE(xy - float2(0.0, aura.w));
                soft = aura.y * smoothstep(-aura.z, aura.z, dropped);
            }
            // Past the contact line (and the soft shadow) outside the shape the material leaves the pixel as it is (both stages add nothing).
            if (d <= -1.25) {
                if (soft <= 0.0) return half4(0.0);
                return half4(0.0, 0.0, 0.0, half(flags.w * soft));
            }
            float coverage = clamp(d + 0.5, 0.0, 1.0);
            // ED-320 16 / ED-356: past the bend's support, the ring's, the inner light's and the rim's (Optics.Deep in C#) the
            // lens bends nothing and lights nothing: the inside of a shape reads one texel and skips the normal's four distances.
            float deep = max(max(optics.z + LG_BENDSUPPORT * optics.y, LG_GLOWREACH), LG_RINGREACH);
            float2 n = float2(0.0, 0.0);
            float lit = 1.0;
            if (d < deep) {
                // P1 finite-difference inward normal follows curved edges and the merged neck.
                float2 normal = float2(-sceneDistance(xy + float2(1.0, 0.0)) + sceneDistance(xy - float2(1.0, 0.0)),
                                      -sceneDistance(xy + float2(0.0, 1.0)) + sceneDistance(xy - float2(0.0, 1.0)));
                n = normalize(normal + float2(0.000001));
                // ED-358's law (item 4): the edge's light (rim, inner light, contact line) and its bend follow how light the backdrop is
                // 24 pt in from the edge (0.6 of a smaller piece's smaller side), past where the card's scrim shadow thins: the Mac's large dark card (27 under its scrim and shadow) has none, the composer pill (57) and the title
                // capsules (52) have it whole. A transparent backdrop (the Acrylic) counts as a mid grey.
                half4 deepRead = backdrop.eval(clamp(xy + n * min(24.0, 0.6 * min(extent.x, extent.y)), window.xy, window.zw));
                half4 deepColour = unpremultiply(deepRead);
                float level = float(dot(deepColour.rgb, half3(0.2126, 0.7152, 0.0722)) * deepRead.a + (1.0 - deepRead.a) * 0.25);
                // Small surfaces retain the rim on a dark backdrop too; the panel law takes over between 60 and 120 pt
                // on the smaller side (OD-0107, T2's small-surface ruling; GlassMaterialPanelLawTests).
                lit = mix(1.0, smoothstep(LG_LITFROM, LG_LITTO, level), law.w);
                if (lit < 1.0) {
                    // The deep inward band needs a direction that varies smoothly through a rounded corner's medial axis.
                    // Average its distance gradient over one blur sigma; the lit lens keeps its one-pixel edge normal.
                    float span = clamp(0.15 * min(extent.x, extent.y), 1.0, 12.0);
                    float2 broad = float2(-sceneDistance(xy + float2(span, 0.0)) + sceneDistance(xy - float2(span, 0.0)),
                                          -sceneDistance(xy + float2(0.0, span)) + sceneDistance(xy - float2(0.0, span)));
                    n = mix(n, normalize(broad + float2(0.000001)), 1.0 - lit);
                }
            }
            // The contact line just outside the edge, dark where the edge faces sideways (the Mac frames' 1 px dark line at a capsule's
            // ends), and dashersw/liquid-glass-js's soft shadow in miniature.
            float shade = d < 0.25 ? clamp(1.0 - abs(d + 0.5) / 0.75, 0.0, 1.0) * (LG_SHADEBASE + LG_SHADESIDE * n.x * n.x) * (LG_SHADEDARK + (1.0 - LG_SHADEDARK) * lit) : 0.0;
            // OD-0123: a piece that asks for no contact line (the voice pill) draws none; the multiplication by 1.0 leaves every other piece's pixels as they were.
            shade *= 1.0 - bare;
            // ED-NEW-glasslook-1: the look's shadow depth (look.w; 1 in the release look).
            shade = min(shade * look.w, 1.0);
            if (soft > 0.0) shade = 1.0 - (1.0 - shade) * (1.0 - soft);
            // What the pixel becomes is mix(under, value, amount): stage 0 (BlendMode.DstOut) takes `amount` of the pixel under away,
            // stage 1 (BlendMode.Plus) adds value * amount. The glass is translucent: its value keeps the alpha of what lies under it.
            float amount = flags.w * (1.0 - (1.0 - coverage) * (1.0 - shade));
            if (stage < 0.5) return half4(0.0, 0.0, 0.0, half(amount));
            half4 outside = half4(0.0, 0.0, 0.0, half(flags.w * (1.0 - coverage) * shade));
            if (coverage <= 0.0) return outside;
            // Avoid double tinting existing overlays; tint the merged neck outside those overlays.
            bool tinted = ptr.w < 0.5 || raw > 0.0;
            if (d >= deep) return inside(backdrop.eval(xy), at, extent, film, law, tint, tinted, highlight, flags.w, aura.x);
            // rizzytoday/liquid-glass's displacement map (buildDisplacementMap): a full bend at the edge that falls off as the
            // complement of the blurred neutral centre (a Gaussian edge, here its logistic form), with the per-channel scales of its
            // chromatic aberration (scale -180 + 0 / 10 / 20: red 1, green 0.944, blue 0.889); plus dashersw/liquid-glass-js's rim
            // ring (rimIntensity * exp(-rimDistance * d), rimDistance 0.8). Sampled outward along the inward normal.
            float dd = max(d, 0.0);
            float lens = (1.0 + exp(-LG_LOGISTIC * optics.z / optics.y)) / (1.0 + exp(LG_LOGISTIC * (dd - optics.z) / optics.y));
            lens = max(lens - LG_BENDFLOOR, 0.0) / (1.0 - LG_BENDFLOOR);
            float ring = optics.w * max(exp(-0.8 * dd) - LG_RINGFLOOR, 0.0) / (1.0 - LG_RINGFLOOR);
            // ED-NEW-glasslook-3: the look's profile (finish.x; 0 in the release look) turns the bezel into a convex squircle lens (kube.io's
            // liquid glass and rizzytoday's convex bezel): a surface of height (1 - (1 - s)^4)^(1/4) across the bezel, s from 0 at the edge to 1 where
            // it meets the flat centre, refracting by Snell's law (n = LG_IOR); its bend, normalised to 1 at the edge, falls smoothly to 0 with no
            // slope where the centre begins, so the centre is undistorted. The ring is the logistic profile's alone.
            if (finish.x > 0.0) {
                float s = clamp(dd / max(optics.z + LG_BENDSUPPORT * optics.y, 1.0), 0.0, 1.0);
                float q = 1.0 - s;
                float q4 = q * q * q * q;
                float slope = q * q * q / pow(max(1.0 - q4, 0.000001), 0.75);
                float theta = atan(slope);
                float squircle = tan(theta - asin(sin(theta) / LG_IOR)) / LG_SQUIRCLEEDGE;
                lens = mix(lens, squircle, finish.x);
                ring *= 1.0 - finish.x;
            }
            float disp = (optics.x * lens + ring) * lit;
            // The reads stay inside the window's surface (`window`, half a device pixel in): past it the backdrop holds nothing, so a piece that fills its
            // window read transparent at its edge and its outer ring came out translucent over an opaque backdrop (REVIEW_GlassWindowEdgeTests).
            // An unlit (dark, large) piece reads its band 6 blur sigmas in. Three removed the outside backdrop's bleed, but still read the
            // thin part of g3's real radius-28 cast shadow (native Mac calibration: band 48.10 against 43.99 +/- 3). Six reaches the body's
            // darker coverage while the small, lit surfaces keep their refraction (ED-359; Draw's BlurSigma).
            float reach = LG_BANDSIGMAS * clamp(0.15 * min(extent.x, extent.y), 1.0, 12.0);
            // Meet the unbent fast body with zero displacement and zero slope; an abrupt end leaves a rectangular seam.
            disp -= (1.0 - lit) * max(reach - dd, 0.0) * (1.0 - smoothstep(0.0, deep, dd));
            // OD-0121: a piece that asks for no refraction (the voice pill) bends nothing; the multiplication by 1.0 leaves every other piece's pixels as they were.
            disp *= 1.0 - plain;
            float2 readR = clamp(xy - n * disp, window.xy, window.zw);
            // The fringe: green and blue bend rizzytoday's 170 / 180 and 160 / 180 of red, their split times the look's (look.y).
            float dispG = disp * (1.0 - (1.0 - LG_GREEN) * look.y);
            float dispB = disp * (1.0 - (1.0 - LG_BLUE) * look.y);
            // ED-NEW-glasslook-3: the look's fringe cap (finish.w pt, red to blue; 0 in the release look: uncapped): a hint of colour, never a rainbow.
            if (finish.w > 0.0) {
                dispG = disp >= 0.0 ? max(dispG, disp - 0.5 * finish.w) : min(dispG, disp + 0.5 * finish.w);
                dispB = disp >= 0.0 ? max(dispB, disp - finish.w) : min(dispB, disp + finish.w);
            }
            float2 readG = clamp(xy - n * dispG, window.xy, window.zw);
            float2 readB = clamp(xy - n * dispB, window.xy, window.zw);
            half4 r = backdrop.eval(readR);
            half4 g = backdrop.eval(readG);
            half4 b = backdrop.eval(readB);
            // ED-NEW-glasslook-1: the lens rim can be clear (the look's clarity, look.x; 0 in the release look). rizzytoday/liquid-glass displaces the backdrop itself (its SVG filter has no blur), so
            // what lies past the edge is seen bent into the rim band, sharp, with its red, green and blue apart; read through the frost the
            // same bend was a smear (the 12 pt blur hid a 30 pt bend). In the band the reads take the unblurred pixels under the glass
            // (`sharp`), most of them at the edge and fading with the bend's own profile into the frosted body, so text on the body is
            // unchanged. An unlit (dark, large) piece and one without refraction stay frosted (the Mac head card; the voice pill).
            // A small piece bends little (the Mac's small pieces read little from past their edge): a clear rim with no bend would only show
            // what lies under the edge, unglassed, so the clarity follows the bend, none under LG_CLEARFROM pt and whole from LG_CLEARTO.
            // A read the window's edge clamps would replicate the edge's pixel into a streak: it stays frosted.
            float2 rawR = xy - n * disp;
            float clamped = length(rawR - readR);
            float clear = look.x * lens * lit * (1.0 - plain) * smoothstep(LG_CLEARFROM, LG_CLEARTO, optics.x) * clamp(1.0 - clamped * 0.5, 0.0, 1.0);
            float gain = law.x;
            if (clear > 0.004) {
                r = mix(r, sharp.eval(readR), half(clear));
                g = mix(g, sharp.eval(readG), half(clear));
                b = mix(b, sharp.eval(readB), half(clear));
            }
            // Each read is lifted on its own, so the fringe is the bend's (not amplified by the saturation); deep inside the three are one.
            half4 col = brighten(filmed(half3(lift(r, film.w, gain).r, lift(g, film.w, gain).g, lift(b, film.w, gain).b), g.a, at, extent, film, law), aura.x);
            if (tinted) col = over(col, half3(tint.rgb), half(tint.a));
            // The specular rim: a bright line on the edge's first point, all around and strongest where the edge faces up or down,
            // the top brightest (the Mac frames: +33 to +41 levels on the composer pill and the title capsules); the light inner edge
            // under it.
            float rim = clamp(1.5 - d, 0.0, 1.0);
            float facing = n.y * n.y;
            float spec = lit * rim * (LG_RIMBASE + (1.0 - LG_RIMBASE) * facing) * (n.y > 0.0 ? LG_RIMTOP : LG_RIMBOTTOM);
            // ED-NEW-glasslook-3: the look's hairline (finish.z; 0 in the release look): the rim becomes a specular hairline lit from the top left,
            // brightest where the edge faces the light, fading around the shape, with a faint counter-highlight where it faces away.
            if (finish.z > 0.0) {
                float toward = dot(-n, LG_LIGHT);
                float hair = lit * rim * (LG_HAIRBASE + LG_HAIRMAIN * pow(max(toward, 0.0), 2.0) + LG_HAIRCOUNTER * pow(max(-toward, 0.0), 2.0));
                spec = mix(spec, hair, finish.z);
            }
            // The light inner edge, times the look's glow (finish.y; 1 in the release look, 0 for no inner bevel).
            float glow = d < LG_GLOWREACH ? lit * LG_GLOW * pow(clamp(1.0 - (d - 1.0) / (LG_GLOWREACH - 1.0), 0.0, 1.0), 3.0) * (LG_GLOWSIDE + (1.0 - LG_GLOWSIDE) * facing) * (1.0 - rim) * finish.y : 0.0;
            col = over(col, half3(1.0), half(clamp((spec + glow) * look.z + highlight, 0.0, 1.0)));
            return col * half(coverage * flags.w) + outside;
            }
            """.Replace("SHADOWDISTANCE", n == 1 ? "distance0" : "sceneDistance", StringComparison.Ordinal));
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
        half3 lift(half4 under, float saturation, float gain) {
            half4 c = unpremultiply(under);
            half luma = dot(c.rgb, half3(0.2126, 0.7152, 0.0722));
            half3 s = clamp(luma + (c.rgb - luma) * half(saturation), 0.0, 1.0);
            // ED-358's law (item 4), the Mac's dark glass a gain on what lies under it: c + gain c (1 - c)^2.
            return s + half(gain) * s * (1.0 - s) * (1.0 - s);
        }
        half4 filmed(half3 lifted, half alpha, float2 at, float2 extent, float4 film, float4 law) {
            half ramp = half(smoothstep(LG_FILMBEGIN, LG_FILMEND, clamp(at.y / max(extent.y, 1.0), 0.0, 1.0)));
            half grey = half(mix(film.x, film.y, ramp));
            // The film covers what the backdrop leaves transparent at film.z (the Acrylic shows through it, lifted) and an opaque backdrop at law.y.
            half cover = half(film.z) * (1.0 - alpha) + half(law.y) * alpha;
            return over(half4(lifted * (1.0 - half(law.z) * ramp) * alpha, alpha), half3(grey), cover);
        }
        half4 body(half4 under, float2 at, float2 extent, float4 film, float4 law) { return filmed(lift(under, film.w, law.x), under.a, at, extent, film, law); }
        // ED-NEW-glasslook-3: the look's lift (aura.x; 0 in the release look): what the glass shows a little brighter, toward white, at its own alpha.
        half4 brighten(half4 col, float amount) { return amount > 0.0 ? half4(col.rgb + half(amount) * (col.a - col.rgb), col.a) : col; }
        // The inside of a shape, past Optics.Deep: the body, the tint, the pointer's highlight (the deep branch and the `interior` drawing).
        half4 inside(half4 texel, float2 at, float2 extent, float4 film, float4 law, float4 tint, bool tinted, float highlight, float amount, float brightness) {
            half4 col = brighten(body(texel, at, extent, film, law), brightness);
            if (tinted) col = over(col, half3(tint.rgb), half(tint.a));
            return over(col, half3(1.0), half(clamp(highlight, 0.0, 1.0))) * half(amount);
        }

        """;

    // OD-0147: the approved material (GlassCanvas), the design canvas's fragment shader, its Liquid glass branch (E:/Droppy/work/design/glass-ui,
    // FS_GLASS), per piece by cvA.x: 0 the knob looks above, 1 glass, 2 a flat control on glass (no glass on glass). The canvas's u_m rows are cvA to
    // cvI (GlassCanvasSpec). Its frost reads a mip spiral whose radius ramps from 1.2 px in the outer 4.5 px to the body's frost from 10 px in; here
    // the unblurred pixels (`sharp`) and the frost (`backdrop`) are mixed over the same ramp. What lies behind keeps its alpha: the body covers it
    // premultiplied, the tint film covers what is transparent (as the release film did, ED-356), and the lift, the pointer, the press and the line of
    // light lie over it as white.
    private const string CanvasSource = """
        float cvLuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
        float4 cvOver(float4 below, float3 colour, float a) { return float4(colour * a + below.rgb * (1.0 - a), a + below.a * (1.0 - a)); }
        float3 cvColour(half4 c) { return c.a > 0.002 ? float3(c.rgb) / float(c.a) : float3(0.0); }
        float cvTanh(float x) { float e = exp(2.0 * clamp(x, -8.0, 8.0)); return (e - 1.0) / (e + 1.0); }
        half4 cvRead(float2 at, float ramp) {
            float2 p = clamp(at, window.xy, window.zw);
            if (ramp >= 1.0) return backdrop.eval(p);
            if (ramp <= 0.0) return sharp.eval(p);
            return mix(sharp.eval(p), backdrop.eval(p), half(ramp));
        }
        // The body: compress toward one calm value (darks lift, only bright content comes down, mid-tones keep 85 %), saturate, the neutral tint
        // (takes colour out, not light), the tint colour, the white lift, the text scrim, the press, the pointer's glow and the darkened edge band.
        float4 cvBody(float3 c, float ba, float lb, float d, float2 at, float2 extent, float2 ptr, float4 cB, float4 cC, float4 cD, float4 cF, float4 cG, float4 cH, float4 cI) {
            float gy = clamp(at.y / max(extent.y, 1.0), 0.0, 1.0);
            float l = lb;
            float lt = cC.x + (l - cC.x) * (1.0 - cC.y);
            float fl = 0.85 - 0.4 * smoothstep(0.5, 0.95, l);
            c = lt < l ? c * max(lt / max(l, 0.0001), fl) : c + (lt - l);
            l = cvLuma(c);
            c = max(mix(float3(l), c, cC.z), 0.0);
            float ln = cvLuma(c);
            float film = mix(cB.z, cB.w, gy);
            c = mix(c, float3(mix(cB.y, ln, CV_LOCAL)), film);
            if (cH.w > 0.0) { float lc = cvLuma(c); c = mix(c, cH.rgb * (0.35 + 1.1 * lc), cH.w); }
            float4 P = float4(clamp(c, 0.0, 1.0) * ba, ba);
            P = cvOver(P, float3(cB.y), film * (1.0 - ba));
            P = cvOver(P, float3(1.0), cC.w);
            if (cG.y > 0.0) {
                float lc = cvLuma(P.a > 0.002 ? P.rgb / P.a : float3(0.0));
                float sc = cG.y * smoothstep(4.0, 10.0, d);
                P = float4(P.rgb * (1.0 - sc * smoothstep(0.3, 0.7, lc) * cF.w), P.a);
                P = cvOver(P, float3(1.0), sc * smoothstep(0.3, 0.7, 1.0 - lc) * (1.0 - cF.w));
            }
            P = cvOver(P, float3(1.0), 0.06 * cI.y);
            if (cF.y > 0.0 && cI.x > 0.001) { float2 q = at - ptr; P = cvOver(P, float3(1.0), cF.y * cI.x * exp(-dot(q, q) / (2.0 * cF.z * cF.z))); }
            float eb = 1.0 - clamp(d / max(cD.x, 0.5), 0.0, 1.0);
            return float4(P.rgb * (1.0 - cD.y * eb * eb), P.a);
        }
        half4 cvGlass(float2 xy, float d, float2 at, float2 extent, float2 ptr, float op, float4 tint, bool tinted,
                      float4 cA, float4 cB, float4 cC, float4 cD, float4 cE, float4 cF, float4 cG, float4 cH, float4 cI) {
            d += cI.z;
            float px = cG.w;
            float aa = 0.7 * px;
            float mask = smoothstep(-aa, aa, d);
            float a = mask * op;
            if (cA.x > 1.5) {
                float4 fill = cvOver(float4(cH.rgb * cH.a, cH.a), float3(1.0), 0.075 * cI.x + 0.05 * cI.y);
                if (stage < 0.5) return half4(0.0, 0.0, 0.0, half(fill.a * a));
                return half4(fill * a);
            }
            float sh = 0.0;
            if (d < aa) {
                if (cE.z > 0.0) { float s1 = sceneDistance(xy - float2(0.0, cE.x)) - cI.z; sh = cE.z * 0.5 * (1.0 - cvTanh(0.85 * s1 / max(cE.y, 0.5))); }
                if (cE.w > 0.0) { float s2 = sceneDistance(xy - float2(0.0, 1.0)) - cI.z; sh = 1.0 - (1.0 - sh) * (1.0 - cE.w * 0.5 * (1.0 - cvTanh(0.85 * s2 / 2.5))); }
                sh *= op;
            }
            if (stage < 0.5) return half4(0.0, 0.0, 0.0, half(a + (1.0 - mask) * sh));
            half4 outside = half4(0.0, 0.0, 0.0, half((1.0 - mask) * sh));
            if (mask <= 0.0) return outside;
            float dd = max(d, 0.0);
            float bez = max(cA.z, 1.0);
            float deep = max(max(CV_MAGNIFY * bez, CV_FULLAT), max(cD.x, 12.0));
            float ramp = smoothstep(CV_SHARPTO, CV_FULLAT, dd);
            float face = 0.0;
            float ndl = 0.0;
            float3 c = float3(0.0);
            float ba = 0.0;
            if (dd < deep) {
                // the outward normal; the bezel pulls in what lies just outside the rim (a convex arc, steep at the rim, zero past the bezel) and
                // magnifies about 1.1x at the rim, flat by 2.2 bezels in; the red and blue reads part along it by the fringe
                float2 g = float2(sceneDistance(xy + float2(0.5, 0.0)) - sceneDistance(xy - float2(0.5, 0.0)),
                                  sceneDistance(xy + float2(0.0, 0.5)) - sceneDistance(xy - float2(0.0, 0.5)));
                float2 n = dot(g, g) > 0.00000001 ? normalize(g) : float2(0.0, -1.0);
                ndl = dot(n, CV_LIGHT);
                face = max(ndl, 0.0);
                float t = clamp(dd / bez, 0.0, 1.0);
                float t1 = 1.0 - t;
                float k = 1.0 - sqrt(max(0.0, 1.0 - t1 * t1));
                float mg = max(cB.x, 1.0);
                float wm = bez * CV_MAGNIFY;
                float u = max(wm - dd, 0.0);
                float disp = cA.y * t1 * t1 - (mg - 1.0) / (2.0 * mg) * u * u / wm;
                float2 p = xy + n * disp;
                if (cA.w > 0.0 && abs(disp) > 1.0) {
                    float2 cs = n * sign(disp) * cA.w * 0.5 * k;
                    half4 rr = cvRead(p + cs, ramp);
                    half4 gg = cvRead(p, ramp);
                    half4 bb = cvRead(p - cs, ramp);
                    c = float3(cvColour(rr).r, cvColour(gg).g, cvColour(bb).b);
                    ba = float(gg.a);
                } else {
                    half4 r0 = cvRead(p, ramp);
                    c = cvColour(r0);
                    ba = float(r0.a);
                }
            } else {
                half4 r0 = cvRead(xy, ramp);
                c = cvColour(r0);
                ba = float(r0.a);
            }
            float lb = cvLuma(c);
            float4 P = cvBody(c, ba, lb, dd, at, extent, ptr, cB, cC, cD, cF, cG, cH, cI);
            if (tinted) P = cvOver(P, float3(tint.rgb), tint.a);
            if (dd < deep) {
                // the line of light, about one device pixel: a silver hairline on dark ground, the rim light where the edge faces the light, a faint
                // counter-light where it faces away, the one-corner glint (face^14), and the soft inner light on the lit side under it
                float lw = cG.z;
                float line = 1.0 - smoothstep(lw * 0.55, lw * 1.45, dd);
                float lit = pow(max(face, 0.000001), 1.6);
                float cnt = pow(max(-ndl, 0.0), 3.0);
                float dk = 1.0 - smoothstep(0.12, 0.45, lb);
                float glw = exp(-dd / 1.6) * (1.0 - line);
                float gl2 = pow(max(face, 0.000001), 14.0);
                float df = 1.0 - clamp(dd / 4.5, 0.0, 1.0);
                df *= df * (1.0 - line);
                float a1 = line * (cF.x * dk * (0.35 + 0.65 * face) + cD.z * lit + cD.w * cnt + cG.x * gl2) + (0.12 * cD.z * lit + 0.18 * cG.x * gl2) * glw + 0.16 * cD.z * lit * df;
                P = cvOver(P, float3(1.0), clamp(a1, 0.0, 1.0));
            }
            // dither: no banding in the soft gradients
            P = float4(P.rgb + (fract(sin(dot(floor(xy / px), float2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0 * P.a, P.a);
            return half4(P * a) + outside;
        }
        half4 cvInterior(float2 xy, float2 at, float2 extent, float2 ptr, float op, float4 tint, bool tinted,
                         float4 cB, float4 cC, float4 cD, float4 cF, float4 cG, float4 cH, float4 cI) {
            half4 r0 = backdrop.eval(xy);
            float3 c = cvColour(r0);
            float4 P = cvBody(c, float(r0.a), cvLuma(c), 1000.0, at, extent, ptr, cB, cC, cD, cF, cG, cH, cI);
            if (tinted) P = cvOver(P, float3(tint.rgb), tint.a);
            // the same dither as the whole drawing's, so the inside is byte for byte what the full shader draws there
            P = float4(P.rgb + (fract(sin(dot(floor(xy / cG.w), float2(12.9898, 78.233))) * 43758.5453) - 0.5) / 255.0 * P.a, P.a);
            return half4(P * op);
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
            .Replace("LG_GLOWSIDE", F(Optics.GlowSide), StringComparison.Ordinal)
            .Replace("LG_HAIRBASE", F(Optics.HairBase), StringComparison.Ordinal)
            .Replace("LG_HAIRMAIN", F(Optics.HairMain), StringComparison.Ordinal)
            .Replace("LG_HAIRCOUNTER", F(Optics.HairCounter), StringComparison.Ordinal)
            .Replace("LG_LIGHT", "float2(" + F(Optics.LightX) + ", " + F(Optics.LightY) + ")", StringComparison.Ordinal)
            .Replace("LG_IOR", F(Optics.Ior), StringComparison.Ordinal)
            .Replace("LG_SQUIRCLEEDGE", F(Optics.SquircleEdge), StringComparison.Ordinal)
            .Replace("LG_GLOW", F(Optics.Glow), StringComparison.Ordinal)
            .Replace("LG_FILMBEGIN", F(FilmBegin), StringComparison.Ordinal)
            .Replace("LG_FILMEND", F(FilmEnd), StringComparison.Ordinal)
            .Replace("LG_BANDSIGMAS", F(UnlitBandSigmas), StringComparison.Ordinal)
            .Replace("LG_LITFROM", F(LitFrom), StringComparison.Ordinal)
            .Replace("LG_LITTO", F(LitTo), StringComparison.Ordinal)
            .Replace("LG_SHADEDARK", F(Optics.ShadeDark), StringComparison.Ordinal)
            .Replace("LG_CLEARFROM", F(Optics.ClearFrom), StringComparison.Ordinal)
            .Replace("LG_CLEARTO", F(Optics.ClearTo), StringComparison.Ordinal)
            .Replace("CV_SHARPTO", F(GlassCanvas.SharpTo), StringComparison.Ordinal)
            .Replace("CV_FULLAT", F(GlassCanvas.FullAt), StringComparison.Ordinal)
            .Replace("CV_MAGNIFY", F(GlassCanvas.MagnifyWidth), StringComparison.Ordinal)
            .Replace("CV_LOCAL", F(GlassCanvas.Local), StringComparison.Ordinal)
            .Replace("CV_LIGHT", "float2(" + F(GlassCanvas.LightX) + ", " + F(GlassCanvas.LightY) + ")", StringComparison.Ordinal);
    }

    // The pixels under a material as they are when it draws: what a whole drawing of the window has drawn before it (ED-320 11). The surface
    // holds them only inside the region Avalonia redraws in this frame; outside it the surface is the last finished frame, the glass itself and what lies
    // over it, so a redraw of a control over the glass blurred the glass's own output into its frost (8 to 11 levels, GlassMaterialPartialRedrawReviewTests).
    // Outside that region nothing under the material has changed (whatever changes adds its bounds to the region), so the material keeps what it took the
    // last time and takes new pixels only inside the region. With no known region (another dirty-rect tracker, another surface) it takes them all, as before.
    // ED-320 16 (frame cost): the kept pixels live in one surface the material keeps, and only the region's pixels are copied out of the
    // window's surface (a whole snapshot of it, written to right after, made Skia copy the whole window every frame); the blur's own surface is kept too.
    // Both are made again when their size, the window's surface or the GPU context changes, and given up when the material leaves (Released).
    private sealed partial class Backdrop
    {
        private static readonly ConcurrentQueue<IDisposable> released = new();
        private SKSurface? kept, blurred;
        private SKRectI area;
        private IntPtr surface, context, blurredContext; // blurredContext: the GPU context the blur's own surface was made on
        private SKSizeI size, keptSize, blurredSize;
        private bool valid;

        /// <summary>The window surface's size at the last Take (device pixels).</summary>
        public SKSizeI Whole => size;

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
            return lease.GrContext is { } gpu ? SKSurface.Create(gpu, true, info) : SKSurface.Create(info); // ED-NEW-stutterfix-3: budgeted, so a released texture goes back to Skia's scratch pool for the next one
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

        // ED-NEW-stutterfix-1 (diagnostics only): the merged rect of a tracker the reach is bound to.
        public static SKRectI? Combined(object tracker)
        {
            try
            {
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
        private object? observed; // the tracker, kept for InteractionProbe's frame report even after the reach gives up

        internal void Bind(object dirtyRects, Action<object, SKRectI> add) => (tracker, addRect, observed) = (dirtyRects, add, dirtyRects);
        internal object? Tracker => observed;
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
                case "set_RenderedVisuals" when args is [int count]:
                    rendered = count;
                    // ED-NEW-stutterfix-1: set by Avalonia right after the frame is drawn, while its merged dirty rect is still the tracker's.
                    if (InteractionProbe.On && InteractionProbe.FrameDrawn is { } drawn && observed is { } frameTracker && Frame.Combined(frameTracker) is { } merged)
                        drawn(frameTracker, (merged.Left, merged.Top, merged.Right, merged.Bottom), count);
                    return null;
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
                    var add = spread.Adds(changed);
                    if (add.IsEmpty || changed.Contains(add)) continue;
                    grown.Add(material);
                    addRect(tracker, add);
                    queue.Add(add);
                }
            }
            pass.Clear();
        }
    }

    // ED-356: the film over the glass body (BodySource), between the light appearance's and the dark one's: darkness 0 is light, 1 is dark.
    // x and y are the grey at the top and at the bottom, z its opacity, w the saturation of the colour under it. The dark ends are
    // dashersw/liquid-glass-js's tint gradient (topTint 1.0, bottomTint 0.7, tintOpacity 0.2) fitted to the Mac composer pill over its backdrop
    // of about 57 (frames 01, 29, 30): 0.17 of a grey of 0.94 above the middle and 0.7 at the bottom gives 88 at the middle and 77 low down,
    // against 88 and 77 (dashersw's own 0.2 and 1.0 gave 89 and a top 6 levels too light); the disabled small glass button over (52,59,60)
    // comes out (82,90,91) against the reference's (75,84,85). The light ends are a whiter, stronger frost (no light-appearance frame was
    // measured). The saturation is the lift of the Mac glass's colour (outside G-B 10, inside 12 after the film).
    internal static float[] Film(double darkness, double tint = 1, double saturation = 1)
    {
        var t = Math.Clamp(darkness, 0, 1);
        static float Mix(double light, double dark, double t) => (float)(light + (dark - light) * t);
        // ED-NEW-glasslook-1: a look's Tint scales the film's opacity (z), held under 1, and its Saturation the lift of the colour under it (w).
        return [Mix(FilmLight[0], FilmDark[0], t), Mix(FilmLight[1], FilmDark[1], t), (float)Math.Min(0.95, Mix(FilmLight[2], FilmDark[2], t) * tint),
            (float)(Mix(FilmLight[3], FilmDark[3], t) * saturation)];
    }
    internal static readonly double[] FilmDark = [0.94, 0.70, 0.17, 1.4];
    internal static readonly double[] FilmLight = [1.0, 0.90, 0.30, 1.25];

    // ED-358's law (its item 4), measured by glass-g3 on the Mac frames: over an opaque backdrop the dark glass is a gain, body = 1.47 x
    // backdrop + 4 on the composer pill (88 over 57) and the head card (43.8 under its tint, over 27 under its scrim and shadow), not a white film,
    // which over-lit the dark card (57.3 against 43.4). Here c + 0.95 c (1 - c)^2 per channel (89.8 and 47.5 there; it leaves white white and
    // keeps the effort knob's peach, GlassSmallLensTests). The film stays for the part of the backdrop that is transparent (over the Acrylic) and,
    // in the light appearance, over everything (no light frame was measured). x the gain, y the film over an opaque backdrop, z the darkening at
    // the bottom of a small piece (the pill's 77 low down against 88 at its middle; the 267 pt card is flat, 43.4 and 42.7), w the panel share of the edge law.
    internal static float[] Law(double darkness, Size size, double tint = 1)
    {
        var t = Math.Clamp(darkness, 0, 1);
        var small = Math.Clamp((LawFlatFrom - size.Height) / (LawFlatFrom - LawShadedTo), 0, 1);
        var panel = Math.Clamp((Math.Min(size.Width, size.Height) - LawShadedTo) / (LawFlatFrom - LawShadedTo), 0, 1);
        // ED-NEW-glasslook-2: a look's Tint reaches the dark glass over an opaque backdrop too: below 1 the gain falls with it (at 0 the body is
        // the backdrop itself, clear glass); above 1 it stays the measured gain (a brighter dark body would be the milky slab the owner's
        // principles forbid and would lower white text's contrast), so a Tinted look's extra body shows in the film and the frost.
        return [(float)(LawGain * t * Math.Clamp(tint, 0, 1)), (float)Math.Min(0.95, FilmLight[2] * (1 - t) * tint), (float)(LawBottom * small), (float)panel];
    }
    internal const double LawGain = 0.95, LawBottom = 0.13, LawShadedTo = 60, LawFlatFrom = 120;

    // The backdrop's level 12 pt in from the edge from which the edge is lit (none at or below LitFrom, whole from LitTo): the card's 27 / 255 is
    // 0.106 (0.13 to 0.15 near its edge, where the scrim's cast shadow thins), the title capsules' 52 is 0.204 and the pill's 57 is 0.224.
    internal const double LitFrom = 0.15, LitTo = 0.20;

    // Where the film turns from its top grey to its bottom one, as fractions of the piece's height: dashersw's ramp is linear over the whole
    // height; the Mac pill is flat (87 to 88) down to about half its height and darkens to 77 by four fifths of it.
    internal const double FilmBegin = 0.45, FilmEnd = 0.85;

    // ED-356: a piece's refraction, by its size (the shader's `optics`: x the bend at the edge, y the fall-off's width, z the border
    // before it falls, w the rim ring at the edge, all in the piece's units).
    //   - rizzytoday/liquid-glass (MIT, (c) 2026 Riz Roze), core/liquid-glass.ts buildDisplacementMap: the map is the full displacement (scale -180,
    //     half of it at the edge: 90 px) under a neutral grey centre blurred by 11 px and inset by 3.5 % of the smaller side, so the bend is
    //     full at the edge and falls off as a Gaussian edge does (its complement) over about the blur. On its 52 px pill that is a bend of about
    //     the pill's height at the edge over a 14 px band. Here the fall-off is 21 % of the smaller side up to 11 (its 11 on 52), the border 3.5 %
    //     up to 3, and the bend 55 % of the smaller side up to 40 on a piece of 100 pt or more. A smaller piece takes a smaller share, a fifth of it
    //     at 32 pt and under: the Mac frames show the small pieces read little from past their edge (the title capsules' first pixel 68 beside
    //     a backdrop of 50 that a bright strip 15 pt out lifts to 68 under a long bend; the composer pill's top 3 to 6 levels lighter than the Mac's
    //     where the backdrop above it lightens), and the effort knob, 28 pt, keeps the colour under it (GlassSmallLensTests). The per-channel
    //     scales of its chromatic aberration (0 / 10 / 20 on 180) are the fringe: green bends 0.944 and blue 0.889 of red, about 4 to 6 pt from
    //     red to blue at a panel's edge.
    //   - dashersw/liquid-glass-js (MIT, (c) 2025 Armagan Amcalar), container.js setupShader: the rim ring, rimIntensity * exp(-rimDistance * d)
    //     with rimDistance 0.8 per pixel, a sharp extra bend on the edge's first pixels; here 8 % of the smaller side up to 10 pt.
    internal readonly record struct Optics(double Bend, double Sigma, double Border, double Ring)
    {
        internal const double MaxBend = 40, MaxRing = 10;
        /// <summary>The most a look's bend and ring read past the edge together (within MaxReadDisplacement, what the material reads around itself).</summary>
        internal const double MaxLookReach = 60;
        internal const double Logistic = 1.702; // the logistic form of the normal distribution (within 0.0095 of it)
        internal const double BendFloor = 0.01; // the bend's last 1 % is dropped, so it ends: at BendSupport fall-off widths past the border
        internal const double BendSupport = 3.12; // ln(199) / 1.702, where the normalised logistic tail reaches the floor
        internal const double RingFloor = 0.001;
        internal static readonly double RingReach = Math.Log(1 / RingFloor) / 0.8; // where exp(-0.8 d) reaches the floor: 8.63
        internal const double GlowReach = 12; // the light inner edge ends 12 pt in
        internal const double Green = 1 - 10 / 180.0, Blue = 1 - 20 / 180.0;
        // Calibrated on the Mac composer pill (frames 01, 29, 30; compare\pill*.png), 1 px = 1 pt:
        //   - the contact line outside the edge: none where the edge faces up or down (the pill's top and bottom: 60 and 53 outside, as around it),
        //     dark where it faces sideways (its ends: 9 against 32 around);
        //   - the rim's white over the body, all around (15 % of it at the sides) and by how much the edge faces up or down: the top 124 and the
        //     bottom 123 over a body of 88 and 77 (the port: 123 and 124 over the same backdrop);
        //   - the light inner edge: +9, +6, +5, +3, +2 levels 1.5 to 5.5 pt under the top rim (with the bend's own lift there), a fifth of it at the sides.
        internal const double ShadeBase = 0, ShadeSide = 0.6;
        internal const double ShadeDark = 0.05; // the contact line's share left on a dark backdrop (the Mac card: none outside its edge, -3 on its first point)
        internal const double RimBase = 0.15, RimTop = 0.21, RimBottom = 0.20;
        internal const double Glow = 0.035, GlowSide = 0.2;
        // ED-NEW-glasslook-3: the hairline rim (a look's Hairline): its white where the edge faces the light (from the top left, LightX / LightY, y down),
        // where it faces away (the counter-highlight) and all around.
        internal const double HairMain = 0.42, HairCounter = 0.14, HairBase = 0.03;
        internal static readonly double LightX = -0.5 / Math.Sqrt(1.25), LightY = -1 / Math.Sqrt(1.25);
        // The squircle lens (a look's Profile): the index of refraction, and the bend at the edge it is normalised by (a vertical ray meeting the
        // vertical edge: tan(90 deg - asin(1 / n))).
        internal const double Ior = 1.5;
        internal static readonly double SquircleEdge = Math.Tan(Math.PI / 2 - Math.Asin(1 / Ior));
        // The bend (Optics.Bend, pt) under which the rim stays frosted and from which it is wholly clear: the composer pill (46 pt, a bend of 9.1)
        // a third clear, a 64 pt button (20) and anything larger wholly.
        internal const double ClearFrom = 4, ClearTo = 16;

        public static Optics For(Size size)
        {
            var smaller = Math.Max(1, Math.Min(size.Width, size.Height));
            var small = Math.Clamp((smaller - 16) / 84, 0.2, 1);
            return new Optics(Math.Min(MaxBend, 0.55 * smaller * small), Math.Clamp(0.21 * smaller, 1, 11), Math.Min(3, 0.035 * smaller), Math.Min(MaxRing, 0.08 * smaller));
        }

        /// <summary>ED-NEW-glasslook-1: the piece's refraction under a look: the bend and the ring times its Bend (held to
        /// <see cref="MaxLookReach"/> together), the fall-off's width times its Band (at most 14: Optics.Deep stays under SpreadOf's 48).</summary>
        public static Optics For(Size size, GlassLook look)
        {
            var o = For(size);
            var bend = o.Bend * look.BendValue;
            var ring = o.Ring * look.BendValue;
            if (bend + ring > MaxLookReach) (bend, ring) = (bend * MaxLookReach / (bend + ring), ring * MaxLookReach / (bend + ring));
            return new Optics(bend, Math.Min(14, o.Sigma * look.BandValue), o.Border, ring);
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
    // ED-NEW-glass-c10-6: Whole, a piece that reads its own backdrop (the voice pill) is redrawn whole: Skia antialiases a path that the frame's clip cuts
    // differently from the same path drawn whole (1 to 3 levels on edge pixels far from the cut, more beside it), and the pill's rim draws its shade, fringe,
    // rings and highlights as such paths over its glass; a reach rect that ended in its right cap changed its left cap by 3 levels. Its box is small, so
    // drawing all of it costs little; every other piece adds only its reach (ED-320 15 and 16).
    internal readonly record struct Spread(SKRectI Output, int Margin, SKRectI Inner, int Blur, int Bend, bool Whole = false)
    {
        public SKRectI Of(SKRectI changed)
        {
            var wide = Grow(changed, Blur + Bend);
            return Grow(changed, !Inner.IsEmpty && Inner.Contains(wide) ? Blur : Math.Max(Margin, Blur + Bend));
        }

        /// <summary>What a change adds to the frame's region: its reach inside the output, or the whole output when the piece is drawn whole and the reach meets it.</summary>
        public SKRectI Adds(SKRectI changed)
        {
            var reach = SKRectI.Intersect(Of(changed), Output);
            return Whole && !reach.IsEmpty ? Output : reach;
        }
        // Clamped to int: a huge or infinite rect (an unbounded dirty rect) must not overflow the checked arithmetic (Directory.Build.props).
        private static SKRectI Grow(SKRectI r, int by) => new(Clamp((long)r.Left - by), Clamp((long)r.Top - by), Clamp((long)r.Right + by), Clamp((long)r.Bottom + by));
        private static int Clamp(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    // Draws the material; returns what it covers in the surface's pixels and how far a change under it reaches, for the window's Reach.
    // ED-356: the glass is translucent (it keeps the alpha of the pixels under it), so it cannot be drawn over them: the sharp pixels would show
    // through it. Each pixel becomes mix(under, value, amount) in two draws of the one shader: stage 0 with BlendMode.DstOut takes `amount` of the pixel away,
    // stage 1 with BlendMode.Plus adds value * amount. The second is the material; the first evaluates only the shape's distance and its edge.
    // ED-NEW-glasslook-1 (bounded per-frame cost): each piece's uniform names, made once per name and index rather than on every frame.
    private static class UniformName
    {
        private static readonly ConcurrentDictionary<(string, int), string> made = new();
        public static string Of(string name, int index) => made.GetOrAdd((name, index), static k => k.Item1 + k.Item2.ToString(CultureInfo.InvariantCulture));
    }

    private static Spread? Draw(ISkiaSharpApiLease lease, State state, Backdrop kept, SKRectI? dirty)
    {
        var canvas = lease.SkCanvas;
        // the contact line reaches 1.25 past the shape; a look's soft shadow (ED-NEW-glasslook-3) its blur and its drop
        var bounds = UnionBounds(state).Inflate(state.Spacing / 2 + Math.Max(2, state.Regions.Max(r => r.DepthReach) + 1));
        var local = new SKRect((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom);
        var transform = canvas.TotalMatrix;
        var scale = MathF.Max(MathF.Sqrt(transform.ScaleX * transform.ScaleX + transform.SkewY * transform.SkewY),
            MathF.Sqrt(transform.ScaleY * transform.ScaleY + transform.SkewX * transform.SkewX));
        if (scale <= 0) return null;
        var sampled = transform.MapRect(local);
        var output = SKRectI.Ceiling(sampled);
        sampled.Inflate((float)SampledMargin * scale, (float)SampledMargin * scale); // 3 sigma (36), the largest read displacement (72), antialias margin.
        var crop = SKRectI.Ceiling(sampled);
        // Immutable app-owned pixels: no screen capture or visual-tree replay.
        var under = kept.Take(lease, crop, dirty);
        var surface = kept.Blurred(lease, crop);
        if (surface is null) return null;
        // The blur is read only where the shader draws in this frame (the canvas's clip on the shapes) and as far as the refraction displaces from
        // there (the largest bend and ring, the red channel's, and the linear filter's pixel): only that much is cleared and blurred (ED-320 16).
        var reads = SKRectI.Intersect(canvas.DeviceClipBounds, output);
        // 12 pt on a panel; a small lens takes a blur in proportion (15 % of its smaller side), so the colour under
        // it is not washed out by what lies around it (the effort knob over its fill and glow; see the shader note). A look's Blur scales it, to
        // 12 pt at most (SampledMargin reads 3 sigma of 12 around the piece); a container blurs by its largest.
        // OD-0147: a piece in the approved material blurs by its kind's frost (the canvas's sigma, 0.48 of its radius), to 24 pt at most.
        var radius = state.Regions.Max(r => r.Drawn.Canvas is { } canvas ? (float)canvas.Sigma * scale
            : Math.Min(12 * scale, BlurSigma(Math.Min(r.Size.Width, r.Size.Height), scale) * (float)r.Drawn.BlurValue));
        var reach = state.Regions.Max(r => r.Drawn.Canvas is not null ? r.Lens.Reach : Math.Max(r.Lens.Reach,
            Math.Min(r.Size.Width, r.Size.Height) > LawShadedTo ? UnlitBandSigmas * BlurSigma(Math.Min(r.Size.Width, r.Size.Height), 1) : 0)
            * Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22 - r.Transform.M12 * r.Transform.M21)));
        var spread = SpreadOf(state, output, scale, radius);
        if (reads.IsEmpty) return spread;
        var shift = (int)Math.Ceiling(reach * scale) + 2;
        var blurredCanvas = surface.Canvas;
        blurredCanvas.Save();
        blurredCanvas.ClipRect(new SKRect(reads.Left - shift - crop.Left, reads.Top - shift - crop.Top, reads.Right + shift - crop.Left, reads.Bottom + shift - crop.Top));
        // ED-356: cleared to nothing, not to a veil: where the window's pixels are transparent (the Acrylic) or translucent (its scrim), the
        // glass keeps them so (it was an opaque #1E1E20 there, near-black glass over the Acrylic).
        blurredCanvas.Clear(SKColors.Transparent);
        // The unblurred pixels under the glass, read by the clear lens rim (ED-NEW-glasslook-1); released before the next frame writes to the kept
        // surface, so Skia copies nothing.
        using var pixels = under?.Surface.Snapshot();
        if (under is { } taken && pixels is not null)
        {
            using var blur = SKImageFilter.CreateBlur(radius, radius, SKShaderTileMode.Clamp);
            using var blurPaint = new SKPaint { ImageFilter = blur };
            blurredCanvas.DrawImage(pixels, taken.Area.Left - crop.Left, taken.Area.Top - crop.Top, blurPaint);
            // PORT-ONLY (GlassEffect.OwnBackdrop, the voice pill): inside its box such a piece's frost is the blur of the pixels in that box alone, by its
            // own sigma, mirrored at the box's edges (the page's backdrop-filter in Chrome: the border box snapped to the device grid, each edge rounded to
            // the nearest pixel, mirror tiling); it reads its frost there with no bend, so nothing past the box reaches it.
            foreach (var r in state.Regions)
            {
                if (!r.OwnBackdrop) continue;
                var ownerBox = new Rect(r.Size).TransformToAABB(r.Transform);
                var owned = transform.MapRect(new SKRect((float)ownerBox.Left, (float)ownerBox.Top, (float)ownerBox.Right, (float)ownerBox.Bottom));
                var box = new SKRectI((int)MathF.Floor(owned.Left + 0.5f), (int)MathF.Floor(owned.Top + 0.5f), (int)MathF.Floor(owned.Right + 0.5f), (int)MathF.Floor(owned.Bottom + 0.5f));
                var inPixels = SKRectI.Intersect(new SKRectI(box.Left - taken.Area.Left, box.Top - taken.Area.Top, box.Right - taken.Area.Left, box.Bottom - taken.Area.Top),
                    new SKRectI(0, 0, pixels.Width, pixels.Height));
                if (inPixels.IsEmpty) continue;
                using var own = pixels.Subset(inPixels);
                if (own is null) continue;
                var sigma = r.Drawn.Canvas is { } canvasLook ? (float)canvasLook.Sigma * scale
                    : Math.Min(12 * scale, BlurSigma(Math.Min(r.Size.Width, r.Size.Height), scale) * (float)r.Drawn.BlurValue);
                using var ownBlur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Mirror);
                using var ownPaint = new SKPaint { ImageFilter = ownBlur };
                blurredCanvas.Save();
                blurredCanvas.ClipRect(new SKRect(taken.Area.Left + inPixels.Left - crop.Left, taken.Area.Top + inPixels.Top - crop.Top,
                    taken.Area.Left + inPixels.Right - crop.Left, taken.Area.Top + inPixels.Bottom - crop.Top));
                blurredCanvas.Clear(SKColors.Transparent);
                blurredCanvas.DrawImage(own, taken.Area.Left + inPixels.Left - crop.Left, taken.Area.Top + inPixels.Top - crop.Top, ownPaint);
                blurredCanvas.Restore();
            }
        }
        blurredCanvas.Restore();
        using var image = surface.Snapshot();
        var sourceTransform = transform;
        sourceTransform.TransX -= crop.Left;
        sourceTransform.TransY -= crop.Top;
        if (!sourceTransform.TryInvert(out var shaderTransform)) return null;
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        using var backdrop = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, shaderTransform);
        // Without kept pixels the rim reads the blur (the same program either way: the warm-up and the variants bind both children).
        var sharpTransform = transform;
        if (under is { } kept2) { sharpTransform.TransX -= kept2.Area.Left; sharpTransform.TransY -= kept2.Area.Top; }
        using var sharp = pixels is not null && sharpTransform.TryInvert(out var toSharp)
            ? pixels.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, toSharp)
            : image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, shaderTransform);
        var effect = Effect(state.Regions.Length);
        if (drawnCounts.TryAdd(state.Regions.Length, true)) ShapeCountDrawn?.Invoke(state.Regions.Length);
        // The window's surface in the shader's coordinates, half a device pixel in (the bend's reads are clamped to it).
        var surfaceRect = kept.Whole.Width > 0 && kept.Whole.Height > 0 ? new SKRect(0.5f, 0.5f, kept.Whole.Width - 0.5f, kept.Whole.Height - 0.5f) : new SKRect(-1e6f, -1e6f, 1e6f, 1e6f);
        var windowLocal = transform.TryInvert(out var toLocal) ? toLocal.MapRect(surfaceRect) : surfaceRect;
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["merge"] = (float)(2 * state.Spacing), ["stage"] = 0f, ["interior"] = 0f, ["window"] = new[] { windowLocal.Left, windowLocal.Top, windowLocal.Right, windowLocal.Bottom },
        };
        for (var i = 0; i < state.Regions.Length; i++)
        {
            var r = state.Regions[i];
            var inverse = r.Transform.Invert();
            var values = new (string Name, float[] Value)[]
            {
                (UniformName.Of("transform", i), [(float)inverse.M11, (float)inverse.M12, (float)inverse.M21, (float)inverse.M22]),
                (UniformName.Of("translate", i), [(float)inverse.M31, (float)inverse.M32]),
                (UniformName.Of("size", i), [(float)r.Size.Width, (float)r.Size.Height]),
                (UniformName.Of("corners", i), [(float)r.Corners.TopLeft, (float)r.Corners.TopRight, (float)r.Corners.BottomRight, (float)r.Corners.BottomLeft]),
                (UniformName.Of("tint", i), [r.Tint.R / 255f, r.Tint.G / 255f, r.Tint.B / 255f, r.Tint.A / 255f]),
                (UniformName.Of("film", i), Film(r.Darkness, r.Drawn.TintValue, r.Drawn.SaturationValue)),
                (UniformName.Of("law", i), Law(r.Darkness, r.Size, r.Drawn.TintValue)),
                (UniformName.Of("optics", i), r.Lens.Uniform),
                (UniformName.Of("look", i), [(float)r.Drawn.ClarityValue, (float)r.Drawn.FringeValue, (float)r.Drawn.RimValue, (float)r.Drawn.ShadowValue]),
                (UniformName.Of("finish", i), [(float)r.Drawn.ProfileValue, (float)r.Drawn.GlowValue, (float)r.Drawn.HairlineValue, (float)r.Drawn.FringeCapValue]),
                (UniformName.Of("aura", i), [(float)r.Drawn.LiftValue, (float)r.Drawn.DepthValue, (float)(r.DepthReach > 0 ? GlassLook.DepthRadius(r.Smaller) : 0), (float)(r.DepthReach > 0 ? GlassLook.DepthOffset(r.Smaller) : 0)]),
                (UniformName.Of("flags", i), [(float)r.Shape.Kind, r.Shape.Style == RoundedCornerStyle.Continuous ? (r.Shape.CircularInBand ? 2f : 1f) : 0f,
                    (float)Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22 - r.Transform.M12 * r.Transform.M21)), (float)r.Opacity]),
                (UniformName.Of("pointer", i), [(float)r.Pointer.X, (float)r.Pointer.Y, r.Pressed ? 0.12f : r.Hovered ? 0.06f : 0f, r.DrawnTint ? 1f : 0f]),
            };
            foreach (var (name, value) in values) uniforms[name] = value;
            var canvasRows = CanvasUniforms(r, scale);
            for (var k = 0; k < CanvasRows.Length; k++) uniforms[UniformName.Of(CanvasRows[k], i)] = canvasRows[k];
            uniforms[UniformName.Of("plain", i)] = r.NoRefraction ? 1f : 0f;
            uniforms[UniformName.Of("bare", i)] = r.NoContactLine ? 1f : 0f;
        }
        var children = new SKRuntimeEffectChildren(effect) { ["backdrop"] = backdrop, ["sharp"] = sharp };
        using var maskShader = effect.ToShader(uniforms, children);
        uniforms["stage"] = 1f;
        using var shader = effect.ToShader(uniforms, children);
        // One rule for the opacity bytes: rounded. The inside's DstOut fill is the edge stage's amount there (the piece's opacity times this paint's alpha).
        var faded = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(lease.CurrentOpacity * 255), 0, 255));
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
        using var innerMask = new SKPaint { Color = SKColors.Black.WithAlpha((byte)Math.Clamp(Math.Round(faded.Alpha * state.Regions[0].Opacity), 0, 255)), BlendMode = SKBlendMode.DstOut };
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

    // OD-0147: the approved material's rows (GlassCanvasSpec, the canvas's u_m), by piece; a piece in a knob look draws none (cvA.x 0).
    private static readonly string[] CanvasRows = ["cvA", "cvB", "cvC", "cvD", "cvE", "cvF", "cvG", "cvH", "cvI"];
    private static readonly float[][] NoCanvas = [.. Enumerable.Range(0, 9).Select(_ => new float[4])];

    private static float[][] CanvasUniforms(Region r, float scale)
    {
        if (r.Drawn.Canvas is not { } canvas) return NoCanvas;
        var s = canvas.Spec(r.Smaller, r.Darkness, r.HoverAmount, r.PressAmount, 1 / Math.Max(scale, 0.01));
        var flat = canvas.Kind == GlassKind.Flat;
        // a flat control shows its own tint as a flat fill (unless its shape's fill draws it), and white under the pointer
        float[] fill = flat
            ? r.Tint.A > 0 && !r.DrawnTint ? [r.Tint.R / 255f, r.Tint.G / 255f, r.Tint.B / 255f, r.Tint.A / 255f] : [1f, 1f, 1f, 0f]
            : [(float)s.TintR, (float)s.TintG, (float)s.TintB, (float)s.TintAmount];
        return
        [
            [flat ? 2f : 1f, (float)s.Bend, (float)s.Bezel, (float)s.Fringe],
            [(float)s.Magnify, (float)s.Grey, (float)s.TintTop, (float)s.TintBottom],
            [(float)s.Mid, (float)s.Compress, (float)s.Saturation, (float)s.Lift],
            [(float)s.Band, (float)s.BandDark, (float)s.Spec, (float)s.Counter],
            [(float)s.ShadowY, (float)s.ShadowSigma, (float)s.ShadowAlpha, (float)s.ContactAlpha],
            [(float)s.Hair, (float)s.Glow, (float)s.GlowSigma, (float)s.Dark],
            [(float)s.Glint, (float)s.Scrim, (float)s.Line, 1 / Math.Max(scale, 0.01f)],
            fill,
            [(float)s.Hover, (float)s.Press, (float)s.Swell, 0f],
        ];
    }

    // The approved material's lens: its bend and the red read's part of the fringe past the edge (the blur's reach), and its depth: past the
    // magnification (2.2 bezels), the full frost, the edge band and the soft inner light, it reads one texel (Inside, the shader's deep).
    private static Optics CanvasOptics(GlassCanvas canvas, double smaller)
    {
        var s = canvas.Spec(smaller, 1, 1, 1, 1);
        var deep = Math.Max(Math.Max(GlassCanvas.MagnifyWidth * s.Bezel, GlassCanvas.FullAt), Math.Max(s.Band, 12)) + s.Swell;
        return new Optics(s.Bend + s.Fringe, 0, deep, 0);
    }

    // The inside of a lone rounded rectangle, in the material's coordinates: past 1.6 corner radii from every side (Apple's corner curve lies within them,
    // ContinuousCorner.Extent) and deeper than Optics.Deep (the shader's own test, half a unit further in): there the shader reads one texel and its
    // bend, ring, rim, inner light and contact line add nothing. Null for a container's union, a turned or a non-rectangular shape, or one too small to have one.
    private static SKRect? Inside(State state)
    {
        if (state.Regions is not [{ Shape.Kind: GlassShapeKind.RoundedRectangle, Transform: { M12: 0, M21: 0 } } r] || r.Drawn.Canvas?.Kind == GlassKind.Flat) return null;
        var distanceScale = Math.Sqrt(Math.Abs(r.Transform.M11 * r.Transform.M22));
        if (distanceScale <= 0) return null;
        var deep = r.Lens.Deep / distanceScale;
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
        var bend = (int)Math.Ceiling(MaxReadDisplacement * scale) + 2; // the larger of the 50 pt lit bend and the 72 pt unlit band read
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
        return new Spread(output, margin, inner, blur, bend, state.Regions.Any(r => r.OwnBackdrop));
    }
}
