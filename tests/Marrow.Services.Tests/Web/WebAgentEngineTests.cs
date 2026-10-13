using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W11, model access (DESIGN.md 5.10, 14 "W11 ... approvals, the cross-origin-after-read rule, guards,
// hard denies, Stop, Pause, take-over"; the test list "denies; take-over pause; Stop cancels queued steps; untrusted marking").
// Every tool runs against a scripted page behind FakeWebEngine's CDP channel; the cards are a fake asker.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebAgentEngineTests
{
    private static string Tab(string tab = "t1", string extra = "") => "{\"tab\":\"" + tab + "\"" + extra + "}";

    private static string Ref(string reference, string extra = "", string tab = "t1") => Tab(tab, ",\"ref\":\"" + reference + "\"" + extra);

    // The invite page's refs after one read: e1 Email, e2 Role, e3 Send invite, e4 Password, e5 Docs, e6 Buy now, e7 भेजें,
    // e8 Remember me, e9 Next.
    private static async Task<FakeAgentTab> ReadInvite(WebAgentRig rig, string url = "https://a.test/invite", bool shared = false, VisusTabKind kind = VisusTabKind.Agent)
    {
        var tab = await rig.InviteTab(url, shared, kind);
        var read = await rig.Call(WebToolSchemas.Read, Tab());
        Assert.Null(read.Error);
        return tab;
    }

    // ---- Tabs, the switch and addresses ----

    [Fact]
    public async Task TheModelSeesItsOwnTabsAndSharedOnesNeverPrivateOnes()
    {
        using var rig = new WebAgentRig();
        rig.Host.Add("https://a.test/mine");
        rig.Host.Add("https://b.test/shared", VisusTabKind.Browse, shared: true);
        rig.Host.Add("https://c.test/private", VisusTabKind.Private, shared: true);
        rig.Host.Add("https://d.test/unshared", VisusTabKind.Browse);

        var tabs = await rig.Call(WebToolSchemas.Tabs);

        Assert.Contains("t1 (yours) https://a.test/mine", tabs.Text, StringComparison.Ordinal);
        Assert.Contains("t2 (shared by the user) https://b.test/shared", tabs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("c.test", tabs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("d.test", tabs.Text, StringComparison.Ordinal);
        Assert.Contains(WebAgentText.OpenTag, tabs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingRunsWhileVisusModelAccessIsOff()
    {
        using var rig = new WebAgentRig { Settings = WebAgentSettings.Default with { Enabled = false } };

        var result = await rig.Call(WebToolSchemas.Tabs);

        Assert.Equal(WebErrorCodes.Off, result.Error);
        Assert.False(rig.Hub.Offered);
    }

    [Fact]
    public async Task AnAddressTheModelInventedIsNotOpened()
    {
        using var rig = new WebAgentRig();

        var result = await rig.Call(WebToolSchemas.Open, "{\"url\":\"https://b.test/x\"}");

        Assert.Equal(WebErrorCodes.UnknownUrl, result.Error);
        Assert.Empty(rig.Host.Opened);
    }

    [Fact]
    public async Task AnAddressFromTheConversationOpensInAnAgentTab()
    {
        using var rig = new WebAgentRig();
        rig.Host.Conversation.Add("https://docs.example.com");

        var result = await rig.Call(WebToolSchemas.Open, "{\"url\":\"docs.example.com/x\"}");

        Assert.True(result.Error is null, result.Text);
        Assert.Equal(["https://docs.example.com/x"], rig.Host.Opened);
        Assert.StartsWith("Opened t1 at https://docs.example.com/x.", result.Text, StringComparison.Ordinal);
        Assert.Contains("<untrusted-page-content origin=\"https://docs.example.com\" marker=\"", result.Text, StringComparison.Ordinal);
        Assert.Empty(rig.Asker.Asked);
        Assert.Empty(rig.Engine.ReadOrigins);
    }

    [Fact]
    public async Task AQueryOpensTheUsersSearchEngine()
    {
        using var rig = new WebAgentRig();

        var result = await rig.Call(WebToolSchemas.Open, "{\"query\":\"visus browser\"}");

        Assert.Null(result.Error);
        Assert.Equal([SearchEngines.QueryUrl(SearchEngines.Default, "visus browser")], rig.Host.Opened);
        Assert.Contains(rig.Activity, a => a.Step == "Searching for visus browser");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Users/me/notes.txt")]
    [InlineData("https://vault.bitwarden.com/")]
    public async Task InternalSchemesAndTheDenyListAreRefused(string url)
    {
        using var rig = new WebAgentRig();
        rig.Host.Conversation.Add(url);

        var result = await rig.Call(WebToolSchemas.Open, "{\"url\":\"" + url + "\"}");

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Empty(rig.Host.Opened);
    }

    // ---- Reading ----

    [Fact]
    public async Task APageReadsAsUntrustedLinesWithRefsAndItsSiteCounts()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab();

        var read = await rig.Call(WebToolSchemas.Read, Tab());

        Assert.Null(read.Error);
        Assert.Contains("<untrusted-page-content origin=\"https://a.test\" marker=\"", read.Text, StringComparison.Ordinal);
        Assert.Contains("e3 button \"Send invite\"", read.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", read.Text, StringComparison.Ordinal);
        Assert.Equal("https://a.test", read.ReadOrigin);
        Assert.Equal(["https://a.test"], rig.Engine.ReadOrigins);
        Assert.Empty(rig.Asker.Asked);
        Assert.Contains(rig.Activity, a => a.Step == "Reading the page" && a.Phase == WebAgentPhase.Working);
    }

    [Fact]
    public async Task ReadingASharedTabAsksAndNeverOffersAlways()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Site = _ => Task.FromResult(WebSiteAnswer.Deny);
        var tab = await rig.InviteTab(shared: true, kind: VisusTabKind.Browse);

        var read = await rig.Call(WebToolSchemas.Read, Tab());

        var ask = Assert.Single(rig.Asker.Of<WebSiteAsk>());
        Assert.True(ask.Shared);
        Assert.False(ask.AllowAlways);
        Assert.Equal("Let Claude Sonnet use a.test in Visus?", ask.Title);
        Assert.Equal(WebErrorCodes.NotApproved, read.Error);
        Assert.Equal(0, tab.Page.Count("Accessibility.getFullAXTree"));
    }

    [Fact]
    public async Task FindReturnsRefsAndText()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab();

        var found = await rig.Call(WebToolSchemas.Find, Tab(extra: ",\"query\":\"send\""));

        Assert.Contains("button \"Send invite\"", found.Text, StringComparison.Ordinal);
        Assert.Contains(WebAgentText.OpenTag, found.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScreenshotIsAJpegOfCssPixelsAndOnlyAVisionModelGetsThePicture()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab();

        var shot = await rig.Call(WebToolSchemas.Screenshot, Tab());

        Assert.True(shot.Error is null, shot.Text);
        Assert.NotNull(shot.Jpeg);
        Assert.Equal((1280, 800), (shot.ImageWidth, shot.ImageHeight));
        Assert.Contains("1280 x 800", shot.Text, StringComparison.Ordinal);
        Assert.Equal("https://a.test", shot.ReadOrigin);
        Assert.Equal(2, ((JsonArray)WebMcpTools.Content(shot, vision: true)["content"]!).Count);
        Assert.Single((JsonArray)WebMcpTools.Content(shot, vision: false)["content"]!);
    }

    [Fact]
    public async Task ConsoleLinesAreNumberedSoTheNextCallGetsOnlyNewOnes()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Fake.EmitCdp("Runtime.consoleAPICalled", "{\"type\":\"error\",\"args\":[{\"type\":\"string\",\"value\":\"boom\"}]}");

        var first = await rig.Call(WebToolSchemas.Console, Tab());
        var again = await rig.Call(WebToolSchemas.Console, Tab(extra: ",\"since\":1"));

        Assert.Contains("#1 error: boom", first.Text, StringComparison.Ordinal);
        Assert.Contains("next: 1", first.Text, StringComparison.Ordinal);
        Assert.StartsWith("Nothing new", again.Text, StringComparison.Ordinal);
    }

    // ---- Waiting, history and the network log ----

    [Fact]
    public async Task WaitForLoadAnswersOnceThePageIsDoneAndGivesUpAtItsTimeout()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var loaded = await rig.Call(WebToolSchemas.Wait, Tab(extra: ",\"for\":\"load\""));
        tab.Loading = true;
        var start = rig.Clock.NowMs;
        var loading = await rig.Call(WebToolSchemas.Wait, Tab(extra: ",\"for\":\"load\",\"timeout\":400"));
        var waited = rig.Clock.NowMs - start;
        var capped = await rig.Call(WebToolSchemas.Wait, Tab(extra: ",\"for\":\"load\",\"timeout\":90000"));
        var wrong = await rig.Call(WebToolSchemas.Wait, Tab(extra: ",\"for\":\"forever\""));

        Assert.Null(loaded.Error);
        Assert.Equal("t1 finished loading.", loaded.Text);
        Assert.Equal(WebErrorCodes.Timeout, loading.Error);
        Assert.Contains("Waited 400 ms; the page is still loading.", loading.Text, StringComparison.Ordinal);
        Assert.InRange(waited, 400, 500);
        Assert.Contains("Waited 25000 ms", capped.Text, StringComparison.Ordinal); // web_wait waits up to 25 s (DESIGN 5.10)
        Assert.Equal(WebErrorCodes.BadArgs, wrong.Error);
    }

    [Fact]
    public async Task WaitForIdleHoldsWhileARequestRunsAndWaitForARefFindsIt()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Fake.EmitCdp("Network.requestWillBeSent", "{\"requestId\":\"r1\",\"type\":\"Fetch\",\"request\":{\"method\":\"GET\",\"url\":\"https://a.test/api/people\"}}");

        var busy = await rig.Call(WebToolSchemas.Wait, Tab(extra: ",\"for\":\"idle\",\"timeout\":300"));
        tab.Fake.EmitCdp("Network.loadingFinished", "{\"requestId\":\"r1\",\"encodedDataLength\":10}");
        var finishedAt = rig.Clock.NowMs;
        var idle = await rig.Call(WebToolSchemas.Wait, Tab(extra: ",\"for\":\"idle\""));
        var seen = await rig.Call(WebToolSchemas.Wait, Ref("e3", ",\"for\":\"ref\""));

        Assert.Equal(WebErrorCodes.Timeout, busy.Error);
        Assert.Contains("requests are still running.", busy.Text, StringComparison.Ordinal);
        Assert.Equal("t1 has no requests running.", idle.Text);
        Assert.True(rig.Clock.NowMs - finishedAt >= 500, "idle means half a second with no request");
        Assert.Equal("e3 is on the page.", seen.Text);
    }

    [Fact]
    public async Task NavigateGoesBackAndReloadsOnTheSameSite()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var nothing = await rig.Call(WebToolSchemas.Navigate, Tab(extra: ",\"action\":\"back\""));
        tab.Fake.CanGoBack = true;
        tab.BackUrl = "https://a.test/home";
        var back = await rig.Call(WebToolSchemas.Navigate, Tab(extra: ",\"action\":\"back\""));
        var reload = await rig.Call(WebToolSchemas.Navigate, Tab(extra: ",\"action\":\"reload\""));
        var wrong = await rig.Call(WebToolSchemas.Navigate, Tab(extra: ",\"action\":\"home\""));

        Assert.Equal(WebErrorCodes.BadArgs, nothing.Error);
        Assert.Contains("t1 has no page to go back to.", nothing.Text, StringComparison.Ordinal);
        Assert.Null(back.Error);
        Assert.StartsWith("t1 is at https://a.test/home.", back.Text, StringComparison.Ordinal);
        Assert.Contains(WebAgentText.OpenTag, back.Text, StringComparison.Ordinal); // the title is page text
        Assert.Null(reload.Error);
        Assert.Equal(["https://a.test/home", "https://a.test/home"], tab.Navigations.TakeLast(2));
        Assert.Equal(WebErrorCodes.BadArgs, wrong.Error);
        Assert.Empty(rig.Asker.Of<WebNavigateAsk>());
    }

    [Fact]
    public async Task GoingBackToAnotherSiteAfterAReadIsStoppedLikeALink()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Fake.CanGoBack = true;
        tab.BackUrl = "https://b.test/search?q=invite";

        var back = await rig.Call(WebToolSchemas.Navigate, Tab(extra: ",\"action\":\"back\""));

        Assert.Equal(WebErrorCodes.Blocked, back.Error);
        Assert.Contains("web_open", back.Text, StringComparison.Ordinal);
        Assert.Equal("https://a.test/invite", tab.Url);
    }

    [Fact]
    public async Task NetworkLinesKeepNoHeadersOrBodiesAndTheNextCallGetsOnlyNewOnes()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Fake.EmitCdp("Network.requestWillBeSent",
            "{\"requestId\":\"r1\",\"type\":\"Fetch\",\"request\":{\"method\":\"POST\",\"url\":\"https://a.test/api/invite\",\"headers\":{\"Authorization\":\"Bearer s3cret\",\"Cookie\":\"sid=abc\"},\"postData\":\"email=ann%40a.test\"}}");
        tab.Fake.EmitCdp("Network.responseReceived", "{\"requestId\":\"r1\",\"response\":{\"status\":201,\"headers\":{\"Set-Cookie\":\"sid=def\"}}}");
        tab.Fake.EmitCdp("Network.loadingFinished", "{\"requestId\":\"r1\",\"encodedDataLength\":2048}");

        var first = await rig.Call(WebToolSchemas.Network, Tab());
        var next = first.Text.Split("next: ")[1].Split('\n')[0];
        var again = await rig.Call(WebToolSchemas.Network, Tab(extra: ",\"since\":" + next));

        Assert.Null(first.Error);
        Assert.Contains("POST 201 https://a.test/api/invite (fetch 2 KB", first.Text, StringComparison.Ordinal);
        Assert.Contains(WebAgentText.OpenTag, first.Text, StringComparison.Ordinal);
        foreach (var secret in new[] { "s3cret", "sid=", "email=", "Authorization", "Cookie" })
            Assert.DoesNotContain(secret, first.Text, StringComparison.Ordinal);
        Assert.Equal("https://a.test", first.ReadOrigin);
        Assert.StartsWith("Nothing new in t1's network", again.Text, StringComparison.Ordinal);
    }

    // ---- Per-site approval ----

    [Fact]
    public async Task TheFirstActionOnASiteAsksAndAllowOnceLastsWhileTheTabStaysThere()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var first = await rig.Call(WebToolSchemas.Click, Ref("e9"));
        var second = await rig.Call(WebToolSchemas.Click, Ref("e9"));

        var ask = Assert.Single(rig.Asker.Of<WebSiteAsk>());
        Assert.True(ask.AllowAlways);
        Assert.Equal("https://a.test", ask.Origin);
        Assert.StartsWith("Clicked e9 in t1.", first.Text, StringComparison.Ordinal);
        Assert.Null(second.Error);
        Assert.Equal(2, tab.Page.Mouse("mousePressed").Count());
        var press = tab.Page.Mouse("mousePressed").First();
        Assert.Equal((160d, 520d), (press["x"]!.GetValue<double>(), press["y"]!.GetValue<double>()));
        // the read and each click hold the tab for their whole call (DESIGN 2.3), and each click's input is a step inside it
        Assert.Equal(5, tab.Steps);
        Assert.Equal(6, tab.InputSteps.Count); // move, press, release per click
        Assert.All(tab.InputSteps, open => Assert.Equal(2, open));
        Assert.Equal(0, tab.OpenSteps);
    }

    [Fact]
    public async Task DontAllowStopsTheActionBeforeAnyInput()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Site = _ => Task.FromResult(WebSiteAnswer.Deny);
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e9"));

        Assert.Equal(WebErrorCodes.NotApproved, result.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AlwaysAllowIsKeptForTheSite()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Site = _ => Task.FromResult(WebSiteAnswer.AlwaysAllow);
        await ReadInvite(rig);
        await rig.Call(WebToolSchemas.Click, Ref("e9"));
        rig.Host.Add("https://a.test/other");
        await rig.Call(WebToolSchemas.Tabs);
        await rig.Call(WebToolSchemas.Read, Tab("t2"));

        await rig.Call(WebToolSchemas.Click, Ref("e9", tab: "t2"));

        Assert.True(rig.Grants.Site("https://a.test"));
        Assert.Single(rig.Asker.Of<WebSiteAsk>());
    }

    [Fact]
    public async Task ScrollingNeedsNoApproval()
    {
        using var rig = new WebAgentRig();
        var tab = await rig.InviteTab();

        var result = await rig.Call(WebToolSchemas.Scroll, Tab(extra: ",\"dy\":600"));

        Assert.Equal("Scrolled t1 down by 600 px.", result.Text);
        Assert.Single(tab.Page.Mouse("mouseWheel"));
        Assert.Empty(rig.Asker.Asked);
    }

    // ---- Guards and hard denies ----

    [Fact]
    public async Task ASendButtonShowsTheGuardCardAndCancelStopsIt()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e3"));

        var card = Assert.Single(rig.Asker.Of<WebGuardAsk>());
        Assert.Equal("Send invite?", card.Title);
        Assert.Equal("The model will press Send invite in Visus for you.", card.Detail);
        Assert.Equal("Send invite", card.ConfirmLabel);
        Assert.Equal("Asked because this sends a message. Enter never confirms.", card.Foot);
        Assert.Equal("Team invites", card.Chat);
        Assert.Equal("Claude Sonnet", card.Model);
        Assert.Contains(new KeyValuePair<string, string>("To", "lee@example.com"), card.Rows);
        Assert.Contains(card.Rows, r => r.Key == "Site");
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
        Assert.Contains(rig.Activity, a => a.Phase == WebAgentPhase.Asking && a.Step == "Check the card");
    }

    [Fact]
    public async Task AConfirmedSendRunsOnce()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e3"));

        Assert.Null(result.Error);
        Assert.Single(tab.Page.Mouse("mousePressed"));
        Assert.Contains(rig.Activity, a => a.Step == "Pressing Send invite");
    }

    [Fact]
    public async Task ALocalisedSendWordAsksToo()
    {
        using var rig = new WebAgentRig();
        await ReadInvite(rig);

        await rig.Call(WebToolSchemas.Click, Ref("e7"));

        Assert.Equal(WebGuardCategory.Send, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
    }

    [Fact]
    public async Task APaymentIsNeverClickedAndNothingAsks()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e6"));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, result.Text);
        Assert.Empty(rig.Asker.Of<WebGuardAsk>());
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AClickOnAPointIsJudgedByWhatIsThere()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Click, Tab(extra: ",\"x\":150,\"y\":370"));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Empty(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task ACoverOverTheTargetIsJudgedToo()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Page.HitOverride = 3 + 1; // Send invite lies over Next's centre

        await rig.Call(WebToolSchemas.Click, Ref("e9"));

        Assert.Single(rig.Asker.Of<WebGuardAsk>());
    }

    [Fact]
    public async Task PasswordFieldsNeverTakeText()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Type, Ref("e4", ",\"text\":\"hunter3\""));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(WebAgentPolicy.PasswordRefusal, result.Text);
        Assert.Equal(0, tab.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task APageThatMovesFocusToAPasswordFieldGetsNoText()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Page.FocusTo = 5;

        var result = await rig.Call(WebToolSchemas.Type, Ref("e1", ",\"text\":\"lee@example.com\""));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(0, tab.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task TypingFillsAFieldWithoutTheUsersKeyboard()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Type, Ref("e1", ",\"text\":\"lee@example.com\""));

        Assert.StartsWith("Typed 15 characters into e1 in t1.", result.Text, StringComparison.Ordinal);
        var insert = Assert.Single(tab.Page.Calls, c => c.Method == "Input.insertText");
        Assert.Equal("lee@example.com", insert.Params["text"]!.GetValue<string>());
        Assert.Contains(rig.Activity, a => a.Step == "Filling in the email");
    }

    [Fact]
    public async Task EnterAfterTypingInAPostFormAsksFirst()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Type, Ref("e1", ",\"text\":\"lee@example.com\",\"submit\":true"));

        Assert.Equal(WebGuardCategory.Submit, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Equal(0, tab.Page.Count("Input.insertText"));
    }

    [Fact]
    public async Task EnterOnAFocusedSendButtonAsksAndOtherKeysRun()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await ReadInvite(rig);
        tab.Page.Focused = 4;

        var enter = await rig.Call(WebToolSchemas.Key, Tab(extra: ",\"keys\":\"enter\""));
        var moves = await rig.Call(WebToolSchemas.Key, Tab(extra: ",\"keys\":\"tab shift+tab\""));

        Assert.Equal(WebErrorCodes.Cancelled, enter.Error);
        Assert.Null(moves.Error);
        Assert.Equal(4, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task CtrlEnterCountsAsASend()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        var tab = await ReadInvite(rig);
        tab.Page.Focused = 10;

        await rig.Call(WebToolSchemas.Key, Tab(extra: ",\"keys\":\"ctrl+enter\""));

        Assert.Equal(WebGuardCategory.Send, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
    }

    [Fact]
    public async Task LettersNeverGoIntoAFocusedPasswordField()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Page.Focused = 5;

        var result = await rig.Call(WebToolSchemas.Key, Tab(extra: ",\"keys\":\"a b c\""));

        Assert.Equal(WebErrorCodes.Denied, result.Error);
        Assert.Equal(0, tab.Page.Count("Input.dispatchKeyEvent"));
    }

    [Fact]
    public async Task SelectChoosesAnOptionAndACheckboxIsClicked()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var chose = await rig.Call(WebToolSchemas.Select, Ref("e2", ",\"value\":\"Admin\""));
        var ticked = await rig.Call(WebToolSchemas.Select, Ref("e8", ",\"value\":\"true\""));

        Assert.Contains("Admin", chose.Text, StringComparison.Ordinal);
        Assert.Equal("Admin", tab.Page.Nodes.Single(n => n.Backend == 3).Chosen);
        Assert.Contains(rig.Activity, a => a.Step == "Choosing the role");
        Assert.Null(ticked.Error);
        Assert.Single(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task AStaleRefSaysToReadAgain()
    {
        using var rig = new WebAgentRig();
        await ReadInvite(rig);

        var result = await rig.Call(WebToolSchemas.Click, Ref("e99"));

        Assert.Equal(WebErrorCodes.StaleRef, result.Error);
    }

    // ---- The cross-origin rule ----

    [Fact]
    public async Task AnotherSiteAfterAReadAsksWithTheQuerySpelledOut()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Navigate = _ => Task.FromResult(WebNavigateAnswer.Deny);
        rig.Host.Conversation.Add("https://evil.test");
        await rig.InviteTab();
        await rig.Call(WebToolSchemas.Read, Tab(extra: ",\"mode\":\"text\""));

        var result = await rig.Call(WebToolSchemas.Open, "{\"url\":\"https://evil.test/c?q=42%20apples\"}");

        var ask = Assert.Single(rig.Asker.Of<WebNavigateAsk>());
        Assert.Equal(["https://a.test"], ask.From);
        Assert.Equal("https://evil.test/c?q=42%20apples", ask.Url);
        Assert.True(ask.CarriesPageText);
        Assert.Equal(new WebQueryField("q", "42 apples", true), Assert.Single(ask.Query));
        Assert.Equal(WebErrorCodes.NotApproved, result.Error);
        Assert.Empty(rig.Host.Opened);
    }

    [Fact]
    public async Task AlwaysForThisSitePairIsKept()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Navigate = _ => Task.FromResult(WebNavigateAnswer.AlwaysForPair);
        rig.Host.Conversation.Add("https://b.test");
        await ReadInvite(rig);

        var first = await rig.Call(WebToolSchemas.Open, "{\"url\":\"https://b.test/one\"}");
        var second = await rig.Call(WebToolSchemas.Open, "{\"url\":\"https://b.test/two\"}");

        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.Single(rig.Asker.Of<WebNavigateAsk>());
        Assert.True(rig.Grants.Pair("https://a.test", "https://b.test"));
    }

    [Fact]
    public async Task ALinkTheModelClicksToAnotherSiteAfterAReadIsStopped()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        tab.Page.OnCall = (method, p) =>
        {
            if (method == "Input.dispatchMouseEvent" && (string?)p["type"] == "mouseReleased") tab.Navigate("https://b.test/docs");
        };

        var click = await rig.Call(WebToolSchemas.Click, Ref("e5"));

        Assert.Equal(WebErrorCodes.Blocked, click.Error);
        Assert.Contains("web_open", click.Text, StringComparison.Ordinal);
        Assert.Equal("https://a.test/invite", tab.Url);
        tab.Page.OnCall = null;

        var open = await rig.Call(WebToolSchemas.Open, "{\"url\":\"https://b.test/docs\",\"tab\":\"t1\"}");

        Assert.Single(rig.Asker.Of<WebNavigateAsk>());
        Assert.Null(open.Error);
        Assert.Equal("https://b.test/docs", tab.Url);
    }

    [Fact]
    public async Task TheUsersOwnNavigationIsNeverJudged()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        var starting = tab.Fake.RaiseNavigationStarting("https://b.test/elsewhere", userInitiated: true);

        Assert.False(starting.Cancel);
    }

    [Fact]
    public async Task ASharedTabIsNotMovedToAnotherSiteWithoutTheCard()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Guard = _ => Task.FromResult(false);
        rig.Host.Conversation.Add("https://b.test");
        var tab = await rig.InviteTab(shared: true, kind: VisusTabKind.Browse);

        var result = await rig.Call(WebToolSchemas.Open, "{\"url\":\"https://b.test/x\",\"tab\":\"t1\"}");

        Assert.Equal(WebGuardCategory.NewSite, Assert.Single(rig.Asker.Of<WebGuardAsk>()).Category);
        Assert.Equal(WebErrorCodes.Cancelled, result.Error);
        Assert.Empty(tab.Navigations);
    }

    // ---- Waiting for the user ----

    [Fact]
    public async Task AnAskWaitsTwentyFiveSecondsThenTheSameCallJoinsTheSameCard()
    {
        using var rig = new WebAgentRig();
        var answer = new TaskCompletionSource<WebSiteAnswer>();
        rig.Asker.Site = _ => answer.Task;
        var tab = await ReadInvite(rig);

        var first = rig.Start(WebToolSchemas.Click, Ref("e9"));
        await PcTestWait.Until(() => rig.Clock.Pending >= 2);
        Assert.False(first.IsCompleted);
        rig.Clock.Advance(WebAgentEngine.AskWaitMs);
        var waited = await first;

        Assert.Equal(WebErrorCodes.WaitingForUser, waited.Error);
        Assert.False(waited.Failed);
        Assert.Equal(1, rig.Engine.PendingAsks);

        var second = rig.Start(WebToolSchemas.Click, Ref("e9"));
        await PcTestWait.Until(() => rig.Clock.Pending >= 2);
        answer.SetResult(WebSiteAnswer.AllowOnce);
        var done = await second;

        Assert.Null(done.Error);
        Assert.Single(rig.Asker.Of<WebSiteAsk>());
        Assert.Single(tab.Page.Mouse("mousePressed"));
    }

    [Fact]
    public async Task TheTurnsEndWithdrawsTheCard()
    {
        using var rig = new WebAgentRig();
        rig.Asker.Site = _ => new TaskCompletionSource<WebSiteAnswer>().Task;
        await ReadInvite(rig);
        var call = rig.Start(WebToolSchemas.Click, Ref("e9"));
        await PcTestWait.Until(() => rig.Clock.Pending >= 2);

        rig.Hub.EndTurn(WebAgentRig.Thread);

        Assert.True(rig.Asker.Tokens.Single().IsCancellationRequested);
        Assert.Equal(WebErrorCodes.Stopped, (await call).Error);
    }

    // ---- Stop, Pause and take-over ----

    [Fact]
    public async Task StopEndsTheRunningStepAndTheQueuedOnesForTheRestOfTheTurn()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        var hold = new TaskCompletionSource();
        tab.Page.Hold = ("Input.dispatchMouseEvent", hold.Task);
        var reads = tab.Page.Count("Accessibility.getFullAXTree");

        var running = rig.Start(WebToolSchemas.Click, Ref("e9"));
        await PcTestWait.Until(() => tab.Page.Count("Input.dispatchMouseEvent") == 1);
        var queued = rig.Start(WebToolSchemas.Read, Tab());
        Assert.False(queued.IsCompleted);

        rig.Hub.Stop();

        Assert.Equal(WebErrorCodes.Stopped, (await running).Error);
        Assert.Equal(WebErrorCodes.Stopped, (await queued).Error);
        Assert.Equal(reads, tab.Page.Count("Accessibility.getFullAXTree"));
        Assert.True(rig.Engine.StoppedForTurn);
        Assert.Equal(WebErrorCodes.Stopped, (await rig.Call(WebToolSchemas.Tabs)).Error);
        Assert.Contains(rig.Activity, a => a.Phase == WebAgentPhase.Stopped && a.Step == "Every web step ended. Ctrl+Alt+Esc does the same.");

        hold.SetResult();
        rig.Hub.EndTurn(WebAgentRig.Thread);
        Assert.Null((await rig.Call(WebToolSchemas.Tabs)).Error);
    }

    [Fact]
    public async Task TheStopChordAndTheChatsStopEndTheWebSteps()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab();

        rig.Run.StopKey();
        var afterChord = await rig.Call(WebToolSchemas.Tabs);
        rig.Hub.EndTurn(WebAgentRig.Thread);
        rig.Hub.StopTurn(WebAgentRig.Thread);
        var afterChat = await rig.Call(WebToolSchemas.Tabs);

        Assert.Equal(WebErrorCodes.Stopped, afterChord.Error);
        Assert.Equal(WebErrorCodes.Stopped, afterChat.Error);
    }

    [Fact]
    public async Task PauseHoldsTheQueueUntilResume()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab();
        rig.Hub.Pause();

        var call = rig.Start(WebToolSchemas.Read, Tab());
        Assert.True(await PcTestWait.StillWaiting(call));
        Assert.Contains(rig.Activity, a => a.Phase == WebAgentPhase.Paused && a.Step == "The page stays as it is");
        rig.Hub.Resume();
        var read = await call;

        Assert.Null(read.Error);
        Assert.Contains(rig.Activity, a => a.Step == "Resuming");
    }

    [Fact]
    public async Task APauseLongerThanTheWaitAnswersPaused()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab();
        rig.Hub.Pause();

        var call = rig.Start(WebToolSchemas.Read, Tab());
        await PcTestWait.Until(() => rig.Clock.Pending >= 1);
        rig.Clock.Advance(WebAgentEngine.HoldWaitMs);

        Assert.Equal(WebErrorCodes.Paused, (await call).Error);
    }

    [Fact]
    public async Task TheUserTakingOverPausesThatTabOnlyUntilResume()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        await rig.Call(WebToolSchemas.Click, Ref("e9"));
        rig.Host.Add("https://a.test/other");
        await rig.Call(WebToolSchemas.Tabs);

        tab.RaiseUserInput();
        var other = await rig.Call(WebToolSchemas.Read, Tab("t2"));
        var held = rig.Start(WebToolSchemas.Read, Tab());
        await PcTestWait.Until(() => rig.Clock.Pending >= 1);
        rig.Clock.Advance(WebAgentEngine.HoldWaitMs);
        var refused = await held;
        rig.Hub.Resume();
        var back = await rig.Call(WebToolSchemas.Read, Tab());

        Assert.Contains(rig.Activity, a => a.Phase == WebAgentPhase.TookOver && a.Tab == "t1");
        Assert.Null(other.Error);
        Assert.Equal(WebErrorCodes.TookOver, refused.Error);
        Assert.Null(back.Error);
    }

    [Fact]
    public async Task AClickInThePageSeenByTheOverlayIsATakeOver()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);
        await rig.Call(WebToolSchemas.Click, Ref("e9"));

        tab.Fake.EmitCdp("Runtime.bindingCalled", "{\"name\":\"" + WebAgentOverlay.Binding + "\",\"payload\":\"pointer\",\"executionContextId\":7}");

        Assert.Contains(rig.Activity, a => a.Phase == WebAgentPhase.TookOver);
    }

    [Fact]
    public async Task InputBeforeTheModelDrivesTheTabIsNoTakeOver()
    {
        using var rig = new WebAgentRig();
        var tab = await ReadInvite(rig);

        tab.RaiseUserInput();

        Assert.DoesNotContain(rig.Activity, a => a.Phase == WebAgentPhase.TookOver);
    }

    // ---- Previews ----

    [Fact]
    public async Task ScriptsRunOnlyInPreviewsAfterTheProjectAllowsThem()
    {
        using var rig = new WebAgentRig();
        await rig.InviteTab("https://shop.example.com/");
        rig.Host.Add("http://localhost:5173/", VisusTabKind.Preview, shared: true);
        await rig.Call(WebToolSchemas.Tabs);

        var site = await rig.Call(WebToolSchemas.Eval, Tab(extra: ",\"expression\":\"1+1\""));
        var preview = await rig.Call(WebToolSchemas.Eval, Tab("t2", ",\"expression\":\"1+1\""));
        var again = await rig.Call(WebToolSchemas.Eval, Tab("t2", ",\"expression\":\"2+2\""));

        Assert.Equal(WebErrorCodes.NotSupported, site.Error);
        Assert.Contains("42", preview.Text, StringComparison.Ordinal);
        Assert.Contains(WebAgentText.OpenTag, preview.Text, StringComparison.Ordinal);
        Assert.Null(again.Error);
        Assert.Equal("Let Claude Sonnet run scripts in localhost:5173?", Assert.Single(rig.Asker.Of<WebEvalAsk>()).Title);
        Assert.True(rig.Grants.Eval(rig.Host.Project!, "http://localhost:5173"));
    }

    [Fact]
    public async Task ADevicePresetSizesAPreview()
    {
        using var rig = new WebAgentRig();
        var preview = rig.Host.Add("http://localhost:5173/", VisusTabKind.Preview, shared: true);
        await rig.Call(WebToolSchemas.Tabs);

        var result = await rig.Call(WebToolSchemas.Device, Tab(extra: ",\"preset\":\"phone\""));

        Assert.Equal("t1 now shows 390 x 844 CSS px as a touch device.", result.Text);
        var size = Assert.Single(preview.Page.Calls, c => c.Method == "Emulation.setDeviceMetricsOverride");
        Assert.Equal(3, size.Params["deviceScaleFactor"]!.GetValue<double>());
    }

    // ---- The nth-pc server and the API providers ----

    [Fact]
    public async Task TheNthPcServerListsAndRunsTheWebToolsWithTactusOff()
    {
        using var rig = new WebAgentRig();
        rig.Host.Add("https://a.test/");
        var registration = new PcRegistration(WebAgentRig.Thread, ProviderKind.Claude, "claude-sonnet", true);
        var server = new PcControlMcpServer(registration, _ => null, () => PcSettings.Default) { Web = new WebMcpTools(() => rig.Hub) };
        var bare = new PcControlMcpServer(registration, _ => null, () => PcSettings.Default);

        var list = JsonNode.Parse((await server.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", CancellationToken.None))!)!;
        var init = JsonNode.Parse((await server.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{}}", CancellationToken.None))!)!;
        var call = JsonNode.Parse((await server.Handle("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"web_tabs\",\"arguments\":{}}}", CancellationToken.None))!)!;
        var none = JsonNode.Parse((await bare.Handle("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/list\"}", CancellationToken.None))!)!;

        var tools = list["result"]!["tools"]!.AsArray();
        Assert.Equal(16, tools.Count);
        Assert.All(tools, t => Assert.StartsWith("web_", t!["name"]!.GetValue<string>(), StringComparison.Ordinal));
        Assert.Equal(WebAgentText.Instructions, init["result"]!["instructions"]!.GetValue<string>());
        Assert.Contains("t1 (yours)", call["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(call["result"]!["isError"]!.GetValue<bool>());
        Assert.Empty(none["result"]!["tools"]!.AsArray());
    }

    [Fact]
    public async Task ApiProvidersGetTheToolsWhileVisusModelAccessIsOn()
    {
        using var rig = new WebAgentRig();
        var tools = new WebNativeTools(() => rig.Hub, WebAgentRig.Thread, "gpt");

        var definitions = tools.Definitions;
        var result = await tools.Execute(WebToolSchemas.Tabs, JSONValue.Parse("{}")!, CancellationToken.None);
        rig.Settings = rig.Settings with { Enabled = false };

        Assert.Equal(16, definitions.Count);
        Assert.Equal("Visus \u00B7 List tabs", result.Title);
        Assert.False(result.Failed);
        Assert.Empty(tools.Definitions);
        Assert.False(tools.Owns(WebToolSchemas.Read));
        Assert.Null(tools.Instructions);
    }

    [Fact]
    public void StandingGrantsAreKeptInTheirFileAndForgotten()
    {
        var file = Path.Combine(Path.GetTempPath(), "nth-w11-grants-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var grants = new WebAgentGrants(file);
            grants.GrantSite("https://a.test");
            grants.GrantPair("https://a.test", "https://b.test");

            var reopened = new WebAgentGrants(file);

            Assert.True(reopened.Site("https://a.test"));
            Assert.True(reopened.Pair("https://a.test", "https://b.test"));
            Assert.False(reopened.Pair("https://b.test", "https://a.test"));
            reopened.Clear();
            Assert.False(new WebAgentGrants(file).Site("https://a.test"));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
