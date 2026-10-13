using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces security review H2 (E:/Droppy/work/scratch/lead/web-sec-findings.md; DESIGN.md 5.10 the guarded-action card
// and the hard denies, 8.4 "Clipboard read: agent tabs denied", "Clipboard write: never from page script"; ED-NEW-web-sec-agent-2).
// A modifier never takes a key past the guards: Space with Ctrl or Alt still presses the focused button, a chord that edits a field
// is judged on that field, and a chord that pastes, copies or cuts (the user's Windows clipboard) never runs.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentKeyChordTests
{
    private static string Keys(string keys) => "{\"tab\":\"t1\",\"keys\":\"" + keys + "\"}";

    // The invite page, read once: 2 Email, 4 Send invite, 5 Password, 7 Buy now, 10 Next.
    private static async Task<FakeAgentTab> Invite(WebAgentRig rig, long focused)
    {
        var tab = await rig.InviteTab();
        Assert.Null((await rig.Call(WebToolSchemas.Read, "{\"tab\":\"t1\"}")).Error);
        tab.Page.Focused = focused;
        return tab;
    }

    [Fact]
    public async Task CtrlSpaceOnAFocusedSendButtonAsksFirst()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await Invite(rig, 4);

        var result = await rig.Call(WebToolSchemas.Key, Keys("ctrl+space"));

        Assert.Equal(WebGuardCategory.Send, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task AltSpaceOnBuyNowIsNeverPressed()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig, 7);

        var result = await rig.Call(WebToolSchemas.Key, Keys("alt+space"));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, result.Text);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Theory]
    [InlineData("ctrl+v")]
    [InlineData("ctrl+shift+v")]
    [InlineData("meta+v")]
    [InlineData("shift+insert")]
    [InlineData("ctrl+c")]
    [InlineData("ctrl+x")]
    [InlineData("ctrl+insert")]
    [InlineData("shift+delete")]
    public async Task ClipboardChordsNeverRun(string chord)
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig, 2);

        var result = await rig.Call(WebToolSchemas.Key, Keys("a " + chord));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Contains("clipboard", result.Text, StringComparison.Ordinal);
        Assert.Equal(2, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Theory]
    [InlineData("ctrl+z")]
    [InlineData("ctrl+backspace")]
    [InlineData("backspace")]
    [InlineData("delete")]
    [InlineData("alt+a")]
    [InlineData("ctrl+space")]
    public async Task ChordsThatEditAPasswordFieldAreRefused(string chord)
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig, 5);

        var result = await rig.Call(WebToolSchemas.Key, Keys(chord));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(WebAgentPolicy.PasswordRefusal, result.Text);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task MovingAroundStillRunsInAnyField()
    {
        using var rig = new WebAgentRig();
        var tab = await Invite(rig, 5);

        var result = await rig.Call(WebToolSchemas.Key, Keys("tab shift+tab left ctrl+end escape"));

        Assert.Null(result.Error);
        Assert.Equal(10, tab.Page.Count("Input.dispatchKeyEvent"));
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
    }
}
