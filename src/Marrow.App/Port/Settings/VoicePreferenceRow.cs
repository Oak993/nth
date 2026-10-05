// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-1 C; first-release voice typing, VT-14): the slow-PC preference row of the hardware card (BUILD-PLAN VT-14).
using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>
/// `How should voice typing work here?` in the welcome-voice words V2: a two-way control, `See my words as I speak` (live words with a smaller model) and `Get every word right` (the accurate model,
/// text after you stop), whose detail line follows the answer. ENGINEERING DECISION (the spec places the row under the recommendation box): it folds open in the hardware card, under a hairline,
/// only where the question applies (<see cref="SlowPcPreference.IsOffered"/>). An answer is stored with the hardware it was given for and applied by the controller. With none stored
/// the accurate model shows chosen, which is what is in use until the user answers (R8), and the detail asks the question.
/// </summary>
internal sealed class VoicePreferenceRow : Decorator
{
    private const string LiveId = "live";
    private const string AccurateId = "accurate";

    private readonly AppSettings settings;
    private readonly SpeechModelController controller;
    private bool refreshing;
    private bool answering;

    public VoicePreferenceRow(AppSettings settings, SpeechModelController controller)
    {
        this.settings = settings;
        this.controller = controller;
        Options = new CzSegmentedControl(SlowPcPreference.Title, [(LiveId, SlowPcPreference.LiveTitle), (AccurateId, SlowPcPreference.AccurateTitle)], IdOf(SlowPcPreference.Shown(settings.VoiceTyping)), small: true);
        Options.SelectionChanged += id => Pick(id);
        foreach (var button in Options.Buttons) button.Invoked += () => Pick(button.Id); // a press on the chosen one still answers (it is not a change to the control)
        Row = new VoiceSettingsRow(SlowPcPreference.Title, Options, DetailNow());
        Fold = new CzFoldRow(new StackPanel { Children = { new ChromeRowDivider(), Row } }, IsOfferedNow());
        Child = Fold;
    }

    public CzSegmentedControl Options { get; }

    public VoiceSettingsRow Row { get; }

    public CzFoldRow Fold { get; }

    private static string IdOf(LiveWordsChoice choice) => choice == LiveWordsChoice.Live ? LiveId : AccurateId;

    private bool IsOfferedNow() => SlowPcPreference.IsOffered(settings.VoiceHardware, settings.VoiceTyping);

    // The question while nothing is answered; then the chosen option's own detail.
    private string DetailNow()
    {
        var typing = settings.VoiceTyping;
        if (typing.SlowChoice is null) return SlowPcPreference.Question;
        if (typing.SlowChoice == LiveWordsChoice.Accurate) return SlowPcPreference.AccurateDetail;
        var accurate = SlowPcPreference.AccurateModelId(settings.VoiceHardware);
        return accurate is not null && SelfTestPlan.StepDown(accurate) is { } smaller ? SlowPcPreference.LiveDetail(smaller) : SlowPcPreference.Question;
    }

    private void Pick(string id)
    {
        if (refreshing || answering) return;
        var choice = id == LiveId ? LiveWordsChoice.Live : LiveWordsChoice.Accurate;
        if (settings.VoiceTyping.SlowChoice == choice) return; // already the answer: nothing to store
        answering = true;
        var task = controller.Answer(choice);
        task.ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => { answering = false; Refresh(); }), TaskScheduler.Default);
    }

    /// <summary>Shows what the settings say: the chosen option, the detail, and whether the row is offered.</summary>
    public void Refresh()
    {
        refreshing = true;
        try
        {
            Fold.Open = IsOfferedNow(); // first: a control inside a folded row is disabled and takes no selection
            Options.Select(IdOf(SlowPcPreference.Shown(settings.VoiceTyping)));
            Row.SetDetail(DetailNow());
        }
        finally { refreshing = false; }
    }
}
