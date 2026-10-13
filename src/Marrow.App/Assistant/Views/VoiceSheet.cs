// PORT-ONLY: Dextra AS-18 (DESIGN.md rev 5, 2.4 "A voice sheet opens. Armilla at 176 px reacts to the owner's voice and then to the Assistant's own voice, and
// the transcript writes itself live. The reply is spoken aloud and shown as text with the current word highlighted"; 2.5 Armilla at 176 px "follows the voice
// level", Reduce motion: "only the brightness follows the voice"; 2.2 "It hides while the voice sheet is open"; the canvas's .vsheet, .vorb, .vstate, .vtx and
// openVoice, work/design/assistant/index.html lines 363-380, 760-768, 2382-2420; ED-NEW-dextra-as18-10). No Swift twin.
// The voice sheet: a glass card over the home's conversation column. A state word (Listening, Thinking, Speaking, Ready), Armilla at 176 px in the matching pose
// following the microphone while it listens and Dextra's own speech while it speaks, what the owner said (live, then final), what Dextra says with the word being
// spoken lit, a note that says why voice cannot go on, and two buttons: Interrupt (cut Dextra and listen) and Done. Escape is Done. Nothing here decides anything:
// every move is the conversation's. The level is fed once a frame from the display link while the sheet is open; Reduce motion is the mark's own (its engine
// moves only the brightness). Words are lit by a background behind the word being spoken and by the ink of the ones still to come, never by weight or opacity, so
// no line reflows while it speaks and every word keeps 4.5:1.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

public sealed class VoiceSheet : Border
{
    /// <summary>The mark's side in the sheet (2.5: 176 px, full form, follows the voice).</summary>
    public const double MarkSide = 176;

    private readonly VoiceConversation voice;
    private readonly Func<string> chord;
    private readonly TextBlock stateText;
    private readonly TextBlock heardText;
    private readonly TextBlock replyText;
    private readonly TextBlock noteText;
    private readonly TextBlock hint;
    private readonly LiveStatusText live;
    private readonly IBrush past;
    private readonly IBrush upcoming;
    private readonly IBrush lit;
    private readonly List<Run> wordRuns = [];
    private DisplayLink? link;
    private MotionScope.Entry? budget;
    private string shownReply = "";
    private int shownWord = -2;
    private string announcedState = "";
    private string announcedReply = "";

    /// <param name="chord">The talk chord as the owner has it ("Ctrl+Alt+Enter"), for the hint.</param>
    public VoiceSheet(VoiceConversation voice, Func<string>? chord = null)
    {
        this.voice = voice ?? throw new ArgumentNullException(nameof(voice));
        this.chord = chord ?? (() => DextraSettings.DefaultTalkChord);

        Mark = new ArmillaMark { MarkSize = MarkSide, Kind = ArmillaKind.Live, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAccessibilityView(Mark, AccessibilityView.Raw);
        var ring = new Border { Width = MarkSide, Height = MarkSide, Child = Mark, HorizontalAlignment = HorizontalAlignment.Center };

        stateText = TaskLook.Text("", 11, SwiftFont.Weight.Medium, TaskLook.Ink2);
        stateText.HorizontalAlignment = HorizontalAlignment.Center;
        stateText.LetterSpacing = 0.7;
        AutomationProperties.SetAccessibilityView(stateText, AccessibilityView.Raw); // the live region says it once

        heardText = TaskLook.Text("", 14, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        heardText.TextAlignment = TextAlignment.Center;
        heardText.MinHeight = 22;
        heardText.MaxWidth = 460;
        AutomationProperties.SetName(heardText, "You said");

        replyText = TaskLook.Text("", 16, SwiftFont.Weight.Regular, TaskLook.Ink, wrap: true);
        replyText.TextAlignment = TextAlignment.Center;
        replyText.MinHeight = 48;
        replyText.MaxWidth = 460;
        replyText.Inlines = [];
        AutomationProperties.SetName(replyText, "Dextra says");

        noteText = TaskLook.Text("", 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        noteText.TextAlignment = TextAlignment.Center;
        noteText.MaxWidth = 460;
        noteText.IsVisible = false;

        past = TaskLook.Ink.Brush(replyText);
        upcoming = TaskLook.Ink2.Brush(replyText);
        lit = Chrome.Overlay(.16).Brush(replyText);

        var (interrupt, interruptShown) = TaskLook.Small("Interrupt", voice.Interrupt, name: "Interrupt Dextra");
        var (done, doneShown) = TaskLook.Small("Done", Finish, primary: true, name: "Done talking");
        InterruptButton = interrupt;
        DoneButton = done;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { interruptShown, doneShown } };
        AutomationProperties.SetName(buttons, "Voice controls");

        hint = TaskLook.Text("", 11.5, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        hint.TextAlignment = TextAlignment.Center;
        hint.MaxWidth = 460;

        live = new LiveStatusText { Width = 1, Height = 1, ClipToBounds = true, Opacity = 0, IsHitTestVisible = false };

        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { stateText, ring, heardText, replyText, noteText, buttons, hint, live },
        };
        // in a short window nothing shrinks over the buttons: the column tightens, then scrolls (the canvas: overflow-y auto)
        Child = new ScrollViewer
        {
            Content = column,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Padding = new Thickness(22, 16);
        Margin = new Thickness(10);
        SwiftGlass.SetEffect(this, GlassEffect.Regular(GlassShape.RoundedRectangle(20, RoundedCornerStyle.Continuous)));
        AutomationProperties.SetName(this, "Voice");
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Window);
        Focusable = false;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Finish();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        Show();
    }

    /// <summary>Armilla at 176 px.</summary>
    public ArmillaMark Mark { get; }

    public Button InterruptButton { get; }

    public Button DoneButton { get; }

    /// <summary>Done or Escape: the conversation is closed. The home hides the sheet and gives the focus back.</summary>
    public event Action? Closed;

    /// <summary>The word for the state: Listening, Thinking, Speaking or Ready.</summary>
    public string StateText => voice.StateLabel;

    public string HeardText => heardText.Text ?? "";

    public string ReplyText => shownReply;

    public string NoteText => noteText.IsVisible ? noteText.Text ?? "" : "";

    public string HintText => hint.Text ?? "";

    /// <summary>The polite live region: the state, then what Dextra says.</summary>
    public string Announced => live.Text ?? "";

    /// <summary>The word lit now (an index into the words of <see cref="ReplyText"/>), or -1.</summary>
    public int LitWord => shownWord;

    /// <summary>Where the level goes each frame (tests read it; the mark takes it as well).</summary>
    internal Action<double>? LevelSink { get; set; }

    /// <summary>The last level sent to the mark.</summary>
    internal double LastLevel { get; private set; }

    /// <summary>Whether the frame link is running (it runs only while the sheet is on screen and the conversation is open).</summary>
    internal bool Linked => link is not null;

    private void Attach()
    {
        voice.Changed -= OnChanged;
        voice.Changed += OnChanged;
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            budget?.Leave();
            budget = MotionScope.For(top).Join(ArmillaListRank.Voice, () => Mark.Moves ? 1 : 0, () => IsEffectivelyVisible, calm => Mark.Calm = calm);
            Mark.MovesChanged += OnMoves;
        }
        Show();
        Follow();
    }

    private void Detach()
    {
        voice.Changed -= OnChanged;
        Mark.MovesChanged -= OnMoves;
        budget?.Leave();
        budget = null;
        link?.Invalidate();
        link = null;
    }

    private void OnMoves(ArmillaMark _) => budget?.Changed();

    private void OnChanged()
    {
        Show();
        Follow();
    }

    /// <summary>Reads the conversation and shows it: the pose, the words, the note, the buttons. Called on every change.</summary>
    public void Show()
    {
        var phase = voice.Phase;
        var label = voice.StateLabel;
        stateText.Text = label.ToUpperInvariant();
        Mark.State = phase switch
        {
            VoicePhase.Listening => ArmillaState.Listening,
            VoicePhase.Thinking => ArmillaState.Thinking,
            VoicePhase.Speaking => ArmillaState.Speaking,
            _ => ArmillaState.Idle,
        };
        Mark.Activity = label;
        heardText.Text = voice.Heard.Length > 0 ? voice.Heard : phase == VoicePhase.Listening ? "Go ahead, I am listening." : "";
        ShowReply(voice.Reply);
        var note = voice.Note;
        noteText.Text = note ?? "";
        noteText.IsVisible = !string.IsNullOrEmpty(note);
        InterruptButton.IsEnabled = phase is VoicePhase.Speaking or VoicePhase.Thinking;
        hint.Text = $"Hold Dextra's mark in the composer, or press {chord()} anywhere in Windows to start and stop. Speak over it to interrupt; echo cancellation keeps it from hearing itself.";
        ShowWord(voice.SpokenWord);
        Announce(label, voice.Reply, note);
        budget?.Changed();
    }

    // The reply as one run for each word (and for each gap), so lighting a word changes two runs and moves nothing.
    private void ShowReply(string reply)
    {
        if (reply == shownReply) return;
        shownReply = reply;
        shownWord = -2; // lit again from scratch
        wordRuns.Clear();
        var inlines = replyText.Inlines!;
        inlines.Clear();
        var at = 0;
        foreach (var word in SpokenText.Words(reply))
        {
            if (word.Start > at) inlines.Add(new Run(reply[at..word.Start]) { Foreground = past });
            var run = new Run(reply.Substring(word.Start, word.Length)) { Foreground = past };
            wordRuns.Add(run);
            inlines.Add(run);
            at = word.Start + word.Length;
        }
        if (at < reply.Length) inlines.Add(new Run(reply[at..]) { Foreground = past });
    }

    /// <summary>Lights the word being spoken: the ones before it keep the ordinary ink, the ones after it the second ink. Nothing is lit unless Dextra speaks.</summary>
    private void ShowWord(int index)
    {
        if (voice.Phase != VoicePhase.Speaking) index = -1;
        if (index == shownWord) return;
        shownWord = index;
        for (var i = 0; i < wordRuns.Count; i++)
        {
            var run = wordRuns[i];
            run.Background = i == index ? lit : null;
            run.Foreground = index < 0 || i <= index ? past : upcoming;
        }
    }

    private void Announce(string state, string reply, string? note)
    {
        var parts = new List<string>();
        if (state != announcedState && voice.Phase is not VoicePhase.Closed)
        {
            announcedState = state;
            parts.Add(state);
        }
        if (reply.Length > 0 && reply != announcedReply && voice.Phase == VoicePhase.Speaking)
        {
            announcedReply = reply;
            parts.Add(reply);
        }
        if (reply.Length == 0) announcedReply = "";
        if (!string.IsNullOrEmpty(note)) parts.Add(note);
        if (parts.Count == 0) return;
        var line = string.Join(". ", parts);
        if (live.Text == line) live.Text = "";
        live.Text = line;
    }

    // While the sheet is open and shown, once a frame: the level the mark follows and the word lit. The link ends itself with the conversation.
    private void Follow()
    {
        var active = voice.IsOpen && VisualRoot is not null && FrameClock.IsBound;
        if (!active)
        {
            link?.Invalidate();
            link = null;
            return;
        }
        if (link is not null) return;
        link = FrameClock.DisplayLink(Pump);
        link.Add();
    }

    /// <summary>One frame: the voice level goes to the mark (the microphone while it listens, Dextra's speech while it speaks), and the spoken word is lit.</summary>
    internal void Pump()
    {
        if (!voice.IsOpen)
        {
            link?.Invalidate();
            link = null;
            return;
        }
        var level = voice.Phase is VoicePhase.Listening or VoicePhase.Speaking ? voice.Level : 0;
        LastLevel = level;
        if (LevelSink is { } sink) sink(level);
        if (voice.Phase is VoicePhase.Listening or VoicePhase.Speaking) Mark.SetLevel(level);
        if (voice.Phase == VoicePhase.Speaking) ShowWord(voice.SpokenWord);
    }

    private void Finish()
    {
        voice.Close();
        Closed?.Invoke();
    }

    /// <summary>Puts the keyboard on the first button that can take it (Done), as a sheet that just opened does.</summary>
    public void TakeFocus() => DoneButton.Focus(NavigationMethod.Tab);
}
