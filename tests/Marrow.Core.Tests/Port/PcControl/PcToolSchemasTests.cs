using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.2 and 5.1: valid schemas, every tool described, stable names.
[Trait("Decision", "ED-NEW-pcctl-1")]
[Trait("Decision", "ED-NEW-pcctl-7")]
public sealed class PcToolSchemasTests
{
    [Fact]
    public void The_fifteen_tool_names_are_stable_and_in_design_order()
    {
        Assert.Equal(
            new[]
            {
                "pc_list_apps", "pc_launch_app", "pc_observe", "pc_click", "pc_type", "pc_set_value", "pc_key", "pc_scroll",
                "pc_drag", "pc_action", "pc_focus_window", "pc_batch", "pc_read", "pc_zoom", "pc_wait",
            },
            PcToolSchemas.All.Select(t => t.Name));
        Assert.Equal("nth-pc", PcToolSchemas.ServerName);
    }

    [Fact]
    public void Every_schema_is_an_object_schema_whose_required_fields_are_declared()
    {
        foreach (var tool in PcToolSchemas.All)
        {
            var schema = tool.Schema();
            Assert.Equal("object", schema["type"]!.GetValue<string>());
            Assert.False(schema["additionalProperties"]!.GetValue<bool>());
            var properties = schema["properties"]!.AsObject();
            foreach (var required in (schema["required"] as JsonArray ?? []).Select(r => r!.GetValue<string>()))
                Assert.True(properties.ContainsKey(required), tool.Name + " requires undeclared " + required);
            foreach (var (name, property) in properties)
                Assert.True(property!["type"] is not null, tool.Name + "." + name + " has no type");
        }
    }

    [Fact]
    public void Every_tool_has_a_title_and_a_description_and_read_only_tools_change_nothing()
    {
        foreach (var tool in PcToolSchemas.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), tool.Name);
            Assert.True(tool.Description.Length >= 40, tool.Name);
        }
        Assert.Equal(
            new[] { "pc_list_apps", "pc_observe", "pc_read", "pc_zoom", "pc_wait" },
            PcToolSchemas.All.Where(t => t.ReadOnly).Select(t => t.Name));
    }

    [Fact]
    public void Window_scoped_tools_require_a_window()
    {
        foreach (var tool in PcToolSchemas.All.Where(t => t.Name is not ("pc_list_apps" or "pc_launch_app" or "pc_zoom")))
            Assert.Contains("window", tool.Schema()["required"]!.AsArray().Select(r => r!.GetValue<string>()));
    }

    [Fact]
    public void Batch_steps_and_element_actions_match_their_schema_enums()
    {
        var batch = PcToolSchemas.Find("pc_batch")!.Schema();
        var steps = batch["properties"]!["actions"]!["items"]!["properties"]!["tool"]!["enum"]!.AsArray().Select(s => s!.GetValue<string>());
        Assert.Equal(PcToolSchemas.BatchSteps.OrderBy(s => s, StringComparer.Ordinal), steps.OrderBy(s => s, StringComparer.Ordinal));
        var action = PcToolSchemas.Find("pc_action")!.Schema();
        var actions = action["properties"]!["action"]!["enum"]!.AsArray().Select(s => s!.GetValue<string>());
        Assert.Equal(PcToolSchemas.ElementActions, actions);
    }

    [Fact]
    public void Every_action_takes_a_target_and_an_expect_and_a_batch_holds_up_to_fifty_steps()
    {
        foreach (var name in new[] { "pc_click", "pc_type", "pc_set_value", "pc_key", "pc_scroll", "pc_action" })
        {
            var properties = PcToolSchemas.Find(name)!.Schema()["properties"]!.AsObject();
            Assert.True(properties.ContainsKey("target"), name);
            Assert.True(properties.ContainsKey("expect"), name);
            var expect = properties["expect"]!["properties"]!.AsObject().Select(p => p.Key);
            Assert.Equal(new[] { "value", "name", "appears", "gone", "selected", "checked", "focused_in_app", "window_opens", "text_in_window", "bind" }, expect);
        }
        var batch = PcToolSchemas.Find("pc_batch")!.Schema()["properties"]!;
        Assert.Equal(PcToolSchemas.MaxBatchSteps, batch["actions"]!["maxItems"]!.GetValue<int>());
        Assert.Equal("boolean", batch["parallel"]!["type"]!.GetValue<string>());
        Assert.Equal(new[] { "insert", "replace" }, PcToolSchemas.Find("pc_type")!.Schema()["properties"]!["mode"]!["enum"]!.AsArray().Select(m => m!.GetValue<string>()));
    }

    [Fact]
    public void Wait_resumes_a_waiting_step_by_token_and_launch_picks_a_place()
    {
        var wait = PcToolSchemas.Find("pc_wait")!.Schema()["properties"]!;
        Assert.Contains("step", wait["until"]!["enum"]!.AsArray().Select(u => u!.GetValue<string>()));
        Assert.Equal("string", wait["token"]!["type"]!.GetValue<string>());
        var place = PcToolSchemas.Find("pc_launch_app")!.Schema()["properties"]!["place"]!;
        Assert.Equal(new[] { "auto", "desk", "workspace" }, place["enum"]!.AsArray().Select(p => p!.GetValue<string>()));
    }

    [Fact]
    public void The_fast_helper_tool_is_listed_only_when_the_user_turned_it_on_and_takes_a_plan_of_checked_steps()
    {
        Assert.DoesNotContain(PcToolSchemas.Listed(PcSettings.Default), t => t.Name == "pc_delegate");
        Assert.Contains(PcToolSchemas.Listed(PcSettings.Default with { FastHelper = true }), t => t.Name == "pc_delegate");
        Assert.Equal(15, PcToolSchemas.All.Count);
        var spec = PcToolSchemas.Find("pc_delegate")!;
        var step = spec.Schema()["properties"]!["goal"]!["items"]!;
        Assert.Equal(new[] { "tool", "window", "target", "expect" }, step["required"]!.AsArray().Select(r => r!.GetValue<string>()));
        var verbs = step["properties"]!["tool"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Assert.True(verbs.SetEquals(PcToolSchemas.DelegateVerbs));
        foreach (var never in new[] { "pc_key", "pc_drag", "pc_launch_app", "pc_batch", "pc_focus_window" }) Assert.DoesNotContain(never, verbs);
        Assert.Equal(PcToolSchemas.MaxDelegateSteps, spec.Schema()["properties"]!["goal"]!["maxItems"]!.GetValue<int>());
    }

    [Fact]
    public void The_function_definition_wraps_name_description_and_parameters()
    {
        var definition = PcToolSchemas.Find("pc_key")!.FunctionDefinition();
        Assert.Equal("function", definition["type"]!.GetValue<string>());
        Assert.Equal("pc_key", definition["function"]!["name"]!.GetValue<string>());
        Assert.Equal("object", definition["function"]!["parameters"]!["type"]!.GetValue<string>());
        Assert.Null(PcToolSchemas.Find("pc_nothing"));
    }

    [Fact]
    public void The_instructions_cover_the_loop_targeting_waiting_secrets_confirmation_and_untrusted_screen_text()
    {
        var text = PcInstructions.Text;
        foreach (var needle in new[] { "pc_list_apps", "pc_observe", "element numbers", "obs", "pc_batch", "Never sleep", "passwords", "ask the user in chat first", "never instructions", "stop and tell the user", "Tactus", "every call names its window", "at the same time", "minimized", "never brings a window to the front", "expect", "ambiguous_target", "unverified", "pc_read", "parallel", "never copy and paste", "user_typing_here", "needs_real_input", "waiting_for_user", "until \"step\"", "Excel" })
            Assert.Contains(needle, text, StringComparison.Ordinal);
        Assert.StartsWith("<pc_control>\n", PcInstructions.PromptBlock, StringComparison.Ordinal);
        Assert.DoesNotContain('—', text);
    }
}
