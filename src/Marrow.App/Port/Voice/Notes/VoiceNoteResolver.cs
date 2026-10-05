// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): from the facts at a seam to the note's case.
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Port.Voice.Cloud;

namespace Marrow.App.Port.Voice.Notes;

/// <summary>
/// Which of the twelve notes a failure is (V-17.6). The session says what kind of failure it was; the cause behind it says which: a capture device exception
/// (busy, gone), an OpenAI cloud exception (V-18.4), the model store (a download under way) and the processor (AVX2). Cancelled and NoSpeech are V-14.6's own
/// designed notes and give null here.
/// </summary>
public static class VoiceNoteResolver
{
    public static VoiceNoteCase? Resolve(VoiceNoteFacts facts)
    {
        if (facts.Kind is VoiceNoteKind.Cancelled or VoiceNoteKind.NoSpeech) return null;
        if (facts.Kind == VoiceNoteKind.Blocked) return VoiceNoteCase.Blocked;
        if (facts.Kind == VoiceNoteKind.NoMicrophone) return VoiceNoteCase.NoMicrophone;
        if (facts.Cause is OpenAiTranscriptionException cloud) return Cloud(cloud.Note);
        if (facts.Kind == VoiceNoteKind.CaptureFailed) return Capture(facts);
        if (facts.Kind == VoiceNoteKind.ModelLoadFailed)
        {
            if (facts.ModelDownloading) return VoiceNoteCase.ModelStillDownloading;
            return facts.ProcessorLacksAvx2 ? VoiceNoteCase.NoAvx2 : VoiceNoteCase.ModelFailedToLoad;
        }
        return VoiceNoteCase.EngineFailure;
    }

    private static VoiceNoteCase Cloud(CloudNote note) => note switch
    {
        CloudNote.KeyMissing => VoiceNoteCase.CloudNoKey,
        CloudNote.KeyRefused => VoiceNoteCase.CloudKeyRefused,
        CloudNote.QuotaOrRate => VoiceNoteCase.CloudOutOfQuota,
        CloudNote.NetworkFailure => VoiceNoteCase.CloudNoNetwork,
        _ => VoiceNoteCase.EngineFailure,
    };

    // An exception that says another app holds the endpoint is Busy; one that says the endpoint is not there (any more) is Unplugged; the in-use device event is
    // Busy too. Anything else the capture failed on has no note of its own: the engine note ("Voice typing stopped on an error", ED-NEW-cloud-vt-17).
    private static VoiceNoteCase Capture(VoiceNoteFacts facts)
    {
        if (facts.Cause is CaptureDeviceException device)
        {
            return device.Failure switch
            {
                CaptureDeviceFailure.InUse => VoiceNoteCase.MicrophoneBusy,
                CaptureDeviceFailure.NotFound or CaptureDeviceFailure.Removed => VoiceNoteCase.MicrophoneUnplugged,
                _ => VoiceNoteCase.EngineFailure,
            };
        }
        return facts.DeviceEvent == CaptureDeviceEventKind.InUse ? VoiceNoteCase.MicrophoneBusy : VoiceNoteCase.EngineFailure;
    }
}
