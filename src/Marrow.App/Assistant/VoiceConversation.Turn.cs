// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "What voice may answer", "Spoken status on request", 12.6 "Voice: Stop everything", 12.6b "Pause everything";
// unit AS-18; ED-NEW-dextra-as18-4, -5, -6, -8). No Swift twin.
// The thinking half of the conversation: one request, from its audio to the words said back. A local engine turns the audio into words; the words are
// a command the voice layer answers itself, an answer to the card Dextra has just read out, or a request for Dextra (or an Astur by name); the answer is
// made fit to be said, said, and the conversation goes back to listening. Every await re-checks the turn number: a barge-in, a stop or a close makes
// whatever arrives afterwards harmless.
using Marrow.App.Safety;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class VoiceConversation
{
    // The engine and model the last load was for: a second request does not load again.
    private static readonly Lock LoadGate = new();
    private static ISpeechEngine? loadedEngine;
    private static SpeechModel? loadedModel;

    /// <summary>What the sheet says after Stop everything: what was stopped, and that nothing was undone (12.6).</summary>
    public const string StoppedLine = "Stopped everything. Nothing was undone.";

    private async Task RunAsync(int id, float[] audio, CancellationToken token)
    {
        try
        {
            var text = await TranscribeAsync(audio, DecodeMode.Final, token).ConfigureAwait(false);
            if (Stale(id)) return;
            if (text.Length == 0)
            {
                Back(id, "I did not catch that.");
                return;
            }
            lock (gate)
            {
                heard = text;
                reply = "";
                speaker = null;
            }
            Notify();
            await HandleAsync(id, text, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // a barge-in, a stop or a close: the new state is already shown
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!Stale(id)) Back(id, "Voice could not understand that: " + Brief(error));
        }
    }

    private async Task<string> TranscribeAsync(float[] audio, DecodeMode decode, CancellationToken token)
    {
        var engine = o.Engine();
        var model = o.Model() ?? throw new InvalidOperationException("No speech model is set up.");
        bool load;
        lock (LoadGate) load = !ReferenceEquals(loadedEngine, engine) || !Equals(loadedModel, model);
        if (load)
        {
            await engine.LoadAsync(model, new EngineLoadOptions(), token).ConfigureAwait(false);
            lock (LoadGate)
            {
                loadedEngine = engine;
                loadedModel = model;
            }
        }
        var hypothesis = await engine.TranscribeAsync(audio, new DecodeRequest(o.Language(), o.Vocabulary(), decode, TimeSpan.Zero), token).ConfigureAwait(false);
        return string.Join(' ', hypothesis.Words.Select(w => w.Text.Trim()).Where(w => w.Length > 0)).Trim();
    }

    private async Task HandleAsync(int id, string text, CancellationToken token)
    {
        using var spoken = o.Speech.BeginTurn(text);
        var command = VoiceCommands.Parse(text);
        var card = await OnUi(() => StillWaiting(Focus)).ConfigureAwait(false);

        switch (command.Kind)
        {
            case VoiceCommandKind.Hush:
                o.Speech.Stop();
                Back(id, null);
                return;
            case VoiceCommandKind.StopEverything:
                await StopEverythingAsync(id);
                return;
            case VoiceCommandKind.PauseEverything:
                ClearFocus();
                await SayAsync(id, SpokenText.First(await OnUi(() => o.Controls.PauseAll("Voice: Pause everything")).ConfigureAwait(false), 2, out _), null, null);
                return;
            case VoiceCommandKind.ResumeEverything:
                ClearFocus();
                await SayAsync(id, SpokenText.First(await OnUi(() => o.Controls.ResumeAll()).ConfigureAwait(false), 2, out _), null, null);
                return;
            case VoiceCommandKind.Status:
                var answer = await OnUi(() => VoiceStatus.Compose(command.Ask, o.Jobs.Snapshot(o.Time.GetUtcNow()), o.Decisions.Waiting, o.Digest)).ConfigureAwait(false);
                lock (gate) focus = answer.Focus;
                await SayAsync(id, answer.Text, null, null);
                return;
        }

        if (card is not null && VoiceCardRules.Targets(card, command, text))
        {
            await AnswerCardAsync(id, card, command, text);
            return;
        }
        ClearFocus();
        var answered = await o.Brain.AskAsync(text, token).ConfigureAwait(false);
        if (Stale(id)) return;
        lock (gate)
        {
            focus = answered.Card;
            speaker = answered.Speaker;
        }
        await SayAsync(id, SpokenText.ForSpeech(answered.Text), answered.Voice, answered.Speaker);
    }

    // Stop everything acts at once and never asks (12.6): the stop is sent first, then Dextra says what happened.
    private async Task StopEverythingAsync(int id)
    {
        ClearFocus();
        selfStop = true;
        try { o.Controls.StopAll(StopSource.Voice); }
        finally { selfStop = false; }
        await SayAsync(id, StoppedLine, null, null);
    }

    // Voice answers the card, or opens it on the screen and says which button to tap; it never answers a card of an irreversible class (VoiceCardRules).
    private async Task AnswerCardAsync(int id, DecisionItem card, VoiceCommand command, string text)
    {
        var move = VoiceCardRules.Decide(card, command, text);
        switch (move.Kind)
        {
            case VoiceCardMoveKind.OnScreen:
                await OnUi(() =>
                {
                    o.ShowCard?.Invoke(card.Id);
                    return true;
                }).ConfigureAwait(false);
                await SayAsync(id, move.Line, null, null);
                return;
            case VoiceCardMoveKind.Unclear:
                await SayAsync(id, move.Line, null, null);
                return;
        }
        var refused = move.Answer is { } given ? await OnUi(() => o.Decisions.Answer(card.Id, given)).ConfigureAwait(false) : "No answer.";
        ClearFocus();
        await SayAsync(id, refused is null ? move.Line : SpokenText.First(refused, 2, out _), null, null);
    }

    private async Task SayAsync(int id, string text, string? voice, string? who)
    {
        if (Stale(id)) return;
        if (text.Length == 0)
        {
            Back(id, null);
            return;
        }
        lock (gate)
        {
            if (disposed || id != turn) return;
            reply = text;
            speaker = who;
            phase = VoicePhase.Speaking;
            note = null;
        }
        Notify();
        using var lease = TakeLease();
        var end = await o.Speech.SpeakAsync(text, voice).ConfigureAwait(false);
        if (Stale(id)) return;
        switch (end)
        {
            case SpeechEnd.Failed:
                AfterReply(id, "Dextra could not speak that. The words are on screen.");
                break;
            case SpeechEnd.Finished:
                AfterReply(id, null);
                break;
        }
    }

    private IDisposable? TakeLease()
    {
        if (o.StopLease is not { } take) return null;
        var lease = take();
        if (lease is null) lock (gate) note = "The stop key is not available. Say stop everything, or close this sheet.";
        return lease;
    }

    // The reply is over. A toggled conversation listens a little longer for a follow-up; a held one is done and shuts the microphone.
    private void AfterReply(int id, string? line)
    {
        if (Stale(id)) return;
        o.Post(() =>
        {
            if (Stale(id)) return;
            if (Mode == VoiceStart.Toggle)
            {
                Listen(followUp: true, keepWords: true);
                if (line is not null) lock (gate) note = line;
                Notify();
            }
            else Rest(line);
        });
    }

    // Back to listening (toggle) or rest (hold) after a request that came to nothing: the reason is on the sheet.
    private void Back(int id, string? line)
    {
        if (Stale(id)) return;
        o.Post(() =>
        {
            if (Stale(id)) return;
            if (Mode == VoiceStart.Toggle)
            {
                Listen(followUp: true, keepWords: true);
                if (line is not null) lock (gate) note = line;
                Notify();
            }
            else Rest(line);
        });
    }

    // Work that belongs to the UI thread (the board, the queue, the controls) run there; the caller awaits its result.
    private Task<T> OnUi<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        o.Post(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception error) { done.SetException(error); }
        });
        return done.Task;
    }

    private DecisionItem? StillWaiting(DecisionItem? card) =>
        card is null ? null : o.Decisions.Waiting.FirstOrDefault(c => c.Id == card.Id && c.State == DecisionState.Open);

    private void ClearFocus()
    {
        lock (gate) focus = null;
    }

    private static string Brief(Exception error)
    {
        var message = error.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 120 ? message : message[..120];
    }
}
