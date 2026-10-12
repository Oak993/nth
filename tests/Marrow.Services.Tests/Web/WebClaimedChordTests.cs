using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.7 "When focus is inside Visus, browser keys
// (Ctrl+T, Ctrl+W, Ctrl+L, Ctrl+Tab, F5, Alt+Left) are Visus's"; ED-NEW-web-w8a-2). A view's owner claims the browser keys it
// handles from inside the page: the key is kept from the page, and the owner runs it after the key event, once per press.
// nth's reserved chords still win.
[Trait("Decision", "ED-NEW-web-w8a-2")]
public sealed class WebClaimedChordTests
{
    private static readonly KeyChord NewTab = new(0x14, ChordModifiers.Control); // Ctrl+T
    private static readonly KeyChord ExpandPane = new(WebKeys.Backslash, ChordModifiers.Control | ChordModifiers.Shift);

    [Fact]
    public void A_claimed_chord_is_kept_from_the_page_and_runs_after_the_key_event()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var claimed = new List<KeyChord>();
        view.ClaimsChord = chord => chord == NewTab;
        view.ClaimedChordPressed += claimed.Add;

        var key = WebRig.Fake(view).RaiseKey(NewTab);

        Assert.True(key.Handled);
        Assert.Empty(claimed); // never inside the browser's synchronous handler
        rig.RunPosted();
        Assert.Equal(NewTab, Assert.Single(claimed));
    }

    [Fact]
    public void A_held_key_is_kept_from_the_page_but_runs_once()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var claimed = 0;
        view.ClaimsChord = chord => chord == NewTab;
        view.ClaimedChordPressed += _ => claimed++;
        var fake = WebRig.Fake(view);

        Assert.True(fake.RaiseKey(NewTab).Handled);
        Assert.True(fake.RaiseKey(NewTab, repeat: true).Handled);
        Assert.True(fake.RaiseKey(NewTab, repeat: true).Handled);
        rig.RunPosted();
        Assert.Equal(1, claimed);
    }

    [Fact]
    public void Keys_nobody_claims_reach_the_page()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var claimed = 0;
        view.ClaimsChord = chord => chord == NewTab;
        view.ClaimedChordPressed += _ => claimed++;

        Assert.False(WebRig.Fake(view).RaiseKey(new KeyChord(0x2E, ChordModifiers.Control)).Handled); // Ctrl+C
        rig.RunPosted();
        Assert.Equal(0, claimed);
    }

    [Fact]
    public void A_reserved_chord_wins_over_a_claim()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        var claimed = 0;
        var reserved = 0;
        view.ClaimsChord = _ => true;
        view.ClaimedChordPressed += _ => claimed++;
        rig.Surface.ReservedChordPressed += (_, _) => reserved++;

        Assert.True(WebRig.Fake(view).RaiseKey(ExpandPane).Handled);
        rig.RunPosted();
        Assert.Equal(1, reserved);
        Assert.Equal(0, claimed);
    }

    [Fact]
    public async Task The_favicon_comes_from_the_engine()
    {
        using var rig = new WebRig();
        var view = rig.OpenLive(shown: true);
        WebRig.Fake(view).FaviconBytes = [0x89, 0x50, 0x4E, 0x47];

        var icon = await view.GetFaviconAsync();

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, icon);
    }
}
