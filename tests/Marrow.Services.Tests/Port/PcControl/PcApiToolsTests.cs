// PORT-ONLY: Tactus (ED-NEW-pcctl-42; DESIGN.md 2.4, 3.4, 4.8). The nth-pc tools as native tools for the API providers:
// nothing while Tactus is off; while on, the listed tools as function definitions, the instructions block, one call on
// the chat's engine with its text and (for a model that takes images) its screenshots; and the history rule that keeps
// only the newest two screenshots. Fake desktop and capture, no provider.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-42")]
public sealed class PcApiToolsTests
{
    private static SessionConfiguration Config(Guid thread) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        Model = "deepseek-chat",
        IgnisThreadID = thread,
    };

    private static JSONValue Shots(params (string Obs, int Crops)[] shots) => PcNativeTools.ScreenshotMessage(
        [.. shots.SelectMany(s => Enumerable.Range(0, s.Crops + 1).Select(_ => ("pc_observe", new PcImage([1, 2, 3], 4, 3, s.Obs))))]);

    private static JSONValue Text(string role, string text) => JSONValue.FromDictionaryLiteral(("role", role), ("content", text));

    [Fact]
    public void Nothing_is_offered_while_tactus_is_off_or_unbound()
    {
        var thread = Guid.NewGuid();
        Assert.Null(PcNativeTools.For(Config(thread), vision: true));
        using var rig = new PcAssemblyRig(PcSettings.Default);
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        Assert.Null(PcNativeTools.For(Config(thread), vision: true));

        var settings = PcSettings.Default;
        var tools = new PcNativeTools(new PcRegistration("chat-off", ProviderKind.Deepseek, null, true), _ => null, () => settings);
        Assert.Empty(tools.Definitions);
        Assert.False(tools.Owns(PcToolSchemas.Observe));
        Assert.Null(tools.PromptBlock());
    }

    [Fact]
    public async Task While_on_the_listed_tools_the_instructions_and_one_call_on_the_chats_engine()
    {
        using var rig = new PcAssemblyRig();
        rig.Notepad();
        var assembly = rig.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        var thread = Guid.NewGuid();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var each = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        var tools = PcNativeTools.For(Config(thread), vision: true)!;

        Assert.Equal(thread.ToString("D"), tools.Registration.Thread);
        Assert.True(tools.Registration.Vision);
        Assert.False(PcNativeTools.For(Config(thread), vision: false)!.Registration.Vision);
        Assert.Equal(PcToolSchemas.Listed(rig.Settings).Select(s => s.Name), tools.Definitions.Select(d => d["function"]!["name"]!.JSONValueString!));
        Assert.All(tools.Definitions, d => Assert.Equal("function", d["type"]!.JSONValueString));
        Assert.DoesNotContain(tools.Definitions, d => d["function"]!["name"]!.JSONValueString == PcToolSchemas.Delegate);
        Assert.False(tools.Owns(PcToolSchemas.Delegate));
        Assert.False(tools.Owns("read_file"));
        Assert.Equal(PcInstructions.Text, tools.PromptBlock());
        Assert.StartsWith("Tactus \u00B7 ", PcNativeTools.Title(PcToolSchemas.Observe), StringComparison.Ordinal);
        rig.Settings = rig.Settings with { FastHelper = true };
        Assert.True(tools.Owns(PcToolSchemas.Delegate));

        var seen = await tools.Execute(PcToolSchemas.Observe, JSONValue.FromDictionaryLiteral(("window", "w:1a"), ("screenshot", "yes")), CancellationToken.None);
        Assert.False(seen.Failed, seen.Text);
        Assert.Contains("Notepad", seen.Text, StringComparison.Ordinal);
        Assert.NotEmpty(seen.Images);
        Assert.Contains(rig.Audits[thread.ToString("D")].Entries, e => e.Tool == PcToolSchemas.Observe);

        var unknown = await tools.Execute("pc_format_disk", JSONValue.FromDictionaryLiteral(), CancellationToken.None);
        Assert.True(unknown.Failed);
        rig.Settings = rig.Settings with { Enabled = false };
        Assert.True((await tools.Execute(PcToolSchemas.Observe, JSONValue.FromDictionaryLiteral(("window", "w:1a")), CancellationToken.None)).Failed);
        PcControlBridge.Forget(thread.ToString("D"));
    }

    [Fact]
    public async Task A_text_only_model_gets_the_text_and_no_images()
    {
        using var rig = new PcAssemblyRig();
        rig.Notepad();
        var assembly = rig.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        using var engine = new PcControlEngine(assembly.PartsFor("chat-blind"), new PcCaller("chat-blind", () => true, false));
        var tools = new PcNativeTools(new PcRegistration("chat-blind", ProviderKind.Deepseek, null, false), _ => engine, () => rig.Settings);

        var seen = await tools.Execute(PcToolSchemas.Observe, JSONValue.FromDictionaryLiteral(("window", "w:1a"), ("screenshot", "yes")), CancellationToken.None);

        Assert.False(seen.Failed, seen.Text);
        Assert.Empty(seen.Images);
        Assert.Contains("does not take images", seen.Text, StringComparison.Ordinal);
        Assert.True((await new PcNativeTools(new PcRegistration("chat-none", ProviderKind.Deepseek, null, true), _ => null, () => rig.Settings)
            .Execute(PcToolSchemas.Observe, JSONValue.FromDictionaryLiteral(("window", "w:1a")), CancellationToken.None)).Failed);
    }

    [Fact]
    public void The_screenshot_message_carries_each_image_after_its_label()
    {
        var message = PcNativeTools.ScreenshotMessage([("pc_observe", new PcImage([0xFF, 0xD8, 0xFF, 0xD9], 4, 3, "o:7")), ("pc_zoom", new PcImage([1], 1, 1, "o:7"))]);
        var parts = message["content"]!.JSONValueArray!;

        Assert.Equal("user", message["role"]!.JSONValueString);
        Assert.Equal(new[] { "text", "image_url", "text", "image_url" }, parts.Select(p => p["type"]!.JSONValueString!));
        Assert.Equal("nth-pc screenshot o:7 (from pc_observe)", parts[0]["text"]!.JSONValueString);
        Assert.Equal("data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }), parts[1]["image_url"]!["url"]!.JSONValueString);
        Assert.True(PcNativeTools.IsScreenshotMessage(message));
        Assert.False(PcNativeTools.IsScreenshotMessage(Text("user", "nth-pc screenshot o:7")));
        Assert.False(PcNativeTools.IsScreenshotMessage(JSONValue.FromDictionaryLiteral(("role", "user"), ("content", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "look")), JSONValue.FromDictionaryLiteral(("type", "image_url")))))));
    }

    [Fact]
    public void Only_the_newest_two_screenshots_stay_and_crops_go_with_their_screenshot()
    {
        var system = Text("system", "rules");
        var task = Text("user", "do it");
        var history = new List<JSONValue> { system, task, Shots(("o:1", 1)), Text("tool", "r"), Shots(("o:2", 0), ("o:3", 2)), Text("assistant", "ok") };

        var pruned = PcNativeTools.Pruned(history, 2);

        Assert.Same(system, pruned[0]);
        Assert.Same(task, pruned[1]);
        Assert.Equal("[screenshot o:1 removed]", pruned[2]["content"]!.JSONValueString);
        Assert.Same(history[3], pruned[3]);
        Assert.Same(history[4], pruned[4]);
        Assert.Same(history[5], pruned[5]);

        var tighter = PcNativeTools.Pruned(pruned, 1);
        var parts = tighter[4]["content"]!.JSONValueArray!;
        Assert.Equal(new[] { "text", "image_url", "text", "image_url", "text", "image_url", "text" }, parts.Select(p => p["type"]!.JSONValueString!));
        Assert.All(parts.Take(6).Where(p => p["type"]!.JSONValueString == "text"), p => Assert.StartsWith("nth-pc screenshot o:3 ", p["text"]!.JSONValueString!, StringComparison.Ordinal));
        Assert.Equal("[screenshot o:2 removed]", parts[^1]["text"]!.JSONValueString);
        Assert.Equal(tighter.Select(m => m.CompactString), PcNativeTools.Pruned(tighter, 1).Select(m => m.CompactString));

        var none = PcNativeTools.Pruned(tighter, 0);
        Assert.Equal("[screenshot o:2 removed] [screenshot o:3 removed]", none[4]["content"]!.JSONValueString);
        Assert.DoesNotContain(none, PcNativeTools.IsScreenshotMessage);
        Assert.DoesNotContain(none, m => m.CompactString.Contains("image_url", StringComparison.Ordinal));
    }
}
