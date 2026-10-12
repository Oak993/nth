// PORT-ONLY, owner-ruled addition (OD-0150 Composer Light v2; ED-NEW-lightv2-4): the shader lights' sections of the Customization
// page, rendered from the catalogue's declarations (ShaderLightCatalog: each light's controls and defaults), so a light added to
// the list gets its rows without this file changing. Look holds a row for each control a light declares (the colour pack as
// swatches that wrap onto lines, Custom's four colour wells, Spread, Intensity, Speed); Reset returns the light shown to the
// page's defaults. The rows edit the light picked now; each light keeps its own options (the owner: "all intensity, speed,
// spread should be user editable"; "Add more color options though"). Text and light (ED-NEW-lightv2-5: the v4 page's three
// modes, Light behind text and Text halo, the halo only for Text above light, as the page disables it) and the wallpaper's
// dimming near the composer (ED-NEW-lightv2-6: Auto or Off, and its strength) are one setting for every light; Reset returns
// them to how they ship too.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class ShaderSections
{
    private readonly CzLightEditor editor;
    private readonly Func<string> accent;
    private bool showing;

    internal CzSection Look { get; }
    internal CzSection Reset { get; }
    internal CzSwatchRow Packs { get; }
    internal CzRow Colours { get; }
    internal CzRow YourColours { get; }
    internal CzRowSlot YourColoursSlot { get; }
    internal IReadOnlyList<CzColorWell> Wells { get; }
    internal CzWellPicker Picker { get; } = new();
    internal CzSliderRow Spread { get; }
    internal CzSliderRow Intensity { get; }
    internal CzSliderRow Speed { get; }
    internal CzRow Defaults { get; }
    internal CzSection Text { get; }
    internal CzSegmentRow TextMode { get; }
    internal CzSliderRow Behind { get; }
    internal CzSliderRow Halo { get; }
    internal CzSection Wallpaper { get; }
    internal CzSegmentRow Dim { get; }
    internal CzSliderRow DimStrength { get; }

    /// <summary>The Text and light modes' ids (the v4 page's TL.mode) and names.</summary>
    internal static readonly (string Id, string Text)[] TextModes = [("above", "Text above light"), ("light", "Light above text"), ("diff", "Difference")];
    internal static readonly (string Id, string Text)[] DimModes = [("auto", "Auto"), ("off", "Off")];
    internal CzCapsuleButton ResetButton { get; }

    /// <summary>Each control's row slot (open when the light picked declares it).</summary>
    internal IReadOnlyDictionary<ShaderLightControl, CzRowSlot> Slots { get; }

    internal IReadOnlyList<CzSection> Sections => [Look, Text, Wallpaper, Reset];

    /// <summary>The light the rows edit: the one picked (kept while another style shows).</summary>
    internal ShaderLightDef Light => ShaderLightCatalog.Find(editor.Settings.ShaderId) ?? ShaderLightCatalog.All[0];

    private ShaderLightOptions Options => editor.Settings.ShaderOptions(Light.Id);

    internal static string IntensityText(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    internal static string SpeedText(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
    internal static string SpreadText(double v) => CzText.Fmt(v, 0) + " px";

    /// <param name="accent">The app's accent colour (#RRGGBB), which the Accent pack is made from.</param>
    internal ShaderSections(CzLightEditor editor, Func<string> accent, bool open)
    {
        this.editor = editor;
        this.accent = accent;
        var o = Options;

        // Look: the colour pack, Custom's colours, Spread, Intensity, Speed
        Packs = CzSwatchRow.ShaderPacks(o.Pack, PackColours);
        Packs.SelectionChanged += id => { if (!showing) Edit(x => x with { Pack = id }); };
        Colours = new CzRow("Colours", PackName(o.Pack), Packs, topAligned: true);
        var wells = new CzColorWell[4];
        for (var i = 0; i < 4; i++)
        {
            var k = i;
            wells[i] = new CzColorWell("Colour " + (i + 1), (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), o.Custom[i], Picker.Toggle);
            wells[i].ColorChanged += hex => { if (!showing) Edit(x => x with { Custom = x.Custom.With(k, UiverseAuraOptions.NormalizedHex(hex) is { } n ? UiverseAuraOptions.OpaqueHex(n) : x.Custom[k]) }); };
        }
        Wells = wells;
        YourColours = new CzRow("Your colours", "Four colours the light moves through", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { wells[0], wells[1], wells[2], wells[3] } });
        Spread = new CzSliderRow(editor, "Spread", ShaderLightCatalog.SpreadSlider.Min, ShaderLightCatalog.SpreadSlider.Max, ShaderLightCatalog.SpreadSlider.Step, inverted: false, SpreadText,
            "How far the light reaches onto the window", o.Spread, null, v => { if (v is { } x) Edit(y => y with { Spread = x }); });
        Intensity = new CzSliderRow(editor, "Intensity", ShaderLightCatalog.IntensitySlider.Min, ShaderLightCatalog.IntensitySlider.Max, ShaderLightCatalog.IntensitySlider.Step, inverted: false, IntensityText,
            "How bright the light is", o.Intensity, null, v => { if (v is { } x) Edit(y => y with { Intensity = x }); });
        Speed = new CzSliderRow(editor, "Speed", ShaderLightCatalog.SpeedSlider.Min, ShaderLightCatalog.SpeedSlider.Max, ShaderLightCatalog.SpeedSlider.Step, inverted: false, SpeedText,
            "How fast the light moves", o.Speed, null, v => { if (v is { } x) Edit(y => y with { Speed = x }); });
        var look = new CzRowStack();
        var colourSlot = look.Add(Colours);
        YourColoursSlot = look.Add(YourColours, open: o.Pack == ShaderLightCatalog.CustomPack);
        var slots = new Dictionary<ShaderLightControl, CzRowSlot>
        {
            [ShaderLightControl.Pack] = colourSlot,
            [ShaderLightControl.Spread] = look.Add(Spread.Row),
            [ShaderLightControl.Intensity] = look.Add(Intensity.Row),
            [ShaderLightControl.Speed] = look.Add(Speed.Row),
        };
        Slots = slots;
        Look = new CzSection("Look", look, open);

        // Text and light: how the chat text above the composer meets the light (every light)
        var settings = editor.Settings;
        TextMode = new CzSegmentRow("Text and light", TextModeDetail(settings.TextMode), TextModes, TextModes[(int)settings.TextMode].Id,
            id => editor.Edit(s => s with { TextMode = (ComposerLightTextMode)Array.FindIndex(TextModes, m => m.Id == id) }), stacked: true);
        Behind = new CzSliderRow(editor, "Light behind text", 0, 1, 0.01, inverted: false, CzRowText.Percent01,
            BehindDetail, settings.TextBehind, null, v => { if (v is { } x) editor.Edit(s => s with { TextBehind = x }); });
        Halo = new CzSliderRow(editor, "Text halo", 0, 1, 0.01, inverted: false, CzRowText.Percent01,
            HaloDetail(settings.TextMode), settings.TextHalo, null, v => { if (v is { } x) editor.Edit(s => s with { TextHalo = x }); });
        Halo.Row.Dimmed = settings.TextMode != ComposerLightTextMode.TextAbove;
        var text = new CzRowStack();
        text.Add(TextMode.Row);
        text.Add(Behind.Row);
        text.Add(Halo.Row);
        Text = new CzSection("Text and light", text, open);

        // the wallpaper behind the light
        Dim = new CzSegmentRow("Dim wallpaper behind light", DimDetail(settings.WallpaperDim), DimModes, DimModes[(int)settings.WallpaperDim].Id,
            id => editor.Edit(s => s with { WallpaperDim = id == "off" ? WallpaperDimMode.Off : WallpaperDimMode.Auto }));
        DimStrength = new CzSliderRow(editor, "Strength", 0, 1, 0.01, inverted: false, CzRowText.Percent01,
            StrengthDetail, settings.WallpaperDimStrength, null, v => { if (v is { } x) editor.Edit(s => s with { WallpaperDimStrength = x }); });
        DimStrength.Row.Dimmed = settings.WallpaperDim == WallpaperDimMode.Off;
        var wallpaper = new CzRowStack();
        wallpaper.Add(Dim.Row);
        wallpaper.Add(DimStrength.Row);
        Wallpaper = new CzSection("Wallpaper", wallpaper, open);

        // Reset: the light shown, back to the page's values (and the text and wallpaper settings as they ship)
        ResetButton = new CzCapsuleButton("Reset to defaults");
        ResetButton.SetAtDefaults(IsDefault(), animate: false);
        ResetButton.Invoked += () => editor.Edit(s => s.WithTextAndDimDefaults() with { Shaders = s.Shaders.Without(Light.Id) });
        Defaults = new CzRow("Defaults", ResetDetail(), ResetButton);
        var reset = new CzRowStack();
        reset.Add(Defaults);
        Reset = new CzSection(null, reset, open);
        ShowControls();
    }

    private void Edit(Func<ShaderLightOptions, ShaderLightOptions> change)
    {
        var id = Light.Id;
        editor.Edit(s => s with { Shaders = s.Shaders.With(id, change(s.ShaderOptions(id))) });
    }

    // Accent's and Custom's swatches: the accent's four, the light's own four
    private IReadOnlyList<string> PackColours(string pack) => pack == ShaderLightCatalog.AccentPack ? ShaderLightCatalog.AccentColours(accent()) : Options.Custom.All;

    private static string PackName(string pack) => ShaderLightCatalog.Pack(pack)?.Name ?? pack;

    private bool IsDefault() => Options == Light.Defaults && editor.Settings.TextAndDimAtDefaults;

    private string ResetDetail() => IsDefault() ? Light.Name + " is as it ships" : "Return " + Light.Name + "'s colours, spread, intensity and speed, and the text and wallpaper settings, to how they ship";

    internal const string BehindDetail = "How much the light dims behind the chat text";
    internal const string StrengthDetail = "How much the wallpaper dims near the composer";

    internal static string TextModeDetail(ComposerLightTextMode mode) => mode switch
    {
        ComposerLightTextMode.LightAbove => "The light washes softly over the chat text",
        ComposerLightTextMode.Difference => "The chat text inverts against the light",
        _ => "The chat text stays on top, with a soft dark halo where the light is bright",
    };

    internal static string HaloDetail(ComposerLightTextMode mode) =>
        mode == ComposerLightTextMode.TextAbove ? "A soft dark halo behind the chat text where the light is bright" : "Only with Text above light";

    internal static string DimDetail(WallpaperDimMode mode) =>
        mode == WallpaperDimMode.Off ? "The wallpaper shows as it is around the composer" : "Dims the wallpaper near the composer, more for bright or busy wallpapers";

    // the rows the light picked declares
    private void ShowControls()
    {
        foreach (var (control, slot) in Slots) slot.Open = Light.Controls.Contains(control);
        YourColoursSlot.Open = Light.Controls.Contains(ShaderLightControl.Pack) && Options.Pack == ShaderLightCatalog.CustomPack;
    }

    /// <summary>Shows what the settings say now for the light picked (the sections' folds for the style are the page's).</summary>
    internal void Apply()
    {
        var o = Options;
        showing = true;
        try
        {
            Packs.Recolour(ShaderLightCatalog.AccentPack, ShaderLightCatalog.AccentColours(accent()));
            Packs.Recolour(ShaderLightCatalog.CustomPack, o.Custom.All);
            Packs.Show(o.Pack);
            for (var i = 0; i < 4; i++) Wells[i].Display(o.Custom[i]);
        }
        finally { showing = false; }
        Colours.SetDetail(PackName(o.Pack));
        Spread.Apply(o.Spread, null, "How far the light reaches onto the window");
        Intensity.Apply(o.Intensity, null, "How bright the light is");
        Speed.Apply(o.Speed, null, "How fast the light moves");
        var settings = editor.Settings;
        TextMode.Apply(TextModes[(int)settings.TextMode].Id, TextModeDetail(settings.TextMode));
        Behind.Apply(settings.TextBehind, null, BehindDetail);
        Halo.Apply(settings.TextHalo, null, HaloDetail(settings.TextMode), dimmed: settings.TextMode != ComposerLightTextMode.TextAbove);
        Dim.Apply(DimModes[(int)settings.WallpaperDim].Id, DimDetail(settings.WallpaperDim));
        DimStrength.Apply(settings.WallpaperDimStrength, null, StrengthDetail, dimmed: settings.WallpaperDim == WallpaperDimMode.Off);
        ShowControls();
        ResetButton.SetAtDefaults(IsDefault());
        Defaults.SetDetail(ResetDetail());
    }
}
