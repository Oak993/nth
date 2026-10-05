// PORT-ONLY CZ-4 (customization-SPEC CZ-6; OWNER OD-0085, OD-0089; DESIGN cz.js:506-546, 658-667): the Border beam's four sections of the Customization page: Look (Motion, Shape, Colours), Pace and strength, the Advanced card (folded until
// opened, shared with the Uiverse light's) and the Reset card. Every row reads and edits the app's composer light through CzLightEditor; Auto values come from BorderBeamResolved.AutoValues on the preview composer's size (CZ-6).
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class BeamSections
{
    private readonly CzLightEditor editor;
    private readonly CzAdvancedState advanced;
    private readonly Func<(double Width, double Height)> composerSize;
    private readonly Func<bool> dark;
    private bool line;

    internal CzSection Look { get; }
    internal CzSection Pace { get; }
    internal CzSection AdvancedCard { get; }
    internal CzSection Reset { get; }

    internal CzSegmentRow Motion { get; }
    internal CzRow Shape { get; }
    internal CzSegmentSets ShapeSets { get; }
    internal CzSegmentedControl RotateShapes { get; }
    internal CzSegmentedControl PulseShapes { get; }
    internal CzRow Colours { get; }
    internal CzSwatchRow Swatches { get; }
    internal CzSliderRow Speed { get; }
    internal CzSliderRow Brightness { get; }
    internal CzSliderRow Intensity { get; }
    internal CzToggleRow Follow { get; }
    internal CzAdvancedButton AdvancedButton { get; }
    internal CzRow AdvancedRow { get; }
    internal CzRowSlot AdvancedNest { get; }
    internal CzSliderRow ColourShift { get; }
    internal CzSliderRow Saturation { get; }
    internal CzSliderRow ColourDrift { get; }
    internal CzToggleRow HoldStill { get; }
    internal CzSliderRow GlowSize { get; }
    internal CzSliderRow EdgeLine { get; }
    internal CzSliderRow InnerGlow { get; }
    internal CzSliderRow OuterGlow { get; }
    internal CzSliderRow Corner { get; }
    internal CzSegmentRow TunedFor { get; }
    internal CzRowSlot OutsideCaption { get; }
    internal CzSliderRow CoreSoftness { get; }
    internal CzSliderRow BloomSoftness { get; }
    internal CzSliderRow GlowWidth { get; }
    internal CzSliderRow GlowHeight { get; }
    internal CzSliderRow GlowBoost { get; }
    internal CzRowSlot LineCaption { get; }
    internal CzSliderRow Flare { get; }
    internal CzRow Defaults { get; }
    internal CzCapsuleButton ResetButton { get; }

    private readonly CzRowSlot coreSlot, bloomSlot, widthSlot, heightSlot, boostSlot, flareSlot;
    private bool showing;

    private static readonly string[] PaletteIds = ["colorful", "mono", "ocean", "sunset", "forest", "candy", "ice", "gold"];

    internal IReadOnlyList<CzSection> Sections => [Look, Pace, AdvancedCard, Reset];

    internal BeamSections(CzLightEditor editor, CzAdvancedState advanced, Func<(double Width, double Height)> composerSize, Func<bool> dark, bool open)
    {
        this.editor = editor;
        this.advanced = advanced;
        this.composerSize = composerSize;
        this.dark = dark;
        var b = editor.Settings.Beam;
        var auto = AutoOf(b);
        line = b.Type == BeamType.Line;

        // Look
        Motion = new CzSegmentRow("Motion", CzRowText.MotionDetail(b.Family), [("rotate", "Rotate"), ("pulse", "Pulse")], b.Family == BeamFamily.Rotate ? "rotate" : "pulse",
            id => editor.EditBeam(o => o.WithFamily(id == "rotate" ? BeamFamily.Rotate : BeamFamily.Pulse)));
        RotateShapes = new CzSegmentedControl("Shape", [("md", "Large"), ("line", "Line"), ("sm", "Small")], b.Family == BeamFamily.Rotate ? ComposerLightSettings.TypeName(b.Type) : "md", small: true);
        PulseShapes = new CzSegmentedControl("Shape", [("pulse-inner", "Inside"), ("pulse-outside", "Outside")], b.Family == BeamFamily.Pulse ? ComposerLightSettings.TypeName(b.Type) : "pulse-inner", small: true);
        ShapeSets = new CzSegmentSets(RotateShapes, PulseShapes, b.Family == BeamFamily.Rotate);
        RotateShapes.SelectionChanged += PickShape;
        PulseShapes.SelectionChanged += PickShape;
        Shape = new CzRow("Shape", CzText.ShapeDetails[ComposerLightSettings.TypeName(b.Type)], ShapeSets);
        Swatches = CzSwatchRow.BeamPalettes(ComposerLightSettings.PaletteName(b.Palette));
        Swatches.SelectionChanged += id => { if (!showing) editor.EditBeam(o => o with { Palette = (BeamPalette)Array.IndexOf(PaletteIds, id) }); };
        Colours = new CzRow("Colours", CzText.PaletteNames[ComposerLightSettings.PaletteName(b.Palette)], Swatches);
        var look = new CzRowStack();
        look.Add(Motion.Row);
        look.Add(Shape);
        look.Add(Colours);
        Look = new CzSection("Look", look, open);

        // Pace and strength
        Speed = new CzSliderRow(editor, "Speed", 0.5, 14, 0.05, inverted: true, CzRowText.Seconds, CzRowText.SpeedDetail(b.Duration is null, b.Type), b.Duration, auto.Duration, v => editor.EditBeam(o => o with { Duration = v }));
        Brightness = new CzSliderRow(editor, "Brightness", 0.2, 3, 0.05, inverted: false, CzRowText.X2, CzRowText.BrightnessDetail(b.Brightness is null), b.Brightness, auto.Brightness, v => editor.EditBeam(o => o with { Brightness = v }));
        Intensity = Plain("Intensity", 0, 1, 0.01, CzRowText.Percent01, "How strongly the light shows", b.Strength, (o, v) => o with { Strength = v });
        Follow = new CzToggleRow("Follow the composer", "Softer at rest, fuller as you type and while a turn runs", () => editor.Settings.Beam.Respond, v => editor.EditBeam(o => o with { Respond = v }));
        var pace = new CzRowStack();
        pace.Add(Speed.Row);
        pace.Add(Brightness.Row);
        pace.Add(Intensity.Row);
        pace.Add(Follow.Row);
        Pace = new CzSection("Pace and strength", pace, open);

        // Advanced
        AdvancedButton = new CzAdvancedButton("Advanced", "Colour recipe, glow layers and corner");
        AdvancedRow = new CzRow("Advanced", "Colour recipe, glow layers and corner", AdvancedButton);
        ColourShift = Plain("Colour shift", -180, 180, 1, CzRowText.Deg, "Turns every colour around the wheel", b.HueShift, (o, v) => o with { HueShift = v });
        Saturation = new CzSliderRow(editor, "Saturation", 0, 2.5, 0.05, inverted: false, CzRowText.X2, CzRowText.SaturationDetail(b.Saturation is null), b.Saturation, auto.Saturation, v => editor.EditBeam(o => o with { Saturation = v }));
        ColourDrift = Plain("Colour drift", 0, 180, 1, v => CzRowText.Drift(v, line), CzRowText.DriftDetail(b), b.HueRange, (o, v) => o with { HueRange = v });
        HoldStill = new CzToggleRow("Hold colours still", CzRowText.HoldDetail(b.Palette == BeamPalette.Mono),
            () => editor.Settings.Beam.Palette == BeamPalette.Mono || editor.Settings.Beam.StaticColors, v => editor.EditBeam(o => o with { StaticColors = v }));
        GlowSize = Plain("Glow size", 0.25, 3, 0.05, CzRowText.X2, "Softness of every glow", b.GlowSize, (o, v) => o with { GlowSize = v });
        EdgeLine = Plain("Edge line", 0, 2, 0.05, CzRowText.X2, "The thin line on the edge", b.StrokeX, (o, v) => o with { StrokeX = v });
        InnerGlow = Plain("Inner glow", 0, 2, 0.05, CzRowText.X2, "The wash inside the edge", b.InnerX, (o, v) => o with { InnerX = v });
        OuterGlow = Plain("Outer glow", 0, 2, 0.05, CzRowText.X2, "The bloom beyond the edge", b.BloomX, (o, v) => o with { BloomX = v });
        // the page's Corner runs 0..26; a stored value above that (the store allows 32) shows at the right end and is not rewritten until the user moves it (CZ-13.2)
        Corner = new CzSliderRow(editor, "Corner", 0, 26, 1, inverted: false, CzRowText.Corner, CzRowText.RadiusDetail(b.Radius is null), b.Radius, ComposerLightAnimator.ComposerRadius, v => editor.EditBeam(o => o with { Radius = v }));
        TunedFor = new CzSegmentRow("Tuned for", CzRowText.TunedDetail(b.Theme), [("auto", "Auto"), ("dark", "Dark"), ("light", "Light")], ThemeId(b.Theme),
            id => editor.EditBeam(o => o with { Theme = id switch { "dark" => BeamTheme.Dark, "light" => BeamTheme.Light, _ => BeamTheme.Auto } }));
        var outside = new CzCaptionRow("Outside glow");
        CoreSoftness = new CzSliderRow(editor, "Core softness", 0, 20, 0.5, inverted: false, CzRowText.Px, "The bright glow near the edge", b.CoreBlur, auto.CoreBlur, v => editor.EditBeam(o => o with { CoreBlur = v }));
        BloomSoftness = new CzSliderRow(editor, "Bloom softness", 0, 60, 0.5, inverted: false, CzRowText.Px, "The wide glow around it", b.BloomBlur, auto.BloomBlur, v => editor.EditBeam(o => o with { BloomBlur = v }));
        GlowWidth = new CzSliderRow(editor, "Glow width", 0.35, 4, 0.01, inverted: false, CzRowText.X2, "How far it spreads sideways", b.GlowX, auto.GlowX, v => editor.EditBeam(o => o with { GlowX = v }));
        GlowHeight = new CzSliderRow(editor, "Glow height", 0.35, 4, 0.01, inverted: false, CzRowText.X2, "How far it spreads up and down", b.GlowY, auto.GlowY, v => editor.EditBeam(o => o with { GlowY = v }));
        GlowBoost = Plain("Glow boost", 0.5, 2, 0.01, CzRowText.X2, "Extra brightness in the glow", b.Boost, (o, v) => o with { Boost = v });
        var lineCaption = new CzCaptionRow("Line");
        Flare = Plain("Flare", 0, 3, 0.05, CzRowText.X2, "How much the streak flares as it goes", b.Spike, (o, v) => o with { Spike = v });

        var po = b.Type == BeamType.PulseOutside;
        var nest = new CzRowStack();
        nest.Add(ColourShift.Row);
        nest.Add(Saturation.Row);
        nest.Add(ColourDrift.Row);
        nest.Add(HoldStill.Row);
        nest.Add(GlowSize.Row);
        nest.Add(EdgeLine.Row);
        nest.Add(InnerGlow.Row);
        nest.Add(OuterGlow.Row);
        nest.Add(Corner.Row);
        nest.Add(TunedFor.Row);
        OutsideCaption = nest.Add(outside, caption: true, open: po);
        coreSlot = nest.Add(CoreSoftness.Row, open: po);
        bloomSlot = nest.Add(BloomSoftness.Row, open: po);
        widthSlot = nest.Add(GlowWidth.Row, open: po);
        heightSlot = nest.Add(GlowHeight.Row, open: po);
        boostSlot = nest.Add(GlowBoost.Row, open: po);
        LineCaption = nest.Add(lineCaption, caption: true, open: line);
        flareSlot = nest.Add(Flare.Row, open: line);
        var advancedRows = new CzRowStack();
        advancedRows.Add(AdvancedRow);
        AdvancedNest = advancedRows.Add(nest, open: advanced.Open);
        AdvancedCard = new CzSection(null, advancedRows, open);
        // the open state is shared with the Uiverse light's Advanced row and is the page's (session) state
        AdvancedButton.Open = advanced.Open;
        AdvancedButton.OpenChanged += isOpen => advanced.Open = isOpen;
        advanced.Changed += SyncAdvanced;

        // Reset
        ResetButton = new CzCapsuleButton("Reset to defaults");
        ResetButton.SetAtDefaults(CzRowText.BeamIsDefault(b), animate: false);
        ResetButton.Invoked += () => editor.EditBeam(_ => BorderBeamOptions.Default);
        Defaults = new CzRow("Defaults", CzRowText.BeamResetDetail(CzRowText.BeamIsDefault(b)), ResetButton);
        var reset = new CzRowStack();
        reset.Add(Defaults);
        Reset = new CzSection(null, reset, open);
    }

    private CzSliderRow Plain(string title, double min, double max, double step, Func<double, string> format, string detail, double value, Func<BorderBeamOptions, double, BorderBeamOptions> set) =>
        new(editor, title, min, max, step, inverted: false, format, detail, value, null, v => { if (v is { } x) editor.EditBeam(o => set(o, x)); });

    private (double Duration, double Brightness, double Saturation, double CoreBlur, double BloomBlur, double GlowX, double GlowY) AutoOf(BorderBeamOptions b)
    {
        var (w, h) = composerSize();
        return BorderBeamResolved.AutoValues(b, dark(), w, h, ComposerLightAnimator.ComposerRadius);
    }

    private static string ThemeId(BeamTheme theme) => theme switch { BeamTheme.Dark => "dark", BeamTheme.Light => "light", _ => "auto" };

    private void PickShape(string id)
    {
        if (showing) return;
        var type = id switch { "md" => BeamType.Large, "line" => BeamType.Line, "sm" => BeamType.Small, "pulse-inner" => BeamType.PulseInner, _ => BeamType.PulseOutside };
        editor.EditBeam(o => o.WithType(type));
    }

    private void SyncAdvanced()
    {
        AdvancedButton.Open = advanced.Open;
        AdvancedNest.Open = advanced.Open;
    }

    /// <summary>Shows what the settings say now (the sections' folds for the style are the page's).</summary>
    internal void Apply()
    {
        var b = editor.Settings.Beam;
        var auto = AutoOf(b);
        var typeName = ComposerLightSettings.TypeName(b.Type);
        var mono = b.Palette == BeamPalette.Mono;
        line = b.Type == BeamType.Line;
        var po = b.Type == BeamType.PulseOutside;
        showing = true;
        try
        {
            Motion.Apply(b.Family == BeamFamily.Rotate ? "rotate" : "pulse", CzRowText.MotionDetail(b.Family));
            ShapeSets.ShowFirst(b.Family == BeamFamily.Rotate);
            if (b.Family == BeamFamily.Rotate) RotateShapes.Show(typeName); else PulseShapes.Show(typeName);
            Swatches.Show(ComposerLightSettings.PaletteName(b.Palette));
            TunedFor.Apply(ThemeId(b.Theme), CzRowText.TunedDetail(b.Theme));
        }
        finally { showing = false; }
        Shape.SetDetail(CzText.ShapeDetails[typeName]);
        Colours.SetDetail(CzText.PaletteNames[ComposerLightSettings.PaletteName(b.Palette)]);
        Speed.Apply(b.Duration, auto.Duration, CzRowText.SpeedDetail(b.Duration is null, b.Type));
        Brightness.Apply(b.Brightness, auto.Brightness, CzRowText.BrightnessDetail(b.Brightness is null));
        Intensity.Apply(b.Strength, null, "How strongly the light shows");
        Follow.Apply("Softer at rest, fuller as you type and while a turn runs");
        ColourShift.Apply(b.HueShift, null, "Turns every colour around the wheel");
        Saturation.Apply(b.Saturation, auto.Saturation, CzRowText.SaturationDetail(b.Saturation is null));
        ColourDrift.Apply(b.HueRange, null, CzRowText.DriftDetail(b), CzRowText.DriftDimmed(b));
        HoldStill.Apply(CzRowText.HoldDetail(mono), mono);
        GlowSize.Apply(b.GlowSize, null, "Softness of every glow");
        EdgeLine.Apply(b.StrokeX, null, "The thin line on the edge");
        InnerGlow.Apply(b.InnerX, null, "The wash inside the edge");
        OuterGlow.Apply(b.BloomX, null, "The bloom beyond the edge");
        Corner.Apply(b.Radius, ComposerLightAnimator.ComposerRadius, CzRowText.RadiusDetail(b.Radius is null));
        CoreSoftness.Apply(b.CoreBlur, auto.CoreBlur, "The bright glow near the edge");
        BloomSoftness.Apply(b.BloomBlur, auto.BloomBlur, "The wide glow around it");
        GlowWidth.Apply(b.GlowX, auto.GlowX, "How far it spreads sideways");
        GlowHeight.Apply(b.GlowY, auto.GlowY, "How far it spreads up and down");
        GlowBoost.Apply(b.Boost, null, "Extra brightness in the glow");
        Flare.Apply(b.Spike, null, "How much the streak flares as it goes");
        OutsideCaption.Open = coreSlot.Open = bloomSlot.Open = widthSlot.Open = heightSlot.Open = boostSlot.Open = po;
        LineCaption.Open = flareSlot.Open = line;
        var isDefault = CzRowText.BeamIsDefault(b);
        ResetButton.SetAtDefaults(isDefault);
        Defaults.SetDetail(CzRowText.BeamResetDetail(isDefault));
    }
}
