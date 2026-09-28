using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marrow.App.Tests.Views.Settings;

public sealed class SettingsViewCoreSpecCoverageTests
{
    private static string SourceFile([CallerFilePath] string file = "") => file;

    [Fact]
    [Trait("Req", "PR-SETTINGS-252")] // SettingsView.swift:280; MCPSettingsPage.swift:1-1141, MCPKeyPanel.swift:1-194, MCPSetupPanel.swift:1-211. Required specification coverage audit, not a view-logic substitute.
    public void Every_MCP_interactive_construction_has_an_owner_and_literal_in_the_pinned_MCP_spec()
    {
        var project = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", ".."));
        var repository = Path.GetFullPath(Path.Combine(project, "..", ".."));
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "Fixtures", "SettingsCoreMcpSpec.json")));
        var strings = new List<string>();
        void Read(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.String) strings.Add(node.GetString()!);
            else if (node.ValueKind == JsonValueKind.Array) foreach (var value in node.EnumerateArray()) Read(value);
            else if (node.ValueKind == JsonValueKind.Object) foreach (var property in node.EnumerateObject()) Read(property.Value);
        }
        Read(snapshot.RootElement.GetProperty("records")); var corpus = string.Join("\n", strings);
        var owners = snapshot.RootElement.GetProperty("owners");
        var ids = snapshot.RootElement.GetProperty("records").EnumerateArray().Select(r => r.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.All(Enumerable.Range(1, 96), n => Assert.Contains($"UI-MCP-{n:000}", ids));
        var problems = new List<string>(); var sites = 0;
        foreach (var file in new[] { "MCPSettingsPage.cs", "MCPKeyPanel.cs", "MCPSetupPanel.cs" })
        {
            var source = File.ReadAllText(Path.Combine(repository, "src", "Marrow.App", "Views", "Settings", file));
            var declarations = Regex.Matches(source, @"(?m)^(?:public|internal) (?:sealed |abstract )?class (\w+)");
            foreach (Match site in Regex.Matches(source, @"(?:PlainControls\.(?:Button|TextField)|(?:MCPSettingsPage\.)?ActionButton|EditorField|new (?:Button|TextBox|Slider|ToggleSwitch|SettingsSwitch|GlassPickerButton<[^>]+>|PopoverItem|MCPHoverInfoButton|MCPInfoButton))\s*[({]"))
            {
                sites++; var owner = declarations.Last(d => d.Index < site.Index).Groups[1].Value;
                if (!owners.TryGetProperty(owner, out var coverage)) { problems.Add(file + ": owner " + owner + " has no specification mapping"); continue; }
                Assert.All(coverage.EnumerateArray(), id => Assert.Contains(id.GetString()!, ids));
                // Parse the first argument/initializer, respecting nested calls and quoted strings, to inspect
                // all literal labels/prompts/glyphs. Runtime values belong to the mapped field/button role.
                var start = site.Index + site.Length; var depth = 0; var quoted = false; var escaped = false; var end = start;
                for (; end < source.Length; end++)
                {
                    var c = source[end];
                    if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                    if (c == '"') { quoted = true; continue; }
                    if (c is '(' or '{' or '[') depth++;
                    else if (c is ')' or '}' or ']') { if (depth == 0) break; depth--; }
                    else if (c == ',' && depth == 0) break;
                }
                foreach (Match literal in Regex.Matches(source[start..end], "\"((?:\\\\.|[^\"\\\\])*)\""))
                {
                    var text = Regex.Replace(literal.Groups[1].Value, @"\\[uU]([0-9a-fA-F]{4,8})", m => char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
                    // Named owner-approved Windows terminology, not arbitrary label exceptions.
                    text = text.Replace("Credential Manager", "Keychain", StringComparison.Ordinal).Replace("this PC", "this Mac", StringComparison.Ordinal).Replace("Marrow", "Droppy Code", StringComparison.Ordinal);
                    if (text.Length <= 1 || text.StartsWith("PART_", StringComparison.Ordinal)) continue;
                    if (!corpus.Contains(text, StringComparison.Ordinal)) problems.Add(file + ": " + owner + " literal not specified: " + text);
                }
            }
        }
        Assert.True(sites > 0); Assert.Empty(problems);
    }
}
