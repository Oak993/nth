using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.4 "Taint is cleared only by the owner's own message ... or by the mission ending";
// ED-NEW-dextra-as7-14): a restart is neither, and the home chat's provider session resumes with what it read, so its
// taint is saved at every change and comes back when nth starts again. A record nth cannot read makes every home chat
// count as tainted until the owner's message (and is healed); one a newer nth wrote is left untouched.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-14")]
public sealed class HomeTaintRestartTests
{
    [Fact]
    public void The_home_chats_taint_comes_back_after_a_restart_and_only_the_owners_message_clears_it()
    {
        ChatThread home;
        JsonObject saved;
        using (var before = new DextraTaintRig())
        {
            home = before.Home();
            before.Tool(home.Id, ToolCall.Kind.Web, "https://hostile.example/page");
            saved = before.Saved[^1];
            Assert.Single(before.Saved);
            before.Tool(home.Id, ToolCall.Kind.Web, "https://hostile.example/page");
            Assert.Single(before.Saved); // nothing changed, nothing written
        }

        using var after = new DextraTaintRig(load: () => saved, chats: [home]);
        Assert.Equal([ContentOrigin.Web], after.Controller.HomeTaint(home.Id).Taint.Sources.Select(s => s.Origin));
        Assert.True(after.Controller.PolicyContext(DextraTaintRig.AsChat(home)).Tainted);

        // A child's report delivered into it is no owner's message; the owner's own message is, and it is saved.
        after.Controller.AbsorbHome(home.Id, Taint.From(ContentOrigin.Email, "a reply"));
        Assert.True(after.Controller.HomeTaint(home.Id).IsTainted);
        after.Controller.OwnerMessage(home.Id);
        Assert.False(after.Controller.HomeTaint(home.Id).IsTainted);
        Assert.Empty(HomeTaintJournal.FromJson(after.Saved[^1])!);
    }

    [Fact]
    public void A_record_nth_cannot_read_taints_every_home_chat_until_the_owners_message_and_is_written_again()
    {
        using var first = new DextraTaintRig();
        var home = first.Home();
        var other = first.Home();
        var mission = first.Mission();
        var lead = first.Chats[mission.LeadThread!.Value];

        using var after = new DextraTaintRig(load: () => throw new InvalidDataException("damaged"), chats: [home, other, lead]);
        Assert.Equal(AssistantController.UnknownAfterRestart, after.Controller.HomeTaint(home.Id).Taint);
        Assert.True(after.Controller.HomeTaint(other.Id).IsTainted);
        Assert.False(after.Controller.HomeTaint(lead.Id).IsTainted);
        var healed = HomeTaintJournal.FromJson(after.Saved[^1])!;
        Assert.Equal(new[] { home.Id, other.Id }.Order(), healed.Keys.Order());
        after.Controller.OwnerMessage(home.Id);
        Assert.False(after.Controller.HomeTaint(home.Id).IsTainted);
        Assert.True(after.Controller.HomeTaint(other.Id).IsTainted);

        using var damaged = new DextraTaintRig(load: () => new JsonObject { ["version"] = 1, ["chats"] = "nonsense" }, chats: [home]);
        Assert.True(damaged.Controller.HomeTaint(home.Id).IsTainted);
    }

    [Fact]
    public void A_record_a_newer_nth_wrote_is_left_untouched_and_the_taint_still_holds_in_this_session()
    {
        using var first = new DextraTaintRig();
        var home = first.Home();
        var newer = new JsonObject { ["version"] = HomeTaintJournal.Version + 1, ["chats"] = new JsonObject(), ["future"] = true };

        using var after = new DextraTaintRig(load: () => newer, chats: [home]);
        Assert.True(after.Controller.HomeTaint(home.Id).IsTainted);
        after.Tool(home.Id, ToolCall.Kind.Web, "https://hostile.example/page");
        after.Controller.OwnerMessage(home.Id);
        Assert.Empty(after.Saved);
        Assert.False(after.Controller.HomeTaint(home.Id).IsTainted);
    }

    [Fact]
    public void A_save_that_fails_is_reported_and_the_taint_stands()
    {
        using var rig = new DextraTaintRig();
        var faults = new List<string>();
        rig.Controller.Runner.Faulted += faults.Add;
        var home = rig.Home();
        rig.SaveFails = true;
        rig.Tool(home.Id, ToolCall.Kind.Web, "https://hostile.example/page");
        Assert.True(rig.Controller.HomeTaint(home.Id).IsTainted);
        Assert.Contains(faults, f => f.StartsWith("The home chats' taint could not be saved", StringComparison.Ordinal));
    }
}
