using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// DESIGN 2.3, 5.10, 8.1: the private tab rules and where an address opens.
[Trait("Decision", "ED-NEW-web-w8a-1")]
public sealed class VisusTabRulesTests
{
    [Fact]
    public void Each_kind_has_its_profile()
    {
        Assert.Equal(WebProfileKind.Browse, VisusTabRules.Profile(VisusTabKind.Browse));
        Assert.Equal(WebProfileKind.Private, VisusTabRules.Profile(VisusTabKind.Private));
        Assert.Equal(WebProfileKind.Preview, VisusTabRules.Profile(VisusTabKind.Preview));
        Assert.Equal(WebProfileKind.Agent, VisusTabRules.Profile(VisusTabKind.Agent));
    }

    [Fact]
    public void A_private_tab_keeps_nothing_comes_back_never_and_is_never_seen()
    {
        Assert.False(VisusTabRules.RecordsHistory(VisusTabKind.Private));
        Assert.False(VisusTabRules.MayRemember(VisusTabKind.Private));
        Assert.False(VisusTabRules.IsPersisted(VisusTabKind.Private));
        Assert.False(VisusTabRules.MayReopen(VisusTabKind.Private));
        Assert.False(VisusTabRules.ModelMaySee(VisusTabKind.Private, sharedWithModel: true));
    }

    [Fact]
    public void Browse_and_preview_tabs_keep_history_and_choices_and_are_seen_only_when_shared()
    {
        foreach (var kind in new[] { VisusTabKind.Browse, VisusTabKind.Preview })
        {
            Assert.True(VisusTabRules.RecordsHistory(kind));
            Assert.True(VisusTabRules.MayRemember(kind));
            Assert.True(VisusTabRules.IsPersisted(kind));
            Assert.True(VisusTabRules.MayReopen(kind));
            Assert.False(VisusTabRules.ModelMaySee(kind, sharedWithModel: false));
            Assert.True(VisusTabRules.ModelMaySee(kind, sharedWithModel: true));
        }
    }

    [Fact]
    public void An_agent_tab_is_the_models_and_remembers_nothing()
    {
        Assert.False(VisusTabRules.RecordsHistory(VisusTabKind.Agent));
        Assert.False(VisusTabRules.MayRemember(VisusTabKind.Agent));
        Assert.True(VisusTabRules.ModelMaySee(VisusTabKind.Agent, sharedWithModel: false));
    }

    [Theory]
    [InlineData(VisusTabKind.Browse, "http://localhost:5173/", VisusTabKind.Preview)]
    [InlineData(VisusTabKind.Browse, "https://example.com/", VisusTabKind.Browse)]
    [InlineData(VisusTabKind.Preview, "https://example.com/", VisusTabKind.Browse)]
    [InlineData(VisusTabKind.Preview, "http://192.168.1.20:8080/", VisusTabKind.Preview)]
    [InlineData(VisusTabKind.Private, "http://localhost:5173/", VisusTabKind.Private)]
    [InlineData(VisusTabKind.Private, "https://example.com/", VisusTabKind.Private)]
    [InlineData(VisusTabKind.Agent, "http://localhost:5173/", VisusTabKind.Agent)]
    public void Private_and_agent_tabs_keep_their_kind_others_follow_the_host(VisusTabKind from, string url, VisusTabKind kind)
    {
        Assert.Equal(kind, VisusTabRules.KindForOpened(from, url));
        Assert.Equal(kind, VisusTabRules.KindForTyped(from, url));
    }
}
