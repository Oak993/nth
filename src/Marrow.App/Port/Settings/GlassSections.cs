// PORT-ONLY, owner-requested (owner 2026-10-11, relayed by the lead: "add glass UI settings inside Customization, so the user can set it as he wishes";
// OD-0107; ED-NEW-glasslook-2) and owner-approved (OD-0147: "Glass UI Approved, ship it with option for user to be able to set all those settings ...
// but ship with its current default values"): the Customization page's Glass section, the design canvas's Glass settings panel
// (E:/Droppy/work/design/glass-ui/index.html, "Settings > Customization > Glass, as the app will ship it") with its words, ranges, steps and units:
// the live preview tile, the Look (Clear, Regular, Tinted) and the Transparency slider between them, the eight fine-tuning sliders with the Custom
// mark and Reset, the behaviour (tint colour, motion, Increase contrast, Reduce transparency, Translucent sidebar) and Reset to defaults. Every control
// writes the app's glass setting, which the settings mirror into the material: every glass piece of every window redraws with new uniforms on the
// next frame, no new program. A Look, a switch or a reset glides every surface to it (GlassLooks.Blend) while its sliders glide on the page; a drag
// is live; the store is written when a drag or a glide ends.
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class GlassSections
{
    internal const string Heading = "Glass";

    private readonly Func<GlassSettings> read;
    private readonly Action<GlassSettings> write;
    private readonly Action commit;
    private readonly CzNumber glide;
    private GlassSettings glideFrom = GlassSettings.Default, glideTo = GlassSettings.Default;
    private bool showing;

    internal CzSection Section { get; }
    internal GlassPreviewTile Preview { get; }
    internal CzSegmentRow Look { get; }
    internal CzSlider Transparency { get; }
    internal CzRow FineTuning { get; }
    internal CzCapsuleButton ResetFineTuning { get; }
    internal IReadOnlyDictionary<string, CzSlider> Sliders { get; }
    internal CzMenuButton TintColour { get; }
    internal CzSegmentRow Motion { get; }
    internal CzToggleRow IncreaseContrast { get; }
    internal CzToggleRow ReduceTransparency { get; }
    internal CzToggleRow TranslucentSidebar { get; }
    internal CzCapsuleButton Reset { get; }

    /// <param name="read">The app's glass setting; read inside a tracked read by the page.</param>
    /// <param name="write">Puts a new glass setting in the app's settings (live: the material follows at once).</param>
    /// <param name="commit">The settings' coalesced write goes out now (a drag, a key or a glide has ended).</param>
    internal GlassSections(Func<GlassSettings> read, Action<GlassSettings> write, Action commit)
    {
        this.read = read;
        this.write = write;
        this.commit = commit;
        var settings = read().Held();
        var rows = new CzRowStack();

        Preview = new GlassPreviewTile();
        rows.Add(Preview);
        glide = new CzNumber(new CzMotion(Preview), 0, Glided); // the tile lives and leaves with the section

        Look = new CzSegmentRow(GlassText.Look, GlassText.LookDetail(settings), GlassSettings.Looks.Select(l => (l.Id, l.Name)).ToArray(),
            settings.Look is { } look ? GlassSettings.IdOf(look) : "regular", Pick);
        if (settings.Look is null) Look.Control.ShowNone();
        rows.Add(Look.Row);

        var t = GlassSettings.TransparencyKnob;
        Transparency = Slider(t, settings.Transparency, value => write(read().At(value)), GlassText.TransparencyValue);
        Avalonia.Automation.AutomationProperties.SetName(Transparency, GlassText.TransparencyName);
        rows.Add(new CzRow(t.Name, t.Description, Transparency, dimmed => Transparency.Dimmed = dimmed));

        rows.Add(new CzCaptionRow(GlassText.FineTuningCaption));
        ResetFineTuning = new CzCapsuleButton(GlassText.ResetFineTuning, detail: GlassText.ResetFineTuningDetail);
        ResetFineTuning.Invoked += () => Glide((glide.IsMoving ? glideTo : read()).ResetFineTuning());
        ResetFineTuning.Dimmed = !settings.IsCustom;
        FineTuning = new CzRow(GlassText.FineTuning, GlassText.FineTuningDetail(settings.IsCustom), ResetFineTuning, dimmed => ResetFineTuning.Dimmed = dimmed || !read().IsCustom);
        rows.Add(FineTuning);
        var sliders = new Dictionary<string, CzSlider>();
        foreach (var knob in GlassSettings.Knobs)
        {
            var id = knob.Id;
            var slider = Slider(knob, settings.Get(id), value => write(read().With(id, value)));
            sliders[id] = slider;
            rows.Add(new CzRow(knob.Name, knob.Description, slider, dimmed => slider.Dimmed = dimmed));
        }
        Sliders = sliders;

        rows.Add(new CzCaptionRow(GlassText.BehaviourCaption));
        TintColour = new CzMenuButton(GlassText.TintColour, GlassSettings.TintColours.Select(c => (c.Id, c.Name)).ToArray(), IdOf(settings.TintColour), GlassText.TintColourDetail);
        TintColour.SelectionChanged += id =>
        {
            if (showing || GlassSettings.TintColours.FirstOrDefault(c => c.Id == id) is not { Id: not null } colour) return;
            var now = glide.IsMoving ? glideTo : read();
            if (now.TintColour != colour.Value) Glide(now with { TintColour = colour.Value });
        };
        rows.Add(new CzRow(GlassText.TintColour, GlassText.TintColourDetail, TintColour, dimmed => TintColour.Dimmed = dimmed));
        Motion = new CzSegmentRow(GlassText.Motion, GlassText.MotionDetail, [("full", GlassText.MotionFull), ("reduced", GlassText.MotionReduced)],
            ReducedMotion(settings) ? "reduced" : "full", id =>
            {
                if (glide.IsMoving) glide.Jump(1);
                write(read() with { ReducedMotion = id == "reduced" });
                commit();
            });
        rows.Add(Motion.Row);
        IncreaseContrast = new CzToggleRow(GlassText.IncreaseContrast, GlassText.IncreaseContrastDetail,
            () => read().IncreaseContrast ?? GlassLooks.System.IncreaseContrast, on => Switch(s => s with { IncreaseContrast = on }));
        rows.Add(IncreaseContrast.Row);
        ReduceTransparency = new CzToggleRow(GlassText.ReduceTransparency, GlassText.ReduceTransparencyDetail,
            () => read().ReduceTransparency ?? GlassLooks.System.ReduceTransparency, on => Switch(s => s with { ReduceTransparency = on }));
        rows.Add(ReduceTransparency.Row);
        TranslucentSidebar = new CzToggleRow(GlassText.TranslucentSidebar, GlassText.TranslucentSidebarDetail,
            () => read().TranslucentSidebar, on => Switch(s => s with { TranslucentSidebar = on }));
        rows.Add(TranslucentSidebar.Row);

        Reset = new CzCapsuleButton(GlassText.Reset, detail: GlassText.ResetDetail);
        Reset.Invoked += () => Glide(GlassSettings.Default);
        Reset.Dimmed = settings == GlassSettings.Default;
        rows.Add(new CzRow(GlassText.Defaults, GlassText.ResetDetail, Reset));
        rows.Add(new CzNoteRow(GlassText.Note));

        Section = new CzSection(Heading, rows, open: true);
    }

    private CzSlider Slider(GlassKnob knob, double value, Action<double> edit, Func<double, string>? format = null)
    {
        var slider = new CzSlider(knob.Name, knob.Min, knob.Max, knob.Step, value, format: format ?? knob.Format, detail: knob.Description);
        slider.ValueChanged += v =>
        {
            if (showing || v is not { } picked) return;
            if (glide.IsMoving) glide.Jump(1); // a slider touched mid-glide: the glide lands first, then the slider's value
            edit(picked);
        };
        // a drag has ended, or a key has been pressed and lifted: the settings' coalesced write goes out now
        slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => commit(), RoutingStrategies.Bubble, handledEventsToo: true);
        slider.AddHandler(InputElement.KeyUpEvent, (_, _) => commit(), RoutingStrategies.Bubble, handledEventsToo: true);
        return slider;
    }

    private static string IdOf(GlassTintColour colour) => GlassSettings.TintColours.First(c => c.Value == colour).Id;

    private static bool ReducedMotion(GlassSettings settings) => settings.ReducedMotion ?? GlassLooks.System.ReduceMotion;

    /// <summary>Shows what the setting says now in every row (a change from elsewhere, a glide's frame, a slider's own echo); during a glide, the
    /// glide's own point.</summary>
    internal void Apply() => Show(glide.IsMoving ? glideFrom.Toward(glideTo, glide.Value) : read().Held());

    private void Show(GlassSettings settings)
    {
        showing = true;
        try
        {
            if (Transparency.Value != settings.Transparency) Transparency.Update(settings.Transparency, null);
            foreach (var (id, slider) in Sliders)
            {
                var value = settings.Get(id);
                if (slider.Value != value) slider.Update(value, null);
            }
            if (settings.Look is { } look)
            {
                var id = GlassSettings.IdOf(look);
                if (Look.Control.SelectedId != id) Look.Apply(id, GlassText.LookDetail(settings));
            }
            else
            {
                Look.Control.ShowNone();
                Look.Row.SetDetail(GlassText.LookDetail(settings));
            }
            var tint = IdOf(settings.TintColour);
            if (TintColour.SelectedId != tint) TintColour.Show(tint);
            var motion = ReducedMotion(settings) ? "reduced" : "full";
            if (Motion.Control.SelectedId != motion) Motion.Apply(motion, GlassText.MotionDetail);
        }
        finally { showing = false; }
        var custom = read().IsCustom;
        ResetFineTuning.Dimmed = !custom;
        FineTuning.SetDetail(GlassText.FineTuningDetail(custom));
        Reset.Dimmed = read().Held() == GlassSettings.Default;
    }

    private void Pick(string id)
    {
        var look = GlassSettings.Looks.FirstOrDefault(l => l.Id == id);
        if (look.Id is null) return;
        var now = glide.IsMoving ? glideTo : read();
        if (now.Look == look.Value && !now.IsCustom) return;
        Glide(now.At(look.Value)); // every fine-tuning value the stop's own (the canvas's setG); the switches stay
    }

    private void Switch(Func<GlassSettings, GlassSettings> change)
    {
        var now = glide.IsMoving ? glideTo : read();
        var next = change(now);
        if (next != now) Glide(next);
    }

    /// <summary>Glides from where the glass is to <paramref name="to"/> (0.4 s, the page's easing; at once under Reduce motion): the setting is
    /// <paramref name="to"/> at once (stored when the glide ends), every surface crossfades to it (GlassLooks.Blend), and the sliders glide.</summary>
    internal void Glide(GlassSettings to)
    {
        if (glide.IsMoving) glide.Jump(1);
        glideFrom = read().Held();
        glideTo = to.Held();
        if (glideFrom == glideTo) return;
        GlassLooks.Blend(glideFrom, 0);
        write(glideTo);
        glide.Jump(0);
        glide.To(1, CzMotion.Ez(0.4));
    }

    private void Glided(double t)
    {
        if (t <= 0) return;
        GlassLooks.Blend(glideFrom, t);
        Show(glideFrom.Toward(glideTo, t));
        if (t >= 1) commit();
    }
}

/// <summary>The words of the Glass section: the design canvas's (OD-0147).</summary>
internal static class GlassText
{
    internal const string Look = "Look";
    /// <summary>The Transparency slider's accessible name (the canvas's aria-label).</summary>
    internal const string TransparencyName = "Liquid Glass, more clear to more tinted";
    internal const string FineTuningCaption = "Fine-tuning";
    internal const string FineTuning = "Fine-tuning";
    internal const string ResetFineTuning = "Reset";
    internal const string ResetFineTuningDetail = "Every fine-tuning value back to the look's own.";
    internal const string BehaviourCaption = "Behaviour";
    internal const string TintColour = "Tint colour";
    internal const string TintColourDetail = "A small colour cast, capped so it stays glass.";
    internal const string Motion = "Motion";
    internal const string MotionDetail = "Bulge on press and lens knobs.";
    internal const string MotionFull = "Full";
    internal const string MotionReduced = "Reduced";
    internal const string IncreaseContrast = "Increase contrast";
    internal const string IncreaseContrastDetail = "Sharper edges and a stronger line of light.";
    internal const string ReduceTransparency = "Reduce transparency";
    internal const string ReduceTransparencyDetail = "Solid surfaces instead of glass.";
    internal const string TranslucentSidebar = "Translucent sidebar";
    internal const string TranslucentSidebarDetail = "Let the window behind tint the sidebar.";
    internal const string Defaults = "Defaults";
    internal const string Reset = "Reset to defaults";
    internal const string ResetDetail = "Regular, every value at its default, the switches following Windows.";
    internal const string Note = "Glass changes apply live to every glass surface in every window. Text on glass keeps a contrast of at least 4.5:1 at any setting.";

    /// <summary>The Look row's detail (the canvas's): what the slider's position is.</summary>
    internal static string LookDetail(GlassSettings settings) => settings.Look switch
    {
        GlassVariant.Clear => "More clear: the most see-through.",
        GlassVariant.Tinted => "More tinted: frostier, reads best.",
        GlassVariant.Regular => "Default, the iOS 27 midpoint.",
        _ => "Between two looks.",
    };

    /// <summary>The Transparency slider's value as the canvas shows it: the Look at a stop ("Regular, default" at the default), else the nearest
    /// look's name, then the position: "Regular, default (50)", "More clear (12)".</summary>
    internal static string TransparencyValue(double transparency)
    {
        var at = Math.Round(transparency);
        var stop = GlassSettings.Looks.FirstOrDefault(l => Math.Abs(l.At - at) < 0.5);
        var name = stop.Id is null ? GlassSettings.LookName(at) : stop.Value == GlassVariant.Regular ? "Regular, default" : stop.Name;
        return $"{name} ({at.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
    }

    /// <summary>The fine-tuning row's detail: the canvas's Custom mark when a value differs from the look's own.</summary>
    internal static string FineTuningDetail(bool custom) => custom ? "Custom" : "Every value the look's own.";
}
