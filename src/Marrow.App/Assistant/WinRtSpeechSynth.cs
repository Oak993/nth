// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.2 "SpeechOut.cs (Windows.Media.SpeechSynthesis, local voices only; no cloud voice)", 15.6; owner 2026-10-11: free and
// on the PC only; unit AS-18; ED-NEW-dextra-as18-7). No Swift twin.
// The PC's own voices through Windows.Media.SpeechSynthesis (the WinRT projection needs this project's Windows TFM, which is why the adapter is here and
// the rest of SpeechOut is in Services). SpeechSynthesizer.AllVoices lists the voices installed on this PC; the Edge "Online" voices are not in it, and
// SpeechVoices refuses one anyway. The text becomes a WAV stream in memory with a cue for each word; it is decoded to mono floats and handed to the
// speakers by the player. Nothing is sent anywhere and nothing is written to disk. Never used by automated tests (a test binds ScriptedSpeechSynth); the
// Native test (VoiceConversationNativeTests) makes a real sentence.
using System.Buffers.Binary;
using Marrow.Services.Assistant;
using Windows.Media.Core;
using Windows.Media.SpeechSynthesis;

namespace Marrow.App.Assistant;

public sealed class WinRtSpeechSynth : ISpeechSynth
{
    public IReadOnlyList<SynthVoice> Voices()
    {
        try
        {
            return [.. SpeechSynthesizer.AllVoices.Select(v => new SynthVoice(v.Id, v.DisplayName, v.Language, Local: true))];
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }

    public async Task<Synthesis> SynthesizeAsync(string text, SynthVoice? voice, double rate, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using var synthesizer = new SpeechSynthesizer();
        synthesizer.Options.IncludeWordBoundaryMetadata = true;
        synthesizer.Options.SpeakingRate = Math.Clamp(rate <= 0 ? 1.0 : rate, 0.5, 3.0);
        if (voice is not null && SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == voice.Id) is { } installed) synthesizer.Voice = installed;
        using var stream = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[stream.Size];
        using (var input = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream))
        {
            await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        var audio = DecodeWave(bytes);
        return new Synthesis(audio, Cues(stream, text));
    }

    // RIFF/WAVE, 16-bit PCM (what the synthesizer writes), any channels and rate: mono floats at the stream's own rate.
    internal static SynthAudio DecodeWave(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 44 || !wave[..4].SequenceEqual("RIFF"u8) || !wave.Slice(8, 4).SequenceEqual("WAVE"u8)) throw new InvalidDataException("The voice did not make a WAV stream.");
        int channels = 1, rate = 22050, bits = 16;
        var at = 12;
        while (at + 8 <= wave.Length)
        {
            var id = wave.Slice(at, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(at + 4, 4)), (uint)(wave.Length - at - 8));
            if (id.SequenceEqual("fmt "u8))
            {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wave.Slice(at + 10, 2));
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(wave.Slice(at + 12, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wave.Slice(at + 22, 2));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (bits != 16 || channels < 1) throw new InvalidDataException("The voice made sound this PC does not read (" + bits + " bit, " + channels + " channels).");
                var data = wave.Slice(at + 8, size);
                var frames = data.Length / (2 * channels);
                var samples = new float[frames];
                for (var f = 0; f < frames; f++)
                {
                    var sum = 0f;
                    for (var c = 0; c < channels; c++) sum += BinaryPrimitives.ReadInt16LittleEndian(data.Slice((f * channels + c) * 2, 2)) / 32768f;
                    samples[f] = sum / channels;
                }
                return new SynthAudio(samples, rate);
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("The voice made no sound.");
    }

    // The word boundaries the synthesizer put in the stream: where in the text each word starts and when. Without them the sheet spreads the words over the sound.
    private static IReadOnlyList<SynthCue> Cues(SpeechSynthesisStream stream, string text)
    {
        var cues = new List<SynthCue>();
        try
        {
            foreach (var track in stream.TimedMetadataTracks)
            {
                if (!string.Equals(track.Id, "SpeechWord", StringComparison.Ordinal)) continue;
                foreach (var cue in track.Cues)
                {
                    if (cue is not SpeechCue word || word.StartPositionInInput is not { } start || word.EndPositionInInput is not { } end) continue;
                    if (start < 0 || end <= start || end > text.Length) continue;
                    cues.Add(new SynthCue(start, end - start, cue.StartTime));
                }
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            cues.Clear();
        }
        return cues;
    }
}
