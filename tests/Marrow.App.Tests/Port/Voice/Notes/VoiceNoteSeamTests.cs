using Marrow.App.Port.Voice.Notes;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Port.Voice.Cloud;

namespace Marrow.App.Tests.Port.Voice.Notes;

// VT-17 (OD-0106 Notes A; voice-SPEC V-4.4, V-4.5, V-17.6, V-18.4): each note is driven by a scripted failure at its seam, through a real VoiceSession, and the bridge
// says which note, on which surface, for how long. No microphone, engine, network or CLI is touched.
public sealed class VoiceNoteSeamTests
{
    private static void Click(VoiceNoteRig rig)
    {
        rig.Session.Activate();
        rig.Settle();
    }

    [Fact]
    [Trait("Req", "V-4.5")]
    [Trait("Req", "V-14.6")]
    public void A_denied_microphone_shows_the_locked_note()
    {
        using var rig = new VoiceNoteRig();
        rig.Microphone.Current = MicrophoneAccessStatus.Denied;

        Click(rig);

        Assert.Equal(VoiceNoteCase.Blocked, rig.Case);
        Assert.Same(VoiceNotes.Blocked, rig.Bridge.Current!.Note);
        Assert.Equal(0, rig.Capture.StartCount); // a denied microphone opens no device
    }

    [Fact]
    [Trait("Req", "Q-V-7 A")]
    public void No_capture_endpoint_shows_the_no_microphone_note()
    {
        using var rig = new VoiceNoteRig();
        rig.Microphone.Current = MicrophoneAccessStatus.NoDevice;

        Click(rig);

        Assert.Equal(VoiceNoteCase.NoMicrophone, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-4.4")]
    public void A_capture_that_cannot_open_because_another_app_holds_it_shows_the_busy_note()
    {
        using var rig = new VoiceNoteRig();
        rig.Capture.FailStart = new CaptureDeviceException(CaptureDeviceFailure.InUse, "AUDCLNT_E_DEVICE_IN_USE");

        Click(rig);

        Assert.Equal(VoiceNoteCase.MicrophoneBusy, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-4.4")]
    public void Another_app_taking_the_microphone_mid_session_shows_the_busy_note()
    {
        using var rig = new VoiceNoteRig();
        rig.QuietRoom();
        Click(rig);
        Assert.Null(rig.Case);

        rig.Capture.RaiseDeviceEvent(CaptureDeviceEventKind.InUse);
        rig.Settle();

        Assert.Equal(VoiceNoteCase.MicrophoneBusy, rig.Case);
    }

    [Theory]
    [InlineData(CaptureDeviceFailure.NotFound)]
    [InlineData(CaptureDeviceFailure.Removed)]
    [Trait("Req", "V-4.4")]
    public void A_capture_endpoint_that_is_gone_when_it_opens_shows_the_unplugged_note(CaptureDeviceFailure failure)
    {
        using var rig = new VoiceNoteRig();
        rig.Capture.FailStart = new CaptureDeviceException(failure, "endpoint gone");

        Click(rig);

        Assert.Equal(VoiceNoteCase.MicrophoneUnplugged, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-4.4")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void A_capture_that_fails_for_another_reason_shows_the_engine_note()
    {
        using var rig = new VoiceNoteRig();
        rig.Capture.FailStart = new CaptureDeviceException(CaptureDeviceFailure.Other, "something else");

        Click(rig);

        Assert.Equal(VoiceNoteCase.EngineFailure, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-4.4")]
    public void A_device_removed_mid_session_finishes_the_text_first_and_then_shows_the_unplugged_note()
    {
        using var rig = new VoiceNoteRig();
        rig.Speak(TimeSpan.FromSeconds(0.5));
        Click(rig);
        rig.Advance(3);

        rig.Capture.RaiseDeviceEvent(CaptureDeviceEventKind.DeviceRemoved);
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State); // the final pass is running
        Assert.Null(rig.Case); // the note comes after the pass (V-4.4: "runs the final pass on what it has, then shows the device note")
        rig.Advance(3);

        Assert.NotEqual(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(VoiceNoteCase.MicrophoneUnplugged, rig.Case);
        Assert.Equal(VoiceStopReason.DeviceEvent, rig.Session.LastStopReason);
    }

    [Theory]
    [InlineData(CaptureDeviceEventKind.DefaultDeviceChanged)]
    [InlineData(CaptureDeviceEventKind.FormatChanged)]
    [Trait("Req", "V-4.4")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void A_changed_default_device_or_format_stops_the_session_with_no_unplugged_note(CaptureDeviceEventKind kind)
    {
        using var rig = new VoiceNoteRig();
        rig.Speak();
        Click(rig);
        rig.Advance(3);

        rig.Capture.RaiseDeviceEvent(kind);
        rig.Advance(3);

        Assert.Equal(VoiceStopReason.DeviceEvent, rig.Session.LastStopReason);
        Assert.Null(rig.Case);
        Assert.Empty(rig.Shown);
    }

    [Fact]
    [Trait("Req", "V-17.6")]
    public void No_model_while_it_is_downloading_shows_the_still_downloading_note()
    {
        using var rig = new VoiceNoteRig { Model = null, ModelDownloading = true };

        Click(rig);

        Assert.Equal(VoiceNoteCase.ModelStillDownloading, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-17.6")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void The_still_downloading_note_stays_away_while_the_pill_itself_shows_the_download()
    {
        using var rig = new VoiceNoteRig { Model = null, ModelDownloading = true, PillShowsDownload = true };

        Click(rig);

        Assert.Null(rig.Case);
        Assert.Empty(rig.Shown);
    }

    [Fact]
    [Trait("Req", "V-17.6")]
    public void No_model_and_no_download_shows_the_failed_to_load_note()
    {
        using var rig = new VoiceNoteRig { Model = null };

        Click(rig);

        Assert.Equal(VoiceNoteCase.ModelFailedToLoad, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-17.6")]
    public void An_engine_that_fails_to_load_shows_the_failed_to_load_note()
    {
        using var rig = new VoiceNoteRig();
        rig.QuietRoom();
        rig.Engine.FailLoads(new InvalidOperationException("corrupt model"));

        Click(rig);

        Assert.Equal(VoiceNoteCase.ModelFailedToLoad, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-17.6")]
    public void A_load_that_fails_on_a_processor_without_AVX2_shows_the_no_AVX2_note()
    {
        using var rig = new VoiceNoteRig();
        rig.QuietRoom();
        rig.Processor.Avx2Supported = false;
        rig.Engine.FailLoads(new DllNotFoundException("whisper.dll"));

        Click(rig);

        Assert.Equal(VoiceNoteCase.NoAvx2, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-17.6")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void A_processor_that_cannot_be_asked_is_not_blamed()
    {
        using var rig = new VoiceNoteRig(new FailClosedProcessorFeatures());
        rig.QuietRoom();
        rig.Engine.FailLoads(new DllNotFoundException("whisper.dll"));

        Click(rig);

        Assert.Equal(VoiceNoteCase.ModelFailedToLoad, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-17.6")]
    public void A_decode_that_throws_shows_the_engine_note()
    {
        using var rig = new VoiceNoteRig();
        rig.Speak();
        Click(rig);
        rig.Advance(4); // the speech is over and the live passes are done
        rig.Engine.FailNextDecode(new InvalidOperationException("decode failed"));
        rig.Session.Activate(); // click the orb to finish: the final pass decodes
        rig.Advance(3);

        Assert.Equal(VoiceNoteCase.EngineFailure, rig.Case);
    }

    [Theory]
    [InlineData(CloudNote.KeyMissing, VoiceNoteCase.CloudNoKey)]
    [InlineData(CloudNote.KeyRefused, VoiceNoteCase.CloudKeyRefused)]
    [InlineData(CloudNote.QuotaOrRate, VoiceNoteCase.CloudOutOfQuota)]
    [InlineData(CloudNote.NetworkFailure, VoiceNoteCase.CloudNoNetwork)]
    [InlineData(CloudNote.EngineFailure, VoiceNoteCase.EngineFailure)]
    [Trait("Req", "V-18.4")]
    public void Each_cloud_failure_at_load_maps_to_its_note(CloudNote cloud, VoiceNoteCase expected)
    {
        using var rig = new VoiceNoteRig();
        rig.QuietRoom();
        rig.Engine.FailLoads(new OpenAiTranscriptionException(cloud));

        Click(rig);

        Assert.Equal(expected, rig.Case);
    }

    [Theory]
    [InlineData(CloudNote.KeyMissing, VoiceNoteCase.CloudNoKey)]
    [InlineData(CloudNote.KeyRefused, VoiceNoteCase.CloudKeyRefused)]
    [InlineData(CloudNote.QuotaOrRate, VoiceNoteCase.CloudOutOfQuota)]
    [InlineData(CloudNote.NetworkFailure, VoiceNoteCase.CloudNoNetwork)]
    [InlineData(CloudNote.EngineFailure, VoiceNoteCase.EngineFailure)]
    [Trait("Req", "V-18.4")]
    public void Each_cloud_failure_at_the_final_pass_maps_to_its_note(CloudNote cloud, VoiceNoteCase expected)
    {
        using var rig = new VoiceNoteRig();
        rig.Speak();
        Click(rig);
        rig.Advance(4);
        rig.Engine.FailNextDecode(new OpenAiTranscriptionException(cloud, cloud == CloudNote.KeyRefused ? 401 : null));
        rig.Session.Activate();
        rig.Advance(3);

        Assert.Equal(expected, rig.Case);
    }

    [Fact]
    [Trait("Req", "V-14.6")]
    public void Cancelled_and_no_speech_are_not_this_units_notes()
    {
        using var rig = new VoiceNoteRig();
        rig.QuietRoom();
        Click(rig);

        rig.Session.Cancel();
        rig.Advance(3);

        Assert.Null(rig.Case);
        Assert.Empty(rig.Shown);
    }

    [Fact]
    [Trait("Oracle", "OD-0106")]
    public void An_empty_draft_shows_the_note_in_the_field_and_a_draft_with_text_in_the_notice_line()
    {
        using var empty = new VoiceNoteRig { Draft = "" };
        empty.Microphone.Current = MicrophoneAccessStatus.NoDevice;
        Click(empty);
        using var typed = new VoiceNoteRig { Draft = "Add a shortcut for the theme switch" };
        typed.Microphone.Current = MicrophoneAccessStatus.NoDevice;
        Click(typed);

        Assert.Equal(VoiceNoteSurface.Field, empty.Bridge.Current!.Surface);
        Assert.Equal(VoiceNoteSurface.NoticeLine, typed.Bridge.Current!.Surface);
    }

    [Fact]
    [Trait("Req", "V-14.6")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void Typing_while_a_note_shows_moves_it_to_the_notice_line_and_clearing_the_draft_moves_it_back()
    {
        using var rig = new VoiceNoteRig { Draft = "" };
        rig.Microphone.Current = MicrophoneAccessStatus.NoDevice;
        Click(rig);
        var count = rig.Shown.Count;

        rig.Draft = "hello";
        rig.Bridge.RefreshSurface();
        Assert.Equal(VoiceNoteSurface.NoticeLine, rig.Bridge.Current!.Surface);
        rig.Draft = "";
        rig.Bridge.RefreshSurface();
        Assert.Equal(VoiceNoteSurface.Field, rig.Bridge.Current!.Surface);
        rig.Bridge.RefreshSurface(); // nothing moved: nothing is announced again
        Assert.Equal(count + 2, rig.Shown.Count);
        Assert.Equal(VoiceNoteCase.NoMicrophone, rig.Bridge.Current.Note.Case);
    }

    [Fact]
    [Trait("Req", "V-14.6")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void A_note_stays_5_2_seconds_and_is_then_gone()
    {
        using var rig = new VoiceNoteRig();
        rig.Microphone.Current = MicrophoneAccessStatus.NoDevice;
        Click(rig);
        Assert.NotNull(rig.Bridge.Current);

        rig.Advance(5.19);
        Assert.NotNull(rig.Bridge.Current);
        rig.Advance(0.02);

        Assert.Null(rig.Bridge.Current);
        Assert.Null(rig.Shown[^1]);
    }

    [Fact]
    [Trait("Req", "V-14.6")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void A_newer_note_replaces_the_older_one_and_restarts_the_5_2_seconds()
    {
        using var rig = new VoiceNoteRig();
        rig.Microphone.Current = MicrophoneAccessStatus.NoDevice;
        Click(rig);
        rig.Advance(4);
        rig.Microphone.Current = MicrophoneAccessStatus.Denied;
        Click(rig);
        Assert.Equal(VoiceNoteCase.Blocked, rig.Case);

        rig.Advance(4); // 8 s after the first note, 4 s after the second
        Assert.Equal(VoiceNoteCase.Blocked, rig.Case);
        rig.Advance(1.3);

        Assert.Null(rig.Bridge.Current);
    }

    [Fact]
    [Trait("Req", "V-14.5")]
    [Trait("Decision", "ED-NEW-cloud-vt-17")]
    public void A_presentation_carries_the_notes_length_and_announcement()
    {
        using var rig = new VoiceNoteRig();
        rig.Microphone.Current = MicrophoneAccessStatus.Denied;

        Click(rig);

        var shown = rig.Bridge.Current!;
        Assert.Equal(VoiceNotes.DisplayLength, shown.Length);
        Assert.Equal(VoiceNotes.Announcement(VoiceNotes.Blocked), shown.Announcement);
    }
}
