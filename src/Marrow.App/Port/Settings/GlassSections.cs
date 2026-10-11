// PORT-ONLY, owner-requested (owner 2026-10-11, relayed by the lead: "add glass UI settings inside Customization, so the user can set it as he wishes";
// OD-0107; ED-NEW-glasslook-2): the Customization page's Glass section, planned as Apple's Liquid Glass setting (iOS 27 and macOS 27, the owner's
// target): the live preview tile, the Look (Clear, Regular, Tinted), one fine-tuning slider per knob the two MIT repos' live demos expose
// (GlassSettings.Knobs), and Reset to defaults. Every control writes the app's glassLook setting, which the settings mirror into the material's tuning:
// every glass piece of every window redraws with new uniforms on the next frame, no new program. A new Look crossfades every surface to it
// (GlassLooks.Blend) and Reset glides every knob home; a drag or a glide writes the store when it ends.
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class GlassSections
{
    internal const string Heading = "Glass";
    internal const double SliderStep = 0.05;

    private readonly Func<GlassSettings> read;
    private readonly Action<GlassSettings> write;
    private readonly Action commit;
    private readonly CzNumber glide;
    private GlassSettings glideFrom = GlassSettings.Default, glideTo = GlassSettings.Default;
    private bool showing;

    internal CzSection Section { get; }
    internal GlassPreviewTile Preview { get; }
    internal CzSegmentRow Look { get; }
    internal IReadOnlyDictionary<string, CzSlider> Sliders { get; }
    internal CzButton Reset { get; }

    /// <summary>The clarity a slider on Auto shows: the composer's own in <paramref name="look"/> (the look table's).</summary>
    internal static double OwnClarity(GlassVariant look) => GlassLooks.For(GlassSurface.Composer, look).Clarity;

    /// <param name="read">The app's glassLook setting; read inside a tracked read by the page.</param>
    /// <param name="write">Puts a new glassLook setting in the app's settings (live: the material follows at once).</param>
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

        Look = new CzSegmentRow(GlassText.Look, GlassText.LookDetail(settings.Look), GlassSettings.Looks.Select(l => (l.Id, l.Name)).ToArray(),
            GlassSettings.IdOf(settings.Look), Pick);
        rows.Add(Look.Row);

        var sliders = new Dictionary<string, CzSlider>();
        foreach (var knob in GlassSettings.Knobs)
        {
            var (title, detail) = GlassText.Knob(knob.Id);
            var auto = knob.Id == "clarity" ? OwnClarity(settings.Look) : (double?)null;
            var slider = new CzSlider(title, knob.Min, knob.Max, SliderStep, settings.Get(knob.Id), auto, format: knob.Format, detail: detail);
            var id = knob.Id;
            slider.ValueChanged += value => { if (!showing) Edit(id, value); };
            // a drag has ended, or a key has been pressed and lifted: the settings' coalesced write goes out now
            slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => commit(), RoutingStrategies.Bubble, handledEventsToo: true);
            slider.AddHandler(InputElement.KeyUpEvent, (_, _) => commit(), RoutingStrategies.Bubble, handledEventsToo: true);
            sliders[id] = slider;
            rows.Add(new CzRow(title, detail, slider, dimmed => slider.Dimmed = dimmed));
        }
        Sliders = sliders;

        Reset = new CzButton(GlassText.Reset, GlassText.ResetDetail);
        Reset.Invoked += () => Glide(GlassSettings.Default);
        Reset.Dimmed = settings == GlassSettings.Default;
        rows.Add(new CzRow(GlassText.Defaults, GlassText.ResetDetail, Reset));
        rows.Add(new CzNoteRow(GlassText.Note));

        Section = new CzSection(Heading, rows, open: true);
        glide = new CzNumber(new CzMotion(Section), 0, Glided);
    }

    /// <summary>Shows what the setting says now, in every row (a change from elsewhere, a glide's frame, a slider's own echo).</summary>
    internal void Apply()
    {
        var settings = read().Held();
        showing = true;
        try
        {
            foreach (var (id, slider) in Sliders)
            {
                var value = settings.Get(id);
                var auto = id == "clarity" ? OwnClarity(settings.Look) : (double?)null;
                if (slider.Value != value || slider.AutoValue != auto) slider.Update(value, auto);
            }
            var look = GlassSettings.IdOf(settings.Look);
            if (Look.Control.SelectedId != look) Look.Apply(look, GlassText.LookDetail(settings.Look));
        }
        finally { showing = false; }
        Reset.Dimmed = settings == GlassSettings.Default;
    }

    private void Edit(string id, double? value)
    {
        if (glide.IsMoving) glide.Jump(1); // a slider touched mid-glide: the glide lands first (the Look's crossfade ends), then the slider's value
        write(read().With(id, value));
    }

    private void Pick(string id)
    {
        var look = GlassSettings.Looks.FirstOrDefault(l => l.Id == id);
        if (look.Id is null) return;
        var now = glide.IsMoving ? glideTo : read();
        if (glide.IsMoving) glide.Jump(1);
        if (now.Look == look.Value) return;
        Glide(now with { Look = look.Value }); // the knobs stay; every surface crossfades to its look in the new Look
    }

    /// <summary>Glides from where the glass is to <paramref name="to"/> (0.4 s, the page's easing; at once under Reduce motion): every knob, and a
    /// new Look's crossfade on every surface (GlassLooks.Blend).</summary>
    internal void Glide(GlassSettings to)
    {
        if (glide.IsMoving) glide.Jump(1);
        glideFrom = read().Held();
        glideTo = to.Held();
        glide.Jump(0);
        glide.To(1, CzMotion.Ez(0.4));
    }

    private void Glided(double t)
    {
        if (t <= 0) return;
        var crossfade = glideFrom.Look != glideTo.Look;
        if (t >= 1)
        {
            if (crossfade) GlassLooks.Blend(glideFrom.Look, 1);
            write(glideTo);
            commit();
            return;
        }
        if (crossfade) GlassLooks.Blend(glideFrom.Look, t);
        write(glideFrom.Toward(glideTo, t, OwnClarity(glideTo.Look)));
    }
}

/// <summary>The words of the Glass section.</summary>
internal static class GlassText
{
    internal const string Look = "Look";
    internal const string Defaults = "Defaults";
    internal const string Reset = "Reset to defaults";
    internal const string ResetDetail = "The Regular look, every slider at its own.";
    internal const string Note = "Glass changes apply live to the composer, the toolbar pills, the sidebar and the window. Text on glass keeps a contrast of at least 4.5:1 at any setting.";

    /// <summary>The Look row's detail: what the picked Look does.</summary>
    internal static string LookDetail(GlassVariant look) => look switch
    {
        GlassVariant.Clear => "Clearer glass: more of what lies behind it shows through.",
        GlassVariant.Tinted => "More opaque, tinted glass: more contrast for what lies on it.",
        _ => "The glass as the app draws it. The sliders below fine-tune the chosen look.",
    };

    /// <summary>A knob's title and detail.</summary>
    internal static (string Title, string Detail) Knob(string id) => id switch
    {
        "refraction" => ("Refraction", "How strongly the bezel bends what lies behind the glass."),
        "bezel" => ("Bezel width", "How far in from the edge the bending reaches."),
        "clarity" => ("Clarity", "How clear the bezel is. Auto keeps each surface's own."),
        "frost" => ("Frost", "How much the glass blurs what lies behind it."),
        "fringe" => ("Colour fringe", "The split of red and blue where the bezel bends."),
        "rim" => ("Rim light", "The bright line along the edge of the glass."),
        "tint" => ("Tint", "How much of the glass's own colour lies over what it shows."),
        "saturation" => ("Saturation", "How vivid the colours behind the glass look."),
        "shadow" => ("Shadow depth", "The soft shadow under the glass and the dark line just outside its edge."),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown glass knob."),
    };
}
