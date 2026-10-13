using System.Text.RegularExpressions;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Window.Native;

// PORT-ONLY: web surfaces security review, what only real WebView2 and Chromium show (E:/Droppy/work/scratch/lead/web-sec-findings.md
// H1, H2, M2; ED-NEW-web-sec-agent-1, -2, -4). Write but NEVER run as an implementing agent: the lead takes native.lock and schedules
// each Fact alone. Another site's frame runs in its own process (the page's hit test and focus stop at its iframe), a CDP click is
// a trusted gesture (its target=_blank pop-up arrives user-initiated), and the keys go through Chromium's own input path.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed partial class WebSecAgentNativeTests(WindowManagerNativeSession native)
{
    [GeneratedRegex("(e\\d+) link \"Read the next page\"")]
    private static partial Regex NextLink();

    private string State(WebSecAgentNativeRig rig)
    {
        var read = rig.Call(native, WebToolSchemas.Read, "{\"tab\":\"t1\",\"mode\":\"text\"}");
        Assert.Null(read.Error);
        return read.Text;
    }

    [Fact]
    public void A_point_click_on_Pay_now_inside_another_sites_frame_is_refused_and_presses_nothing() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new WebSecAgentNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The shop page did not load.", 20000);
            Assert.Null(rig.Call(native, WebToolSchemas.Tabs, "{}").Error);

            var click = rig.Call(native, WebToolSchemas.Click, "{\"tab\":\"t1\",\"x\":140,\"y\":160}");

            Assert.Equal(WebErrorCodes.Denied, click.Error);
            Assert.Equal(WebAgentPolicy.PaymentRefusal, click.Text);
            Assert.Contains("pay idle", State(rig), StringComparison.Ordinal);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentNativeTests));

    [Fact]
    public void Letters_into_a_password_field_inside_another_sites_frame_are_refused() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new WebSecAgentNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The shop page did not load.", 20000);
            Assert.Null(rig.Call(native, WebToolSchemas.Tabs, "{}").Error);
            Assert.Null(rig.Call(native, WebToolSchemas.Click, "{\"tab\":\"t1\",\"x\":160,\"y\":235}").Error);

            var keys = rig.Call(native, WebToolSchemas.Key, "{\"tab\":\"t1\",\"keys\":\"h u n t e r\"}");
            var backspace = rig.Call(native, WebToolSchemas.Key, "{\"tab\":\"t1\",\"keys\":\"backspace\"}");

            Assert.Equal(WebErrorCodes.Denied, keys.Error);
            Assert.Equal(WebAgentPolicy.PasswordRefusal, keys.Text);
            Assert.Equal(WebErrorCodes.Denied, backspace.Error);
            Assert.Contains("typed 0", State(rig), StringComparison.Ordinal);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentNativeTests));

    // The paste itself is not tried: that would read the user's real Windows clipboard. The chord is refused before any key event
    // reaches the page, so the fix does not depend on whether a CDP Ctrl+V pastes in WebView2.
    [Fact]
    public void Clipboard_chords_never_reach_a_real_field() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new WebSecAgentNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The shop page did not load.", 20000);
            Assert.Null(rig.Call(native, WebToolSchemas.Tabs, "{}").Error);
            Assert.Null(rig.Call(native, WebToolSchemas.Click, "{\"tab\":\"t1\",\"x\":120,\"y\":75}").Error);

            foreach (var chord in new[] { "ctrl+v", "shift+insert", "ctrl+shift+v", "ctrl+x" })
            {
                var pressed = rig.Call(native, WebToolSchemas.Key, "{\"tab\":\"t1\",\"keys\":\"" + chord + "\"}");
                Assert.Equal(WebErrorCodes.Denied, pressed.Error);
                Assert.Contains("clipboard", pressed.Text, StringComparison.Ordinal);
            }

            Assert.Contains("note 0", State(rig), StringComparison.Ordinal);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentNativeTests));

    [Fact]
    public void A_model_click_on_a_new_tab_link_is_stopped_and_opens_no_tab() => native.Run(_ =>
    {
        var rig = native.OnUi(() => new WebSecAgentNativeRig(native));
        try
        {
            WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The shop page did not load.", 20000);
            Assert.Null(rig.Call(native, WebToolSchemas.Tabs, "{}").Error);
            var read = rig.Call(native, WebToolSchemas.Read, "{\"tab\":\"t1\"}");
            var link = NextLink().Match(read.Text);
            Assert.True(link.Success, read.Text);

            var click = rig.Call(native, WebToolSchemas.Click, "{\"tab\":\"t1\",\"ref\":\"" + link.Groups[1].Value + "\"}");

            Assert.Equal(WebErrorCodes.Blocked, click.Error);
            Assert.Contains("web_open", click.Text, StringComparison.Ordinal);
            Assert.Contains(WebSecAgentNativeSites.Next, click.Text, StringComparison.Ordinal);
            Assert.Equal(1, native.OnUi(() => rig.Session.Tabs.Count));
            Assert.Equal(WebSecAgentNativeSites.Next, native.OnUi(() => rig.Tab.BlockedPopup));
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentNativeTests));
}
