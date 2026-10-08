// PORT-ONLY, owner-ruled addition (OD-0091, OD-0086 and its amendment; first-release voice typing, VT-10, voice-SPEC V-14.2): the voice orb in the composer's controls,
// where the attachment button was.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.UI.Port;
using Marrow.UI.Port.Voice.Orb;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The first item of <c>ComposerControls</c>: <c>D</c> by <c>D</c> (32 or 26), the 72 px <see cref="VoiceOrbView"/> centred on it and overflowing, a hit target 4 px larger
/// all round (the design's <c>.orbbtn{inset:-4px}</c>), tooltip <c>Voice input</c>, a name for each state. The row keeps its 28 pt height whatever <c>D</c> is, and the gap to
/// the model chip is the design's 14 (the row's own 2 and 12 more). A size change is a spring (response 0.34, damping 0.86) and the slot follows it.
/// </summary>
internal sealed class VoiceOrbSlot : Decorator
{
    private const double RowHeight = 28;
    private const double ChipGap = 12; // the row spaces its items by 2; the design leaves 14 between the slot and the chip
    private const double HitInset = 4;
    private static readonly Animation SizeSpring = Animation.Spring(response: 0.34, dampingFraction: 0.86);
    private static readonly Animation LiftUp = Animation.Spring(response: 0.36, dampingFraction: 0.8);   // V-12.2: the orb lifts into the floating pill
    private static readonly Animation LiftDown = Animation.Spring(response: 0.34, dampingFraction: 0.86); // and settles back

    private readonly ComposerVoiceController voice;
    private readonly Canvas canvas = new() { ClipToBounds = false };
    private readonly AnimatedScalar side;
    private readonly AnimatedScalar lift;
    private readonly AnimatedScalar inlineInset;
    private readonly LookMotion motion;
    private readonly LookNumber crossSlide;
    private readonly LookNumber crossOpacity;
    private readonly TranslateTransform liftShift = new();
    private readonly TranslateTransform crossShift = new();
    private readonly ScaleTransform crossGrow = new(0.55, 0.55);
    private int shownSize;
    private DisplayLink? levelLink;
    private ContinuousInstant lastLevel;
    private IInputElement? keyboardBefore;
    private VoiceSessionState lastState;
    private readonly Popup? setup;

    public VoiceOrbSlot(ComposerVoiceController voice)
    {
        this.voice = voice;
        // Keep the 72 px view centred on D/2 during size springs and at fractional desktop scales.
        // Rounding its canvas offset separately from D/2 moves the painted ball away from the pill's anchor.
        UseLayoutRounding = false;
        motion = new LookMotion(this);
        crossSlide = new LookNumber(motion, 0);
        crossOpacity = new LookNumber(motion, 0);
        Cross.RenderTransform = new TransformGroup { Children = { crossGrow, crossShift } };
        Cross.RenderTransformOrigin = RelativePoint.Center;
        Cross.Opacity = 0;
        Cross.IsHitTestVisible = false;
        Cross.Click += (_, _) => voice.Cancel();
        motion.Frame += PlaceCross;
        Orb.RenderTransform = liftShift;
        HitTarget.RenderTransform = liftShift;
        lift = new AnimatedScalar(this, 0, value => liftShift.Y = VoicePillGeometry.FloatLift * value);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        ClipToBounds = false;
        Child = canvas;
        canvas.Children.Add(Cross); // tucked behind the orb at rest (V-12.5)
        canvas.Children.Add(Orb);
        canvas.Children.Add(HitTarget);
        if (voice.FirstRun is { } flow)
        {
            setup = PlainControls.Popover(HitTarget, new Binding<bool>(() => voice.SetupOpen, open => voice.SetupOpen = open),
                () => new VoiceSetupCard(flow), PlacementMode.TopEdgeAlignedRight);
            canvas.Children.Add(setup);
            DetachedFromVisualTree += (_, _) => voice.SetupOpen = false;
        }
        shownSize = voice.OrbSettings.OrbSize;
        side = new AnimatedScalar(this, shownSize, Place);
        inlineInset = new AnimatedScalar(this, InlineInsetTarget, _ => Place(Diameter));
        ToolTip.SetTip(HitTarget, "Voice input");
        HitTarget.Click += (_, _) => voice.Activate();
        HitTarget.PointerEntered += (_, _) => voice.PointerEnter();
        HitTarget.PointerExited += (_, _) =>
        {
            voice.PointerLeave();
            Orb.Pressed = false;
        };
        // V-8.5 "focus is not stolen": a pointer press takes the keyboard to the orb's button (V-14.2: it is focusable). Where it was (the editor) is remembered at the press and given
        // back when the session ends, so the words that land can be sent with Return.
        HitTarget.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            var held = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            if (held is not null && !ReferenceEquals(held, HitTarget)) keyboardBefore = held; // a second press, with the keyboard already on the orb, keeps what the first took it from
        }, RoutingStrategies.Tunnel);
        HitTarget.PointerPressed += (_, e) => Orb.Pressed = e.GetCurrentPoint(HitTarget).Properties.IsLeftButtonPressed;
        HitTarget.PointerReleased += (_, _) => Orb.Pressed = false;
        HitTarget.PointerCaptureLost += (_, _) => Orb.Pressed = false;
        Show(voice.State, voice.PointerOver, voice.OrbSettings);
        AttachedToVisualTree += (_, _) => FollowTheVoice();
        DetachedFromVisualTree += (_, _) => StopFollowing();
        this.Track(() => (voice.State, voice.PointerOver, voice.OrbSettings), shown => Show(shown.State, shown.PointerOver, shown.OrbSettings));
        this.Track(() => (voice.IsLive, voice.LiveStyle), shown => ShowListening(shown.IsLive, shown.LiveStyle));
        this.Track(() => InlineInsetTarget, target => inlineInset.Set(target, LookCurve.Ez(0.42), preserveFrameProgress: true));
    }

    /// <summary>The slot's side now, a spring toward the orb size (V-14.2): the pill's anchor and the radius its material clears round the orb (V-12.12).</summary>
    public double Diameter => side is null ? shownSize : side.Value;

    /// <summary>The cancel cross beside the orb (V-12.5), live only in the Cross style.</summary>
    public VoiceCross Cross { get; } = new(VoiceCrossLook.Beside);

    /// <summary>How far the orb has lifted into the floating pill, 0 to 1 (the spring of V-12.2).</summary>
    internal double Lift => lift.Value;

    /// <summary>The cross's slide, 0 tucked behind the orb to 1 out (0.42 s ez), and its opacity.</summary>
    internal double CrossSlide => crossSlide.Value;

    internal double CrossOpacity => crossOpacity.Value;

    // V-12.5 and V-12.2, V-15's Listening row: the Cross style slides the cross out (transform 0.42 s ez, opacity 0.24 s ease after 0.04 s; back: opacity 0.2 s ease), the Float style lifts the orb
    private void ShowListening(bool live, VoiceListeningStyle style)
    {
        var crossOut = live && style == VoiceListeningStyle.Cross;
        crossSlide.To(crossOut ? 1 : 0, LookCurve.Ez(0.42));
        if (crossOut) crossOpacity.To(1, LookCurve.Ease(0.24), 0.04);
        else crossOpacity.To(0, LookCurve.Ease(0.2));
        Cross.Live = crossOut;
        Cross.IsHitTestVisible = crossOut; // `.xbtn{pointer-events:none}`, auto while live
        var lifted = live && style == VoiceListeningStyle.Float;
        lift.Set(lifted ? 1 : 0, lifted ? LiftUp : LiftDown);
        PlaceCross();
    }

    // `.xbtn`: right edge at the slot's centre, transform translateX(0) scale(.55) tucked, translateX(xo) scale(1) out
    private void PlaceCross()
    {
        var d = side is null ? shownSize : side.Value; // Place runs once from inside the AnimatedScalar's own construction
        var slide = crossSlide.Value;
        Canvas.SetLeft(Cross, d / 2 - VoicePillGeometry.CrossSize);
        Canvas.SetTop(Cross, d / 2 - VoicePillGeometry.CrossSize / 2);
        crossShift.X = VoicePillGeometry.CrossBesideOffset(d) * slide;
        crossGrow.ScaleX = crossGrow.ScaleY = 0.55 + 0.45 * slide;
        Cross.Opacity = crossOpacity.Value;
    }

    /// <summary>The 72 px orb view.</summary>
    public VoiceOrbView Orb { get; } = new();

    /// <summary>The button over the orb.</summary>
    public VoiceOrbButton HitTarget { get; } = new();

    private void Show(VoiceSessionState state, bool hovered, VoiceOrbSettings orb)
    {
        var ended = lastState is VoiceSessionState.Listening or VoiceSessionState.Processing && state is VoiceSessionState.Landing or VoiceSessionState.Rest;
        lastState = state;
        if (ended) GiveKeyboardBack();
        Orb.Kind = orb.Orb;
        Orb.Mode = voice.Mode;
        Orb.Hovered = hovered;
        Orb.SlotDiameter = orb.OrbSize;
        AutomationProperties.SetName(HitTarget, voice.OrbName);
        HitTarget.On = state == VoiceSessionState.Listening;
        FollowTheVoice();
        if (orb.OrbSize == shownSize) return;
        shownSize = orb.OrbSize;
        side.Set(orb.OrbSize, SizeSpring);
    }

    // V-5.5, V-2.2 item 4: once per frame while the voice is heard or the bands are still falling, the composer's smoothed bands go to the orb. The link stops itself when they reach 0.
    private void FollowTheVoice()
    {
        if (levelLink is not null || !FrameClock.IsBound || VisualRoot is null || voice.State != VoiceSessionState.Listening) return;
        lastLevel = SwiftClock.Now;
        levelLink = FrameClock.DisplayLink(StepLevel);
        levelLink.Add();
    }

    private void StepLevel()
    {
        var now = SwiftClock.Now;
        var dt = (now - lastLevel).TotalSeconds;
        lastLevel = now;
        var more = voice.StepLevel(dt);
        Orb.Bands = voice.OrbBands;
        if (more) return;
        levelLink?.Invalidate();
        levelLink = null;
    }

    private void StopFollowing()
    {
        levelLink?.Invalidate();
        levelLink = null;
        voice.ResetLevel();
        Orb.Bands = default;
    }

    // The session ended: the keyboard goes back to where the press took it from, if the orb's button still has it (the user has not moved on).
    private void GiveKeyboardBack()
    {
        var back = keyboardBefore;
        keyboardBefore = null;
        if (back is null || !HitTarget.IsFocused) return;
        back.Focus();
    }

    // The slot's side now: the canvas, the orb centred on it, the hit target round it, and the margins that keep the row 28 high.
    private void Place(double d)
    {
        canvas.Width = d;
        canvas.Height = d;
        Canvas.SetLeft(Orb, (d - OrbScene.ViewPx) / 2);
        Canvas.SetTop(Orb, (d - OrbScene.ViewPx) / 2);
        Canvas.SetLeft(HitTarget, -HitInset);
        PlaceCross();
        Canvas.SetTop(HitTarget, -HitInset);
        HitTarget.Width = d + 2 * HitInset;
        HitTarget.Height = d + 2 * HitInset;
        // Owner T46 placement ED: one point up centres the 40pt inline pill in the 46pt composer, retaining the row's height.
        // Float keeps its approved anchor and lift; the pill itself remains centred on the orb for its clean-cap mask.
        var inset = inlineInset is null ? InlineInsetTarget : inlineInset.Value;
        Margin = new Thickness(0, (RowHeight - d) / 2 - inset, ChipGap, (RowHeight - d) / 2 + inset);
    }

    private double InlineInsetTarget =>
        (voice.Preparing ? VoiceListeningStyle.Pill : voice.IsLive ? voice.LiveStyle : voice.OrbSettings.ListeningStyle) == VoiceListeningStyle.Pill ? 1 : 0;
}
