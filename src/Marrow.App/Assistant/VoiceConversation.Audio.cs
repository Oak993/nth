// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4; unit AS-18; ED-NEW-dextra-as18-2, -3, -8). No Swift twin.
// The audio half of the conversation: it opens the microphone (communications mode), and on the capture's processing thread cuts the sound into 32 ms
// frames at 16 kHz, asks the voice detector about each, and decides: while listening, where a request starts and ends; while Dextra speaks, whether
// the owner is talking over it (the barge-in gate) and then the speech is cut on the spot; while Dextra thinks, whether he has started again.
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.App.Assistant;

public sealed partial class VoiceConversation
{
    private const int Frame = VoiceBargeIn.FrameSamples;
    private const int Rate = VoiceEchoReference.Rate;

    // The audio thread's own state, under audioGate.
    private readonly object audioGate = new();
    private readonly List<float> pending = [];
    private readonly List<float> resampled = [];
    private readonly Queue<float[]> preroll = new();
    private readonly List<float> utterance = [];
    private float[] mono = new float[4096];
    private IVoiceActivityDetector? vad;
    private IStreamingResampler? resampler;
    private VoiceEndpointer endpointer = new();
    private VoiceBargeIn? barge;
    private bool capturing;
    private bool liveBusy;
    private volatile bool holding;
    private long holdOffUntil;
    private int liveFrames;
    private int thinkingNear;
    private CaptureFormat format;

    /// <summary>
    /// Opens the microphone (if it is not open) and listens for a request: <paramref name="followUp"/> waits the short follow-up window after a reply
    /// instead of the first wait. <paramref name="keepWords"/> keeps what is on the sheet (an interrupt keeps the reply it cut).
    /// </summary>
    private void Listen(bool followUp, bool keepWords = false)
    {
        if (!OpenCapture()) return;
        lock (audioGate)
        {
            endpointer = new VoiceEndpointer(o.Endpointer with
            {
                NoSpeechAfter = holding ? null : followUp ? o.FollowUp : o.Endpointer.NoSpeechAfter,
            });
            utterance.Clear();
            liveFrames = 0;
            thinkingNear = 0;
            barge?.Reset();
            o.Speech.Reference.Clear();
            holdOffUntil = followUp ? o.Time.GetTimestamp() + (long)(o.HoldOff.TotalSeconds * o.Time.TimestampFrequency) : 0;
        }
        lock (gate)
        {
            phase = VoicePhase.Listening;
            if (!keepWords)
            {
                heard = "";
            }
            note = null;
        }
        Notify();
    }

    // False (with the reason on the sheet) when the microphone cannot be used: nothing is listened to without it.
    private bool OpenCapture()
    {
        lock (audioGate)
        {
            if (capturing) return true;
        }
        if (o.Model() is null)
        {
            Rest("Voice needs a speech model. Set one up under Settings, Voice typing.");
            return false;
        }
        switch (o.Microphone.Status())
        {
            case MicrophoneAccessStatus.Denied:
                Rest("Microphone access is off for nth. Turn it on in Windows privacy settings.");
                return false;
            case MicrophoneAccessStatus.NoDevice:
                Rest("No microphone was found.");
                return false;
        }
        o.PreemptDictation?.Invoke();
        try
        {
            lock (audioGate)
            {
                vad = o.Vad();
                vad.Reset();
                resampler = o.Resampler();
                resampler.Reset();
                barge = new VoiceBargeIn(o.Speech.Reference, o.BargeIn);
                pending.Clear();
                preroll.Clear();
                utterance.Clear();
                liveBusy = false;
                capturing = true;
            }
            format = o.Capture.Start(this);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (audioGate) capturing = false;
            Rest(error is SeamNotBoundException ? "Voice is not set up yet." : "The microphone could not be opened.");
            return false;
        }
    }

    private void ShutCapture()
    {
        holding = false;
        bool was;
        lock (audioGate)
        {
            was = capturing;
            capturing = false;
        }
        if (!was) return;
        try { o.Capture.Stop(); }
        catch (Exception error) when (error is not OutOfMemoryException) { Fault("The microphone did not close cleanly", error); }
    }

    // ---- the capture's sink ----

    void IAudioCaptureSink.OnAudio(ReadOnlySpan<float> interleavedSamples)
    {
        // everything is taken in OnAudioBlock, on the processing thread; the main-thread delivery has nothing more to add
    }

    void IAudioCaptureSink.OnDeviceEvent(CaptureDeviceEventKind kind)
    {
        if (kind == CaptureDeviceEventKind.FormatChanged) return;
        o.Post(() =>
        {
            if (Phase is VoicePhase.Closed or VoicePhase.Idle) return;
            var saying = Phase == VoicePhase.Speaking;
            ShutCapture();
            if (!saying) SetPhase(VoicePhase.Idle, "The microphone stopped.");
            else Notify();
        });
    }

    void IAudioCaptureSink.OnAudioBlock(CaptureFormat captureFormat, ReadOnlySpan<float> interleavedSamples)
    {
        lock (audioGate)
        {
            if (!capturing || resampler is null || vad is null || captureFormat.SampleRate <= 0) return;
            var channels = Math.Max(1, captureFormat.Channels);
            var frames = interleavedSamples.Length / channels;
            if (frames == 0) return;
            if (mono.Length < frames) mono = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++) sum += interleavedSamples[i * channels + c];
                mono[i] = sum / channels;
            }
            resampled.Clear();
            resampler.Process(captureFormat.SampleRate, mono.AsSpan(0, frames), resampled);
            if (resampled.Count == 0) return;
            Volatile.Write(ref micLevel, LevelOf(resampled));
            pending.AddRange(resampled);
            while (pending.Count >= Frame)
            {
                var frame = pending.GetRange(0, Frame).ToArray();
                pending.RemoveRange(0, Frame);
                OnFrame(frame);
            }
        }
    }

    // -55 dBFS is nothing and -10 dBFS is full: the level the Armilla follows.
    private static double LevelOf(List<float> samples)
    {
        var sum = 0.0;
        foreach (var s in samples) sum += s * s;
        var rms = Math.Sqrt(sum / samples.Count);
        return rms <= 1e-6 ? 0 : Math.Clamp((20 * Math.Log10(rms) + 55) / 45, 0, 1);
    }

    private void OnFrame(float[] frame)
    {
        var probability = vad!.Probability(frame);
        VoicePhase now;
        lock (gate) now = phase;
        switch (now)
        {
            case VoicePhase.Listening:
                Listening(frame, probability);
                break;
            case VoicePhase.Speaking:
                Remember(frame);
                if (barge!.Process(frame, probability)) BargeIn();
                break;
            case VoicePhase.Thinking:
                Remember(frame);
                if (Loud(frame, probability))
                {
                    if (++thinkingNear >= o.BargeIn.Frames) BargeIn();
                }
                else thinkingNear = Math.Max(0, thinkingNear - 1);
                break;
            default:
                Remember(frame);
                break;
        }
    }

    private bool Loud(float[] frame, float probability)
    {
        if (probability < o.BargeIn.SpeechProbability) return false;
        var sum = 0f;
        foreach (var s in frame) sum += s * s;
        return MathF.Sqrt(sum / frame.Length) >= o.BargeIn.MicFloor;
    }

    private void Remember(float[] frame)
    {
        preroll.Enqueue(frame);
        while (preroll.Count > PrerollFrames) preroll.Dequeue();
    }

    private void Listening(float[] frame, float probability)
    {
        var hold = holding;
        if (holdOffUntil != 0 && o.Time.GetTimestamp() < holdOffUntil) probability = 0; // the room still rings with Dextra's own voice
        var kind = endpointer.Feed(probability);
        if (endpointer.InSpeech && kind == EndpointKind.SpeechStarted)
        {
            utterance.Clear();
            foreach (var kept in preroll) utterance.AddRange(kept);
            utterance.AddRange(frame);
            preroll.Clear();
            liveFrames = 0;
            return;
        }
        if (!endpointer.InSpeech && kind == EndpointKind.None)
        {
            Remember(frame);
            // a hold keeps everything it hears, so a word spoken a moment after the press is not lost
            return;
        }
        if (endpointer.InSpeech || kind is EndpointKind.SpeechEnded or EndpointKind.TooLong)
        {
            utterance.AddRange(frame);
            switch (kind)
            {
                case EndpointKind.SpeechEnded when !hold:
                case EndpointKind.TooLong:
                    EndRequest();
                    return;
                case EndpointKind.SpeechEnded:
                    endpointer = new VoiceEndpointer(o.Endpointer with { NoSpeechAfter = null });
                    endpointer.BeginInSpeech();
                    return;
            }
            if (++liveFrames >= o.LiveEveryFrames && !liveBusy) StartLivePass();
            return;
        }
        if (kind == EndpointKind.NoSpeech)
        {
            string? line;
            lock (gate)
            {
                line = reply.Length > 0 ? null : "I did not hear anything.";
                phase = VoicePhase.Idle;
                note = line;
            }
            o.Post(() =>
            {
                ShutCapture();
                Notify();
            });
        }
    }

    // The request is over: take its audio and have it understood. Under audioGate.
    private void EndRequest()
    {
        holding = false;
        if (utterance.Count < Rate / 4)
        {
            utterance.Clear();
            lock (gate)
            {
                phase = VoicePhase.Idle;
                note = "I did not hear anything.";
            }
            o.Post(() =>
            {
                ShutCapture();
                Notify();
            });
            return;
        }
        var audio = utterance.ToArray();
        utterance.Clear();
        holding = false;
        endpointer = new VoiceEndpointer(o.Endpointer);
        var (id, token) = NewTurn();
        lock (gate)
        {
            phase = VoicePhase.Thinking;
            note = null;
        }
        Notify();
        _ = o.Background(() => RunAsync(id, audio, token));
    }

    /// <summary>Ends the request that is being spoken now (the chord pressed again, the hold let go). False when there is nothing to send.</summary>
    private bool EndRequestNow()
    {
        lock (audioGate)
        {
            if (!capturing || (!endpointer.InSpeech && utterance.Count < Rate / 4)) return false;
            if (utterance.Count == 0) return false;
            EndRequest();
            return true;
        }
    }

    // The owner speaks over Dextra (or while it is still working out an answer): the speech stops on this thread, now, and what he has said so far
    // (the kept frames) is the start of his next request.
    private void BargeIn()
    {
        o.Speech.Stop();
        CancelTurn();
        thinkingNear = 0;
        endpointer = new VoiceEndpointer(o.Endpointer with { NoSpeechAfter = null });
        endpointer.BeginInSpeech();
        utterance.Clear();
        foreach (var kept in preroll) utterance.AddRange(kept);
        preroll.Clear();
        liveFrames = 0;
        barge?.Reset();
        o.Speech.Reference.Clear();
        lock (gate)
        {
            phase = VoicePhase.Listening;
            note = null;
            heard = "";
        }
        Notify();
    }

    private void StartLivePass()
    {
        liveBusy = true;
        liveFrames = 0;
        var from = Math.Max(0, utterance.Count - 10 * Rate);
        var snapshot = utterance.GetRange(from, utterance.Count - from).ToArray();
        int id;
        lock (gate) id = turn;
        _ = o.Background(async () =>
        {
            try
            {
                var text = await TranscribeAsync(snapshot, DecodeMode.Live, CancellationToken.None).ConfigureAwait(false);
                lock (gate)
                {
                    if (id == turn && phase == VoicePhase.Listening && text.Length > 0) heard = text;
                }
                Notify();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Fault("A live transcript pass failed", error); // it changes nothing on the sheet: the final pass will say
            }
            finally
            {
                lock (audioGate) liveBusy = false;
            }
        });
    }
}
