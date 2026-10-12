// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1, 15.1; unit AS-2; ED-NEW-dextra-as2-6, -8). No Swift twin.
// The Assistant's live feed as the app holds it: one bus, one set of taps. Both are cheap to make and do nothing until
// something subscribes (the Assistant's controller, when it exists), so an app without the Assistant pays nothing.
using Marrow.App.Assistant;

namespace Marrow.App;

public sealed partial class AppModel
{
    private AssistantFeedTaps? assistantTaps;
    private bool assistantFeedStopped;

    /// <summary>The read-only feed of everything the Assistant may watch (5.1). Subscribe to it; publish only through the taps.</summary>
    public AssistantEventBus AssistantEvents { get; } = new();

    /// <summary>The one-line taps the app's own code calls (ThreadRuntime, AppModel, MultiAccountRouting).</summary>
    internal AssistantFeedTaps AssistantTaps
    {
        get
        {
            if (assistantTaps is null)
            {
                assistantTaps = new AssistantFeedTaps(this, AssistantEvents);
                if (assistantFeedStopped) assistantTaps.Dispose();
            }
            return assistantTaps;
        }
    }

    private void DisposeAssistantFeed()
    {
        if (assistantFeedStopped) return;
        assistantFeedStopped = true;
        assistantTaps?.Dispose();
        AssistantEvents.Dispose();
    }
}
