// PORT-ONLY CZ-7a: customization-SPEC CZ-9/CZ-13.4, OD-0091/OD-0097; voicecmp.js:207-222, voiceopts.js:237-250.
using System.Globalization;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port.Settings;
using Marrow.UI.Port.Voice.Orb;

namespace Marrow.App.Port.Settings;

internal sealed class VoiceOrbSections
{
    private readonly AppSettings settings;
    private readonly Action<string, TimeSpan?> preview;
    private readonly Dictionary<string, CzSegmentRow> segments = [];
    private readonly VoiceOrbView siri, membrane;
    private readonly CzNumber siriGlow, membraneGlow;
    private readonly CzRow orbRow, wordsRow, languageRow;
    private readonly CzMenuButton words, language;
    private bool showing;

    internal CzSection Section { get; }
    internal CzOrbTiles Orbs { get; }

    internal VoiceOrbSections(AppSettings settings, Action<string, TimeSpan?> preview)
    {
        this.settings = settings;
        this.preview = preview;
        var s = settings.VoiceOrb;
        siri = new VoiceOrbView { Kind = VoiceOrbKind.Siri, SlotDiameter = 30, TileGlow = s.Orb == VoiceOrbKind.Siri ? .25 : 0 };
        membrane = new VoiceOrbView { Kind = VoiceOrbKind.Membrane, SlotDiameter = 30, TileGlow = s.Orb == VoiceOrbKind.Membrane ? .25 : 0 };
        Orbs = new CzOrbTiles([("siri", "Siri Wave", siri), ("membrane", "Voice Membrane", membrane)], Id("Orb", (int)s.Orb));
        Avalonia.Automation.AutomationProperties.SetName(Orbs, "Orb");
        siriGlow = new CzNumber(new CzMotion(siri), siri.TileGlow!.Value, v => siri.TileGlow = v);
        membraneGlow = new CzNumber(new CzMotion(membrane), membrane.TileGlow!.Value, v => membrane.TileGlow = v);
        orbRow = new CzRow("Orb", Detail("Orb", (int)s.Orb), Orbs, topAligned: true);
        Orbs.SelectionChanged += id => Edit("orb", o => o with { Orb = (VoiceOrbKind)Index("Orb", id) });
        var rows = new CzRowStack();
        rows.Add(orbRow);
        AddSegment(rows, "Listening style", (int)s.ListeningStyle, "lstyle", (o, i) => o with { ListeningStyle = (VoiceListeningStyle)i }, stacked: true, replay: TimeSpan.Zero);
        words = Menu("Pill words", (int)s.Labels, stacked: true);
        wordsRow = new CzRow("Pill words", Detail("Pill words", (int)s.Labels), words, dim => words.Dimmed = dim, stacked: true);
        words.SelectionChanged += id => Edit("labels", o => o with { Labels = (VoicePillWords)Index("Pill words", id) }, TimeSpan.FromMilliseconds(200));
        rows.Add(wordsRow);
        AddSegment(rows, "Orb size", s.OrbSize == 32 ? 0 : 1, "orbSize", (o, i) => o with { OrbSize = i == 0 ? 32 : 26 });
        AddSegment(rows, "Attachment button", (int)s.Attachment, "attach", (o, i) => o with { Attachment = (VoiceAttachment)i });
        AddSegment(rows, "Composer light while you speak", (int)s.LightVoice, "lightVoice", (o, i) => o with { LightVoice = (VoiceLightVoice)i }, stacked: true, replay: TimeSpan.Zero);
        language = Menu("Spoken language", (int)s.LangMode);
        languageRow = new CzRow("Spoken language", LanguageDetail(), language);
        language.SelectionChanged += id =>
        {
            if (showing) return;
            var change = VoiceLanguage.WithLanguageDefault(settings.VoiceOrb, settings.VoiceTyping, (VoiceLangMode)Index("Spoken language", id));
            // Both keys use AppSettings' single coalescer (CZ-13.4/13.5), in this main-actor turn.
            settings.VoiceOrb = change.Orb;
            settings.VoiceTyping = change.Typing;
            settings.FlushPendingWrites(); // a discrete pick commits the two keys together, even inside a previous interval
            Apply();
            preview("langMode", TimeSpan.Zero);
        };
        rows.Add(languageRow);
        AddSegment(rows, "After voice input", (int)s.AfterVoice, "afterVoice", (o, i) => o with { AfterVoice = (VoiceAfterVoice)i }, stacked: true);
        rows.Add(new CzNoteRow(CzText.VoiceNote));
        Section = new CzSection(CzText.VoiceHeading, rows, open: true);
        Apply();
    }

    private static string Id(string title, int index) => CzText.VoiceOptions[title][index].Id;
    private static int Index(string title, string id) => Array.FindIndex(CzText.VoiceOptions[title], o => o.Id == id);
    private static string Detail(string title, int index) => CzText.VoiceOptions[title][index].Detail;
    private static (string Id, string Text)[] Options(string title) => CzText.VoiceOptions[title].Select(o => (o.Id, o.Text)).ToArray();
    private static CzMenuButton Menu(string title, int index, bool stacked = false) => new(title, Options(title), Id(title, index)) { StackedRow = stacked };

    private void AddSegment(CzRowStack rows, string title, int index, string key, Func<VoiceOrbSettings, int, VoiceOrbSettings> change, bool stacked = false, TimeSpan? replay = null)
    {
        var row = new CzSegmentRow(title, Detail(title, index), Options(title), Id(title, index), id => Edit(key, o => change(o, Index(title, id)), replay), stacked);
        segments.Add(title, row);
        rows.Add(row.Row);
    }

    private void Edit(string key, Func<VoiceOrbSettings, VoiceOrbSettings> change, TimeSpan? replay = null)
    {
        if (showing) return;
        settings.VoiceOrb = change(settings.VoiceOrb);
        Apply();
        preview(key, replay);
    }

    private string LanguageDetail() => CzText.SpokenLanguageDetail(settings.VoiceOrb.LangMode, settings.VoiceTyping.Lang, CultureInfo.CurrentUICulture.Name);

    internal void Apply()
    {
        var s = settings.VoiceOrb;
        showing = true;
        try
        {
            Orbs.Show(Id("Orb", (int)s.Orb));
            orbRow.SetDetail(Detail("Orb", (int)s.Orb));
            siriGlow.To(s.Orb == VoiceOrbKind.Siri ? .25 : 0, CzMotion.Ease(.3));
            membraneGlow.To(s.Orb == VoiceOrbKind.Membrane ? .25 : 0, CzMotion.Ease(.3));
            Show("Listening style", (int)s.ListeningStyle);
            words.Show(Id("Pill words", (int)s.Labels));
            wordsRow.Dimmed = s.ListeningStyle == VoiceListeningStyle.Cross;
            wordsRow.SetDetail(wordsRow.Dimmed ? CzText.WordsDimDetail : Detail("Pill words", (int)s.Labels));
            Show("Orb size", s.OrbSize == 32 ? 0 : 1);
            Show("Attachment button", (int)s.Attachment);
            Show("Composer light while you speak", (int)s.LightVoice);
            var light = segments["Composer light while you speak"].Row;
            light.Dimmed = settings.ComposerLight.Style == ComposerLightStyle.Off;
            if (light.Dimmed) light.SetDetail(CzText.LightDimDetail);
            language.Show(Id("Spoken language", (int)s.LangMode));
            languageRow.SetDetail(LanguageDetail());
            Show("After voice input", (int)s.AfterVoice);
        }
        finally { showing = false; }
    }

    private void Show(string title, int index) => segments[title].Apply(Id(title, index), Detail(title, index));
}
