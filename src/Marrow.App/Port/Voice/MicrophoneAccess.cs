// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the privacy gate (voice-SPEC V-4.5).
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;

namespace Marrow.App.Port.Voice;

/// <summary>
/// <see cref="IMicrophoneAccess"/> before any device is opened (V-4.5). No capture endpoint at all is NoDevice (the note names the real problem, whatever the switch says).
/// Windows' answer Denied (by the user or by the system) is Denied; Allowed and Unspecified are Allowed: a desktop app has no declared capability, so Windows may say
/// Unspecified, and a block that says nothing is caught by the all-zero first second (V-4.5). A reader that throws means Unspecified. An endpoint list that throws (the audio
/// service is not running) means no device; an unbound endpoint seam is not swallowed (V-2.5).
/// </summary>
public sealed class MicrophoneAccess : IMicrophoneAccess
{
    private readonly IMicrophonePrivacySource privacy;
    private readonly ICaptureHost host;

    public MicrophoneAccess(IMicrophonePrivacySource privacy, ICaptureHost host)
    {
        this.privacy = privacy;
        this.host = host;
    }

    public MicrophoneAccessStatus Status()
    {
        try
        {
            if (host.CaptureEndpoints().Count == 0) return MicrophoneAccessStatus.NoDevice;
        }
        catch (SeamNotBoundException)
        {
            throw;
        }
        catch (Exception)
        {
            return MicrophoneAccessStatus.NoDevice;
        }
        MicrophonePrivacy answer;
        try
        {
            answer = privacy.Current();
        }
        catch (Exception)
        {
            answer = MicrophonePrivacy.Unspecified;
        }
        return answer is MicrophonePrivacy.DeniedByUser or MicrophonePrivacy.DeniedBySystem ? MicrophoneAccessStatus.Denied : MicrophoneAccessStatus.Allowed;
    }
}
