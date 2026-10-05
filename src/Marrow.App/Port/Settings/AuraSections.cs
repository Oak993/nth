// PORT-ONLY CZ-4 (customization-SPEC CZ-7; OWNER OD-0085, OD-0089, OD-0099 Q-CZ-3 B; DESIGN cz.js:548-569, 668-672): the Uiverse light's five sections of the Customization page: Look (Colours, the two Custom colour wells, Rim light,
// Halftone), Motion, Brightness by moment, the Advanced card (shared with the Border beam's) and the Reset card. The wells open the glass colour picker (CZ-2) through CzWellPicker.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class AuraSections
{
    private readonly CzLightEditor editor;
    private readonly Func<string> makerName, accentName;

    internal CzSection Look { get; }
    internal CzSection Motion { get; }
    internal CzSection Moments { get; }
    internal CzSection AdvancedCard { get; }
    internal CzSection Reset { get; }

    internal CzSegmentRow Colours { get; }
    internal CzRowSlot YourColoursSlot { get; }
    internal CzRow YourColours { get; }
    internal CzColorWell FirstWell { get; }
    internal CzColorWell SecondWell { get; }
    internal CzWellPicker Picker { get; } = new();
    internal CzToggleRow RimLight { get; }
    internal CzToggleRow Halftone { get; }
    internal CzSliderRow Speed { get; }
    internal CzToggleRow Breathing { get; }
    internal CzSliderRow AtRest { get; }
    internal CzSliderRow WhileTyping { get; }
    internal CzSliderRow WhileRunning { get; }
    internal CzAdvancedButton AdvancedButton { get; }
    internal CzRow AdvancedRow { get; }
    internal CzRowSlot AdvancedNest { get; }
    internal CzSliderRow HaloSize { get; }
    internal CzSliderRow HaloSoftness { get; }
    internal CzSliderRow RimWidth { get; }
    internal CzSliderRow DotSpacing { get; }
    internal CzSliderRow DotStrength { get; }
    internal CzRow Defaults { get; }
    internal CzCapsuleButton ResetButton { get; }

    internal IReadOnlyList<CzSection> Sections => [Look, Motion, Moments, AdvancedCard, Reset];

    /// <param name="makerName">The preview maker's name (Colours: Model).</param>
    /// <param name="accentName">The user's accent colour's name (Colours: Accent).</param>
    internal AuraSections(CzLightEditor editor, CzAdvancedState advanced, Func<string> makerName, Func<string> accentName, bool open)
    {
        this.editor = editor;
        this.makerName = makerName;
        this.accentName = accentName;
        var a = editor.Settings.Aura;

        // Look
        Colours = new CzSegmentRow("Colours", ColoursDetail(a.Scheme), [("original", "Original"), ("maker", "Model"), ("accent", "Accent"), ("custom", "Custom")], SchemeId(a.Scheme),
            id => editor.EditAura(o => o with { Scheme = id switch { "maker" => AuraScheme.Maker, "accent" => AuraScheme.Accent, "custom" => AuraScheme.Custom, _ => AuraScheme.Original } }));
        FirstWell = new CzColorWell("First colour", "One side", a.Color1, Picker.Toggle);
        SecondWell = new CzColorWell("Second colour", "The other", a.Color2, Picker.Toggle);
        FirstWell.ColorChanged += hex => editor.EditAura(o => o with { Color1 = hex });
        SecondWell.ColorChanged += hex => editor.EditAura(o => o with { Color2 = hex });
        YourColours = new CzRow("Your colours", "One for each side of the glow", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { FirstWell, SecondWell } });
        RimLight = new CzToggleRow("Rim light", "A bright line hugging the edge", () => editor.Settings.Aura.Rim, v => editor.EditAura(o => o with { Rim = v }));
        Halftone = new CzToggleRow("Halftone", "The glow seen through fine dots", () => editor.Settings.Aura.Dots, v => editor.EditAura(o => o with { Dots = v }));
        var look = new CzRowStack();
        look.Add(Colours.Row);
        YourColoursSlot = look.Add(YourColours, open: a.Scheme == AuraScheme.Custom);
        look.Add(RimLight.Row);
        look.Add(Halftone.Row);
        Look = new CzSection("Look", look, open);

        // Motion
        Speed = new CzSliderRow(editor, "Speed", 3, 40, 0.5, inverted: true, CzRowText.Seconds1, "Seconds for one turn", a.Speed, null, v => Set(v, (o, x) => o with { Speed = x }));
        Breathing = new CzToggleRow("Breathing", "The glow slowly swells and settles", () => editor.Settings.Aura.Breathing, v => editor.EditAura(o => o with { Breathing = v }));
        var motion = new CzRowStack();
        motion.Add(Speed.Row);
        motion.Add(Breathing.Row);
        Motion = new CzSection("Motion", motion, open);

        // Brightness by moment
        AtRest = Plain("At rest", 0, 100, 1, CzRowText.Percent, "While the composer waits", a.Rest, (o, v) => o with { Rest = v });
        WhileTyping = Plain("While typing", 0, 100, 1, CzRowText.Percent, "As you write a message", a.Typing, (o, v) => o with { Typing = v });
        WhileRunning = Plain("While a turn runs", 0, 100, 1, CzRowText.Percent, "As the agent works", a.Running, (o, v) => o with { Running = v });
        var moments = new CzRowStack();
        moments.Add(AtRest.Row);
        moments.Add(WhileTyping.Row);
        moments.Add(WhileRunning.Row);
        Moments = new CzSection("Brightness by moment", moments, open);

        // Advanced
        AdvancedButton = new CzAdvancedButton("Advanced", "Halo, rim and dot sizes");
        AdvancedRow = new CzRow("Advanced", "Halo, rim and dot sizes", AdvancedButton);
        HaloSize = Plain("Halo size", 0, 40, 1, CzRowText.Px, "How far the glow reaches", a.HaloSize, (o, v) => o with { HaloSize = v });
        HaloSoftness = Plain("Halo softness", 4, 64, 1, CzRowText.Px, "How soft its outer edge is", a.HaloBlur, (o, v) => o with { HaloBlur = v });
        RimWidth = Plain("Rim width", 0.5, 5, 0.25, CzRowText.Px, CzRowText.RimWidthDetail(a.Rim), a.RimWidth, (o, v) => o with { RimWidth = v });
        DotSpacing = Plain("Dot spacing", 2, 8, 0.5, CzRowText.Px, CzRowText.DotCellDetail(a.Dots), a.DotCell, (o, v) => o with { DotCell = v });
        DotStrength = Plain("Dot strength", 0, 100, 1, CzRowText.Percent, CzRowText.DotStrengthDetail(a.Dots), a.DotStrength, (o, v) => o with { DotStrength = v });
        var nest = new CzRowStack();
        nest.Add(HaloSize.Row);
        nest.Add(HaloSoftness.Row);
        nest.Add(RimWidth.Row);
        nest.Add(DotSpacing.Row);
        nest.Add(DotStrength.Row);
        var advancedRows = new CzRowStack();
        advancedRows.Add(AdvancedRow);
        AdvancedNest = advancedRows.Add(nest, open: advanced.Open);
        AdvancedCard = new CzSection(null, advancedRows, open);
        AdvancedButton.Open = advanced.Open;
        AdvancedButton.OpenChanged += isOpen => advanced.Open = isOpen;
        advanced.Changed += () => { AdvancedButton.Open = advanced.Open; AdvancedNest.Open = advanced.Open; };

        // Reset
        ResetButton = new CzCapsuleButton("Reset to defaults");
        ResetButton.SetAtDefaults(CzRowText.AuraIsDefault(a), animate: false);
        ResetButton.Invoked += () => editor.EditAura(_ => UiverseAuraOptions.Default);
        Defaults = new CzRow("Defaults", CzRowText.AuraResetDetail(CzRowText.AuraIsDefault(a)), ResetButton);
        var reset = new CzRowStack();
        reset.Add(Defaults);
        Reset = new CzSection(null, reset, open);
    }

    private CzSliderRow Plain(string title, double min, double max, double step, Func<double, string> format, string detail, double value, Func<UiverseAuraOptions, double, UiverseAuraOptions> set) =>
        new(editor, title, min, max, step, inverted: false, format, detail, value, null, v => Set(v, set));

    private void Set(double? v, Func<UiverseAuraOptions, double, UiverseAuraOptions> set)
    {
        if (v is { } x) editor.EditAura(o => set(o, x));
    }

    private static string SchemeId(AuraScheme scheme) => scheme switch { AuraScheme.Maker => "maker", AuraScheme.Accent => "accent", AuraScheme.Custom => "custom", _ => "original" };

    private string ColoursDetail(AuraScheme scheme) => CzText.AuraColours(SchemeId(scheme), makerName(), accentName());

    /// <summary>Shows what the settings say now (the sections' folds for the style are the page's).</summary>
    internal void Apply()
    {
        var a = editor.Settings.Aura;
        Colours.Apply(SchemeId(a.Scheme), ColoursDetail(a.Scheme));
        YourColoursSlot.Open = a.Scheme == AuraScheme.Custom;
        FirstWell.Display(a.Color1);
        SecondWell.Display(a.Color2);
        RimLight.Apply("A bright line hugging the edge");
        Halftone.Apply("The glow seen through fine dots");
        Speed.Apply(a.Speed, null, "Seconds for one turn");
        Breathing.Apply("The glow slowly swells and settles");
        AtRest.Apply(a.Rest, null, "While the composer waits");
        WhileTyping.Apply(a.Typing, null, "As you write a message");
        WhileRunning.Apply(a.Running, null, "As the agent works");
        HaloSize.Apply(a.HaloSize, null, "How far the glow reaches");
        HaloSoftness.Apply(a.HaloBlur, null, "How soft its outer edge is");
        RimWidth.Apply(a.RimWidth, null, CzRowText.RimWidthDetail(a.Rim), dimmed: !a.Rim);
        DotSpacing.Apply(a.DotCell, null, CzRowText.DotCellDetail(a.Dots), dimmed: !a.Dots);
        DotStrength.Apply(a.DotStrength, null, CzRowText.DotStrengthDetail(a.Dots), dimmed: !a.Dots);
        var isDefault = CzRowText.AuraIsDefault(a);
        ResetButton.SetAtDefaults(isDefault);
        Defaults.SetDetail(CzRowText.AuraResetDetail(isDefault));
    }
}
