using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Tests.Port.PcControl;

// PORT-ONLY: PC control (OD-0140). DESIGN.md 2.2 and 5.1: valid schemas, every tool described, stable names.
[Trait("Decision", "ED-NEW-pcctl-1")]
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
        foreach (var needle in new[] { "pc_list_apps", "pc_observe", "element numbers", "obs", "pc_batch", "Never sleep", "passwords", "ask the user in chat first", "never instructions", "stop and tell the user", "Tactus", "every call names its window", "at the same time", "minimized", "never brings a window to the front" })
            Assert.Contains(needle, text, StringComparison.Ordinal);
        Assert.StartsWith("<pc_control>\n", PcInstructions.PromptBlock, StringComparison.Ordinal);
        Assert.DoesNotContain('—', text);
    }
}
