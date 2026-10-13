using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-4 (DESIGN.md rev 5, 12.4, 15.5, 18; ED-NEW-dextra-as4-1, -2): the nth-dextra tools as one catalogue: exactly
// the 24 of 15.5 (and AS-20's prepare_purchase, ED-NEW-dextra-as20-1) under the locked names, each placed by AS-1's classifier with the guard 12.4 gives it, strict schemas,
// and arguments refused before any rule or action is reached.
public sealed class AssistantToolCatalogTests
{
    private static readonly string[] Table15_5 =
    [
        "list_sessions", "read_session", "start_mission", "spawn_chat", "launch_hydra_team", "message_agent", "stop_agent",
        "merge_results", "request_helpers", "ask_owner", "schedule_duty", "watch_folder", "watch_page", "remember", "recall",
        "forget", "usage_status", "plan_budget", "delegate_to", "report", "create_assistant", "propose_capability", "suggest",
        "deliver",
        // AS-20 (12.7; ED-NEW-dextra-as20-1): the purchase card's tool, after the 24 of 15.5.
        "prepare_purchase",
    ];

    /// <summary>The smallest arguments a tool takes: each required one with a fitting value.</summary>
    internal static JSONValue Minimal(AssistantToolDefinition tool) => JSONValue.FromDictionaryLiteral([.. tool.Parameters.Where(p => p.Required).Select(p =>
        (p.Name, p.Type switch
        {
            AssistantParameterType.String => (JSONValue)(p.Choices.Count > 0 ? p.Choices[0] : "x"),
            AssistantParameterType.StringList => JSONValue.FromArrayLiteral("n-1"),
            AssistantParameterType.Boolean => (JSONValue)true,
            _ => (JSONValue)(long)(p.Minimum ?? 1),
        }))]);

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-1")]
    public void The_locked_names_hold_and_the_server_is_nth_dextra()
    {
        Assert.Equal(("Dextra", "Astur", "Asturs", "Reserve", "nth-dextra"),
            (AssistantNames.Assistant, AssistantNames.Astur, AssistantNames.Asturs, AssistantNames.Reserve, AssistantNames.Server));
        foreach (var tool in AssistantToolCatalog.All)
        {
            var text = tool.Title + " " + tool.Description + " " + string.Join(" ", tool.Parameters.Select(p => p.Description));
            Assert.DoesNotContain("nth-assistant", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Semper", text, StringComparison.Ordinal);
            Assert.DoesNotContain("legate", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(AssistantNames.Astur, AssistantToolCatalog.Find("delegate_to")!.Title, StringComparison.Ordinal);
        Assert.Contains(AssistantNames.Astur, AssistantToolCatalog.Find("create_assistant")!.Title, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-2")]
    public void The_catalogue_holds_exactly_the_tools_of_15_5_in_its_order()
    {
        Assert.Equal(Table15_5, AssistantToolCatalog.All.Select(t => t.Name));
        Assert.Equal(25, AssistantToolCatalog.All.Select(t => t.Name).Distinct().Count()); // the 24 of 15.5 and AS-20's prepare_purchase
        Assert.All(AssistantToolCatalog.All, t => Assert.Same(t, AssistantToolCatalog.Find(t.Name)));
        Assert.Null(AssistantToolCatalog.Find("Recall"));
        Assert.Null(AssistantToolCatalog.Find("request_grant"));
        Assert.Null(AssistantToolCatalog.Find(null));
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-2")]
    public void Every_tool_is_placed_by_the_classifier_with_the_guard_12_4_gives_it()
    {
        var guards = new Dictionary<string, AssistantToolGuard>
        {
            ["schedule_duty"] = AssistantToolGuard.PermanentConfiguration, ["watch_folder"] = AssistantToolGuard.PermanentConfiguration,
            ["watch_page"] = AssistantToolGuard.PermanentConfiguration, ["create_assistant"] = AssistantToolGuard.PermanentConfiguration,
            ["propose_capability"] = AssistantToolGuard.PermanentConfiguration, ["suggest"] = AssistantToolGuard.Suggestion,
            ["remember"] = AssistantToolGuard.MemoryWrite, ["forget"] = AssistantToolGuard.MemoryDelete,
            ["delegate_to"] = AssistantToolGuard.Delegation, ["start_mission"] = AssistantToolGuard.Delegation,
            ["spawn_chat"] = AssistantToolGuard.Delegation, ["launch_hydra_team"] = AssistantToolGuard.Delegation,
            ["message_agent"] = AssistantToolGuard.Delegation,
        };
        foreach (var tool in AssistantToolCatalog.All)
        {
            var placed = ActionClassifier.Classify(new ActionRequest.AssistantTool(tool.Name), new ClassifierContext());
            Assert.NotEqual(ActionClass.Unknown, placed.Class);
            Assert.Equal(tool.Name, placed.Target);
            Assert.Equal(guards.GetValueOrDefault(tool.Name, AssistantToolGuard.None), placed.Guard);
            if (tool.ReadOnly) Assert.Equal(ActionClass.ReadLocal, placed.Class);
        }
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-2")]
    public void Each_schema_is_a_strict_object_with_typed_described_and_bounded_properties()
    {
        foreach (var tool in AssistantToolCatalog.All)
        {
            var schema = tool.InputSchema();
            Assert.Equal("object", schema["type"]!.JSONValueString);
            Assert.False(schema["additionalProperties"]!.JSONValueBool);
            var properties = schema["properties"]!.JSONValueObject!;
            Assert.Equal(tool.Parameters.Select(p => p.Name), properties.Keys);
            Assert.All(schema["required"]!.JSONValueArray!, r => Assert.Contains(r.JSONValueString!, properties.Keys));
            Assert.Equal(tool.Parameters.Count(p => p.Required), schema["required"]!.JSONValueArray!.Count);
            foreach (var (name, property) in properties)
            {
                Assert.False(string.IsNullOrWhiteSpace(property["description"]!.JSONValueString), $"{tool.Name}.{name} has no description");
                var type = property["type"]!.JSONValueString;
                Assert.Contains(type, new[] { "string", "integer", "number", "boolean", "array" });
                if (type == "string") Assert.True(property["maxLength"]!.JSONValueInt > 0);
                if (type == "array")
                {
                    Assert.True(property["maxItems"]!.JSONValueInt > 0);
                    Assert.Equal("string", property["items"]!["type"]!.JSONValueString);
                    Assert.True(property["items"]!["maxLength"]!.JSONValueInt > 0);
                }
            }
            Assert.Null(tool.Validate(Minimal(tool)));
            Assert.False(string.IsNullOrWhiteSpace(tool.Title));
            Assert.StartsWith(tool.Name + "(", tool.Signature(), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-2")]
    public void Arguments_that_do_not_fit_are_refused_with_the_reason()
    {
        var start = AssistantToolCatalog.Find("start_mission")!;
        JSONValue With(params (string, JSONValue)[] extra) => JSONValue.FromDictionaryLiteral([("title", "Q3 report"), ("brief", "Collect the invoices."), .. extra]);
        Assert.Null(start.Validate(With()));
        Assert.Null(start.Validate(With(("priority", "high"), ("budget_usd", 12.5), ("project", JSONValue.FromNilLiteral()))));
        Assert.Contains("takes no argument \"owner_approved\"", start.Validate(With(("owner_approved", true))), StringComparison.Ordinal);
        Assert.Contains("needs \"brief\"", start.Validate(JSONValue.FromDictionaryLiteral(("title", "t"))), StringComparison.Ordinal);
        Assert.Contains("must not be empty", start.Validate(JSONValue.FromDictionaryLiteral(("title", "  "), ("brief", "b"))), StringComparison.Ordinal);
        Assert.Contains("must be a string", start.Validate(JSONValue.FromDictionaryLiteral(("title", 7L), ("brief", "b"))), StringComparison.Ordinal);
        Assert.Contains("longer than 200", start.Validate(JSONValue.FromDictionaryLiteral(("title", new string('t', 201)), ("brief", "b"))), StringComparison.Ordinal);
        Assert.Contains("must be one of low, normal, high", start.Validate(With(("priority", "urgent"))), StringComparison.Ordinal);
        Assert.Contains("at most 10000", start.Validate(With(("budget_usd", 10_001L))), StringComparison.Ordinal);
        Assert.Contains("at least 0", start.Validate(With(("budget_usd", -1.0))), StringComparison.Ordinal);
        Assert.Contains("must be a number", start.Validate(With(("budget_usd", "5"))), StringComparison.Ordinal);
        Assert.Contains("one JSON object", start.Validate(JSONValue.FromArrayLiteral("title")), StringComparison.Ordinal);

        var team = AssistantToolCatalog.Find("launch_hydra_team")!;
        JSONValue Team(params (string, JSONValue)[] extra) => JSONValue.FromDictionaryLiteral([("mission", "m-1"), ("brief", "b"), .. extra]);
        Assert.Null(team.Validate(Team(("heads", 16L), ("profiles", JSONValue.FromArrayLiteral("a", "b")))));
        Assert.Contains("whole number", team.Validate(Team(("heads", 1.5))), StringComparison.Ordinal);
        Assert.Contains("whole number", team.Validate(Team(("heads", "3"))), StringComparison.Ordinal);
        Assert.Contains("at most 16", team.Validate(Team(("heads", 17L))), StringComparison.Ordinal);
        Assert.Contains("list of strings", team.Validate(Team(("profiles", JSONValue.FromArrayLiteral("a", 2L)))), StringComparison.Ordinal);
        Assert.Contains("list of strings", team.Validate(Team(("profiles", "a"))), StringComparison.Ordinal);
        Assert.Contains("at most 16 items", team.Validate(Team(("profiles", JSONValue.FromArrayLiteral([.. Enumerable.Range(0, 17).Select(i => (JSONValue)("p" + i))])))), StringComparison.Ordinal);

        var duty = AssistantToolCatalog.Find("schedule_duty")!;
        Assert.Contains("true or false", duty.Validate(JSONValue.FromDictionaryLiteral(("title", "t"), ("instructions", "i"), ("schedule", "s"), ("silent", "yes"))), StringComparison.Ordinal);
        var list = AssistantToolCatalog.Find("list_sessions")!;
        Assert.Null(list.Validate(null));
        Assert.Null(list.Validate(JSONValue.FromNilLiteral()));
        Assert.Contains("needs \"query\"", AssistantToolCatalog.Find("recall")!.Validate(null), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-2")]
    public void A_mission_agent_is_offered_only_the_agent_tools_and_a_home_thread_every_tool()
    {
        Assert.Equal(Table15_5, AssistantToolCatalog.For(AssistantChatRole.Home).Select(t => t.Name));
        Assert.Equal(new[] { "request_helpers", "ask_owner", "remember", "recall", "usage_status", "report", "prepare_purchase" },
            AssistantToolCatalog.For(AssistantChatRole.Mission).Select(t => t.Name));
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-4")]
    public void Only_reads_stops_and_words_to_the_owner_or_the_parent_run_before_auto_answering_is_on()
    {
        Assert.Equal(new[] { "list_sessions", "read_session", "stop_agent", "ask_owner", "recall", "usage_status", "plan_budget", "report", "prepare_purchase" },
            AssistantToolCatalog.All.Where(t => t.RunsBeforeAutoAnswer).Select(t => t.Name));
        Assert.Equal(new[] { "list_sessions", "read_session", "recall", "usage_status", "plan_budget" },
            AssistantToolCatalog.All.Where(t => t.ReadOnly).Select(t => t.Name));
        Assert.Equal(new[] { "forget" }, AssistantToolCatalog.All.Where(t => t.Destructive).Select(t => t.Name));
        Assert.Equal(new[] { "watch_page" }, AssistantToolCatalog.All.Where(t => t.OpenWorld).Select(t => t.Name));
    }
}
