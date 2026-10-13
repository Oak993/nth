using System.Text.RegularExpressions;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Window.Native;

// PORT-ONLY: web surfaces security review, second pass, what only real WebView2 and Chromium show (the adversarial review of
// ED-NEW-web-sec-agent-1, -3 and -5; ED-NEW-web-sec-agent-7, -8, -9). Write but NEVER run as an implementing agent: the lead takes
// native.lock and schedules each Fact alone. A CSS transform on another site's frame changes where Chromium sends a click; the frame's
// innerWidth read in nth's isolated world includes its scroll bar, so an unmoved frame that scrolls still matches its owner's box; a
// mousemove handler runs before Input.dispatchMouseEvent returns; and an out-of-process frame's field reports its own address.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed partial class WebSecAgentGapNativeTests(WindowManagerNativeSession native)
{
    // pay.invalid's frame at half size: Help fills its top-left corner, Pay now sits at (100, 100) inside it. The frame starts at
    // (40, 120) of the page, so the page point (95, 175) is Help if the frame were only moved, and Pay now where Chromium sends it.
    private const string ScaledShop = """
        <!doctype html><html><head><title>Shop</title></head><body style="margin:0;font:16px sans-serif">
        <p id="state" style="position:absolute;left:300px;top:20px;margin:0">pay idle, help 0</p>
        <iframe src="https://pay.invalid/help" title="Payment" style="position:absolute;left:40px;top:120px;width:400px;height:220px;border:0;transform:scale(.5);transform-origin:0 0"></iframe>
        <script>
        let pay = 'idle', help = 0;
        const show = () => { document.getElementById('state').textContent = 'pay ' + pay + ', help ' + help; };
        addEventListener('message', e => { const d = String(e.data); if (d === 'pay') pay = 'pressed'; else if (d === 'help') help++; show(); });
        </script></body></html>
        """;

    private const string HelpFrame = """
        <!doctype html><html><head><title>Card</title></head><body style="margin:0;font:16px sans-serif">
        <button style="position:absolute;left:0;top:0;width:60px;height:60px" onclick="parent.postMessage('help', '*')">Help</button>
        <button style="position:absolute;left:100px;top:100px;width:160px;height:40px" onclick="parent.postMessage('pay', '*')">Pay now</button>
        </body></html>
        """;

    // The same frame unmoved and taller inside than its box, so it shows a scroll bar; Pay now's centre on the page is (140, 160).
    private const string ScrollingShop = """
        <!doctype html><html><head><title>Shop</title></head><body style="margin:0;font:16px sans-serif">
        <p id="state" style="position:absolute;left:300px;top:20px;margin:0">pay idle</p>
        <iframe src="https://pay.invalid/tall" title="Payment" style="position:absolute;left:40px;top:120px;width:400px;height:220px;border:0"></iframe>
        <script>
        addEventListener('message', e => { if (String(e.data) === 'pay') document.getElementById('state').textContent = 'pay pressed'; });
        </script></body></html>
        """;

    private const string TallFrame = """
        <!doctype html><html><head><title>Card</title></head><body style="margin:0;font:16px sans-serif">
        <button style="position:absolute;left:20px;top:20px;width:160px;height:40px" onclick="parent.postMessage('pay', '*')">Pay now</button>
        <div style="position:absolute;left:0;top:0;width:10px;height:1200px"></div>
        </body></html>
        """;

    // Next, which a mousemove over it covers with Delete account.
    private const string SwappingShop = """
        <!doctype html><html><head><title>Shop</title></head><body style="margin:0;font:16px sans-serif">
        <p id="state" style="position:absolute;left:300px;top:20px;margin:0">next 0, deleted 0</p>
        <button id="next" style="position:absolute;left:20px;top:400px;width:160px;height:40px">Next</button>
        <script>
        let next = 0, deleted = 0;
        const show = () => { document.getElementById('state').textContent = 'next ' + next + ', deleted ' + deleted; };
        const nextButton = document.getElementById('next');
        nextButton.addEventListener('click', () => { next++; show(); });
        nextButton.addEventListener('mousemove', () => {
          if (document.getElementById('delete')) return;
          const b = document.createElement('button');
          b.id = 'delete'; b.textContent = 'Delete account';
          b.style.cssText = 'position:absolute;left:20px;top:400px;width:160px;height:40px';
          b.addEventListener('click', () => { deleted++; show(); });
          document.body.appendChild(b);
        });
        </script></body></html>
        """;

    // A customer page with its account number and vendor.invalid's chat frame, whose Message field reports what it received.
    private const string CrmShop = """
        <!doctype html><html><head><title>Customer</title></head><body style="margin:0;font:16px sans-serif">
        <p style="position:absolute;left:20px;top:20px;margin:0">Customer Ada, account 99417723.</p>
        <p id="state" style="position:absolute;left:300px;top:20px;margin:0">chat 0</p>
        <iframe src="https://vendor.invalid/chat" title="Chat" style="position:absolute;left:40px;top:120px;width:400px;height:220px;border:0"></iframe>
        <script>
        addEventListener('message', e => { const d = String(e.data); if (d.startsWith('chat ')) document.getElementById('state').textContent = d; });
        </script></body></html>
        """;

    private const string ChatFrame = """
        <!doctype html><html><head><title>Chat</title></head><body style="margin:0;font:16px sans-serif">
        <input aria-label="Message" style="position:absolute;left:20px;top:20px;width:200px;height:30px" oninput="parent.postMessage('chat ' + this.value.length, '*')">
        </body></html>
        """;

    [GeneratedRegex("(e\\d+) button \"Next\"")]
    private static partial Regex NextButton();

    [GeneratedRegex("(e\\d+) textbox \"Message\"")]
    private static partial Regex MessageField();

    private string State(WebSecAgentNativeRig rig)
    {
        var read = rig.Call(native, WebToolSchemas.Read, "{\"tab\":\"t1\",\"mode\":\"text\"}");
        Assert.Null(read.Error);
        return read.Text;
    }

    private WebSecAgentNativeRig Open(string shop, string frameStart, string frame)
    {
        var rig = native.OnUi(() => new WebSecAgentNativeRig(native, new WebSecAgentNativeSites(shop, new Dictionary<string, string> { [frameStart] = frame })));
        WindowManagerNativeMethods.Until(() => native.OnUi(() => rig.Loaded), "The shop page did not load.", 20000);
        Assert.Null(rig.Call(native, WebToolSchemas.Tabs, "{}").Error);
        return rig;
    }

    [Fact]
    public void A_point_in_another_sites_frame_the_page_shows_at_half_size_is_never_clicked() => native.Run(_ =>
    {
        var rig = Open(ScaledShop, "https://pay.invalid/help", HelpFrame);
        try
        {
            var click = rig.Call(native, WebToolSchemas.Click, "{\"tab\":\"t1\",\"x\":95,\"y\":175}");

            Assert.Equal(WebErrorCodes.Denied, click.Error);
            Assert.Contains("frame", click.Text, StringComparison.Ordinal);
            Assert.Contains("pay idle, help 0", State(rig), StringComparison.Ordinal);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentGapNativeTests));

    [Fact]
    public void An_unmoved_frame_with_a_scroll_bar_is_still_judged_on_the_button_inside() => native.Run(_ =>
    {
        var rig = Open(ScrollingShop, "https://pay.invalid/tall", TallFrame);
        try
        {
            var click = rig.Call(native, WebToolSchemas.Click, "{\"tab\":\"t1\",\"x\":140,\"y\":160}");

            Assert.Equal(WebErrorCodes.Denied, click.Error);
            Assert.Equal(WebAgentPolicy.PaymentRefusal, click.Text);
            Assert.Contains("pay idle", State(rig), StringComparison.Ordinal);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentGapNativeTests));

    [Fact]
    public void A_button_swapped_in_by_mousemove_is_never_pressed() => native.Run(_ =>
    {
        var rig = Open(SwappingShop, "https://unused.invalid/", "");
        try
        {
            var read = rig.Call(native, WebToolSchemas.Read, "{\"tab\":\"t1\"}");
            var next = NextButton().Match(read.Text);
            Assert.True(next.Success, read.Text);

            var click = rig.Call(native, WebToolSchemas.Click, "{\"tab\":\"t1\",\"ref\":\"" + next.Groups[1].Value + "\"}");

            Assert.Equal(WebErrorCodes.StaleRef, click.Error);
            Assert.Contains("next 0, deleted 0", State(rig), StringComparison.Ordinal);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentGapNativeTests));

    [Fact]
    public void The_pages_text_typed_into_another_sites_frame_inside_it_waits_for_the_card() => native.Run(_ =>
    {
        var rig = Open(CrmShop, "https://vendor.invalid/chat", ChatFrame);
        try
        {
            Assert.Contains("99417723", State(rig), StringComparison.Ordinal);
            var read = rig.Call(native, WebToolSchemas.Read, "{\"tab\":\"t1\"}");
            var field = MessageField().Match(read.Text);
            Assert.True(field.Success, read.Text);

            var typed = rig.Call(native, WebToolSchemas.Type, "{\"tab\":\"t1\",\"ref\":\"" + field.Groups[1].Value + "\",\"text\":\"99417723\"}");

            Assert.Equal(WebErrorCodes.WaitingForUser, typed.Error);
            Assert.Contains("chat 0", State(rig), StringComparison.Ordinal);
        }
        finally { native.OnUi(rig.Dispose); }
    }, factType: typeof(WebSecAgentGapNativeTests));
}
