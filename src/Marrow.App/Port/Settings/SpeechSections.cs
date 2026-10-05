// PORT-ONLY CZ-8 (customization-SPEC CZ-11.1; DESIGN cz.js:642-654): the Customization page's Speech section, the first one that is not about the composer, so the pinned stage lets go above it. A summary of the Voice typing page:
// three link rows (Speech model, Language, Engine), each with a Change capsule and chevron that opens that page. Every detail follows the app's voiceTyping and voiceOrb settings (a changed sentence crossfades, as every row's does, OD-0076).
using Avalonia.Automation;
using Marrow.App.Port.Voice;
using Marrow.Core.Port;
using Marrow.Core.Port.Settings;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class SpeechSections
{
    private readonly AppSettings settings;
    private readonly CzRow model, language, engine;

    internal CzSection Section { get; }

    /// <param name="settings">The app's settings, read inside tracked reads by the page.</param>
    /// <param name="openVoiceTyping">What each Change does: a page change to Voice typing.</param>
    internal SpeechSections(AppSettings settings, Action openVoiceTyping)
    {
        this.settings = settings;
        model = Row(CzSpeechText.ModelTitle, ModelDetail(), openVoiceTyping);
        language = Row(CzSpeechText.LanguageTitle, LanguageDetail(), openVoiceTyping);
        engine = Row(CzSpeechText.EngineTitle, EngineDetail(), openVoiceTyping);
        var rows = new CzRowStack();
        rows.Add(model);
        rows.Add(language);
        rows.Add(engine);
        Section = new CzSection(CzSpeechText.Heading, rows, open: true);
    }

    /// <summary>Shows what the settings say now in every row: a changed sentence crossfades.</summary>
    internal void Apply()
    {
        Show(model, ModelDetail());
        Show(language, LanguageDetail());
        Show(engine, EngineDetail());
    }

    private string ModelDetail() => CzSpeechText.ModelDetail(settings.VoiceTyping.Engine, settings.VoiceTyping.Model);
    private string LanguageDetail() => CzSpeechText.LanguageDetail(settings.VoiceTyping.Lang, settings.VoiceOrb.LangMode, WindowsDisplayLanguage.Current());
    private string EngineDetail() => CzSpeechText.EngineDetail(settings.VoiceTyping.Engine);

    // CZ-15: the visible label stays `Change`; the control's accessible name is its row's title and its help text the row's detail
    private static void Show(CzRow row, string detail)
    {
        row.SetDetail(detail);
        AutomationProperties.SetHelpText(row.Control, detail);
    }

    private static CzRow Row(string title, string detail, Action open)
    {
        var button = new CzCapsuleButton(CzSpeechText.Change, link: true);
        button.Invoked += open;
        AutomationProperties.SetName(button, title);
        AutomationProperties.SetHelpText(button, detail);
        return new CzRow(title, detail, button);
    }
}
