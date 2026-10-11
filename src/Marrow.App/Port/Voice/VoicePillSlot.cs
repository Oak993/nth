// PORT-ONLY, owner-ruled addition (OD-0091, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.1, V-12.2, V-12.6, V-12.9, V-12.10, V-15): where the composer's pill stands and what feeds it.
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.UI.Port;
using Marrow.App.Port.Voice.Countdown;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The composer's layer for the listening pill, over the composer's glass and its glow and under its text and controls (the orb, a child of the controls, stands over the pill; the text column keeps clear
/// of it). It places the pill at the orb (V-12.2: inline, centred on the orb's centre with its right end fixed at <c>cx + H/2</c>, opening to the left; float, 52 above the composer's top with its left end at
/// <c>cx - H/2</c>, opening to the right), in the same arrange pass that gives the pill its new width, so the fixed end never moves a frame late. While the session is live it reads the session's label state and
/// the language it heard once per frame (V-6.3, V-12.10), passes the picked word set (V-12.9) and keeps the text column's right inset (V-12.6) for the composer to ease. The Cross style has no pill: the
/// cross beside the orb is the orb slot's (V-12.5).
/// </summary>
internal sealed class VoicePillSlot : Panel
{
    private readonly ComposerVoiceController voice;
    private VoiceOrbSlot? orb;
    private DisplayLink? link;
    private bool transcribingSeen;
    private bool wasOpen;
    private double lastInset = double.NaN;
    private Point? placedFor;
    private CapCountdownLine? countdown;

    public VoicePillSlot(ComposerVoiceController voice)
    {
        this.voice = voice;
        // A panel with no background takes no pointer itself. Keep its subtree enabled so the pill's cross can be hit.
        IsHitTestVisible = true;
        ClipToBounds = false;
        Pill = new VoicePill();
        Children.Add(Pill);
        this.Track(() => (voice.IsLive, voice.LiveStyle, voice.Preparing, voice.PreparationProgress), shown =>
        {
            Pill.ShowPreparation(shown.Preparing, shown.PreparationProgress);
            ShowLive(shown.IsLive || shown.Preparing, shown.Preparing ? VoiceListeningStyle.Pill : shown.LiveStyle);
        });
        this.Track(() => voice.OrbSettings.Labels, words => Pill.Words = words);
        this.Track(() => voice.OrbSettings.OrbSize, _ => ReportInset());
        Pill.Cross.Click += (_, _) => voice.Cancel();
        AttachedToVisualTree += (_, _) =>
        {
            countdown = new CapCountdownLine(voice.Session,
                () => EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion,
                () => LookDraw.IsDark(ActualThemeVariant) ? CountdownPalette.Dark : CountdownPalette.Light);
            countdown.Announced += CountdownAnnouncement;
            Follow();
            Step();
        };
        DetachedFromVisualTree += (_, _) => { Unfollow(); countdown?.Dispose(); countdown = null; };
    }

    /// <summary>The pill.</summary>
    public VoicePill Pill { get; }

    /// <summary>The space the text column keeps free on its right while the session is live (V-12.6): it eases to this over 0.42 s ez.</summary>
    public double InsetTarget { get; private set; }

    /// <summary>The composer lets the text column know its right inset's new target.</summary>
    public event Action<double>? InsetTargetChanged;

    /// <summary>The floating pill's rectangle (open, at the width it is easing to) in this layer's coordinates, or null when no float pill is open or opening (V-12.7: a line it crosses fades).</summary>
    public Rect? FloatTarget { get; private set; }

    /// <summary>The floating pill's rectangle changed (opened, closed, or its width target moved).</summary>
    public event Action? FloatTargetChanged;

    /// <summary>
    /// ED-NEW-voicefix-ui-9: the inline pill's left end, in this layer's coordinates, at the width it is easing to while it is open; null when no inline pill is open. In a narrow
    /// composer the buttons at its left move out of the way of that end.
    /// </summary>
    public double? InlineLeftTarget { get; private set; }

    /// <summary>ED-NEW-voicefix-ui-9: the closed inline pill's left end (the disc round the orb), where an opening pill's end starts from; null before the orb is placed.</summary>
    public double? InlineHomeLeft { get; private set; }

    /// <summary>The inline pill's left end target or its closed place changed (opened, closed, or its width target or the orb moved).</summary>
    public event Action? InlineTargetChanged;

    /// <summary>The orb's slot this layer follows (the composer's controls made it).</summary>
    public void Attach(VoiceOrbSlot slot)
    {
        orb = slot;
        slot.LayoutUpdated += (_, _) => FollowOrb();
        FollowOrb();
    }

    private void FollowOrb()
    {
        if (orb is null) return;
        if (Math.Abs(Pill.OrbDiameter - orb.Diameter) > 0.001) Pill.OrbDiameter = orb.Diameter;
        // a layout pass that moved nothing asks for no more layout (this runs after every pass)
        var centre = OrbCentre();
        if (centre != placedFor)
        {
            placedFor = centre;
            InvalidateArrange();
        }
        ReportInset();
    }

    private void ShowLive(bool live, VoiceListeningStyle style)
    {
        var pill = style != VoiceListeningStyle.Cross;
        if (live && pill && !wasOpen) transcribingSeen = false;
        if (pill) Pill.Style = style; // the style takes effect with the session that began under it (V-15)
        Pill.Open = live && pill;
        wasOpen = live && pill;
        Follow();
        Step();
        ReportInset();
    }

    // ---- once a frame while the session is live or the pill is still folding -----------------------------------------------------------------------

    private void Follow()
    {
        if (link is not null || !FrameClock.IsBound || VisualRoot is null || !(voice.IsLive || voice.Preparing || Pill.IsMoving || Pill.Language is not null)) return;
        link = FrameClock.DisplayLink(Step);
        link.Add();
    }

    private void Unfollow()
    {
        link?.Invalidate();
        link = null;
    }

    private void Step()
    {
        Pill.ShowCountdown(voice.IsLive && voice.LiveStyle != VoiceListeningStyle.Cross ? countdown?.Frame() : null);
        if (voice.IsLive && !voice.Preparing && Pill.Open)
        {
            var label = voice.Session.Label;
            Pill.State = label;
            if (label != VoiceLabelState.Listening) transcribingSeen = true; // the page shows the name when the label first shows Transcribing (or Finishing)
            Pill.Language = transcribingSeen && voice.Session.LanguageNamed && voice.Session.DetectedLanguage is { } code ? VoiceLanguageNames.NameOf(code) : null;
        }
        else if (!Pill.Open && Pill.ShownOpacity <= 0 && !Pill.IsMoving)
        {
            // folded: the name leaves with the fold, the next session starts clean
            Pill.Language = null;
            transcribingSeen = false;
            Pill.State = VoiceLabelState.Listening;
        }
        ReportInset();
        if (link is not null && !voice.IsLive && !voice.Preparing && !Pill.IsMoving && Pill.Language is null)
        {
            link.Invalidate();
            link = null;
        }
    }

    private void CountdownAnnouncement(string words)
    {
        voice.Announce(words);
        Step();
        Follow();
    }

    private void ReportInset()
    {
        var d = orb?.Diameter ?? voice.OrbSettings.OrbSize;
        var style = Pill.Open ? Pill.Style : voice.LiveStyle;
        var live = voice.IsLive || voice.Preparing;
        var target = VoicePillGeometry.TextRightInset(style, live, d, Pill.OpenWidth);
        if (target != lastInset)
        {
            lastInset = target;
            InsetTarget = target;
            InsetTargetChanged?.Invoke(target);
        }
        UpdateFloat();
    }

    private void UpdateFloat()
    {
        Rect? rect = null;
        if (Pill.Open && Pill.Style == VoiceListeningStyle.Float && OrbCentre() is { } centre)
        {
            var width = Pill.OpenWidth;
            rect = new Rect(VoicePillGeometry.Left(VoiceListeningStyle.Float, centre.X, width), VoicePillGeometry.FloatTop, width, VoicePillGeometry.Height(VoiceListeningStyle.Float));
        }
        double? inline = null;
        double? home = null;
        if (OrbCentre() is { } orbCentre)
        {
            home = VoicePillGeometry.Left(VoiceListeningStyle.Pill, orbCentre.X, VoicePillGeometry.ClosedWidth(VoiceListeningStyle.Pill));
            if (Pill.Open && Pill.Style == VoiceListeningStyle.Pill) inline = VoicePillGeometry.Left(VoiceListeningStyle.Pill, orbCentre.X, Pill.OpenWidth);
        }
        if (inline != InlineLeftTarget || home != InlineHomeLeft)
        {
            InlineLeftTarget = inline;
            InlineHomeLeft = home;
            InlineTargetChanged?.Invoke();
        }
        if (rect == FloatTarget) return;
        FloatTarget = rect;
        FloatTargetChanged?.Invoke();
    }

    // ---- placement --------------------------------------------------------------------------------------------------------------------------------

    // the orb's un-lifted centre in this layer's coordinates (the float pill's lift is the orb's own transform)
    private Point? OrbCentre()
    {
        if (orb is null || orb.TranslatePoint(new Point(orb.Diameter / 2, orb.Diameter / 2), this) is not { } centre) return null;
        return centre;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Pill.Measure(Size.Infinity);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = Pill.DesiredSize.Width;
        var height = Pill.DesiredSize.Height;
        if (OrbCentre() is { } centre)
        {
            var style = Pill.Style;
            var left = VoicePillGeometry.Left(style, centre.X, width);
            var top = style == VoiceListeningStyle.Float ? VoicePillGeometry.FloatTop : centre.Y - height / 2;
            Pill.Arrange(new Rect(left, top, width, height));
        }
        else Pill.Arrange(new Rect(0, 0, width, height));
        return finalSize;
    }
}
