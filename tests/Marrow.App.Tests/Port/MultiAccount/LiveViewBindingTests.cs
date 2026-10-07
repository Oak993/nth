using Marrow.App.Port.MultiAccount.Link;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LiveViewBindingTests
{
    [Fact]
    public void The_composed_app_offers_the_link_model()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            using var binding = new LiveViewBindingScope(rig.App);
            using var model = LinkComposerSeams.Offer.Current(rig.InitialThread.Id);
            Assert.NotNull(model);
        });
    }

    [Fact]
    public void The_composed_app_offers_opinion_entry_associations()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            using var binding = new LiveViewBindingScope(rig.App);
            var context = OpinionTimelineSeams.Offer.Current(rig.InitialThread.Id);
            Assert.NotNull(context);
            Assert.Null(context.PromptEntry(Guid.NewGuid()));
            Assert.Null(context.CombinedEntry(Guid.NewGuid()));
            Assert.Null(context.HeadError(Guid.NewGuid(), Guid.NewGuid()));
        });
    }
}
