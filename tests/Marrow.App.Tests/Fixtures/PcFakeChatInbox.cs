// PORT-ONLY: Tactus (ED-NEW-pcctl-39). A chat inbox that records what the screen put in a chat's composer ("carry") or sent
// there ("send"); a test may make the sends fail, as for a chat that is gone or reverting.
using Marrow.App.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakeChatInbox : ITactusChatInbox
{
    public List<(string Op, string Thread, string Text)> Calls { get; } = [];

    public bool Sends { get; set; } = true;

    public bool Carry(string thread, string draft)
    {
        Calls.Add(("carry", thread, draft));
        return true;
    }

    public bool Send(string thread, string text)
    {
        Calls.Add(("send", thread, text));
        return Sends;
    }
}
