// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0096, OD-0099 Q-V-4 A; first-release voice typing, VT-14): the hardware card of the Voice typing page (voice-SPEC V-16.3.2, V-10.6; DESIGN gen.py:640-653).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>
/// The first card of the Speech model section. The title row `Recommended for this PC` with its detail (never checked, checking, checked by itself, checked just now or on a date) and the
/// `Check my hardware` capsule; under it, folding open when a check starts, the four probe rows resolving one by one; the recommendation box, folding open when the check is done, with the
/// model's name, the reason line and either `Use recommended` and `Keep {model}` or the state line; the slow-PC preference row (Q-V-1 C) where it applies; and the note. It shows what the
/// <see cref="SpeechModelController"/> says and does what the buttons ask of it; every change of what it shows crossfades, folds or scales in (OD-0076).
/// </summary>
internal sealed class VoiceHardwareCard : Decorator
{
    private readonly SpeechModelController controller;
    private readonly AppSettings settings;

    public VoiceHardwareCard(SpeechModelController controller, AppSettings settings)
    {
        this.controller = controller;
        this.settings = settings;
        var phase = controller.Phase;
        var typing = settings.VoiceTyping;
        var recommended = controller.RecommendedFile;

        CheckButton = new VoiceCheckButton(HardwareCardText.ButtonLabel(phase, controller.Record is not null));
        CheckButton.Busy = phase == HardwareCardPhase.Running;
        CheckButton.Invoked += () => _ = controller.Check();
        TitleRow = new VoiceSettingsRow(HardwareCardText.Title, CheckButton, HardwareCardText.Detail(phase, controller.Record, SwiftDate.Now));

        var probeStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Margin = new Thickness(16, 0, 16, 10) };
        var probeRows = new List<VoiceProbeRow>();
        foreach (var key in HardwareCardText.ProbeKeys)
        {
            var row = new VoiceProbeRow(key);
            probeRows.Add(row);
            probeStack.Children.Add(row);
        }
        ProbeRows = probeRows;
        ProbeFold = new CzFoldRow(probeStack, phase != HardwareCardPhase.Idle);

        RecommendationName = new CzCrossfadeText(recommended is null ? "" : ModelCatalog.ModelName(recommended.Id)!) { FontSize = 13, FontWeight = FontWeight.SemiBold, Wrap = true };
        RecommendationName.SetValue(SwiftFont.LineBoxProperty, 18.0);
        Reason = new CzCrossfadeText(controller.Reason) { FontSize = 12, Wrap = true, Secondary = true };
        Reason.SetValue(SwiftFont.LineBoxProperty, 16.0);
        ReasonFold = new CzFoldRow(Reason, controller.Reason.Length > 0);
        var heading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Children = { RecommendationName, ReasonFold } };

        UseButton = new VoicePrimaryButton(HardwareCardText.UseRecommended);
        UseButton.Invoked += () => controller.UseRecommended();
        var current = CurrentModelName();
        KeepButton = new VoiceCheckButton(HardwareCardText.Keep(current ?? "")) { IsVisible = current is not null };
        KeepButton.Invoked += () => controller.Keep();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UseButton, KeepButton } };
        UseFold = new CzFoldRow(buttons, UseOpen());

        var tick = new CzCheckMark { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = .7 };
        StateText = new CzCrossfadeText(StateLine()) { FontSize = 12, Secondary = true, VerticalAlignment = VerticalAlignment.Center };
        var stateLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { tick, StateText } };
        StateFold = new CzFoldRow(stateLine, StateOpen());
        tick.SetChecked(true);

        var box = new VoiceRecommendationBox(new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Children = { heading, UseFold, StateFold } });
        RecommendationFold = new CzFoldRow(box, RecommendationOpen());

        Preference = new VoicePreferenceRow(settings, controller);
        Note = new CzCrossfadeText(HardwareCardText.Note) { FontSize = 11.5, Wrap = true, Secondary = true, Margin = new Thickness(16, 0, 16, 12) };
        Note.SetValue(SwiftFont.LineBoxProperty, 16.0);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(TitleRow);
        stack.Children.Add(ProbeFold);
        stack.Children.Add(RecommendationFold);
        stack.Children.Add(Preference);
        stack.Children.Add(Note);
        Child = new ChromeCard(() => stack);
        ApplyRows();
    }

    public VoiceSettingsRow TitleRow { get; }

    public VoiceCheckButton CheckButton { get; }

    public IReadOnlyList<VoiceProbeRow> ProbeRows { get; }

    public CzFoldRow ProbeFold { get; }

    public CzFoldRow RecommendationFold { get; }

    public CzCrossfadeText RecommendationName { get; }

    public CzCrossfadeText Reason { get; }

    public CzFoldRow ReasonFold { get; }

    public CzFoldRow UseFold { get; }

    public VoicePrimaryButton UseButton { get; }

    public VoiceCheckButton KeepButton { get; }

    public CzFoldRow StateFold { get; }

    public CzCrossfadeText StateText { get; }

    public VoicePreferenceRow Preference { get; }

    public CzCrossfadeText Note { get; }

    // What the settings and the controller say, one place each (the constructor and Refresh read the same).
    private ModelFile? Recommended => controller.RecommendedFile;

    private string? CurrentModelName() => ModelCatalog.Find(settings.VoiceTyping.Model)?.Model;

    private bool RecommendationOpen() => controller.Phase == HardwareCardPhase.Done && Recommended is not null;

    private bool InUseOrPending() => Recommended is { } rec
        && (string.Equals(settings.VoiceTyping.Model, rec.Id, StringComparison.Ordinal) || string.Equals(controller.PendingId, rec.Id, StringComparison.Ordinal));

    private bool UseOpen() => RecommendationOpen() && !InUseOrPending() && !controller.Decided;

    private bool StateOpen() => RecommendationOpen() && (InUseOrPending() || controller.Decided);

    private string StateLine() =>
        Recommended is { } rec && string.Equals(settings.VoiceTyping.Model, rec.Id, StringComparison.Ordinal) ? HardwareCardText.InUse
        : Recommended is { } pending && string.Equals(controller.PendingId, pending.Id, StringComparison.Ordinal) ? HardwareCardText.DownloadingThenInUse
        : HardwareCardText.Kept(CurrentModelName() ?? "");

    /// <summary>Shows what the controller and the settings say now.</summary>
    public void Refresh()
    {
        var phase = controller.Phase;
        TitleRow.SetDetail(HardwareCardText.Detail(phase, controller.Record, SwiftDate.Now));
        CheckButton.SetLabel(HardwareCardText.ButtonLabel(phase, controller.Record is not null));
        CheckButton.Busy = phase == HardwareCardPhase.Running;
        ProbeFold.Open = phase != HardwareCardPhase.Idle;
        ApplyRows();
        if (Recommended is { } rec)
        {
            RecommendationName.SetText(ModelCatalog.ModelName(rec.Id)!);
            if (controller.Reason.Length > 0) Reason.SetText(controller.Reason);
        }
        ReasonFold.Open = controller.Reason.Length > 0;
        var current = CurrentModelName();
        if (current is not null) KeepButton.SetLabel(HardwareCardText.Keep(current));
        KeepButton.IsVisible = current is not null;
        if (StateOpen()) StateText.SetText(StateLine());
        RecommendationFold.Open = RecommendationOpen();
        UseFold.Open = UseOpen();
        StateFold.Open = StateOpen();
        Preference.Refresh();
    }

    // The four probe rows: waiting rows are dim, the row whose probe runs shows the spinner, a row the probe has shows its check and its value.
    private void ApplyRows()
    {
        var phase = controller.Phase;
        var rows = controller.Rows;
        for (var i = 0; i < ProbeRows.Count; i++)
        {
            var resolved = i < rows.Count;
            var row = ProbeRows[i];
            row.SetOn(phase != HardwareCardPhase.Idle);
            row.SetBusy(phase == HardwareCardPhase.Running && i == rows.Count);
            row.SetDone(resolved);
            row.Value.SetText(resolved ? rows[i].Text : "");
        }
    }
}
