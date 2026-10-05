// PORT-ONLY CZ-8 (customization-SPEC CZ-11.1; DESIGN cz.js:642-654): the Customization page's Speech section, the first one that is not about the composer, so the pinned stage lets go above it. A summary of the Voice typing page:
// three link rows (Speech model, Language, Engine), each with a Change capsule and chevron that opens that page. Every detail follows the app's voiceTyping and voiceOrb settings (a changed sentence crossfades, as every row's does, OD-0076).
using System.Globalization;
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
        model.SetDetail(ModelDetail());
        language.SetDetail(LanguageDetail());
        engine.SetDetail(EngineDetail());
    }

    private string ModelDetail() => CzSpeechText.ModelDetail(settings.VoiceTyping.Engine, settings.VoiceTyping.Model);
    private string LanguageDetail() => CzSpeechText.LanguageDetail(settings.VoiceTyping.Lang, settings.VoiceOrb.LangMode, CultureInfo.CurrentUICulture.Name);
    private string EngineDetail() => CzSpeechText.EngineDetail(settings.VoiceTyping.Engine);

    private static CzRow Row(string title, string detail, Action open)
    {
        var button = new CzCapsuleButton(CzSpeechText.Change, link: true);
        button.Invoked += open;
        Avalonia.Automation.AutomationProperties.SetName(button, CzSpeechText.Change);
        return new CzRow(title, detail, button);
    }
}
