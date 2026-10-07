using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Port.MultiAccount.Link;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.App.Port.MultiAccount.Usage;

namespace Marrow.App.Tests.Fixtures;

internal sealed class LiveViewBindingScope : IDisposable
{
    private readonly Action restore;
    internal LiveViewBindingScope(AppModel app)
    {
        var ports = ThreadPortSeams.Offer.Current;
        var usage = MultiAccountUsageSeams.Offer.Current;
        var cards = MultiAccountCardSeams.Offer.Current;
        var links = LinkComposerSeams.Offer.Current;
        var opinions = OpinionTimelineSeams.Offer.Current;
        var composer = AskOthersComposerSeams.Offer.Current;
        restore = () =>
        {
            ThreadPortSeams.Offer.SetProcessDefault(ports);
            MultiAccountUsageSeams.Offer.SetProcessDefault(usage);
            MultiAccountCardSeams.Offer.SetProcessDefault(cards);
            LinkComposerSeams.Offer.SetProcessDefault(links);
            OpinionTimelineSeams.Offer.SetProcessDefault(opinions);
            AskOthersComposerSeams.Offer.SetProcessDefault(composer);
            app.DisposeThreadPorts();
        };
        ThreadPortSeams.Bind(app);
    }
    public void Dispose() => restore();
}
