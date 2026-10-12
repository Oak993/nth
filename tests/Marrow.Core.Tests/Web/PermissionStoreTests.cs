using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// DESIGN 5.5: permissions are denied by default and asked in nth's prompt ("localhost:5173 wants to use your camera"; Allow this
// time / Always on this site / Block; "Enter does nothing here. Esc blocks."), stored per profile and origin and listed with
// Remove; notifications are blocked in preview; the agent profile and artifacts never ask. ED-NEW-web-w8b-1: nth's store is the
// only memory (the engine keeps nothing), private tabs keep nothing, Block is kept like Always.
[Trait("Decision", "ED-NEW-web-w8b-1")]
[Trait("Feature", "W-8b")]
public sealed class PermissionStoreTests
{
    [Fact]
    public void The_prompt_words_are_the_canvas_words()
    {
        Assert.Equal("localhost:5173 wants to use your camera", SitePermissions.Title("localhost:5173", SitePermissionKind.Camera));
        Assert.Equal(("Allow this time", "Always on this site", "Block"), (SitePermissions.AllowOnceLabel, SitePermissions.AllowAlwaysLabel, SitePermissions.BlockLabel));
        Assert.Equal("Enter does nothing here. Esc blocks.", SitePermissions.Foot);
        Assert.Equal("Camera allowed this time", SitePermissions.Confirmation("localhost:5173", SitePermissionKind.Camera, SitePermissionAnswer.AllowOnce));
        Assert.Equal("Camera always allowed on localhost:5173", SitePermissions.Confirmation("localhost:5173", SitePermissionKind.Camera, SitePermissionAnswer.AllowAlways));
        Assert.Equal("Camera blocked on this site", SitePermissions.Confirmation("localhost:5173", SitePermissionKind.Camera, SitePermissionAnswer.Block));
    }

    [Fact]
    public void Buttons_are_named_in_full_for_a_screen_reader()
    {
        // 10.1: "The prompt's buttons are named in full ('Allow localhost:5173 to use your camera this time')".
        Assert.Equal("Allow localhost:5173 to use your camera this time", SitePermissions.AllowOnceName("localhost:5173", SitePermissionKind.Camera));
        Assert.StartsWith("Always allow localhost:5173 to ", SitePermissions.AllowAlwaysName("localhost:5173", SitePermissionKind.Camera), StringComparison.Ordinal);
        Assert.StartsWith("Block localhost:5173 from ", SitePermissions.BlockName("localhost:5173", SitePermissionKind.Camera), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_kind_the_design_lists_has_its_words()
    {
        foreach (var kind in Enum.GetValues<SitePermissionKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SitePermissions.Verb(kind)), kind.ToString());
            Assert.False(string.IsNullOrWhiteSpace(SitePermissions.Noun(kind)), kind.ToString());
            Assert.False(string.IsNullOrWhiteSpace(SitePermissions.Gerund(kind)), kind.ToString());
            Assert.DoesNotContain("{", SitePermissions.Title("a.test", kind), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Who_asks_and_who_may_keep_an_answer()
    {
        foreach (var kind in Enum.GetValues<SitePermissionKind>())
        {
            Assert.True(SitePermissions.Asks(WebProfileKind.Browse, kind));
            Assert.Equal(kind != SitePermissionKind.Notifications, SitePermissions.Asks(WebProfileKind.Preview, kind));
            Assert.Equal(kind != SitePermissionKind.Notifications, SitePermissions.Asks(WebProfileKind.Private, kind));
            Assert.False(SitePermissions.Asks(WebProfileKind.Agent, kind));
            Assert.False(SitePermissions.Asks(WebProfileKind.Artifacts, kind));
        }
        Assert.True(SitePermissions.OffersAlways(WebProfileKind.Browse));
        Assert.True(SitePermissions.OffersAlways(WebProfileKind.Preview));
        Assert.False(SitePermissions.OffersAlways(WebProfileKind.Private)); // private tabs leave nothing
        Assert.True(PermissionStore.Keeps(WebProfileKind.Browse));
        Assert.False(PermissionStore.Keeps(WebProfileKind.Private));
        Assert.False(PermissionStore.Keeps(WebProfileKind.Agent));
    }

    [Fact]
    public void Always_keeps_allow_block_keeps_block_and_this_time_keeps_nothing()
    {
        var store = new PermissionStore();
        var changes = 0;
        store.Changed += () => changes++;

        store.Record(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Camera, SitePermissionAnswer.AllowOnce);
        Assert.Null(store.StateOf(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Camera));
        Assert.Equal(0, changes);

        store.Record(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Camera, SitePermissionAnswer.AllowAlways);
        store.Record(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Microphone, SitePermissionAnswer.Block);
        Assert.Equal(SitePermissionState.Allow, store.StateOf(WebProfileKind.Browse, "HTTP://LOCALHOST:5173/path?q", SitePermissionKind.Camera));
        Assert.Equal(SitePermissionState.Block, store.StateOf(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Microphone));
        Assert.Equal(2, changes);

        // Per profile and per origin: the preview profile and another port know nothing of it.
        Assert.Null(store.StateOf(WebProfileKind.Preview, "http://localhost:5173", SitePermissionKind.Camera));
        Assert.Null(store.StateOf(WebProfileKind.Browse, "http://localhost:5174", SitePermissionKind.Camera));
        Assert.Null(store.StateOf(WebProfileKind.Browse, "https://localhost:5173", SitePermissionKind.Camera));

        // A later answer replaces the kept one.
        store.Record(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Camera, SitePermissionAnswer.Block);
        Assert.Equal(SitePermissionState.Block, store.StateOf(WebProfileKind.Browse, "http://localhost:5173", SitePermissionKind.Camera));
        Assert.Equal(2, store.For(WebProfileKind.Browse, "http://localhost:5173").Count);
    }

    [Fact]
    public void Private_agent_and_artifact_profiles_keep_nothing()
    {
        var store = new PermissionStore();
        Assert.False(store.Set(WebProfileKind.Private, "https://a.test", SitePermissionKind.Camera, SitePermissionState.Allow));
        Assert.False(store.Set(WebProfileKind.Agent, "https://a.test", SitePermissionKind.Camera, SitePermissionState.Allow));
        Assert.False(store.Set(WebProfileKind.Artifacts, "https://a.test", SitePermissionKind.Camera, SitePermissionState.Allow));
        store.Record(WebProfileKind.Private, "https://a.test", SitePermissionKind.Camera, SitePermissionAnswer.AllowAlways);
        Assert.Empty(store.All);
    }

    [Fact]
    public void Only_http_and_https_origins_are_kept()
    {
        var store = new PermissionStore();
        Assert.False(store.Set(WebProfileKind.Browse, "file:///C:/x.html", SitePermissionKind.Camera, SitePermissionState.Allow));
        Assert.False(store.Set(WebProfileKind.Browse, "javascript:alert(1)", SitePermissionKind.Camera, SitePermissionState.Allow));
        Assert.False(store.Set(WebProfileKind.Browse, "", SitePermissionKind.Camera, SitePermissionState.Allow));
        Assert.False(store.Set(WebProfileKind.Browse, null, SitePermissionKind.Camera, SitePermissionState.Allow));
        Assert.Equal("https://a.test", PermissionStore.Clean("https://A.Test/page#x"));
        Assert.Equal("http://localhost:5173", PermissionStore.Clean("http://localhost:5173/"));
        Assert.Null(PermissionStore.Clean("about:blank"));
    }

    [Fact]
    public void Remove_and_forget_take_answers_back()
    {
        var store = new PermissionStore();
        store.Set(WebProfileKind.Browse, "https://a.test", SitePermissionKind.Camera, SitePermissionState.Allow);
        store.Set(WebProfileKind.Browse, "https://a.test", SitePermissionKind.Location, SitePermissionState.Block);
        store.Set(WebProfileKind.Browse, "https://b.test", SitePermissionKind.Camera, SitePermissionState.Allow);
        store.Set(WebProfileKind.Preview, "http://localhost:3000", SitePermissionKind.Camera, SitePermissionState.Allow);

        Assert.True(store.Remove(WebProfileKind.Browse, "https://a.test", SitePermissionKind.Camera));
        Assert.False(store.Remove(WebProfileKind.Browse, "https://a.test", SitePermissionKind.Camera));
        Assert.Null(store.StateOf(WebProfileKind.Browse, "https://a.test", SitePermissionKind.Camera));

        store.Forget(WebProfileKind.Browse, "https://a.test");
        Assert.Empty(store.For(WebProfileKind.Browse, "https://a.test"));
        Assert.Single(store.For(WebProfileKind.Browse, "https://b.test"));

        store.Forget(WebProfileKind.Browse);
        Assert.DoesNotContain(store.All, entry => entry.Profile == WebProfileKind.Browse);
        Assert.Single(store.All); // the preview profile's answer stays
    }

    [Fact]
    public void Answers_survive_a_restart_and_a_damaged_or_edited_file_loses_only_what_breaks_the_rules()
    {
        var store = new PermissionStore();
        store.Set(WebProfileKind.Browse, "https://a.test", SitePermissionKind.Camera, SitePermissionState.Allow);
        store.Set(WebProfileKind.Preview, "http://localhost:5173", SitePermissionKind.Microphone, SitePermissionState.Block);
        var back = PermissionStore.FromJson(store.ToJson());
        Assert.Equal(store.All, back.All);

        Assert.Empty(PermissionStore.FromJson("{not json").All);
        Assert.Empty(PermissionStore.FromJson(null).All);
        var edited = """
            [
              {"Profile":"Agent","Origin":"https://a.test","Kind":"Camera","State":"Allow"},
              {"Profile":"Private","Origin":"https://a.test","Kind":"Camera","State":"Allow"},
              {"Profile":"Preview","Origin":"http://localhost:5173","Kind":"Notifications","State":"Allow"},
              {"Profile":"Browse","Origin":"file:///C:/x","Kind":"Camera","State":"Allow"},
              {"Profile":"Browse","Origin":"https://B.test/x","Kind":"Location","State":"Block"}
            ]
            """;
        var kept = Assert.Single(PermissionStore.FromJson(edited).All);
        Assert.Equal(new SitePermission(WebProfileKind.Browse, "https://b.test", SitePermissionKind.Location, SitePermissionState.Block), kept);
    }
}
