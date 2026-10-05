// PORT-ONLY, owner-ruled addition (OD-0098 welcome-voice pick "Place A, Words V2"; first-release WV-1): the "Voice typing" section of the Welcome page.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Port.Voice;
using Marrow.App.Views;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Port.FirstRun;

/// <summary>
/// The "Voice typing" section above Activity in WelcomeView (variant A: Welcome's own section-and-row pattern, a <see cref="ChromeSection"/> over a <see cref="ChromeCard"/>), drawing a <see cref="VoiceSpeedQuestion"/>.
/// OD-0076: nothing cuts. A new state's words are laid over the old (a crossfade, 0.25 s ease) while the card's height moves from the old content's to the new one's (0.35 s, the Getting ready
/// sheet's own curve); the old layer is taken out when it is clear. Under reduce motion a state is taken at once (AnimatedScalar). ENGINEERING DECISION (ED-NEW-cloud-wv-1): the timings are GR-2's,
/// the page's V2 has none of its own in the repository.
/// </summary>
public sealed class WelcomeVoiceSection : Decorator
{
    private const double FadeSeconds = 0.25;
    private const double HeightSeconds = 0.35;

    private readonly VoiceSpeedQuestion question;
    private readonly Grid layers = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Border host;
    private AnimatedScalar? height;
    private VoiceQuestionState shown;

    public WelcomeVoiceSection(VoiceSpeedQuestion question)
    {
        this.question = question;
        host = new Border { Child = layers, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var section = new ChromeSection(WelcomeVoiceWords.Title, () => new ChromeCard(() => host));
        section.MaxWidth = 480; // Welcome's sections are capped at 480 (RootView.swift:337)
        Child = section;
        shown = question.State;
        if (Body(shown) is { } first) layers.Children.Add(first);
        question.Changed += OnChanged;
        DetachedFromVisualTree += (_, _) => question.Changed -= OnChanged;
        AttachedToVisualTree += (_, _) =>
        {
            question.Changed -= OnChanged;
            question.Changed += OnChanged;
            OnChanged();
        };
    }

    /// <summary>Test-only: the layers now in the card (two while a change is fading).</summary>
    internal IReadOnlyList<Control> Layers => [.. layers.Children.OfType<Control>()];

    /// <summary>The choice at <paramref name="index"/> of the state shown: what its button does. Ask: 0 yes, 1 no. Slow: 0 live words, 1 text after you stop.</summary>
    internal Task Choose(int index) => shown switch
    {
        VoiceQuestionState.Ask when index == 0 => Guarded(() => question.YesAsync(null, CancellationToken.None)),
        VoiceQuestionState.Ask when index == 1 => Done(question.No),
        VoiceQuestionState.Slow when index is 0 or 1 => Guarded(() => question.AnswerAsync(index == 0 ? LiveWordsChoice.Live : LiveWordsChoice.Accurate, CancellationToken.None)),
        _ => Task.CompletedTask,
    };

    private static Task Done(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static async Task Guarded(Func<Task> run)
    {
        try
        {
            await run().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Show();
        else Dispatcher.UIThread.Post(Show);
    }

    private void Show()
    {
        var state = question.State;
        if (state == shown) return;
        shown = state;
        var oldHeight = host.Bounds.Height;
        var width = host.Bounds.Width;
        var old = layers.Children.OfType<Control>().ToList();
        var next = Body(state);
        if (next is not null)
        {
            next.Opacity = 0;
            layers.Children.Add(next);
        }
        foreach (var layer in old)
        {
            var fadeOut = new AnimatedScalar(layer, 1, value =>
            {
                layer.Opacity = value;
                if (value <= 0) layers.Children.Remove(layer);
            });
            fadeOut.Set(0, GettingReadyLook.Fade(FadeSeconds));
        }
        if (next is not null)
        {
            var fadeIn = new AnimatedScalar(next, 0, value => next.Opacity = value);
            fadeIn.Set(1, GettingReadyLook.Fade(FadeSeconds));
        }
        var target = 0.0;
        if (next is not null && width > 0)
        {
            next.Measure(new Size(width, double.PositiveInfinity));
            target = next.DesiredSize.Height;
        }
        if (oldHeight <= 0 || width <= 0 || Math.Abs(oldHeight - target) < 0.5) return;
        height = new AnimatedScalar(host, oldHeight, value => host.Height = Math.Abs(value - target) < 0.001 ? double.NaN : value);
        height.Set(target, GettingReadyLook.Settle(HeightSeconds));
    }

    private Control? Body(VoiceQuestionState state)
    {
        var message = WelcomeVoiceWords.Message(state);
        if (message is null) return null;
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(text);
        stack.Children.Add(text);
        if (WelcomeVoiceWords.Note(state) is { } note)
        {
            var noteText = new TextBlock { Text = note, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(noteText);
            noteText.Foreground = Chrome.SecondaryText.Brush(noteText);
            stack.Children.Add(noteText);
        }
        var choices = WelcomeVoiceWords.Choices(state);
        if (choices.Count > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            for (var index = 0; index < choices.Count; index++)
            {
                var chosen = index;
                row.Children.Add(WelcomeView.GlassProminentButton(new TextBlock { Text = choices[index] }, Chrome.CapsuleContentHeight, () => _ = Choose(chosen)));
            }
            stack.Children.Add(row);
        }
        return new Border { Padding = new Thickness(16, 11), Child = stack, HorizontalAlignment = HorizontalAlignment.Stretch };
    }
}
