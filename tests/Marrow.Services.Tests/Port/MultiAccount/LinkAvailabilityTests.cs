using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class LinkAvailabilityTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("out")]
    [InlineData("off")]
    public void An_unavailable_link_is_hidden_from_the_port_while_the_explicit_tap_intent_is_retained(string why)
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        ILinkPort port = engine;
        var a = h.Add(); port.Link(h.Thread, a.Id);
        Assert.Equal(a.Id, Assert.Single(port.Linked(h.Thread)));
        if (why == "active") h.ChangeActive(a.Id);
        else h.Change(why == "out" ? a with { HasRoom = false } : a with { Enabled = false });
        Assert.Empty(port.Linked(h.Thread));
        if (why == "active") h.ChangeActive(h.Rows[0].Id); else h.Change(a);
        Assert.Equal(a.Id, Assert.Single(port.Linked(h.Thread)));
        Assert.Empty(h.Flights);
    }

    [Fact]
    public void An_explicit_unlink_while_the_account_is_out_removes_the_retained_intent()
    {
        var h = new ConversationHeadHarness();
        using var engine = new LinkEngine(h);
        var a = h.Add(); engine.Link(h.Thread, a.Id);
        h.Change(a with { HasRoom = false });
        engine.Unlink(h.Thread, a.Id); h.Change(a);
        Assert.Empty(engine.Linked(h.Thread));
    }
}
