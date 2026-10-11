using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 3.7: a focused web view keeps every key except nth's reserved chords, which reach nth through the
// controller's AcceleratorKeyPressed: decided at once (the browser waits for the handler) and run after the key event.
// DESIGN 10: Tab out of the page moves focus on (MoveFocusRequested, Avalonia WebView issue #76).
[Trait("Decision", "ED-NEW-web-9")]
[Trait("Feature", "W-7")]
public sealed class WebChordForwardingTests
{
    private static readonly KeyChord ExpandPane = new(WebKeys.Backslash, ChordModifiers.Control | ChordModifiers.Shift);

    [Fact]
    public void A_reserved_chord_is_kept_from_the_page_and_runs_after_the_key_event()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var pressed = new List<(WebSurfaceView View, KeyChord Chord)>();
        rig.Surface.ReservedChordPressed += (v, c) => pressed.Add((v, c));

        var key = WebRig.Fake(view).RaiseKey(ExpandPane);

        Assert.True(key.Handled);
        Assert.Empty(pressed); // not inside the browser's synchronous handler
        rig.RunPosted();
        var (who, chord) = Assert.Single(pressed);
        Assert.Same(view, who);
        Assert.Equal(ExpandPane, chord);
    }

    [Fact]
    public void Other_keys_reach_the_page()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var fake = WebRig.Fake(view);

        Assert.False(fake.RaiseKey(new KeyChord(WebKeys.Escape, ChordModifiers.None)).Handled); // Esc inside a page is the page's
        Assert.False(fake.RaiseKey(new KeyChord(0x2E, ChordModifiers.Control)).Handled); // Ctrl+C
        Assert.False(fake.RaiseKey(new KeyChord(WebKeys.F6, ChordModifiers.Control)).Handled);
        Assert.Empty(rig.Posted);
    }

    [Fact]
    public void Key_up_and_system_key_up_of_a_reserved_chord_are_not_taken()
    {
        using var rig = new WebRig();
        var fake = WebRig.Fake(rig.OpenLive(shown: true));
        Assert.False(fake.RaiseKey(ExpandPane, WebKeyEventKind.KeyUp).Handled);
        Assert.False(fake.RaiseKey(new KeyChord(WebKeys.Escape, ChordModifiers.Control | ChordModifiers.Alt), WebKeyEventKind.SystemKeyUp).Handled);
        Assert.Empty(rig.Posted);
    }

    [Fact]
    public void Alt_chords_arrive_as_system_keys_and_are_still_reserved()
    {
        using var rig = new WebRig();
        var fake = WebRig.Fake(rig.OpenLive(shown: true));
        var stop = new KeyChord(WebKeys.Escape, ChordModifiers.Control | ChordModifiers.Alt);
        Assert.True(fake.RaiseKey(stop, WebKeyEventKind.SystemKeyDown).Handled);
        Assert.Single(rig.Posted);
    }

    [Fact]
    public void The_shortcut_layer_decides_which_chords_are_reserved()
    {
        using var rig = new WebRig();
        var fake = WebRig.Fake(rig.OpenLive(shown: true));
        var custom = new KeyChord(0x21, ChordModifiers.Control | ChordModifiers.Alt); // Ctrl+Alt+F
        rig.Surface.IsReservedChord = chord => chord == custom;
        Assert.True(fake.RaiseKey(custom).Handled);
        Assert.False(fake.RaiseKey(ExpandPane).Handled);
    }

    [Fact]
    public void The_default_reserved_list_is_the_designs()
    {
        var expected = new[]
        {
            new KeyChord(WebKeys.F6, ChordModifiers.None),
            new KeyChord(WebKeys.F6, ChordModifiers.Shift),
            new KeyChord(WebKeys.Escape, ChordModifiers.Control | ChordModifiers.Alt),
            new KeyChord(WebKeys.Backslash, ChordModifiers.Control | ChordModifiers.Shift),
            new KeyChord(WebKeys.Up, ChordModifiers.Control | ChordModifiers.Alt),
            new KeyChord(WebKeys.Down, ChordModifiers.Control | ChordModifiers.Alt),
            new KeyChord(WebKeys.Left, ChordModifiers.Control | ChordModifiers.Alt),
            new KeyChord(WebKeys.Right, ChordModifiers.Control | ChordModifiers.Alt),
            new KeyChord(WebKeys.B, ChordModifiers.Control | ChordModifiers.Shift),
        };
        Assert.Equal(expected.Length, WebKeys.DefaultReserved.Count);
        Assert.All(expected, chord => Assert.Contains(chord, WebKeys.DefaultReserved));
    }

    [Fact]
    public void Chords_are_built_from_physical_scan_codes()
    {
        Assert.Equal(new KeyChord(0xE048, ChordModifiers.Control | ChordModifiers.Alt), WebKeys.ChordFrom(0x48, isExtendedKey: true, control: true, alt: true, shift: false));
        Assert.Equal(new KeyChord(0x48, ChordModifiers.None), WebKeys.ChordFrom(0x48, isExtendedKey: false, control: false, alt: false, shift: false));
        Assert.Equal(new KeyChord(WebKeys.Backslash, ChordModifiers.Control | ChordModifiers.Shift), WebKeys.ChordFrom(0x2B, false, true, false, true));
    }

    [Fact]
    public void Tab_out_of_the_page_is_handled_and_handed_to_the_host()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var exits = new List<bool>();
        view.FocusExitRequested += exits.Add;

        var forward = WebRig.Fake(view).RaiseMoveFocus(forward: true);
        var back = WebRig.Fake(view).RaiseMoveFocus(forward: false);

        Assert.True(forward.Handled);
        Assert.True(back.Handled);
        Assert.Equal([true, false], exits);
    }
}
