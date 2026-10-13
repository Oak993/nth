// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Asturs are reached by name: 'Channel A, start the next video'. When unsure, it asks 'Channel A or Channel B?'";
// 9.3 "Talking to one directly"; unit AS-18; ED-NEW-dextra-as18-8). No Swift twin.
// Where a request the voice layer does not answer itself goes: to Dextra's home chat as the owner's own words, or by name to an Astur's, and what
// comes back to be said. The conversation sees only IVoiceBrain, so its tests run with a fake; HomeChatBrain is the app's, over the controller's
// Talk (which routes by name and raises "Which one?") and the chats' own runtimes.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>An answer to say, and who gives it.</summary>
public sealed record VoiceAnswer(string Text)
{
    /// <summary>The Astur's own voice, when an Astur answers (its profile's Voice); null is Dextra's.</summary>
    public string? Voice { get; init; }

    /// <summary>The Astur who answers, for the sheet's line.</summary>
    public string? Speaker { get; init; }

    /// <summary>A card the answer asks the owner about ("Which one?"): the next "Channel A" or "the second one" answers it.</summary>
    public DecisionItem? Card { get; init; }
}

public interface IVoiceBrain
{
    /// <summary>Sends what the owner said and completes with the answer to say. Throws OperationCanceledException when cancelled.</summary>
    Task<VoiceAnswer> AskAsync(string heard, CancellationToken cancellationToken);
}

/// <summary>The brain over Dextra's home chat and the Asturs' (the app's production binding).</summary>
public sealed class HomeChatBrain(AppModel model, DextraHost host, TimeSpan? wait = null) : IVoiceBrain
{
    /// <summary>The longest a spoken request waits for its answer.</summary>
    public static readonly TimeSpan MostWait = TimeSpan.FromMinutes(3);

    private readonly TimeSpan most = wait ?? MostWait;

    public async Task<VoiceAnswer> AskAsync(string heard, CancellationToken cancellationToken)
    {
        if (host.Controller is not { } controller) return new VoiceAnswer("Dextra is not running.");
        var home = await OnMain(() => host.HomeThread(), cancellationToken).ConfigureAwait(false);
        if (home is null) return new VoiceAnswer("Dextra has no thread yet. Open the home once, then ask again.");
        var talk = await OnMain(() => controller.Talk(home.Id, heard, spoken: true), cancellationToken).ConfigureAwait(false);
        switch (talk.Kind)
        {
            case TalkKind.Asked:
                var card = talk.Card is { } id ? controller.Queue.Waiting.FirstOrDefault(c => c.Id == id) : null;
                return new VoiceAnswer(card is null ? "Which one did you mean?" : WhichLine(card)) { Card = card };
            case TalkKind.NoHome:
                return new VoiceAnswer(talk.Note ?? "That one has no thread to talk in yet.");
            case TalkKind.Sent when talk.Thread is { } thread && talk.Message.Length > 0:
                return await Reply(thread, talk.Target, cancellationToken).ConfigureAwait(false);
            case TalkKind.Sent:
                return new VoiceAnswer(talk.Target is { } named ? $"Opened {SpokenText.Phrase(named.Name)}." : "Done.") { Speaker = talk.Target?.Name };
        }
        // Dextra's own thread: the owner's words start a fresh, untainted turn, as when he types them (12.4).
        var sent = await OnMain(() =>
        {
            var runtime = model.Runtime(home.Id);
            if (runtime.IsRunning) return false;
            controller.OwnerMessage(home.Id);
            return runtime.SendMissionMessage(talk.Message);
        }, cancellationToken).ConfigureAwait(false);
        return sent ? await Reply(home.Id, null, cancellationToken).ConfigureAwait(false) : new VoiceAnswer("I am still working on your last request.");
    }

    private async Task<VoiceAnswer> Reply(Guid thread, AssistantProfile? speaker, CancellationToken cancellationToken)
    {
        var finished = new TaskCompletionSource<TurnStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Done(Guid id, TurnStatus status, bool continues)
        {
            if (id == thread && !continues) finished.TrySetResult(status);
        }
        model.ThreadTurnFinished += Done;
        try
        {
            using var timeout = new CancellationTokenSource(most);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var registration = linked.Token.Register(() => finished.TrySetCanceled(linked.Token));
            TurnStatus status;
            try
            {
                status = await finished.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new VoiceAnswer("That is taking a while. The answer will be in the chat.") { Speaker = speaker?.Name, Voice = speaker?.Voice };
            }
            var text = await OnMain(() => LastAnswer(model.Runtime(thread)), cancellationToken).ConfigureAwait(false);
            if (status == TurnStatus.Interrupted) return new VoiceAnswer("Stopped.") { Speaker = speaker?.Name, Voice = speaker?.Voice };
            return new VoiceAnswer(text.Length > 0 ? text : "Done. There is nothing to read out.") { Speaker = speaker?.Name, Voice = speaker?.Voice };
        }
        finally
        {
            model.ThreadTurnFinished -= Done;
        }
    }

    // What the chat answered since the owner's last message: the assistant messages after the last user entry, joined.
    private static string LastAnswer(Runtime.ThreadRuntime runtime)
    {
        var parts = new List<string>();
        foreach (var entry in runtime.Entries.Reverse())
        {
            if (entry.Item.TimelineItemContent is TimelineItem.Content.User) break;
            if (entry.Item.TimelineItemContent is TimelineItem.Content.Assistant message && message.Value.Text.Trim().Length > 0) parts.Insert(0, message.Value.Text.Trim());
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>"Channel A or Channel B?" for the question that asks which one.</summary>
    public static string WhichLine(DecisionItem card)
    {
        var names = card.Options.Select(o => SpokenText.Phrase(o, 40)).Where(o => o.Length > 0).ToList();
        return names.Count switch
        {
            0 => "Which one did you mean?",
            1 => names[0] + "?",
            _ => string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1] + "?",
        };
    }

    private static Task<T> OnMain<T>(Func<T> work, CancellationToken cancellationToken) =>
        Avalonia.Threading.Dispatcher.UIThread.CheckAccess()
            ? Task.FromResult(work())
            : Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(work, Avalonia.Threading.DispatcherPriority.Normal, cancellationToken).GetTask();
}
