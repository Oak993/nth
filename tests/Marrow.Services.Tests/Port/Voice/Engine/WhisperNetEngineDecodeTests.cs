using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-7.1, V-7.2, V-7.10: what the engine asks of the backend for a live pass and for the final pass, and what it hands back to the transcriber.
public sealed class WhisperNetEngineDecodeTests : IDisposable
{
    private readonly EngineRig rig = new();

    public void Dispose() => rig.Dispose();

    private async Task Load() => await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));

    private static DecodeRequest Live(string language = "en", string prompt = "") => new(language, prompt, DecodeMode.Live, TimeSpan.Zero);

    private static DecodeRequest Final(string language = "en", string prompt = "") => new(language, prompt, DecodeMode.Final, TimeSpan.Zero);

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task A_live_pass_is_greedy_with_best_of_one()
    {
        await Load();
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(2), Live(), CancellationToken.None));
        var decode = Assert.Single(rig.Backend.Decodes);
        Assert.Equal(BackendSampling.Greedy, decode.Sampling);
        Assert.Equal(1, decode.BestOf);
        Assert.Equal(32000, rig.Backend.DecodedSamples[0]);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task The_final_pass_is_a_beam_search_of_beam_five()
    {
        await Load();
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(2), Final(), CancellationToken.None));
        var decode = Assert.Single(rig.Backend.Decodes);
        Assert.Equal(BackendSampling.BeamSearch, decode.Sampling);
        Assert.Equal(5, decode.BeamSize);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task Token_timestamps_are_on_and_the_temperature_is_zero_with_the_default_fallback_left_to_the_library()
    {
        await Load();
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(), CancellationToken.None));
        Assert.True(rig.Backend.Decodes[0].TokenTimestamps);
        Assert.Equal(0f, rig.Backend.Decodes[0].Temperature);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.13")]
    public async Task A_forced_language_is_passed_as_its_code_and_auto_asks_for_detection()
    {
        await Load();
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live("hi"), CancellationToken.None));
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live("auto"), CancellationToken.None));
        Assert.Equal("hi", rig.Backend.Decodes[0].Language);
        Assert.Null(rig.Backend.Decodes[1].Language);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.3")]
    public async Task The_prompt_is_passed_as_the_initial_prompt()
    {
        await Load();
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(prompt: "previous words"), CancellationToken.None));
        Assert.Equal("previous words", rig.Backend.Decodes[0].Prompt);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.2")]
    public async Task The_answer_is_the_words_from_the_segments_in_order()
    {
        rig.Backend.Script = _ =>
        [
            new BackendSegment([new BackendToken(" Hello", 0.2, 0.6, false), new BackendToken(",", 0.6, 0.65, false)], "en"),
            new BackendSegment([new BackendToken(" there", 1.0, 1.4, false)], "en"),
        ];
        await Load();
        var hypothesis = await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(2), Live(), CancellationToken.None));
        Assert.Equal([new HypothesisWord("Hello,", 0.2, 0.65), new HypothesisWord("there", 1.0, 1.4)], hypothesis.Words);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-12.10")]
    public async Task The_detected_language_is_reported_only_for_auto()
    {
        rig.Backend.Script = _ => [new BackendSegment([new BackendToken(" hola", 0.0, 0.4, false)], "es")];
        await Load();
        var auto = await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live("auto"), CancellationToken.None));
        var forced = await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live("en"), CancellationToken.None));
        Assert.Equal("es", auto.Language);
        Assert.Null(forced.Language);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.8")]
    public async Task No_audio_is_not_sent_to_the_backend()
    {
        await Load();
        var hypothesis = await EngineRig.Guard(rig.Engine.TranscribeAsync(ReadOnlyMemory<float>.Empty, Live(), CancellationToken.None));
        Assert.Empty(hypothesis.Words);
        Assert.Empty(rig.Backend.Decodes);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task Decodes_never_overlap_the_second_waits_for_the_first()
    {
        await Load();
        var release = rig.Backend.HoldDecodes();
        var first = rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(), CancellationToken.None);
        var second = rig.Engine.TranscribeAsync(EngineRig.Audio(1), Final(), CancellationToken.None);
        Assert.Single(rig.Backend.Decodes);
        release();
        await EngineRig.Guard(Task.WhenAll(first, second));
        Assert.Equal(2, rig.Backend.Decodes.Count);
        Assert.Equal(1, rig.Backend.MaxInFlight);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.10")]
    public async Task A_cancelled_pass_throws_cancellation_and_the_engine_decodes_again_afterwards()
    {
        await Load();
        var release = rig.Backend.HoldDecodes();
        using var cts = new CancellationTokenSource();
        var pass = rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EngineRig.Guard(pass));
        release();
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(), CancellationToken.None));
        Assert.Equal(2, rig.Backend.Decodes.Count);
        Assert.True(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.10")]
    public async Task A_token_cancelled_before_the_call_never_reaches_the_backend()
    {
        await Load();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(), cts.Token));
        Assert.Empty(rig.Backend.Decodes);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-15")]
    public async Task A_backend_failure_in_a_pass_reaches_the_caller_and_keeps_the_model()
    {
        rig.Backend.Script = _ => throw new InvalidOperationException("decode failed");
        await Load();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(), CancellationToken.None));
        Assert.True(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task A_decode_before_any_model_was_loaded_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Engine.TranscribeAsync(EngineRig.Audio(1), Live(), CancellationToken.None));
        Assert.Empty(rig.Backend.Loads);
    }
}
