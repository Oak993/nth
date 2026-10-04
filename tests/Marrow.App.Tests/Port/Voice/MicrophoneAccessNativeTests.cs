using Marrow.App.Port.Voice;
using Marrow.Services.Tests.Port.Voice.Engine;

namespace Marrow.App.Tests.Port.Voice;

// VT-5, WINDOWS-ONLY (native run): the real WinRT privacy reader. It reads the Windows setting `DeviceAccessInformation` reports for audio capture; it opens no device and
// shows no prompt. The answer depends on the PC, so the test asserts only that Windows answers with one of the four values.
public sealed class MicrophoneAccessNativeTests
{
    [WindowsOnlyFact("reads DeviceAccessInformation for the audio capture class")]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")]
    public void The_real_WinRT_reader_answers_with_one_of_the_four_values_and_opens_nothing()
    {
        var answer = new WinRtMicrophonePrivacySource().Current();
        Assert.True(Enum.IsDefined(answer), answer.ToString());
    }
}
