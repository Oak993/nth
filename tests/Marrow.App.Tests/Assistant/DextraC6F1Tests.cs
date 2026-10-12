using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Providers;
using System.Reflection;

namespace Marrow.App.Tests.Assistant;

// Regression for the accepted Dextra C6-F1 finding. Offline only.
public sealed class DextraC6F1Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-1")]
    public void Continuation_of_an_inherited_hidden_head_stays_hidden()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("c6-forward"));
        var parent = AppModelSamples.Thread(project) with { HiddenFromAssistant = true };
        var head = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id, HiddenFromAssistant = false };
        h.WriteLibrary([project], [parent, head]);
        AppModelHarness.WriteDocument(head.Id);
        var model = h.MakeModel();
        Assert.True(FeedChatFacts.From(head, model.Thread).Hidden);
        var forwarded = h.Main.Loop.Run(() => model.ForwardThread(head.Id), AppModelHarness.Bound);
        Assert.NotNull(forwarded);
        var facts = h.Main.OnMain(() => FeedChatFacts.From(forwarded!, model.Thread));
        Assert.True(facts.Hidden, "The source is hidden by its parent; the continuation lost that privacy state.");
    }
}
