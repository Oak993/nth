using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Capture;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5 (OD-0099 Q-V-19 B; voice-SPEC V-16.2 `mic`): the Microphone row's list from a scripted enumerator and the stored pick's fallback. VT-13 draws the row from these.
public sealed class MicrophoneChoicesTests
{
    private static ScriptedCaptureHost Host() => new(("{0.0.1.00000000}.{aaa}", "Microphone Array (Realtek)"), ("{0.0.1.00000000}.{bbb}", "Headset (USB)"));

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // Q-V-19 B: "Windows default" is the first item
    public void The_list_is_Windows_default_first_then_every_active_endpoint_in_the_systems_order()
    {
        var items = MicrophoneChoices.Items(Host());
        Assert.Equal(
            [
                new MicrophoneItem(null, "Windows default"),
                new MicrophoneItem("{0.0.1.00000000}.{aaa}", "Microphone Array (Realtek)"),
                new MicrophoneItem("{0.0.1.00000000}.{bbb}", "Headset (USB)"),
            ], items);
        Assert.Equal(VoiceTypingSettings.WindowsDefaultMicrophone, items[0].Label);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // a PC with no microphone still has the first item
    public void With_no_endpoint_the_list_is_only_Windows_default()
    {
        Assert.Equal([new MicrophoneItem(null, "Windows default")], MicrophoneChoices.Items(new ScriptedCaptureHost()));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // enumerating lists; it opens nothing
    public void Listing_opens_no_device()
    {
        var host = Host();
        MicrophoneChoices.Items(host);
        MicrophoneChoices.Resolve(host, "{0.0.1.00000000}.{bbb}");
        Assert.Empty(host.OpenRequests);
        Assert.Empty(host.Devices);
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // V-16.2: `mic` absent is "Windows default"
    public void No_pick_and_the_Windows_default_pick_both_mean_the_default()
    {
        var host = Host();
        Assert.Equal(new MicrophoneResolution(null, false), MicrophoneChoices.Resolve(host, null));
        Assert.Equal(new MicrophoneResolution(null, false), MicrophoneChoices.Resolve(host, VoiceTypingSettings.WindowsDefaultMicrophone));
        Assert.Equal(new MicrophoneResolution(null, false), MicrophoneChoices.Resolve(host, ""));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // a pick that is there opens that endpoint
    public void A_pick_that_is_there_resolves_to_its_endpoint()
    {
        Assert.Equal(new MicrophoneResolution("{0.0.1.00000000}.{bbb}", false), MicrophoneChoices.Resolve(Host(), "{0.0.1.00000000}.{bbb}"));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // Q-V-19 B plan row: "the chosen device missing falls back to the default"
    public void A_pick_that_is_missing_falls_back_to_the_default_and_says_so()
    {
        Assert.Equal(new MicrophoneResolution(null, true), MicrophoneChoices.Resolve(Host(), "{0.0.1.00000000}.{gone}"));
        Assert.Equal(new MicrophoneResolution(null, true), MicrophoneChoices.Resolve(new ScriptedCaptureHost(), "{0.0.1.00000000}.{aaa}"));
    }

    [Fact]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // the stored id is Windows' own; case differences in a registry-copied id are not a different device
    public void Endpoint_ids_match_without_regard_to_case()
    {
        Assert.Equal(new MicrophoneResolution("{0.0.1.00000000}.{aaa}", false), MicrophoneChoices.Resolve(Host(), "{0.0.1.00000000}.{AAA}"));
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // review of VT-13: a menu id is unique and "" is Windows default's, so an endpoint listed twice or with an empty id is listed once or not at all
    public void An_endpoint_listed_twice_or_with_an_empty_id_is_not_listed_again()
    {
        var host = new ScriptedCaptureHost(("{a}", "First"), ("{a}", "First again"), ("", "No id"), ("{A}", "Other case"), ("Windows default", "Named like the default"), ("{b}", "Second"));
        var items = MicrophoneChoices.Items(host);
        Assert.Equal([null, "{a}", "{b}"], items.Select(i => i.Value));
        Assert.Equal(["Windows default", "First", "Second"], items.Select(i => i.Label));
    }
}
