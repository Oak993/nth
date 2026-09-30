using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

public sealed class ThreadRuntimeHydraBriefPresenceTests
{
    [Theory]
    [InlineData(false, false, true, 1)]
    [InlineData(false, true, true, 0)]
    [InlineData(true, false, false, 0)]
    [InlineData(true, true, false, 0)]
    [Trait("Req", "PR-RUNTIME-104")] // The re-announcement guard uses hasSentPrompts; Swift:731-736,2592-2594.
    public void A_head_brief_counts_as_a_sent_prompt_but_not_as_recall_history(
        bool fromHydra, bool isBrief, bool expectedPresence, int expectedHistory)
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var message = new UserMessage
            {
                Text = "head brief",
                HydraBrief = isBrief,
                HydraHeads = fromHydra ? new ValueList<long>([0]) : null,
            };
            var entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.User(message)));
            typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Entries))!
                .SetValue(h.Runtime, new ValueList<TimelineEntry>([entry]));
            Assert.Equal(expectedPresence, h.Runtime.HasSentPrompts);
            Assert.Equal(expectedHistory, h.Runtime.SentPrompts.Count);
        });
    }
}
