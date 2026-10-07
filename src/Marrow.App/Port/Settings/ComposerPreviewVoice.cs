// PORT-ONLY CZ-7b: customization-SPEC CZ-4.3/CZ-4.7, OD-0091/OD-0097.
// The stage uses the composer's real controller, orb/pill/cross/glow and dictation layer.
// Only its synthetic audio/recognition inputs and page-local text differ from a chat composer.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Light;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;

namespace Marrow.App.Port.Settings;

internal sealed partial class ComposerPreview
{
    internal ComposerVoiceController? Voice { get; private set; }
    internal VoiceOrbSlot? VoiceOrb { get; private set; }
    internal VoicePillSlot? VoicePill { get; private set; }
    internal VoiceGlowLayer? Glow { get; private set; }
    internal DictationLayer? Dictation { get; private set; }
    internal VoiceLanguageQuestion? Question { get; private set; }
    internal ComposerAttachButton? OutsideAttach { get; private set; }
    internal double TextInset => padded.Padding.Left;
    internal string VoiceText => voiceText;

    private string voiceText = "";
    private VoiceTypingSettings? answer;
    private ComposerNSTextView? voiceEditor;
    private Popup? questionPopup;
    private WorkItem? replay, selfAnswer, sendClear, sendEnd;
    private AnimatedScalar? attachWidth, attachInset, attachFade, attachScale, rightInset, sendOpacity, sendLift;
    private bool scriptedQuestion, settingState, voiceRunning;
    private TopLevel? voiceWindow;
    private readonly TranslateTransform sendShift = new();

    internal void EnableVoice(Func<VoiceOrbSettings> orb, Func<VoiceTypingSettings> typingSettings)
    {
        if (Voice is not null) return;
        var source = ScriptedVoiceSource.ForDesign(() => new VoiceSessionSettings(orb(), answer ?? typingSettings(), "en", new SpeechModel("preview", null)), value => answer = value);
        Voice = new ComposerVoiceController(source.Session, orb, () => new DraftSelection(voiceEditor?.Text ?? "", 0, voiceEditor?.Text?.Length ?? 0), ReplaceVoice, SendVoice);
        VoiceOrb = new VoiceOrbSlot(Voice);
        Controls.Children.Insert(0, VoiceOrb);
        Glow = new VoiceGlowLayer(Voice);
        VoicePill = new VoicePillSlot(Voice);
        layers.Children.Insert(1, Glow);
        layers.Children.Insert(2, VoicePill);
        VoicePill.Attach(VoiceOrb);
        // The shared cross cancels the real session; the stage also owns pending replays and sends.
        VoicePill.Pill.Cross.Click += (_, _) => CancelVoice();
        VoiceOrb.Cross.Click += (_, _) => CancelVoice();

        voiceEditor = new ComposerNSTextView { IsReadOnly = true, Focusable = false, IsHitTestVisible = false, Opacity = 0, VerticalAlignment = VerticalAlignment.Top };
        DragDrop.SetAllowDrop(voiceEditor, false);
        Dictation = new DictationLayer(voiceEditor, null);
        var voiceField = new Grid { Children = { voiceEditor, Dictation }, RenderTransform = sendShift };
        textCell.Children.Add(voiceField);
        sendOpacity = new AnimatedScalar(this, 1, value => voiceField.Opacity = value);
        sendLift = new AnimatedScalar(this, 0, value => sendShift.Y = value);
        Voice.DictationBegan += selection =>
        {
            Stop();
            typing = false;
            voiceRunning = false;
            sendClear?.Cancel();
            sendEnd?.Cancel();
            sendOpacity.Set(1, null);
            sendLift.Set(0, null);
            voiceText = "";
            voiceEditor.Text = "";
            Dictation.Begin(new DraftSelection("", 0, 0));
            hintOpacity.To(0, Marrow.UI.Port.Settings.CzMotion.Ease(.2));
            typedOpacity.To(0, Marrow.UI.Port.Settings.CzMotion.Ease(.2));
            Caret.IsVisible = false;
            VoiceRest();
            ShowVoicePhase();
        };
        Voice.Transcript += update => { Dictation.Apply(update); ShowVoicePhase(); };
        Voice.DictationAbandoned += Dictation.Abandon;
        Dictation.TextChanged += value => { voiceText = value; ShowVoicePhase(); };
        Dictation.ActiveChanged += () => { voiceEditor.Opacity = Dictation.IsActive ? 0 : 1; ShowVoicePhase(); };

        rightInset = new AnimatedScalar(this, 0, inset => text.Margin = new Thickness(0, 4, inset, 4));
        VoicePill.InsetTargetChanged += target => rightInset.Set(target, LookCurve.Ez(.42));

        OutsideAttach = new ComposerAttachButton(ComposerAttachPlace.Outside, false, Binding<bool>.Constant(false), _ => { }, () => { })
        {
            IsHitTestVisible = false, Focusable = false, Width = 36, Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(10, 0, 0, 20), Opacity = 0,
        };
        Children.Add(OutsideAttach);
        var insideScale = new ScaleTransform(1, 1);
        Attach.RenderTransform = insideScale;
        Attach.RenderTransformOrigin = RelativePoint.Center;
        attachWidth = new AnimatedScalar(this, 0, value =>
        {
            Pill.Width = ComposerWidth - 44 * value;
            Pill.Margin = new Thickness(ComposerLeft + 44 * value, 0, 0, ComposerBottom);
        });
        attachInset = new AnimatedScalar(this, 0, value => padded.Padding = new Thickness(48 - 34 * value, 8, 8, 8));
        attachFade = new AnimatedScalar(this, 0, value => { Attach.Opacity = 1 - value; OutsideAttach.Show(value, attachScale?.Value ?? value); OutsideAttach.IsHitTestVisible = false; });
        attachScale = new AnimatedScalar(this, 0, value => { insideScale.ScaleX = insideScale.ScaleY = 1 - .4 * value; OutsideAttach.Show(attachFade.Value, value); OutsideAttach.IsHitTestVisible = false; });

        Question = new VoiceLanguageQuestion { TopClearance = 56, RenderTransform = new TranslateTransform() };
        Question.Answered += Voice.AnswerLanguage;
        Question.Dismissed += CancelVoice;
        questionPopup = new Popup { Child = Question, PlacementTarget = Pill, Placement = PlacementMode.Left, IsLightDismissEnabled = true };
        // A custom placement hosts the question in the composer's coordinates without the stage's clip.
        questionPopup.Placement = PlacementMode.AnchorAndGravity;
        questionPopup.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        questionPopup.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        PopoverHost.SetSiteCard(questionPopup, Question.Card);
        Question.AttachPopup(VoiceOrb, Pill, questionPopup);
        Children.Add(questionPopup);
        questionPopup.Closed += (_, _) => { if (Voice.State == VoiceSessionState.Asking) CancelVoice(); };

        var placed = false;
        this.Track(orb, value =>
        {
            var target = value.Attachment == VoiceAttachment.Outside ? 1 : 0;
            attachWidth.Set(target, placed ? LookCurve.Ez(.42) : null);
            attachInset.Set(target, placed ? LookCurve.Ez(.34) : null);
            attachFade.Set(target, placed ? LookCurve.Ease(.28) : null);
            attachScale.Set(target, placed ? LookCurve.Ez(.34) : null);
            placed = true;
        });
        this.Track(() => VoiceLightTarget.For(orb().LightVoice, Voice.State), level => Light.VoiceLight = level);
        this.Track(() => Voice.State, ShowQuestion);
        session.VoicePreviewChangeRequested += VoicePick;
        session.VoiceDemoCancelRequested += CancelVoice;
        session.StatePickRequested += StatePicked;
        AttachedToVisualTree += (_, _) => WatchVoice();
        WatchVoice();
        ShowVoicePhase();
    }

    private void ReplaceVoice(int start, int length, string replacement)
    {
        voiceText = replacement;
        voiceEditor!.Text = replacement;
        voiceEditor.Opacity = Dictation!.IsActive ? 0 : 1;
        ShowVoicePhase();
    }

    private void VoiceRest()
    {
        settingState = true;
        try { session.Pick(PreviewState.Rest); }
        finally { settingState = false; }
    }

    private void StatePicked()
    {
        if (settingState) return;
        CancelVoice();
        // cz.js previewState: picking a state replaces even the completed/shown demo.
        // A live cancel keeps the real layer's withdrawal; landed editor text gives way to the picked phase.
        if (Dictation!.Phase != DictationPhase.Withdrawing)
        {
            voiceText = "";
            voiceEditor!.Text = "";
            Dictation.HandOverNow();
            ShowVoicePhase();
        }
    }

    private void VoicePick(string key, TimeSpan? delay)
    {
        replay?.Cancel();
        replay = null;
        if (key == "langMode") answer = null;
        if (delay is null) return;
        if (delay.Value == TimeSpan.Zero) ReplayVoice();
        else replay = MainActor.AsyncAfter(delay.Value, () => { replay = null; ReplayVoice(); });
    }

    private void ReplayVoice()
    {
        CancelVoice();
        scriptedQuestion = true;
        VoiceRest();
        Voice!.Activate();
    }

    internal void CancelVoice()
    {
        replay?.Cancel(); replay = null;
        selfAnswer?.Cancel(); selfAnswer = null;
        sendClear?.Cancel(); sendClear = null;
        sendEnd?.Cancel(); sendEnd = null;
        voiceRunning = false;
        Voice?.Session.Cancel();
        Voice?.ComposerLeaving(); // withdraw a pending send too
        Question?.DismissImmediately();
        if (questionPopup?.IsOpen == true) Presentation.SetPresented(questionPopup, false, VoiceOrb!, CancelVoice);
        ShowVoicePhase();
    }

    private void SendVoice()
    {
        voiceRunning = true;
        sendOpacity!.Set(0, LookCurve.Ease(.22));
        sendLift!.Set(-7, LookCurve.Ez(.3));
        ShowVoicePhase();
        sendClear = MainActor.AsyncAfter(TimeSpan.FromSeconds(.3), () =>
        {
            voiceText = "";
            voiceEditor!.Text = "";
            Dictation!.HandOverNow();
            hintOpacity.To(1, Marrow.UI.Port.Settings.CzMotion.Ease(.25));
            ShowVoicePhase();
        });
        sendEnd = MainActor.AsyncAfter(TimeSpan.FromSeconds(3), () => { voiceRunning = false; VoiceRest(); ShowVoicePhase(); });
    }

    private void ShowVoicePhase()
    {
        if (Voice is null) return;
        var running = voiceRunning || session.State == PreviewState.Running;
        var words = voiceText.Length > 0 || Dictation?.IsActive == true;
        Light.Phase = running ? ComposerLightPhase.Running : words ? ComposerLightPhase.Typing : Voice.IsLive ? ComposerLightPhase.Rest : session.Phase;
        send.Show(running, running || words || session.State == PreviewState.Typing, animated: true);
        dots.Show(running);
        Ring.Run(running);
        if (!Voice.IsLive && !words && !voiceRunning)
        {
            SetTyping(session.State == PreviewState.Typing, false);
            if (!typing) hintOpacity.To(1, Marrow.UI.Port.Settings.CzMotion.Ease(.25));
        }
    }

    private void ShowQuestion(VoiceSessionState state)
    {
        if (state == VoiceSessionState.Asking)
        {
            Question!.ReturnFocus = scriptedQuestion ? null : () => VoiceOrb!.HitTarget.Focus();
            Presentation.SetPresented(questionPopup!, true, VoiceOrb!, CancelVoice);
            Question.Open(takeFocus: !scriptedQuestion);
            if (scriptedQuestion) selfAnswer = MainActor.AsyncAfter(TimeSpan.FromSeconds(1.6), () => Question.Answer("en"));
        }
        else
        {
            selfAnswer?.Cancel(); selfAnswer = null;
            Question!.Close();
            // Keep the answer highlight and closing animation outside the stage clip.
            MainActor.AsyncAfter(TimeSpan.FromSeconds(.46), () =>
            {
                if (!Question.IsOpen && questionPopup!.IsOpen) Presentation.SetPresented(questionPopup, false, VoiceOrb!, CancelVoice);
            });
        }
        scriptedQuestion = false;
        ShowVoicePhase();
    }

    private void WatchVoice()
    {
        if (voiceWindow is not null || TopLevel.GetTopLevel(this) is not { } top) return;
        voiceWindow = top;
        top.AddHandler(InputElement.KeyDownEvent, VoiceKey, RoutingStrategies.Tunnel);
    }

    private void VoiceKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || Voice?.IsLive != true) return;
        if (sender is TopLevel top && (SheetKeyboard.IsTaken(top) || WindowTransients.Newest(top) is not null)) return;
        CancelVoice();
        e.Handled = true;
    }

    private void LeaveVoice()
    {
        replay?.Cancel(); selfAnswer?.Cancel(); sendClear?.Cancel(); sendEnd?.Cancel();
        Voice?.ComposerLeaving();
        voiceWindow?.RemoveHandler(InputElement.KeyDownEvent, VoiceKey);
        voiceWindow = null;
        Question?.DismissImmediately();
        if (questionPopup?.IsOpen == true) Presentation.SetPresented(questionPopup, false, VoiceOrb!, CancelVoice);
    }
}
