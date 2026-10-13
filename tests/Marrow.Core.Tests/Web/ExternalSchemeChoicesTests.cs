using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// DESIGN 8.3: "Remember for this site" for mailto and tel, per origin and profile, never for private or agent tabs.
[Trait("Decision", "ED-NEW-web-w8a-1")]
public sealed class ExternalSchemeChoicesTests
{
    [Fact]
    public void A_remembered_answer_is_per_site_scheme_and_profile()
    {
        var choices = new ExternalSchemeChoices();
        var changes = 0;
        choices.Changed += () => changes++;
        Assert.True(choices.Remember(VisusTabKind.Browse, "https://example.com", "MAILTO"));
        Assert.True(choices.Remember(VisusTabKind.Browse, "https://example.com", "mailto"));
        Assert.Equal(1, changes);
        Assert.True(choices.IsAllowed(VisusTabKind.Browse, "https://example.com", "mailto"));
        Assert.False(choices.IsAllowed(VisusTabKind.Browse, "https://other.example", "mailto"));
        Assert.False(choices.IsAllowed(VisusTabKind.Browse, "https://example.com", "tel"));
        Assert.False(choices.IsAllowed(VisusTabKind.Preview, "https://example.com", "mailto"));
        Assert.False(choices.IsAllowed(VisusTabKind.Private, "https://example.com", "mailto"));
    }

    [Theory]
    [InlineData(VisusTabKind.Private, "mailto")]
    [InlineData(VisusTabKind.Agent, "mailto")]
    [InlineData(VisusTabKind.Browse, "zoommtg")]
    [InlineData(VisusTabKind.Browse, "ms-msdt")]
    public void Private_and_agent_tabs_and_other_schemes_are_never_remembered(VisusTabKind kind, string scheme)
    {
        var choices = new ExternalSchemeChoices();
        Assert.False(choices.Remember(kind, "https://example.com", scheme));
        Assert.Empty(choices.All);
    }

    [Fact]
    public void Forgetting_removes_a_site_or_a_whole_profile()
    {
        var choices = new ExternalSchemeChoices();
        choices.Remember(VisusTabKind.Browse, "https://a.example", "mailto");
        choices.Remember(VisusTabKind.Browse, "https://b.example", "tel");
        choices.Remember(VisusTabKind.Preview, "http://localhost:5173", "mailto");
        choices.Forget(WebProfileKind.Browse, "https://a.example");
        Assert.Equal(2, choices.All.Count);
        choices.Forget(WebProfileKind.Browse);
        Assert.Equal(WebProfileKind.Preview, Assert.Single(choices.All).Profile);
    }

    [Fact]
    public void The_answers_round_trip_and_an_edited_file_cannot_widen_them()
    {
        var choices = new ExternalSchemeChoices();
        choices.Remember(VisusTabKind.Browse, "https://a.example", "mailto");
        Assert.Equal(choices.All, ExternalSchemeChoices.FromJson(choices.ToJson()).All);
        var planted = """[{"Profile":"Browse","Origin":"https://a.example","Scheme":"ms-msdt"},{"Profile":"Private","Origin":"https://a.example","Scheme":"mailto"},{"Profile":"Browse","Origin":"https://a.example","Scheme":"TEL"}]""";
        var read = ExternalSchemeChoices.FromJson(planted);
        Assert.Equal(new ExternalSchemeChoice(WebProfileKind.Browse, "https://a.example", "tel"), Assert.Single(read.All));
        Assert.Empty(ExternalSchemeChoices.FromJson("[oops").All);
    }
}
