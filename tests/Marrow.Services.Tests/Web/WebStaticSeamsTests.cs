using Marrow.Core.NthUi;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W10, the seams the settings bind (E:/Droppy/work/design/web-surfaces/DESIGN.md 9; ED-NEW-web-w10-3): "Keep at most N live tabs" reaches every surface
// that was not given a number, "MCP Apps" decides whether nth says it hosts them, and the teaching reads the live-answers switch.
[Collection(WebStaticSeamsCollection.Name)]
[Trait("Decision", "ED-NEW-web-w10-3")]
public sealed class WebStaticSeamsTests
{
    [Fact]
    public void A_surface_with_no_number_of_its_own_follows_the_live_tabs_setting_and_a_number_set_on_it_wins()
    {
        var before = WebSurfaceOptions.DefaultMaxLiveViews;
        try
        {
            Assert.Equal(4, new WebSurfaceOptions().MaxLiveViews);
            var wanted = 6;
            WebSurfaceOptions.DefaultMaxLiveViews = () => wanted;
            var follows = new WebSurfaceOptions();
            Assert.Equal(6, follows.MaxLiveViews);
            wanted = 3;
            Assert.Equal(3, follows.MaxLiveViews); // read on every use: a change takes effect at once
            Assert.Equal(6, follows.MaxHeldViews); // the held budget is twice the live one
            follows.MaxLiveViews = 5;
            wanted = 8;
            Assert.Equal(5, follows.MaxLiveViews);
        }
        finally
        {
            WebSurfaceOptions.DefaultMaxLiveViews = before;
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(99, 8)]
    public void The_setting_is_brought_into_two_to_eight(int setting, int expected)
    {
        var before = WebSurfaceOptions.DefaultMaxLiveViews;
        try
        {
            WebSurfaceOptions.DefaultMaxLiveViews = () => setting;
            Assert.Equal(expected, new WebSurfaceOptions().MaxLiveViews);
        }
        finally
        {
            WebSurfaceOptions.DefaultMaxLiveViews = before;
        }
    }

    [Fact]
    public void A_surface_made_with_the_setting_bound_holds_at_most_that_many_live_views()
    {
        var before = WebSurfaceOptions.DefaultMaxLiveViews;
        try
        {
            WebSurfaceOptions.DefaultMaxLiveViews = () => 2;
            using var rig = new WebRig();
            Assert.Equal(2, rig.Surface.Options.MaxLiveViews);
        }
        finally
        {
            WebSurfaceOptions.DefaultMaxLiveViews = before;
        }
    }

    [Fact]
    public void Nth_says_it_hosts_MCP_Apps_unless_the_switch_is_off()
    {
        var before = MCPWire.AdvertiseApps;
        try
        {
            var on = MCPWire.ClientCapabilities();
            Assert.NotNull(on["extensions"]?[MCPWire.UiExtension]);
            Assert.NotNull(on["resources"]);

            MCPWire.AdvertiseApps = () => false;
            var off = MCPWire.ClientCapabilities();
            Assert.Null(off["extensions"]);
            Assert.NotNull(off["resources"]); // nothing else about nth's capabilities changes
            Assert.NotNull(MCPWire.InitializeParams()["capabilities"]);
            Assert.Null(MCPWire.InitializeParams()["capabilities"]!["extensions"]);
        }
        finally
        {
            MCPWire.AdvertiseApps = before;
        }
    }

    [Fact]
    public void The_teaching_reads_the_live_answers_switch_when_a_session_starts()
    {
        var offered = new[] { "artifact_create", "artifact_update", "ui_show", "ui_update", "ui_reference" };
        Assert.True(UiTeaching.Options(offered).LiveAnswers);
        using (UiTeaching.LiveAnswers.Override(static () => false))
        {
            var options = UiTeaching.Options(offered);
            Assert.False(options.LiveAnswers);
            Assert.Contains(NthUiTeaching.LiveOff, UiTeaching.SystemBlock(offered), StringComparison.Ordinal);
            Assert.Contains(NthUiTeaching.LiveOff, UiTeaching.Instructions(offered), StringComparison.Ordinal);
        }
        Assert.DoesNotContain(NthUiTeaching.LiveOff, UiTeaching.SystemBlock(offered), StringComparison.Ordinal);
    }

    [Fact]
    public void The_visual_answers_setting_reaches_the_teaching()
    {
        var offered = new[] { "ui_show", "ui_update" };
        Assert.Equal(VisualAnswerLevel.Balanced, UiTeaching.Options(offered).Visual);
        using (UiTeaching.Visual.Override(static () => VisualAnswerLevel.More))
        {
            Assert.Equal(VisualAnswerLevel.More, UiTeaching.Options(offered).Visual);
            Assert.Contains("The reader likes them", UiTeaching.Instructions(offered), StringComparison.Ordinal);
        }
    }
}
