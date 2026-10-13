using Marrow.Core.Web;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: final security gate S2 (ED-NEW-secfinal-20; DESIGN.md 5.10 "after the user allows it per project"; ED-NEW-web-sec-agent-6).
// web_eval checks that the tab shows a project preview (a dev address), then may wait: for a pause or take-over (Hold) and for
// the "Allow scripts" card. A dev server the model can write to can send its page elsewhere in that time (location = 'https://...'),
// and the script would have run in that other site's document of the agent profile. The tab's address is read again after the
// waits, and the script runs only while the tab is still on the origin that was checked and allowed.
[Trait("Decision", "ED-NEW-secfinal-20")]
public sealed class WebAgentEvalOriginTests
{
    private const string Dev = "http://localhost:3001/";
    private const string Elsewhere = "https://accounts.google.com/signin";

    private static string Eval(string expression = "1+1") => "{\"tab\":\"t1\",\"expression\":\"" + expression + "\"}";

    private static int Runs(FakeAgentTab tab) => tab.Page.Calls.Count(c => c.Method == "Runtime.evaluate" && (string?)c.Params["expression"] == "1+1");

    [Fact]
    public async Task A_page_that_moves_to_another_site_while_the_script_card_waits_gets_no_script()
    {
        using var rig = new WebAgentRig();
        var tab = rig.Host.Add(Dev);
        await rig.Call(WebToolSchemas.Tabs);
        rig.Asker.Script = _ =>
        {
            tab.Fake.Navigate(Elsewhere);
            return Task.FromResult(true);
        };

        var result = await rig.Call(WebToolSchemas.Eval, Eval());

        Assert.Equal(0, Runs(tab));
        Assert.Equal(WebErrorCodes.NotSupported, result.Error);
        Assert.Equal("web_eval runs only in project previews (localhost and LAN dev hosts).", result.Text);
    }

    [Fact]
    public async Task A_page_that_moves_to_another_dev_origin_while_the_script_card_waits_gets_no_script()
    {
        using var rig = new WebAgentRig();
        var tab = rig.Host.Add(Dev);
        await rig.Call(WebToolSchemas.Tabs);
        rig.Asker.Script = _ =>
        {
            tab.Fake.Navigate("http://localhost:4000/");
            return Task.FromResult(true);
        };

        var result = await rig.Call(WebToolSchemas.Eval, Eval());

        Assert.Equal(0, Runs(tab));
        Assert.Equal(WebErrorCodes.NotSupported, result.Error);
    }

    [Fact]
    public async Task A_page_that_moves_to_another_site_during_a_pause_gets_no_script()
    {
        using var rig = new WebAgentRig();
        var tab = rig.Host.Add(Dev);
        await rig.Call(WebToolSchemas.Tabs);
        rig.Grants.GrantEval(rig.Host.Project!, "http://localhost:3001");
        rig.Hub.Pause();

        var call = rig.Start(WebToolSchemas.Eval, Eval());
        Assert.True(await PcTestWait.StillWaiting(call));
        tab.Fake.Navigate(Elsewhere);
        rig.Hub.Resume();
        var result = await call;

        Assert.Equal(0, Runs(tab));
        Assert.Equal(WebErrorCodes.NotSupported, result.Error);
    }

    [Fact]
    public async Task A_page_that_stays_on_its_origin_while_the_script_card_waits_still_runs_the_script()
    {
        using var rig = new WebAgentRig();
        var tab = rig.Host.Add(Dev);
        await rig.Call(WebToolSchemas.Tabs);
        rig.Asker.Script = _ =>
        {
            tab.Fake.Navigate(Dev + "other/page?x=1");
            return Task.FromResult(true);
        };

        var result = await rig.Call(WebToolSchemas.Eval, Eval());

        Assert.Null(result.Error);
        Assert.Equal(1, Runs(tab));
        Assert.Contains("42", result.Text, StringComparison.Ordinal);
    }
}
