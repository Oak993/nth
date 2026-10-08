using Marrow.App.Port.MultiAccount.Link;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
[Collection("Headless")]
public sealed class LiveViewBindingTests(HeadlessSession headless)
{
    // LoginEnvironment.load() posts back to the UI thread: load it off that thread first, as the other headless rig tests do
    private static void LoadLogin() => System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();

    [Fact]
    public void The_composed_app_offers_the_link_model() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var reader = Marrow.App.Runtime.MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<Marrow.Services.Providers.PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig();
        rig.H.Main.OnMain(() =>
        {
            using var binding = new LiveViewBindingScope(rig.App);
            using var model = LinkComposerSeams.Offer.Current(rig.InitialThread.Id);
            Assert.NotNull(model);
        });
    });

    [Fact]
    public void The_composed_app_offers_opinion_entry_associations() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var reader = Marrow.App.Runtime.MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<Marrow.Services.Providers.PlanLimits?>(null));
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
    });
}
