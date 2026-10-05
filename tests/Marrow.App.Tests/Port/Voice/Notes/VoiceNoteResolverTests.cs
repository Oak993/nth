using Marrow.App.Port.Voice.Notes;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Cloud;

namespace Marrow.App.Tests.Port.Voice.Notes;

// VT-17 (voice-SPEC V-17.6): the facts a seam leaves, in the order they are read. Each row is one fact set and the note it must give.
public sealed class VoiceNoteResolverTests
{
    [Theory]
    [InlineData(VoiceNoteKind.Cancelled)]
    [InlineData(VoiceNoteKind.NoSpeech)]
    [Trait("Rule", "R-TEST-4")] // Req V-14.6
    public void The_designed_notes_of_V_14_6_are_not_resolved_here(VoiceNoteKind kind)
    {
        Assert.Null(VoiceNoteResolver.Resolve(new VoiceNoteFacts(kind)));
    }

    [Theory]
    [InlineData(VoiceNoteKind.Blocked, VoiceNoteCase.Blocked)]
    [InlineData(VoiceNoteKind.NoMicrophone, VoiceNoteCase.NoMicrophone)]
    [InlineData(VoiceNoteKind.EngineFailed, VoiceNoteCase.EngineFailure)]
    [InlineData(VoiceNoteKind.ModelLoadFailed, VoiceNoteCase.ModelFailedToLoad)]
    [InlineData(VoiceNoteKind.CaptureFailed, VoiceNoteCase.EngineFailure)]
    [Trait("Rule", "R-TEST-4")] // Req V-17.6
    public void A_kind_with_no_other_fact_gives_its_plain_note(VoiceNoteKind kind, VoiceNoteCase expected)
    {
        Assert.Equal(expected, VoiceNoteResolver.Resolve(new VoiceNoteFacts(kind)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req V-18.4
    public void A_cloud_cause_wins_over_a_download_under_way_or_a_processor_without_AVX2()
    {
        var facts = new VoiceNoteFacts(VoiceNoteKind.ModelLoadFailed, new OpenAiTranscriptionException(CloudNote.KeyRefused, 401), ModelDownloading: true, ProcessorLacksAvx2: true);

        Assert.Equal(VoiceNoteCase.CloudKeyRefused, VoiceNoteResolver.Resolve(facts));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req V-17.6
    public void A_download_under_way_is_named_before_the_processor()
    {
        var facts = new VoiceNoteFacts(VoiceNoteKind.ModelLoadFailed, ModelDownloading: true, ProcessorLacksAvx2: true);

        Assert.Equal(VoiceNoteCase.ModelStillDownloading, VoiceNoteResolver.Resolve(facts));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req V-17.6
    public void A_missing_AVX2_matters_only_for_a_load_that_failed()
    {
        Assert.Equal(VoiceNoteCase.NoAvx2, VoiceNoteResolver.Resolve(new VoiceNoteFacts(VoiceNoteKind.ModelLoadFailed, ProcessorLacksAvx2: true)));
        Assert.Equal(VoiceNoteCase.EngineFailure, VoiceNoteResolver.Resolve(new VoiceNoteFacts(VoiceNoteKind.EngineFailed, ProcessorLacksAvx2: true)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Req V-4.4
    public void The_in_use_device_event_names_the_busy_microphone_even_when_no_exception_came()
    {
        Assert.Equal(VoiceNoteCase.MicrophoneBusy, VoiceNoteResolver.Resolve(new VoiceNoteFacts(VoiceNoteKind.CaptureFailed, DeviceEvent: CaptureDeviceEventKind.InUse)));
    }
}
