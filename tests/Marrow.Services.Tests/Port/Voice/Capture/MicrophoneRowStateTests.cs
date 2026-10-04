using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-13 (OD-0099 Q-V-19 B; voice-SPEC V-16.2 `mic`, V-16.3.1): what the Voice typing page's Microphone row shows, from VT-5's scripted enumerator. No device is opened.
public sealed class MicrophoneRowStateTests
{
    private const string Realtek = "{0.0.1.00000000}.{aaa}";
    private const string Usb = "{0.0.1.00000000}.{bbb}";

    private static ScriptedCaptureHost Host() => new((Realtek, "Microphone Array (Realtek)"), (Usb, "Headset (USB)"));

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // Q-V-19 B: Windows default first and checked when nothing is stored
    public void With_nothing_stored_the_row_lists_the_scripted_endpoints_and_checks_Windows_default()
    {
        var host = Host();
        var state = MicrophoneRowState.Of(host, null);
        Assert.Equal(["Windows default", "Microphone Array (Realtek)", "Headset (USB)"], state.Items.Select(i => i.Label));
        Assert.Equal(new string?[] { null, Realtek, Usb }, state.Items.Select(i => i.Value));
        Assert.Null(state.SelectedValue);
        Assert.False(state.PickIsMissing);
        Assert.Empty(host.OpenRequests);
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // a stored id that is an active endpoint is the checked item, in the system's own spelling of the id
    public void A_stored_endpoint_is_the_checked_item()
    {
        var state = MicrophoneRowState.Of(Host(), Usb.ToUpperInvariant());
        Assert.Equal(Usb, state.SelectedValue);
        Assert.Equal("Headset (USB)", state.SelectedLabel);
        Assert.False(state.PickIsMissing);
    }

    [Theory]
    [InlineData("{0.0.1.00000000}.{gone}")]
    [InlineData("Windows default")]
    [InlineData("")]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // the chosen device missing falls back to the default, as the capture does (VT-5); the stored id is not touched by the row
    public void A_stored_pick_that_is_not_there_shows_Windows_default(string stored)
    {
        var state = MicrophoneRowState.Of(Host(), stored);
        Assert.Null(state.SelectedValue);
        Assert.Equal("Windows default", state.SelectedLabel);
        Assert.Equal(stored == "{0.0.1.00000000}.{gone}", state.PickIsMissing);
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // a PC with no microphone: the row has its first item only
    public void A_PC_with_no_microphone_lists_only_Windows_default()
    {
        var state = MicrophoneRowState.Of(new ScriptedCaptureHost(), Usb);
        Assert.Equal(["Windows default"], state.Items.Select(i => i.Label));
        Assert.Null(state.SelectedValue);
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // the seam fails closed (no composition yet, or a COM error): the row is the first item only and nothing throws
    public void A_host_that_cannot_list_leaves_Windows_default_only()
    {
        var state = MicrophoneRowState.Of(new FailClosedCaptureHost(), Usb);
        Assert.Equal(["Windows default"], state.Items.Select(i => i.Label));
        Assert.Null(state.SelectedValue);
        Assert.False(state.PickIsMissing);
        Assert.Throws<SeamNotBoundException>(() => new FailClosedCaptureHost().CaptureEndpoints());
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // the row follows the devices: an unplugged pick falls back, a plugged-in one is listed, and the same stored id is the pick again after it returns
    public void The_row_follows_plugging_and_unplugging_without_losing_the_stored_id()
    {
        var host = Host();
        var stored = Usb;
        Assert.Equal(Usb, MicrophoneRowState.Of(host, stored).SelectedValue);
        host.Remove(Usb);
        var gone = MicrophoneRowState.Of(host, stored);
        Assert.Null(gone.SelectedValue);
        Assert.True(gone.PickIsMissing);
        Assert.Equal(2, gone.Items.Count);
        host.Add(Usb, "Headset (USB)");
        Assert.Equal(Usb, MicrophoneRowState.Of(host, stored).SelectedValue);
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // menu ids: the first item's id is the empty string, an endpoint's id is its own
    public void Menu_options_use_an_empty_id_for_Windows_default_and_the_endpoint_id_for_the_rest()
    {
        var options = MicrophoneRowState.Of(Host(), null).MenuOptions;
        Assert.Equal([("", "Windows default"), (Realtek, "Microphone Array (Realtek)"), (Usb, "Headset (USB)")], options);
        Assert.Equal("", MicrophoneRowState.Of(Host(), null).SelectedId);
        Assert.Equal(Usb, MicrophoneRowState.Of(Host(), Usb).SelectedId);
        Assert.Null(MicrophoneRowState.ValueOf(""));
        Assert.Equal(Usb, MicrophoneRowState.ValueOf(Usb));
    }

    [Fact]
    [Trait("Feature", "VT-13")]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // two endpoints with one name stay two items
    public void Two_endpoints_with_one_name_stay_two_items()
    {
        var host = new ScriptedCaptureHost(("{a}", "USB Microphone"), ("{b}", "USB Microphone"));
        var state = MicrophoneRowState.Of(host, "{b}");
        Assert.Equal(3, state.MenuOptions.Count);
        Assert.Equal("{b}", state.SelectedValue);
    }
}
