using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// AS-3a (DESIGN.md 15.3, 16): the app keeps a chat's Assistant marks through its saves, and a chat made from a hidden
// chat (a helper, a Hydra head, a continuation) is hidden from the Assistant too, so its words never reach the feed.
public sealed class ChatThreadAssistantFieldsAppTests
{
    private static readonly Guid AssistantID = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid MissionID = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    [Fact]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-1")]
    public void A_library_with_the_marks_loads_into_the_model_and_every_later_save_writes_them_back()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("marks"));
        var marked = AppModelSamples.Thread(project) with { AssistantID = AssistantID, MissionID = MissionID };
        var hidden = AppModelSamples.Thread(project) with { HiddenFromAssistant = true };
        var plain = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [marked, hidden, plain]);
        var model = h.MakeModel();

        h.Main.OnMain(() =>
        {
            Assert.Equal((AssistantID, MissionID, false), (model.Thread(marked.Id)!.AssistantID, model.Thread(marked.Id)!.MissionID, model.Thread(marked.Id)!.HiddenFromAssistant));
            Assert.True(model.Thread(hidden.Id)!.HiddenFromAssistant);
            Assert.Equal((null, null, false), (model.Thread(plain.Id)!.AssistantID, model.Thread(plain.Id)!.MissionID, model.Thread(plain.Id)!.HiddenFromAssistant));
            model.UpdateThread(marked.Id, thread => thread with { IsPinned = true });
            model.UpdateThread(hidden.Id, thread => thread with { Title = "Renamed", HasCustomTitle = true });
            model.UpdateThread(plain.Id, thread => thread with { IsPinned = true });
        });
        h.Main.AdvanceMilliseconds(400);
        DiskWriter.Shared.WaitForQueuedWrites();

        var saved = AppModelHarness.LoadedLibrary().Threads;
        Assert.Equal((AssistantID, MissionID, true), (saved.Single(t => t.Id == marked.Id).AssistantID, saved.Single(t => t.Id == marked.Id).MissionID, saved.Single(t => t.Id == marked.Id).IsPinned));
        Assert.True(saved.Single(t => t.Id == hidden.Id).HiddenFromAssistant);
        Assert.Equal((null, null, false), (saved.Single(t => t.Id == plain.Id).AssistantID, saved.Single(t => t.Id == plain.Id).MissionID, saved.Single(t => t.Id == plain.Id).HiddenFromAssistant));
        var text = Encoding.UTF8.GetString(AppModelHarness.LibraryBytes());
        Assert.Equal(1, Occurrences(text, "\"assistantID\""));
        Assert.Equal(1, Occurrences(text, "\"missionID\""));
        Assert.Equal(1, Occurrences(text, "\"hiddenFromAssistant\""));
    }

    [Theory]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-2")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_helper_of_a_chat_is_hidden_from_the_assistant_exactly_when_its_chat_is(bool hiddenParent)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("helper"));
        var parent = AppModelSamples.Thread(project) with { HiddenFromAssistant = hiddenParent };
        h.WriteLibrary([project], [parent]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model); // A missing test CLI path prevents this send from invoking a real provider.

        h.Main.OnMain(() =>
        {
            var helper = Assert.IsType<ChatThread>(model.SpawnSubagent(parent.Id, "Review", "Inspect the change."));
            Assert.Equal(hiddenParent, helper.HiddenFromAssistant);
            Assert.Null(helper.AssistantID);
            Assert.Null(helper.MissionID);
        });
    }

    [Theory]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-2")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_hydra_head_of_a_chat_is_hidden_from_the_assistant_exactly_when_its_lead_is(bool hiddenLead)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("head"));
        var lead = AppModelSamples.Thread(project) with { HiddenFromAssistant = hiddenLead };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);

        var head = h.Main.OnMain(() => model.SpawnDroppyHead(lead.Id, "task",
            HydraHeadInfo.Origin.Delegated, [], null, null, null, null, static (_, _) => "brief"));

        Assert.NotNull(head);
        Assert.Equal(hiddenLead, head!.HiddenFromAssistant);
        Assert.Equal(hiddenLead, model.Thread(head.Id)!.HiddenFromAssistant);
    }

    [Theory]
    [Trait("Feature", "AS-3a")]
    [Trait("Decision", "ED-NEW-dextra-as3a-2")]
    [InlineData(true)]
    [InlineData(false)]
    public void A_continuation_of_a_chat_is_hidden_from_the_assistant_exactly_when_its_source_is(bool hiddenSource)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("forward"));
        var source = AppModelSamples.Thread(project) with { HiddenFromAssistant = hiddenSource };
        h.WriteLibrary([project], [source]);
        AppModelHarness.WriteDocument(source.Id);
        var model = h.MakeModel();

        var forwarded = h.Main.Loop.Run(() => model.ForwardThread(source.Id), AppModelHarness.Bound);

        Assert.NotNull(forwarded);
        Assert.Equal(hiddenSource, forwarded!.HiddenFromAssistant);
        Assert.Null(forwarded.AssistantID);
        Assert.Null(forwarded.MissionID);
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
