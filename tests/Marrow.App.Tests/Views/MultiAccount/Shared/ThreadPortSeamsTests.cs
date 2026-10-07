using Marrow.App.Port.MultiAccount.Thread;
using Marrow.Core.Models;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.MultiAccount.Shared;

// MA-10 (step 3b, job 0): where the popover, the cards, link up and "Ask others" get their ports. Unbound until the wiring job binds the running app, so a test of another view never builds the app's accounts (the pattern of ComposerCapsuleSeams).
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ThreadPortSeamsTests
{
    [Fact]
    public void Unbound_there_are_no_ports_and_nothing_is_built()
    {
        var offered = ThreadPortSeams.Offer.Current;
        Assert.Null(offered.Account(Guid.NewGuid(), ProviderKind.Claude));
        Assert.Null(offered.Cards);
        Assert.Null(offered.Links);
        Assert.Null(offered.Opinions);
    }

    [Fact]
    public void A_test_overrides_it_for_its_own_flow_and_the_default_comes_back_after()
    {
        var rig = new AccountsRig();
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        var (cards, links, opinions) = (new FakeCardPort(), new FakeLinkPort(), new FakeOpinionsPort());
        using (ThreadPortSeams.Offer.Override(new ThreadPorts((_, _) => port, cards, links, opinions)))
        {
            var offered = ThreadPortSeams.Offer.Current;
            Assert.Same(port, offered.Account(Guid.NewGuid(), ProviderKind.Claude));
            Assert.Same(cards, offered.Cards);
            Assert.Same(links, offered.Links);
            Assert.Same(opinions, offered.Opinions);
        }
        Assert.Null(ThreadPortSeams.Offer.Current.Cards);
    }
}
