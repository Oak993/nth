// PORT-ONLY: Dextra AS-18 (DESIGN.md rev 5, 2.4 "Dextra's look in voice ... Inside the Assistant, the talk button and the voice sheet show Armilla, not the orb:
// 24 px on the button, 176 px in the sheet"; "Hold the orb inside nth (a pointer hold, so no keyboard hook is needed): talk while held"; "Tap the orb"; 16 unit AS-18
// "hold on the talk button"; 2.5 Armilla at 24 px "the composer's talk button"; ED-NEW-dextra-as18-10). No Swift twin.
// The talk button of Dextra's home composer: Armilla at 24 px on a round 32 px button, in the place the dictation orb has in every other composer (the dictation
// orb elsewhere is unchanged). A press that finds nothing going on starts listening and holds it for as long as the pointer stays down; a tap leaves it listening
// and it ends on silence, as the chord does; a press while Dextra talks or listens ends the request or cuts Dextra, as the chord does. Space and Enter on the focused
// button toggle the conversation, so it works with the keyboard and a screen reader. The mark takes the pose of the conversation (Listening, Thinking, Speaking) and
// rests still otherwise.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

public sealed class VoiceTalkButton : Decorator
{
    private readonly Func<VoiceConversation?> conversation;
    private readonly Func<string> chord;
    private readonly Func<DateTimeOffset> now;
    private readonly Button button;
    private readonly Border face;
    private VoiceConversation? watched;
    private DateTimeOffset? pressedAt;
    private bool holding;

    /// <param name="conversation">The conversation now (the host's), or null while Dextra's voice is not running.</param>
    /// <param name="chord">The talk chord's text, for the tip.</param>
    public VoiceTalkButton(Func<VoiceConversation?> conversation, Func<string>? chord = null, Func<DateTimeOffset>? now = null)
    {
        this.conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        this.chord = chord ?? (() => DextraSettings.DefaultTalkChord);
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        Mark = new ArmillaMark { MarkSize = 24, Kind = ArmillaKind.Row, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(Mark, AccessibilityView.Raw);
        face = new Border { Width = 32, Height = 32, Child = Mark, Background = Brushes.Transparent };
        face.Capsule(RoundedCornerStyle.Continuous);
        button = PlainControls.Button(face, Toggle);
        button.KeyboardFocus(16);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.AddHandler(InputElement.PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        button.PointerCaptureLost += (_, _) => Cancelled();
        button.PointerEntered += (_, _) => { if (!holding) face.Background = Chrome.Overlay(.08).Brush(face); };
        button.PointerExited += (_, _) => { if (!holding) face.Background = Brushes.Transparent; };
        AutomationProperties.SetName(button, "Talk");
        VerticalAlignment = VerticalAlignment.Center;
        Child = button;
        AttachedToVisualTree += (_, _) => Watch();
        DetachedFromVisualTree += (_, _) => Unwatch();
        Show();
    }

    public ArmillaMark Mark { get; }

    public Button Button => button;

    /// <summary>True while a press that started listening is held.</summary>
    public bool IsHeld => holding;

    private void Toggle() => conversation()?.Toggle();

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        e.Pointer.Capture(button);
        Press();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = true;
        e.Pointer.Capture(null);
        Release();
    }

    /// <summary>The press, as a pointer or a test makes it.</summary>
    internal void Press()
    {
        if (conversation() is not { } voice) return;
        Watch();
        pressedAt = now();
        holding = voice.PressTalk();
        if (holding) face.Background = Chrome.Overlay(.14).Brush(face);
        Show();
    }

    /// <summary>The release, as a pointer or a test makes it.</summary>
    internal void Release()
    {
        face.Background = Brushes.Transparent;
        if (!holding)
        {
            pressedAt = null;
            return;
        }
        holding = false;
        var held = pressedAt is { } at ? now() - at : TimeSpan.Zero;
        pressedAt = null;
        conversation()?.ReleaseTalk(held);
        Show();
    }

    // The pointer was taken away (a window change): a hold that cannot be released is ended as a tap would, never left listening on its own.
    private void Cancelled()
    {
        if (!holding) return;
        Release();
    }

    private void Watch()
    {
        var voice = conversation();
        if (ReferenceEquals(voice, watched)) return;
        if (watched is not null) watched.Changed -= OnChanged;
        watched = voice;
        if (watched is not null) watched.Changed += OnChanged;
        Show();
    }

    private void Unwatch()
    {
        if (watched is not null) watched.Changed -= OnChanged;
        watched = null;
    }

    private void OnChanged() => Show();

    /// <summary>The mark and the tip as the conversation has them now.</summary>
    public void Show()
    {
        var phase = watched?.Phase ?? VoicePhase.Closed;
        Mark.State = phase switch
        {
            VoicePhase.Listening => ArmillaState.Listening,
            VoicePhase.Thinking => ArmillaState.Thinking,
            VoicePhase.Speaking => ArmillaState.Speaking,
            _ => ArmillaState.Idle,
        };
        var tip = $"Talk (hold to talk, or {chord()} anywhere)";
        ToolTip.SetTip(button, tip);
        button.IsEnabled = conversation() is not null;
        AutomationProperties.SetHelpText(button, tip);
    }
}

/// <summary>Where the composer of Dextra's home thread gets its talk button instead of the dictation orb.</summary>
internal static class DextraTalkSlot
{
    /// <summary>The talk button for the composer of <paramref name="thread"/> when it is Dextra's own home thread; null for every other chat (they keep the orb).</summary>
    public static Control? For(ThreadRuntime runtime, ChatThread thread)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(thread);
        if (thread.AssistantID is not { } assistant || thread.MissionID is not null || thread.ParentThreadID is not null) return null;
        if (runtime.App is not { } app || app.AssistantHost.Id != assistant) return null;
        var host = app.AssistantHost;
        return new VoiceTalkButton(() => host.Voice?.Conversation, () => host.Settings.TalkChord);
    }
}
