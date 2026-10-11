// PORT-ONLY, first-release CZ-4 (customization-SPEC CZ-2.2, CZ-3, CZ-5, CZ-6, CZ-7, CZ-8, CZ-13.1; OWNER OD-0085, OD-0089, OD-0076): the Customization page, layout B "Stage". The chrome row carries the Preview state control (Rest,
// Typing, Running a turn: session state, bound to PreviewSession). Under it, in the page's column: the pinned block (CZ-3's CustomizationStage: the heading, the preview composer on its stage and the model switch), the Style cards, and
// the sections of the picked style (Border beam, Uiverse light or Off), each folding open and shut as the style changes. Every setting is the app's composer light (AppSettings.ComposerLight): a change reaches every composer at once.
// The pinned block is sticky (CZ-6, StickyStageController): it stays at the top of the Settings scroll viewer while the sections of `Sticky` scroll under it, folds its stage as the page scrolls, and lets go when `Sticky` ends.
// The Speech section (CZ-8) is in `Rest`, after the point where the pinned block lets go.
// CZ-7b: the stage voice preview uses scripted input and the real composer voice components.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Settings;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>The Customization page (customization-SPEC CZ-3 to CZ-8).</summary>
internal sealed class CustomizationSettingsPage : Decorator, ISettingsPageChrome
{
    /// <summary>The page is ready: it is listed in the sidebar, found by its search and opened by a request (SA-1's pattern; the other new pages are still hidden).</summary>
    internal const bool IsReady = true;

    private PreviewSession? session;
    private bool echoing, built;

    /// <summary>CZ-3.2: the Preview state control in the chrome row.</summary>
    internal CzSegmentedControl PreviewStatePick { get; }

    /// <summary>CZ-3.3: the sticky container's children: the pinned block, Style, the style's sections. CZ-6 makes the pinned block sticky inside it, and it ends where the block lets go. HOOK: CZ-5 and CZ-7 add the Voice orb and Working dots sections at its end (the controller picks up every child: each is clipped under the block).</summary>
    internal StackPanel Sticky { get; } = new() { Orientation = Avalonia.Layout.Orientation.Vertical };

    /// <summary>CZ-3.3: what follows the sticky container, where the pinned block lets go. The Speech section (CZ-8, CZ-11).</summary>
    internal StackPanel Rest { get; } = new() { Orientation = Avalonia.Layout.Orientation.Vertical };

    internal CustomizationStage? Stage { get; private set; }

    /// <summary>CZ-3.5: what pins and compacts the stage, clips the sections under it and keeps keyboard focus clear of it.</summary>
    internal StickyStageController? Stickiness { get; private set; }
    internal CzStyleCards? StyleCards { get; private set; }
    internal BeamSections? Beam { get; private set; }
    internal AuraSections? Aura { get; private set; }
    internal CzSection? Off { get; private set; }
    internal VoiceOrbSections? Voice { get; private set; }
    internal WorkingDotsSections? Dots { get; private set; }
    internal SpeechSections? Speech { get; private set; }
    internal GlassSections? Glass { get; private set; }
    internal CzAdvancedState Advanced { get; } = new();
    internal PreviewSession? Session => session;

    public CustomizationSettingsPage()
    {
        PreviewStatePick = new CzSegmentedControl("Preview state", [("rest", "Rest"), ("typing", "Typing"), ("running", "Running a turn")], "rest", small: true);
        ChromeAccessories = [PreviewStatePick];
        Child = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Children = { Sticky, Rest } };
        // the pinned block carries the page's top inset itself (CZ-3.4: its 372 includes the 60 above its heading, which CZ-6 needs when it pins), and SettingsView gives a page with chrome controls the same 60 above its content:
        // the page takes that one back, so the heading stays 60 below the sheet's edge and the stage is not pushed down by 120
        Margin = new Thickness(0, -CustomizationStage.TopInset, 0, 0);
        // on attach, in this order: the page is built from the app's model, then it follows the app's settings
        this.Track(() => AppEnvironment.GetAppModel(this), Build);
        this.Track(() => { var settings = AppEnvironment.GetAppModel(this).Settings; return (Light: settings.ComposerLight, settings.Accent, settings.WorkingDots, settings.VoiceOrb, settings.VoiceTyping); }, shown => Apply(shown.Light));
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.Glass, _ => Glass?.Apply()); // ED-NEW-glasslook-2: the Glass rows follow the setting (a glide, a reset, another window)
    }

    /// <inheritdoc />
    public bool HasChromeControls => true;

    /// <inheritdoc />
    public IReadOnlyList<Control> ChromeAccessories { get; }

    private void Build(AppModel model)
    {
        if (built) return;
        built = true;
        var settings = model.Settings;
        session = new PreviewSession(settings.DefaultProvider);
        var editor = new CzLightEditor(() => settings.ComposerLight, value => settings.ComposerLight = value, settings.FlushPendingWrites);
        var style = settings.ComposerLight.Style;

        // the pinned block (CZ-3.4): the stage and its foot; bound to the app's settings by the two functions it reads inside tracked reads
        Stage = new CustomizationStage(() => settings.ComposerLight, () => settings.BackdropOpacity, session, () => settings.WorkingDots, () => settings.VoiceOrb, () => settings.VoiceTyping) { Margin = new Thickness(0, 0, 0, CustomizationStage.GapBelow) };

        // Style (CZ-5)
        StyleCards = new CzStyleCards(StyleId(style));
        StyleCards.SelectionChanged += id => { if (!echoing) editor.Edit(s => s with { Style = StyleOf(id) }); };
        var styleSection = new Border { Padding = new Thickness(0, 0, 0, Chrome.SectionSpacing), Child = new ChromeSection("Style", () => StyleCards) };

        // the three styles' sections (CZ-6, CZ-7, CZ-8); the others' settings are kept while one shows (CZ-5.3)
        Beam = new BeamSections(editor, Advanced, ComposerSize, () => CzTheme.Dark(this), open: style == ComposerLightStyle.Beam);
        Aura = new AuraSections(editor, Advanced, () => PreviewMakers.For(session.Maker).Name, () => AccentName(settings), open: style == ComposerLightStyle.Aura);
        var offRows = new CzRowStack();
        offRows.Add(new CzNoteRow(CzRowText.OffNote));
        Off = new CzSection(null, offRows, open: style == ComposerLightStyle.Off);

        Sticky.Children.Add(Stage);
        Sticky.Children.Add(styleSection);
        foreach (var section in Beam.Sections) Sticky.Children.Add(section);
        foreach (var section in Aura.Sections) Sticky.Children.Add(section);
        Sticky.Children.Add(Off);

        // CZ-7a: the voice rows precede Working dots. CZ-7b subscribes to the session's preview seam.
        Voice = new VoiceOrbSections(settings, session.VoicePreviewTouched);
        Sticky.Children.Add(Voice.Section);

        // Working dots (CZ-10): after the style's sections; the Voice orb section (CZ-7a) goes before it
        Dots = new WorkingDotsSections(() => settings.WorkingDots, value => settings.WorkingDots = value, () => PreviewMakers.For(session.Maker).Name, session.WorkingDotsTouched);
        Sticky.Children.Add(Dots.Section);
        Sticky.Children.Add(Aura.Picker);

        // CZ-3.5: the block is sticky in the scroll viewer the page sits in (found on attach)
        Stickiness = new StickyStageController(this, Sticky, Stage);
        // Speech (CZ-8, CZ-11): the first section after the sticky container, where the pinned block lets go (CZ-3.5.3)
        Speech = new SpeechSections(settings, () => SettingsNavigation.Shared.RequestedPage = SettingsPage.VoiceTyping);
        Rest.Children.Add(Speech.Section);
        WindowsDisplayLanguage.Watch(this, Speech.Apply); // the Language row names the display language under language default C
        // PORT-ONLY (multi-account step 3a, OD-0115, OD-0119): "Pill in the composer" follows the sticky sections, where the pinned block has let go; Speech stays the first section there (CZ-3.5.3), so it goes after Speech
        Rest.Children.Add(new Marrow.App.Port.MultiAccount.Capsule.PillCapsuleSection(settings.CapsuleStyle));
        // PORT-ONLY, owner-requested (OD-0107, ED-NEW-glasslook-2): Glass, after the composer's pill, where the pinned block has let go
        Glass = new GlassSections(() => settings.Glass, value => settings.Glass = value, settings.FlushPendingWrites);
        Rest.Children.Add(Glass.Section);

        // the chrome row's Preview state control is the session's pick (CZ-3.2, CZ-4.6); the session can move it too (touching Working dots)
        PreviewStatePick.SelectionChanged += id => { if (!echoing) session.Pick(StateOf(id)); };
        session.Changed += ShowState;
        ShowState();

        ActualThemeVariantChanged += (_, _) => Apply(settings.ComposerLight);
        session.Changed += () => Aura.Apply();
        session.Changed += Dots.Apply; // the Model colour detail names the preview's maker
    }

    private void ShowState()
    {
        if (session is null) return;
        echoing = true;
        try { PreviewStatePick.Show(session.State switch { PreviewState.Typing => "typing", PreviewState.Running => "running", _ => "rest" }); }
        finally { echoing = false; }
    }

    // what the settings say now, into every row; the folds for the style
    private void Apply(ComposerLightSettings light)
    {
        if (Beam is null || Aura is null || Off is null || StyleCards is null) return;
        echoing = true;
        try { StyleCards.Show(StyleId(light.Style)); }
        finally { echoing = false; }
        Beam.Apply();
        Aura.Apply();
        Dots?.Apply();
        Voice?.Apply();
        Speech?.Apply();
        foreach (var section in Beam.Sections) section.Open = light.Style == ComposerLightStyle.Beam;
        foreach (var section in Aura.Sections) section.Open = light.Style == ComposerLightStyle.Aura;
        Off.Open = light.Style == ComposerLightStyle.Off;
    }

    // CZ-6: the Auto values are the preview composer's: 472 wide and 46 high, the real pill's height (SPEC CZ-6 and ED-335 item 2; the playground's 52 is a web mock)
    private static (double Width, double Height) ComposerSize() => (ComposerPreview.ComposerWidth, ComposerPreview.PillHeight);

    private static string AccentName(AppSettings settings)
    {
        var index = AccentPalette.IndexOf(settings.Accent);
        return AccentPalette.All[index < 0 ? 0 : index].Name;
    }

    private static string StyleId(ComposerLightStyle style) => style switch { ComposerLightStyle.Aura => "aura", ComposerLightStyle.Off => "off", _ => "beam" };

    private static ComposerLightStyle StyleOf(string id) => id switch { "aura" => ComposerLightStyle.Aura, "off" => ComposerLightStyle.Off, _ => ComposerLightStyle.Beam };

    private static PreviewState StateOf(string id) => id switch { "typing" => PreviewState.Typing, "running" => PreviewState.Running, _ => PreviewState.Rest };
}
