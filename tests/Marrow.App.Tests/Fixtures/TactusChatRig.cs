using Marrow.App.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The Tactus chat mirror on fakes: the shared parts, the feed, fake chats and the bridge.</summary>
public sealed record TactusChatRig(PcManualClock Clock, PcRunControl Control, PcPauseControl Pause, PcStopControl Stop, PcAskBroker Broker,
    PcChatFeed Feed, TactusFakeChats Chats, TactusChatBridge Bridge) : IDisposable
{
    public void Dispose()
    {
        Bridge.Dispose();
        Feed.Dispose();
        Broker.Dispose();
        Pause.Dispose();
    }
}
