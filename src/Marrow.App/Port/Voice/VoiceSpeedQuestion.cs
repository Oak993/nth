// PORT-ONLY, owner-ruled addition (OD-0096, OD-0098 Q-V-1 C and the welcome-voice pick; first-release WV-1, voice-SPEC V-7.7, V-10.7): the Welcome voice question and the slow-PC question as one state machine.
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The question (OD-0098: Welcome asks whether the user wants voice typing; yes starts <see cref="VoicePreparation.PrepareAsync"/>, no downloads nothing) and, after a slow speed test, the Q-V-1 C question
/// (live words with a smaller model, or text after you stop with the accurate model). The Welcome section and the orb's card both draw it. <see cref="State"/> changes only through the calls below and
/// <see cref="Changed"/> follows every change. When Welcome is gone (<see cref="CloseWelcome"/>) a question still open is asked at the next orb click (<see cref="AsksAtOrb"/>): the orb wiring is VT-12's.
/// </summary>
public sealed class VoiceSpeedQuestion(VoicePreparation preparation)
{
    private readonly Lock gate = new();
    private VoiceQuestionState state = VoiceQuestionState.Ask;
    private bool welcomeGone;
    private string? error;
    private int generation;
    private CancellationTokenSource? cancellation;

    /// <summary>Where the question is.</summary>
    public VoiceQuestionState State
    {
        get { lock (gate) return state; }
    }

    /// <summary>True once Welcome has gone (<see cref="CloseWelcome"/>).</summary>
    public bool WelcomeGone
    {
        get { lock (gate) return welcomeGone; }
    }

    /// <summary>True when the slow question is still open and Welcome is gone: the next orb click shows it (ED: in the orb's PopoverCard).</summary>
    public bool AsksAtOrb
    {
        get { lock (gate) return welcomeGone && state == VoiceQuestionState.Slow; }
    }

    /// <summary>Why the last run failed (a diagnostic, not UI words); null otherwise.</summary>
    public string? Error
    {
        get { lock (gate) return error; }
    }

    /// <summary>The size of the model the recommendation downloads as V2 writes it ("874 MB"; wv_lib.py <c>{mb}</c>), once the PC is probed; null before.</summary>
    public string? Mb => Megabytes(preparation.CandidateBytes);

    /// <summary>The size of the smaller live model the slow question offers ("264 MB"; <c>{small}</c>); null when there is none.</summary>
    public string? SmallMb => Megabytes(preparation.LiveAnswerBytes);

    // ENGINEERING DECISION (ED-352): decimal megabytes rounded to the whole number, the unit the design's sizes use (874 MB is large-v3-turbo-q8_0's 874188075 bytes).
    private static string? Megabytes(long? bytes) => bytes is { } b ? ((long)Math.Round(b / 1_000_000.0)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " MB" : null;

    /// <summary>Raised after every change of state, on the thread that made it (a run's end is on a pool thread: a view posts to its own).</summary>
    public event Action? Changed;

    /// <summary>
    /// The user's yes (only from Ask): consent, then download, verify and speed test. Ready when it ends well, Slow when the processor is too slow for live words and the question is still to be asked,
    /// back to Ask (with <see cref="Error"/>) when it fails (ED-352).
    /// </summary>
    public async Task YesAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken)
    {
        int run;
        CancellationToken token;
        lock (gate)
        {
            if (state != VoiceQuestionState.Ask) return;
            state = VoiceQuestionState.Yes;
            error = null;
            run = ++generation;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            token = cancellation.Token;
        }
        Changed?.Invoke();
        await PrepareAsync(progress, token, run).ConfigureAwait(false);
    }

    // OD-0098: an orb click is consent even after Maybe later or after Welcome has gone. A click during Yes joins the same work.
    internal async Task AtOrbAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken)
    {
        int run;
        CancellationToken shared;
        lock (gate)
        {
            if (state == VoiceQuestionState.Slow) return;
            if (state != VoiceQuestionState.Yes)
            {
                ++generation;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            run = generation;
            shared = cancellation?.Token ?? cancellationToken;
            state = VoiceQuestionState.Yes;
            error = null;
        }
        Changed?.Invoke();
        using var joined = CancellationTokenSource.CreateLinkedTokenSource(shared, cancellationToken);
        await PrepareAsync(progress, joined.Token, run).ConfigureAwait(false);
    }

    private async Task PrepareAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken, int run)
    {
        preparation.GrantConsent();
        VoicePreparationResult result;
        try
        {
            result = await preparation.PrepareAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Settle(VoiceQuestionState.Ask, null, run);
            throw;
        }
        if (result.Status != VoicePreparationStatus.Prepared) Settle(VoiceQuestionState.Ask, result.Error ?? "The voice model was not prepared.", run);
        else Settle(result.AskSlowQuestion ? VoiceQuestionState.Slow : VoiceQuestionState.Ready, null, run);
    }

    public void Cancel()
    {
        CancellationTokenSource? stopped;
        lock (gate)
        {
            ++generation;
            state = welcomeGone ? VoiceQuestionState.Gone : VoiceQuestionState.Ask;
            error = null;
            stopped = cancellation;
            cancellation = null;
        }
        stopped?.Cancel();
        stopped?.Dispose();
        preparation.Cancel();
        Changed?.Invoke();
    }

    /// <summary>Reads the PC when no probe has run (Getting ready's hardware step does it first): the words name the model's size, which the recommendation gives. Fetches nothing; a failure leaves the sizes unknown.</summary>
    public async Task ProbeAsync()
    {
        try
        {
            await preparation.ProbeAsync(null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }
        Changed?.Invoke();
    }

    /// <summary>The user's no (only from Ask): nothing is downloaded and nothing is stored.</summary>
    public void No() => Move(VoiceQuestionState.Ask, VoiceQuestionState.No);

    /// <summary>The answer to the slow question (only from Slow): stored in the setting (R5 to R8) and applied. A model that cannot be installed leaves the question open with <see cref="Error"/>.</summary>
    public async Task AnswerAsync(LiveWordsChoice choice, CancellationToken cancellationToken)
    {
        if (State != VoiceQuestionState.Slow) return;
        var record = await preparation.AnswerSlowQuestionAsync(choice, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            lock (gate) error = "The answer could not be applied.";
            return;
        }
        Settle(choice == LiveWordsChoice.Live ? VoiceQuestionState.SlowLive : VoiceQuestionState.SlowAcc, null);
    }

    /// <summary>
    /// Welcome has gone. Ask, No, Ready and the answers become Gone; a download still running (Yes) and an open slow question (Slow) stay as they are for the next orb click.
    /// </summary>
    public void CloseWelcome()
    {
        lock (gate)
        {
            if (welcomeGone) return;
            welcomeGone = true;
            if (state is VoiceQuestionState.Yes or VoiceQuestionState.Slow) return;
            state = VoiceQuestionState.Gone;
        }
        Changed?.Invoke();
    }

    private bool Move(VoiceQuestionState from, VoiceQuestionState to)
    {
        lock (gate)
        {
            if (state != from) return false;
            state = to;
            error = null;
        }
        Changed?.Invoke();
        return true;
    }

    private void Settle(VoiceQuestionState to, string? reason, int? run = null)
    {
        lock (gate)
        {
            if (run is { } expected && expected != generation) return;
            error = reason;
            state = welcomeGone && to is VoiceQuestionState.Ready or VoiceQuestionState.Ask ? VoiceQuestionState.Gone : to; // an answer given at the orb stays to be shown there
            cancellation?.Dispose();
            cancellation = null;
        }
        Changed?.Invoke();
    }
}
