using System.Net;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1 "Creating a duty always asks", 7.2, 15.5; ED-NEW-dextra-as10-12): schedule_duty, watch_folder and
// watch_page. The card shows what the duty will be, and what an edit changes; nothing is made until the owner says yes; a bad schedule
// is told on the card and refused after it; a folder he approves is granted; a page is read once first and refused with words when it
// needs the browser or a sign-in.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-12")]
public sealed class DutyToolsTests
{
    private static readonly Guid Thread = Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static AssistantChat Chat() => new(Thread, "dextra", null, AssistantChatRole.Home, ProviderKind.Claude, "model-a");

    private static AssistantToolCall Call(string tool, string arguments) =>
        new(Chat(), AssistantToolCatalog.Find(tool)!, JSONValue.Parse(arguments)!, new Classification(ActionClass.WriteElsewhere, "changes the assistant's standing configuration"),
            new PolicyDecision(Verdict.Ask, PolicyRule.AlwaysConfirmed, "Asks: creating a duty always asks"), AssistantRoute.Mcp);

    private static (DutyTools Tools, DutyHostRig Rig) Make(WebWatcher? web = null)
    {
        var rig = new DutyHostRig();
        rig.Host.Start();
        var tools = new DutyTools(rig.Host.Scheduler, new DutyToolsOptions
        {
            ZoneId = () => "UTC",
            AssistantName = id => id == "dextra" ? "Dextra" : id,
            ApproveFolder = rig.Host.Switches.ApproveFolder,
            Web = web,
            Time = rig.Clock,
            ChatProject = _ => rig.Project.Id,
        });
        return (tools, rig);
    }

    private static Dictionary<string, string> Inputs(IReadOnlyList<DecisionInput>? inputs) => inputs!.ToDictionary(i => i.Name, i => i.Value);

    [Fact]
    public void Another_tool_has_no_duty_card()
    {
        var (tools, rig) = Make();
        using (rig) Assert.Null(tools.Describe(Call("start_mission", """{"title":"x","brief":"y"}""")));
    }

    [Fact]
    public void The_card_for_a_new_duty_shows_its_trigger_what_it_does_its_grants_its_budget_and_how_often()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var inputs = Inputs(tools.Describe(Call("schedule_duty", """{"title":"Morning digest","instructions":"Compose the digest.","schedule":"weekdays 08:00"}""")));
            Assert.Equal("Morning digest", inputs["Duty"]);
            Assert.Equal("Weekdays at 08:00 (UTC)", inputs["Trigger"]);
            Assert.Equal("Compose the digest.", inputs["What it does"]);
            Assert.Equal("Dextra", inputs["Assistant"]);
            Assert.Equal("Inherits Dextra's grants and can only narrow them", inputs["Grants"]);
            Assert.Equal("The plan's own limits", inputs["Budget"]);
            Assert.Equal("Silent unless changed", inputs["Report"]);
            Assert.Equal("About 1 a day", inputs["How often"]);
            Assert.Equal("Dextra asks you to make it", inputs["Made by"]);
            Assert.Empty(rig.Host.Scheduler.Duties); // describing makes nothing
        }
    }

    [Fact]
    public void A_yes_makes_the_duty_in_the_chats_project_owned_by_its_assistant_and_the_first_one_brings_the_card()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var delivered = new List<string>();
            var result = tools.Create(Call("schedule_duty", """{"title":"Morning digest","instructions":"Compose the digest.","schedule":"daily 08:00","silent":false}"""), delivered.Add);
            Assert.False(result.IsError, result.Text);
            Assert.Contains("“Morning digest” is set: Every day at 08:00. Next: Today 08:00.", result.Text, StringComparison.Ordinal);
            var duty = Assert.Single(rig.Host.Scheduler.Duties).Duty;
            Assert.Equal((rig.Project.Id, "dextra", "dextra", ReportStyle.DigestOnly), (duty.ProjectId, duty.AssistantId, duty.CreatedBy, duty.Report));
            Assert.True(rig.Host.FirstCard.Visible);
            Assert.Empty(delivered); // a time duty is made at once; there is nothing to come back later
        }
    }

    [Fact]
    public void A_duty_with_the_same_title_is_a_change_the_card_says_what_changes_and_the_id_stays()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            tools.Create(Call("schedule_duty", """{"title":"Morning digest","instructions":"Compose the digest.","schedule":"daily 08:00"}"""), _ => { });
            var id = rig.Host.Scheduler.Duties[0].Duty.Id;
            var change = Call("schedule_duty", """{"title":"morning digest","instructions":"Compose it with the usage numbers.","schedule":"daily 09:30"}""");
            var inputs = Inputs(tools.Describe(change));
            Assert.Equal("Every day at 09:30 (UTC) (was: Every day at 08:00 (UTC))", inputs["Trigger"]);
            Assert.Equal("Compose it with the usage numbers. (was: Compose the digest.)", inputs["What it does"]);
            Assert.Equal("Dextra asks you to change it", inputs["Made by"]);
            var result = tools.Create(change, _ => { });
            Assert.False(result.IsError, result.Text);
            Assert.Contains("is changed", result.Text, StringComparison.Ordinal);
            var duty = Assert.Single(rig.Host.Scheduler.Duties).Duty;
            Assert.Equal(id, duty.Id);
            Assert.Equal("Every day at 09:30", Marrow.Core.Port.Assistant.ScheduleText.Describe(((TimeTrigger)duty.Trigger).Schedule));
        }
    }

    [Theory]
    [InlineData("""{"title":"X","instructions":"Y","schedule":"whenever"}""")]
    [InlineData("""{"title":"X","instructions":"Y","schedule":"every 2 minutes"}""")]
    [InlineData("""{"title":"X","instructions":"  ","schedule":"daily 08:00"}""")]
    [InlineData("""{"title":" ","instructions":"Y","schedule":"daily 08:00"}""")]
    public void A_duty_that_cannot_work_is_told_on_the_card_and_refused_after_it(string arguments)
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var call = Call("schedule_duty", arguments);
            var card = Inputs(tools.Describe(call));
            Assert.Single(card);
            Assert.False(string.IsNullOrWhiteSpace(card["Not possible"]));
            var result = tools.Create(call, _ => { });
            Assert.True(result.IsError);
            Assert.StartsWith("Not created: ", result.Text, StringComparison.Ordinal);
            Assert.Empty(rig.Host.Scheduler.Duties);
        }
    }

    [Fact]
    public void A_folder_the_owner_says_yes_to_is_granted_and_watched_with_its_pattern()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var folder = Path.Combine(rig.Folder, "inbox");
            Directory.CreateDirectory(folder);
            var json = $$"""{"path":{{System.Text.Json.JsonSerializer.Serialize(folder)}},"instructions":"File the new invoices.","pattern":"*.pdf"}""";
            var card = Inputs(tools.Describe(Call("watch_folder", json)));
            Assert.Equal("New files matching *.pdf in " + folder, card["Trigger"]);
            Assert.Contains("names, sizes and times", card["What it reads"], StringComparison.Ordinal);
            Assert.False(rig.Host.Switches.IsFolderApproved(folder)); // not before the yes
            var result = tools.Create(Call("watch_folder", json), _ => { });
            Assert.False(result.IsError, result.Text);
            Assert.True(rig.Host.Switches.IsFolderApproved(folder));
            var duty = Assert.Single(rig.Host.Scheduler.Duties).Duty;
            Assert.Equal("Watch inbox", duty.Name);
            Assert.Equal("*.pdf", ((FolderChangeTrigger)duty.Trigger).Pattern);
        }
    }

    [Theory]
    [InlineData("relative\\path")]
    [InlineData("Z:\\no\\such\\folder\\anywhere")]
    public void A_folder_that_is_not_a_full_path_or_not_there_is_refused(string path)
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var json = $$"""{"path":{{System.Text.Json.JsonSerializer.Serialize(path)}},"instructions":"File them."}""";
            Assert.True(tools.Create(Call("watch_folder", json), _ => { }).IsError);
            Assert.Empty(rig.Host.Scheduler.Duties);
        }
    }

    [Theory]
    [InlineData("30m", 30)]
    [InlineData("2 hours", 120)]
    [InlineData("1d", 1440)]
    [InlineData("90 min", 90)]
    public void How_often_to_look_is_read_from_the_owners_words(string every, int minutes)
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Html("<body>The espresso machine page, in stock now.</body>"));
        var (tools, rig) = Make(new WebWatcher(new WebWatcherOptions { Handler = handler }));
        using (rig)
        {
            var card = Inputs(tools.Describe(Call("watch_page", $$"""{"url":"https://shop.example/espresso","instructions":"Tell me the price.","every":"{{every}}"}""")));
            Assert.Contains(ScheduleText.Describe(TimeSchedule.Every(minutes)), card["Trigger"], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("2m")]
    [InlineData("1000d")]
    public void A_look_interval_that_makes_no_sense_is_refused(string every)
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var call = Call("watch_page", $$"""{"url":"https://shop.example/espresso","instructions":"Tell me the price.","every":"{{every}}"}""");
            Assert.True(Inputs(tools.Describe(call)).ContainsKey("Not possible"));
        }
    }

    [Fact]
    public void A_page_is_read_once_first_and_then_watched()
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Html("<body>The espresso machine page, in stock now.</body>"));
        var (tools, rig) = Make(new WebWatcher(new WebWatcherOptions { Handler = handler }));
        using (rig)
        {
            var delivered = new List<string>();
            var result = tools.Create(Call("watch_page", """{"url":"https://shop.example/espresso","instructions":"Tell me the price."}"""), text =>
            {
                lock (delivered) delivered.Add(text);
            });
            Assert.False(result.IsError);
            Assert.Contains("Reading the page once first", result.Text, StringComparison.Ordinal);
            MissionRig.Until(() =>
            {
                lock (delivered) return delivered.Count == 1;
            }, "the page to be read");
            Assert.Contains("“Watch shop.example” is set: Every 1 h · changed · shop.example/espresso", delivered[0], StringComparison.Ordinal);
            Assert.Single(rig.Host.Scheduler.Duties);
            Assert.Single(handler.Requests);
        }
    }

    [Theory]
    [InlineData("<html><body><div id=\"root\"></div><script src=\"/app.js\"></script></body></html>", HttpStatusCode.OK, "needs the browser")]
    [InlineData("login", HttpStatusCode.Unauthorized, "asks for a sign-in")]
    [InlineData("gone", HttpStatusCode.InternalServerError, "answered 500")]
    public void A_page_that_cannot_be_watched_is_refused_in_the_chat_and_never_becomes_a_duty(string body, HttpStatusCode status, string said)
    {
        var handler = new RecordingHandler(_ => RecordingHandler.Html(body, status));
        var (tools, rig) = Make(new WebWatcher(new WebWatcherOptions { Handler = handler }));
        using (rig)
        {
            var delivered = new List<string>();
            tools.Create(Call("watch_page", """{"url":"https://app.example/dashboard","instructions":"Tell me."}"""), text =>
            {
                lock (delivered) delivered.Add(text);
            });
            MissionRig.Until(() =>
            {
                lock (delivered) return delivered.Count == 1;
            }, "the page to be read");
            Assert.StartsWith("Not created: ", delivered[0], StringComparison.Ordinal);
            Assert.Contains(said, delivered[0], StringComparison.Ordinal);
            Assert.Empty(rig.Host.Scheduler.Duties);
        }
    }

    [Fact]
    public void Without_a_web_reader_a_page_watch_says_so()
    {
        var (tools, rig) = Make(web: null);
        using (rig)
        {
            var result = tools.Create(Call("watch_page", """{"url":"https://shop.example/a","instructions":"Tell me."}"""), _ => { });
            Assert.True(result.IsError);
            Assert.Contains("web reader", result.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_page_on_this_pc_is_refused_when_it_is_read()
    {
        var web = new WebWatcher(new WebWatcherOptions { Handler = new RecordingHandler(_ => RecordingHandler.Html("<body>router admin page with lots of text</body>")) });
        var (tools, rig) = Make(web);
        using (rig)
        {
            var delivered = new List<string>();
            tools.Create(Call("watch_page", """{"url":"http://192.168.1.1/admin","instructions":"Tell me."}"""), text =>
            {
                lock (delivered) delivered.Add(text);
            });
            MissionRig.Until(() =>
            {
                lock (delivered) return delivered.Count == 1;
            }, "the refusal");
            Assert.Contains("not watched", delivered[0], StringComparison.Ordinal);
            Assert.Empty(rig.Host.Scheduler.Duties);
        }
    }

    [Fact]
    public void What_a_duty_may_do_is_what_it_says_and_a_duty_that_is_gone_may_do_nothing_alone()
    {
        var (tools, rig) = Make();
        using (rig)
        {
            var scope = new DutyScope { AllowedClasses = new ValueList<ActionClass>([ActionClass.ReadWeb]) };
            rig.Save(DutyHostRig.Daily("narrow", 8) with { Scope = scope });
            rig.Save(DutyHostRig.Daily("plain", 8));
            Assert.Equal(scope, tools.ScopeOf("narrow"));
            Assert.Null(tools.ScopeOf("plain"));
            Assert.Empty(tools.ScopeOf("ghost")!.AllowedClasses!);
        }
    }

    [Fact]
    public async Task Through_the_controller_the_call_raises_a_card_with_the_duty_and_a_yes_makes_it()
    {
        var (_, rig) = Make();
        using (rig)
        {
            var call = Call("schedule_duty", """{"title":"Morning digest","instructions":"Compose the digest.","schedule":"daily 08:00"}""");
            var card = Guid.Parse(await rig.Controller.Ask(call, CancellationToken.None));
            var raised = rig.Controller.Queue.Find(card)!;
            Assert.Equal(DecisionKind.ToolCall, raised.Kind);
            Assert.Equal("Compose the digest.", raised.Inputs.Single(i => i.Name == "What it does").Value);
            Assert.StartsWith("Every day at 08:00", raised.Inputs.Single(i => i.Name == "Trigger").Value, StringComparison.Ordinal);
            Assert.Empty(rig.Host.Scheduler.Duties); // nothing before the answer
            Assert.Null(rig.Controller.Queue.Answer(card, DecisionAnswer.AllowOnce));
            var duty = Assert.Single(rig.Host.Scheduler.Duties).Duty;
            Assert.Equal("Morning digest", duty.Name);
        }
    }

    [Fact]
    public async Task Through_the_controller_a_no_makes_nothing()
    {
        var (_, rig) = Make();
        using (rig)
        {
            var call = Call("schedule_duty", """{"title":"Morning digest","instructions":"Compose the digest.","schedule":"daily 08:00"}""");
            var card = Guid.Parse(await rig.Controller.Ask(call, CancellationToken.None));
            Assert.Null(rig.Controller.Queue.Answer(card, DecisionAnswer.Deny()));
            Assert.Empty(rig.Host.Scheduler.Duties);
        }
    }

    [Fact]
    public void Before_the_duties_run_a_duty_tool_says_so()
    {
        var gate = new ForwardingDutyTools();
        var result = gate.Create(Call("schedule_duty", """{"title":"X","instructions":"Y","schedule":"daily 08:00"}"""), _ => { });
        Assert.True(result.IsError);
        Assert.Null(gate.Describe(Call("schedule_duty", "{}")));
        Assert.Empty(gate.ScopeOf("any")!.AllowedClasses!);
    }
}
