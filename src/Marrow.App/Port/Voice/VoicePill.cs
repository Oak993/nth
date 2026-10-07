// PORT-ONLY, owner-ruled addition (OD-0091 fourth to sixth amendments, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.1 to V-12.4, V-12.9, V-12.10, V-12.12, V-15): the listening pill.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The glass pill the orb opens into while voice input listens and finishes (voice-composer.html <c>.vpill</c>; voice-options.html for the clean pill): the orb's cap at one end, the label, the cancel cross
/// in its own disc at the other end. It draws itself at <see cref="VoicePillGeometry"/>'s sizes, with the shared <see cref="GlassMaterial"/> as its body (a capsule, tinted <c>pgBg</c>) and
/// <see cref="VoicePillRim"/> over it for the edge and the light; the host places it (the orb's centre is the pill's anchor) and tells it what to show. Every change is a CSS transition of the design,
/// run by <see cref="LookMotion"/> on the frame clock (OD-0076, nothing cuts; Reduce Motion lands each at once):
/// <list type="bullet">
/// <item>opening: the width from H to W over 0.42 s ez (the pill's one end stays at the orb), the pill's opacity 0.14 s ease; the label in over 0.26 s ease after 0.14 s, sliding in 4 px over
/// 0.4 s ez after 0.1 s; the cross disc in over 0.22 s ease after 0.16 s, from scale 0.6 over 0.38 s ez after 0.12 s;</item>
/// <item>folding: the width to H over 0.42 s ez, the opacity 0.18 s ease after 0.24 s; the label out over 0.2 s ease and 4 px back over 0.32 s ez; the cross out over 0.16 s ease, scale 0.6 over 0.3 s ez;</item>
/// <item>a new label state: the old words out (0.2 s, 0.32 s), the new in (0.26 s after 0.14 s, 0.4 s after 0.1 s), the width easing to the new words over 0.42 s ez (V-15 Listening row);</item>
/// <item>new words while open (V-12.9, the page's <c>.relabel</c>): the shown label out over 0.14 s ease, the words swapped 0.16 s after the change, the new label in by its own crossfade
/// (opacity 0.26 s ease after 0.14 s) and the width easing to the new words over 0.42 s ez; while closed they apply at the next opening;</item>
/// <item>the language name (V-12.10): in over 0.3 s ease after 0.1 s, or with the label's own crossfade when the label changes in that moment; another name crossfades (out 0.1 s, in 0.2 s after 0.08 s).</item>
/// </list>
/// The text sweep (V-12.3) advances only while the pill is open and Reduce Motion is off. ENGINEERING DECISION (V-12.4 against V-12.12): the drop shadow (<c>pgDrop</c>, the float's
/// <c>0 14 30 -12</c>) is not drawn: the page's radial mask (the OD-0097 amendment's fix) is a CSS mask, whose painted area is the border box, so it clips the box shadow with the rest of the pill's
/// material, and V-T132 asks for no difference beside the orb.
/// </summary>
internal sealed class VoicePill : Panel
{
    private static readonly string[] StateNames = ["Listening", "Transcribing", "Finishing"];
    private static readonly TimeSpan RelabelSwap = TimeSpan.FromSeconds(0.16);
    private const double SweepCycle = 2.35;

    private readonly LookMotion motion;
    private readonly LookNumber widthValue, opacityValue, crossOpacity, crossScale, nameOpacity, outgoingOpacity;
    private readonly LookNumber[] labelOpacity = new LookNumber[3];
    private readonly LookNumber[] labelShift = new LookNumber[3];
    private readonly VoicePillLabel[] labels = new VoicePillLabel[3];
    private readonly TranslateTransform[] shifts = [new(-4, 0), new(-4, 0), new(-4, 0)];
    private readonly Border body;
    private readonly VoicePillRim rim = new();
    private readonly Panel material = new() { IsHitTestVisible = false, Background = Brushes.Transparent };
    private readonly Border filtered = new() { IsHitTestVisible = false };
    private readonly Panel contents = new() { Background = Brushes.Transparent };
    private readonly ScaleTransform crossTransform = new(0.6, 0.6);
    private VoiceListeningStyle style = VoiceListeningStyle.Pill;
    private bool open;
    private bool relabeling;
    private VoiceLabelState state = VoiceLabelState.Listening;
    private VoicePillWords set = VoicePillWords.L1;
    private string? language;
    private double orbDiameter = 32;
    private WorkItem? relabelTimer;
    private int relabelSerial;
    private ContinuousInstant? stateChangedAt;
    private DisplayLink? sweepLink;
    private ContinuousInstant sweepLast;
    private double sweepSeconds;
    private double heldWidth;

    public VoicePill()
    {
        motion = new LookMotion(this);
        // The CSS width/anchor and the cap's mask are fractional throughout an ease. Rounding only the laid-out
        // body gives it a different silhouette from its clip, including while a size spring is running.
        UseLayoutRounding = false;
        widthValue = new LookNumber(motion, VoicePillGeometry.ClosedWidth(style));
        opacityValue = new LookNumber(motion, 0);
        crossOpacity = new LookNumber(motion, 0);
        crossScale = new LookNumber(motion, 0.6);
        nameOpacity = new LookNumber(motion, 1);
        outgoingOpacity = new LookNumber(motion, 0);
        IsHitTestVisible = false;
        ClipToBounds = false;
        body = new Border { IsHitTestVisible = false }.Capsule(RoundedCornerStyle.Circular);
        Cross = new VoiceCross(VoiceCrossLook.Disc) { RenderTransformOrigin = RelativePoint.Center };
        Cross.RenderTransform = new TransformGroup { Children = { Cross.PressTransform, crossTransform } };
        material.Children.Add(body);
        material.Children.Add(rim);
        filtered.Child = material;
        Children.Add(filtered);
        // CSS element opacity fades the composed cross and each label, including overlapping paints.
        RenderOptions.SetRequiresFullOpacityHandling(contents, true);
        Children.Add(contents);
        for (var k = 0; k < 3; k++)
        {
            labels[k] = new VoicePillLabel
            {
                Opacity = 0,
                RenderTransform = shifts[k],
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            labelOpacity[k] = new LookNumber(motion, 0);
            labelShift[k] = new LookNumber(motion, -4);
            contents.Children.Add(labels[k]);
        }
        contents.Children.Add(Cross);
        Cross.Opacity = 0;
        Cross.IsHitTestVisible = false;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite); // V-12.9: the label is the pill's polite live region
        ApplyWords();
        ApplyStyle();
        motion.Frame += Draw;
        DetachedFromVisualTree += (_, _) => StopSweep();
        AttachedToVisualTree += (_, _) => { if (open) StartSweep(); };
        ActualThemeVariantChanged += (_, _) => ApplyTint();
        ApplyTint();
        Draw();
    }

    /// <summary>The cancel cross in its disc (V-12.3).</summary>
    public VoiceCross Cross { get; }

    /// <summary>The glass body, the shared material's owner (a test reads its effect).</summary>
    internal Border Body => body;

    /// <summary>The label of each state (a test reads their runs and opacity).</summary>
    internal IReadOnlyList<VoicePillLabel> Labels => labels;

    /// <summary>Inline (<see cref="VoiceListeningStyle.Pill"/>) or float (<see cref="VoiceListeningStyle.Float"/>); the style takes effect with the next opening (V-15). Cross draws no pill.</summary>
    public VoiceListeningStyle Style
    {
        get => style;
        set
        {
            if (value == VoiceListeningStyle.Cross) value = VoiceListeningStyle.Pill;
            if (style == value) return;
            style = value;
            ApplyStyle();
            ApplyWords();
            Retarget();
        }
    }

    /// <summary>The pill is open: it grows from the orb and shows its label (Listening and Processing); closed it is a disc round the orb, clear.</summary>
    public bool Open
    {
        get => open;
        set
        {
            if (open == value) return;
            open = value;
            if (open) StartSweep();
            else StopSweep();
            Retarget();
        }
    }

    /// <summary>The state whose words show (V-6.3's rule is the session's; the pill draws the one it is given).</summary>
    public VoiceLabelState State
    {
        get => state;
        set
        {
            if (state == value) return;
            state = value;
            stateChangedAt = SwiftClock.Now;
            Retarget();
        }
    }

    /// <summary>The picked word set (V-12.9). While open the change relabels; closed it waits for the next opening.</summary>
    public VoicePillWords Words
    {
        get => set;
        set
        {
            if (set == value) return;
            set = value;
            if (!open)
            {
                ApplyWords();
                Retarget();
                return;
            }
            Relabel();
        }
    }

    /// <summary>The language name once it is to show (V-12.10), null for none; the pill lays it out and eases its width. A name that arrives while the label changes arrives with it.</summary>
    public string? Language
    {
        get => language;
        set
        {
            if (language == value) return;
            var was = language;
            language = value;
            ShowLanguage(was, value);
        }
    }

    /// <summary>The orb's slot diameter: the radius the pill's material fades out round (V-12.12).</summary>
    public double OrbDiameter
    {
        get => orbDiameter;
        set
        {
            if (orbDiameter == value) return;
            orbDiameter = value;
            ApplyMask();
        }
    }

    /// <summary>The width the open pill is, or is easing to: the label shown, the name, H + 8 + label + name + 8 + 24 + 8 (V-12.2, V-12.10).</summary>
    public double OpenWidth => VoicePillGeometry.OpenWidth(style, labels[(int)state].WordsWidth, labels[(int)state].NameWidth);

    /// <summary>The width now (eased).</summary>
    public double ShownWidth => widthValue.Value;

    /// <summary>The width it is easing to: the open width, or H closed.</summary>
    public double TargetWidth => widthValue.Target;

    public double PillHeight => VoicePillGeometry.Height(style);

    /// <summary>The pill's own opacity now (0.14 s in; 0.18 s out after 0.24 s).</summary>
    public double ShownOpacity => opacityValue.Value;

    /// <summary>The label of <paramref name="which"/> as drawn now: its opacity.</summary>
    public double LabelOpacity(VoiceLabelState which) => labelOpacity[(int)which].Value;

    public double LabelShift(VoiceLabelState which) => labelShift[(int)which].Value;

    public double CrossOpacity => crossOpacity.Value;

    public double CrossScale => crossScale.Value;

    public double NameOpacity => nameOpacity.Value;

    /// <summary>The text sweep's position now, 1.35 to -0.55 in the design's terms (a test reads it).</summary>
    public double SweepPosition => VoicePillSweep.Position(sweepSeconds);

    /// <summary>Whether the sweep is running (open, Reduce Motion off).</summary>
    public bool SweepRunning => sweepLink is not null;

    /// <summary>True while any transition of the pill is still moving.</summary>
    public bool IsMoving => motion.IsMoving || relabelTimer is not null;

    private bool Still => motion.Still;

    // ---- the targets: the CSS classes of the design (open, on, relabel) as the values they set ----------------------------------------

    private void Retarget()
    {
        var h = PillHeight;
        // `.vpill` (closed): opacity .18s ease .24s, left and width .42s ez; `.vpill.open`: opacity .14s ease, the same left and width
        if (open) opacityValue.To(1, LookCurve.Ease(0.14));
        else opacityValue.To(0, LookCurve.Ease(0.18), 0.24);
        widthValue.To(open ? OpenWidth : h, LookCurve.Ez(0.42));
        // `.vpill .px`: out opacity .16s ease, transform .3s ez; `.vpill.open .px`: opacity .22s ease .16s, transform .38s ez .12s
        if (open)
        {
            crossOpacity.To(1, LookCurve.Ease(0.22), 0.16);
            crossScale.To(1, LookCurve.Ez(0.38), 0.12);
        }
        else
        {
            crossOpacity.To(0, LookCurve.Ease(0.16));
            crossScale.To(0.6, LookCurve.Ez(0.3));
        }
        for (var k = 0; k < 3; k++)
        {
            var on = k == (int)state;
            if (relabeling && on) labelOpacity[k].To(0, LookCurve.Ease(0.14)); // `.vpill.relabel .pl.on{opacity:0!important;transition:opacity .14s ease!important}`
            else if (open && on) labelOpacity[k].To(1, LookCurve.Ease(0.26), 0.14); // `.vpill.open .pl.on`
            else labelOpacity[k].To(0, LookCurve.Ease(0.2)); // `.pl`
            if (open && on) labelShift[k].To(0, LookCurve.Ez(0.4), 0.1);
            else labelShift[k].To(-4, LookCurve.Ez(0.32));
        }
        Cross.IsHitTestVisible = open; // `.vpill.open{pointer-events:auto}`
        IsHitTestVisible = open; // disabling the panel would also disable its cancel control
        Cross.Live = open;
        InvalidateMeasure();
        Draw();
    }

    // V-12.9: the shown label out over 0.14 s, 0.16 s after the change the words are replaced and it comes back by its own crossfade, the width easing to the new words
    private void Relabel()
    {
        relabeling = true;
        relabelTimer?.Cancel();
        var serial = ++relabelSerial;
        Retarget();
        if (Still)
        {
            Settle();
            return;
        }
        relabelTimer = MainActor.AsyncAfter(RelabelSwap, () =>
        {
            if (serial != relabelSerial) return;
            relabelTimer = null;
            ApplyWords();
            relabeling = false;
            Retarget();
        });
    }

    private void Settle()
    {
        relabelTimer?.Cancel();
        relabelTimer = null;
        relabeling = false;
        ApplyWords();
        Retarget();
    }

    private void ShowLanguage(string? was, string? now)
    {
        foreach (var label in labels) label.OutgoingName = null;
        if (now is null)
        {
            foreach (var label in labels) label.LanguageName = null;
            nameOpacity.Jump(1);
            outgoingOpacity.Jump(0);
            Retarget();
            return;
        }
        if (was is null)
        {
            // arrives with the label's own crossfade when the label changes in this very moment, else over 0.3 s after 0.1 s
            var withLabel = stateChangedAt is { } at && SwiftClock.Now - at < TimeSpan.FromMilliseconds(5);
            foreach (var label in labels) label.LanguageName = now;
            if (withLabel || Still) nameOpacity.Jump(1);
            else
            {
                nameOpacity.Jump(0);
                nameOpacity.To(1, LookCurve.Ease(0.3), 0.1);
            }
        }
        else
        {
            // another language: the old name out over 0.1 s, the new in over 0.2 s after 0.08 s
            foreach (var label in labels)
            {
                label.OutgoingName = was;
                label.LanguageName = now;
            }
            outgoingOpacity.Jump(1);
            outgoingOpacity.To(0, LookCurve.Ease(0.1));
            nameOpacity.Jump(0);
            nameOpacity.To(1, LookCurve.Ease(0.2), 0.08);
        }
        Retarget();
    }

    private void ApplyWords()
    {
        var words = VoicePillWordSets.For(set);
        labels[0].Words = words.Listening;
        labels[1].Words = words.Transcribing;
        labels[2].Words = words.Finishing;
    }

    private void ApplyStyle()
    {
        foreach (var label in labels) label.FontSize = VoicePillGeometry.FontSize(style);
        var h = PillHeight;
        // a closed pill is a disc of the style's height
        if (!open) widthValue.Jump(h);
        Height = h;
        Width = widthValue.Value;
        UpdateClip(widthValue.Value);
        ApplyMask();
        InvalidateArrange();
    }

    // ---- the picture ------------------------------------------------------------------------------------------------------------------

    private void ApplyTint()
    {
        var tokens = VoicePillTokens.For(ActualThemeVariant);
        // V-12.4: the shared material in a capsule, tinted pgBg. OD-0121: no refraction (the page's pill is backdrop-filter blur saturate only); OD-0123: no contact line outside the ends; the glass's own frost, rim light and inner shade stay
        var tint = new SwiftColor(tokens.PgBg.R / 255.0, tokens.PgBg.G / 255.0, tokens.PgBg.B / 255.0, 1);
        SwiftGlass.SetEffect(body, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Circular)).Tint(tint, tokens.PgBg.A / 255.0).WithoutRefraction().WithoutContactLine());
    }

    private void ApplyMask()
    {
        var h = PillHeight;
        var width = Math.Max(widthValue.Value, h);
        var centre = style == VoiceListeningStyle.Float ? new Point(h / 2, h / 2) : new Point(width - h / 2, h / 2);
        var mask = VoicePillRim.OrbMask(centre, h, orbDiameter);
        // OD-0121 (5): V-12.12's radial mask, centred on the orb: the shared glass and its rim together in the capsule's coordinates (the material's custom visual has
        // larger sampling bounds than its border; masking that visual separately shifts the cap), and the label and cross with the same mask.
        material.OpacityMask = VoicePillRim.OrbFade(centre, h, style);
        filtered.OpacityMask = mask;
        contents.OpacityMask = mask;
        OpacityMask = null;
        body.OpacityMask = null;
        rim.OpacityMask = null;
    }

    // after the values moved: draw what they hold
    private void Draw()
    {
        var width = widthValue.Value;
        if (width != heldWidth)
        {
            heldWidth = width;
            Width = width;
            ApplyMask();
            UpdateClip(width);
            InvalidateArrange();
        }
        Opacity = opacityValue.Value;
        for (var k = 0; k < 3; k++)
        {
            labels[k].Opacity = labelOpacity[k].Value;
            shifts[k].X = labelShift[k].Value;
            labels[k].NameOpacity = nameOpacity.Value;
            labels[k].OutgoingOpacity = outgoingOpacity.Value;
        }
        Cross.Opacity = crossOpacity.Value;
        crossTransform.ScaleX = crossTransform.ScaleY = crossScale.Value;
        foreach (var label in labels) label.Sweep = VoicePillSweep.Position(sweepSeconds);
        if (outgoingOpacity.Value <= 0 && labels[0].OutgoingName is not null) foreach (var label in labels) label.OutgoingName = null;
        AutomationProperties.SetName(this, labels[(int)state].Words + (language is null ? "" : " " + language));
    }

    // `.vpill{overflow:hidden}`: the label and the cross stay inside the capsule while it opens and folds
    internal Geometry ContentClip { get; private set; } = new RectangleGeometry();

    private void UpdateClip(double width)
    {
        ContentClip = new RectangleGeometry(new Rect(0, 0, Math.Max(width, 0), PillHeight), PillHeight / 2, PillHeight / 2);
        // OD-0121 (2): the capsule's edge is antialiased once, by the shared glass's own distance field (and the rim's rounded rectangle). The material takes a
        // plain rectangle (its own bounds), not a second antialiased capsule, which would square the edge pixels' coverage; only the label and cross take the capsule.
        material.Clip = null;
        material.ClipToBounds = true;
        contents.Clip = ContentClip;
        Clip = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        filtered.Measure(new Size(widthValue.Value, PillHeight));
        foreach (var child in contents.Children) child.Measure(Size.Infinity);
        contents.Measure(new Size(widthValue.Value, PillHeight));
        return new Size(widthValue.Value, PillHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var h = PillHeight;
        var width = finalSize.Width;
        filtered.Arrange(new Rect(0, 0, width, h));
        contents.Arrange(new Rect(0, 0, width, h));
        var left = VoicePillGeometry.LabelLeft(style);
        for (var k = 0; k < 3; k++)
        {
            var size = labels[k].DesiredSize;
            labels[k].Arrange(new Rect(left, (h - size.Height) / 2, size.Width, size.Height));
        }
        var crossX = style == VoiceListeningStyle.Float ? width - VoicePillGeometry.Pad - VoicePillGeometry.CrossSize : VoicePillGeometry.Pad;
        Cross.Arrange(new Rect(crossX, (h - VoicePillGeometry.CrossSize) / 2, VoicePillGeometry.CrossSize, VoicePillGeometry.CrossSize));
        return finalSize;
    }

    // ---- the text sweep ---------------------------------------------------------------------------------------------------------------

    private void StartSweep()
    {
        if (sweepLink is not null || !FrameClock.IsBound || VisualRoot is null || Still) return;
        sweepLast = SwiftClock.Now;
        sweepLink = FrameClock.DisplayLink(StepSweep);
        sweepLink.Add();
    }

    private void StopSweep()
    {
        sweepLink?.Invalidate();
        sweepLink = null;
    }

    private void StepSweep()
    {
        var now = SwiftClock.Now;
        sweepSeconds += (now - sweepLast).TotalSeconds;
        sweepLast = now;
        var position = VoicePillSweep.Position(sweepSeconds);
        foreach (var label in labels) label.Sweep = position;
    }

    /// <summary>The state names, for a test's messages.</summary>
    internal static string NameOf(VoiceLabelState which) => StateNames[(int)which];
}
