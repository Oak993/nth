// PORT-ONLY: SK-11a / WU-6 / DL-11 / DL-12, offline provider-boundary tests.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11a")]
public sealed class ClaudeSkillsTests
{
    private static SessionConfiguration Config(string? resume = "fixture-chat") => new()
    {
        Provider = ProviderKind.Claude, Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!,
        WorkingDirectory = SwiftURL.Create("/tmp")!, ResumeID = resume,
        Environment = new ValueMap<string, string>(new Dictionary<string, string> { ["PATH"] = "user-bin", ["UNCHANGED"] = "spaced value" }),
        RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build
    };
    private static IDisposable Help(string help = "--plugin-dir") => ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes(help), [])));
    private static string Fixture(string name)
    {
        var p = new DirectoryInfo(AppContext.BaseDirectory);
        while (p is not null)
        {
            var file = Path.Combine(p.FullName, "tests", "Marrow.Services.Tests", "Port", "Skills", "Providers", "Fixtures", name);
            if (File.Exists(file)) return file;
            p = p.Parent;
        }
        throw new FileNotFoundException(name);
    }

    [Fact, Trait("Rule", "DL-11")]
    public void Empty_install_matches_frozen_existing_provider_argv_environment_and_RPC_bytes()
    {
        using var runtime = new TestRuntime();
        using var help = Help();
        using var scratch = new StorageScratchFolder();
        var config = Config();
        var recordings = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) =>
        {
            var p = new RecordingClaudeProcess(args, env); recordings.Add(p); return p;
        });
        var legacy = new LegacyClaudeSession(config);
        runtime.Loop.Run(() => legacy.Start(), TimeSpan.FromSeconds(5));
        runtime.Loop.Run(() => legacy.Send(new TurnInput { Text = "fixture", RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build }), TimeSpan.FromSeconds(5));
        legacy.Stop();
        var current = new ClaudeSession(config, () => null);
        runtime.Loop.Run(() => current.Start(), TimeSpan.FromSeconds(5));
        runtime.Loop.Run(() => current.Send(new TurnInput { Text = "fixture", RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build }), TimeSpan.FromSeconds(5));
        current.Stop();
        Assert.Equal(2, recordings.Count);
        Assert.Equal(Encode(recordings[0].Arguments), Encode(recordings[1].Arguments));
        // DL-6 is the expressly required sole exception to raw Mac environment equality.
        Assert.Equal(EncodeEnvironment(ShimPath.ForChild(recordings[0].Environment, Marrow.Services.Store.Storage.Root)), EncodeEnvironment(recordings[1].Environment));
        Assert.Equal(recordings[0].Sent.Count, recordings[1].Sent.Count);
        for (var i = 0; i < recordings[0].Sent.Count; i++) Assert.Equal(recordings[0].Sent[i], recordings[1].Sent[i]);
    }

    [Fact, Trait("Rule", "DL-11")]
    public void Empty_handshake_matches_existing_provider_without_an_extra_help_or_RPC_call()
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => throw new InvalidOperationException("Empty probe must not call help."));
        var c = Config(); var calls = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => { var p = new RecordingClaudeProcess(args, env); calls.Add(p); return p; });
        runtime.Loop.Run(() => LegacyClaudeSession.Handshake(c.Executable!, c.WorkingDirectory, c.Environment), TimeSpan.FromSeconds(5));
        runtime.Loop.Run(() => ClaudeSession.Handshake(c.Executable!, c.WorkingDirectory, c.Environment, () => null), TimeSpan.FromSeconds(5));
        Assert.Equal(Encode(calls[0].Arguments), Encode(calls[1].Arguments));
        Assert.Equal(calls[0].Sent.Single(), calls[1].Sent.Single());
        Assert.Equal(EncodeEnvironment(ShimPath.ForChild(calls[0].Environment, Marrow.Services.Store.Storage.Root)), EncodeEnvironment(calls[1].Environment));
    }

    [Fact, Trait("Rule", "WU-6")]
    public void Start_passes_generation_and_each_installed_plugin_as_single_arguments_and_keeps_user_command_first()
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder(); using var help = Help();
        using var f = new GenerationFixture(); var item = f.Add("A1", "pdf"); var plugin = f.Add("A3", "sales", plugin: true); var service = f.Open();
        var gen = service.Start("claude", "fixture-chat", [item, plugin], f.Switches(), f.Budget, []);
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var events = new List<ProviderEvent>(); var s = new ClaudeSession(Config(), () => gen) { OnEvent = events.Add };
        runtime.Loop.Run(() => s.Start(), TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { gen.Folder }.Concat(gen.PluginFolders), PluginArgs(call!.Arguments));
        Assert.Contains(' ', gen.Folder);
        Assert.Contains("\"name\":\"marrow\"", File.ReadAllText(Path.Combine(gen.Folder, ".claude-plugin", "plugin.json")), StringComparison.Ordinal);
        Assert.Contains("\"version\":\"fixture\"", File.ReadAllText(Path.Combine(gen.Folder, ".claude-plugin", "plugin.json")), StringComparison.Ordinal);
        Assert.Equal(new[] { "pdf", "marrow:pdf" }, Assert.Single(events.OfType<ProviderEvent.Commands>()).Items.Select(x => x.Name));
        s.Stop(); Assert.False(gen.Verify());
    }

    [Fact, Trait("Rule", "WU-6")]
    public void Missing_plugin_flag_never_acquires_a_generation_and_keeps_existing_argv()
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder(); using var help = Help("");
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var s = new ClaudeSession(Config(), () => throw new InvalidOperationException("Unsupported CLI must not acquire."));
        runtime.Loop.Run(() => s.Start(), TimeSpan.FromSeconds(5)); Assert.Empty(PluginArgs(call!.Arguments)); s.Stop();
    }

    [Fact, Trait("Rule", "DL-12")]
    public void Fixture_tool_use_result_and_can_use_tool_emit_only_correlated_normalized_events()
    {
        var events = new List<ToolEvent>(); var s = new ClaudeSession(Config()) { OnSkillEvent = events.Add };
        foreach (var line in File.ReadLines(Fixture("claude-tools.jsonl"))) s.Handle(JSONValue.Parse(line)!);
        Assert.Equal(new ToolEvent[] {
            new CommandHeld("python -m report"), new SkillUsed("pdf"),
            new CommandFinished("python -m report", null, true, "ModuleNotFoundError: No module named 'report'"),
            new CommandFinished("node render.js", null, false, "done") }, events);
    }

    [Fact, Trait("Rule", "DL-12")]
    public void Taps_leave_existing_provider_events_and_permission_responses_unchanged()
    {
        var a = new List<ProviderEvent>(); var b = new List<ProviderEvent>();
        var old = new LegacyClaudeSession(Config()) { OnEvent = a.Add }; var current = new ClaudeSession(Config()) { OnEvent = b.Add, OnSkillEvent = _ => { } };
        foreach (var line in File.ReadLines(Fixture("claude-tools.jsonl"))) { var m = JSONValue.Parse(line)!; old.Handle(m); current.Handle(m); }
        Assert.Equal(a, b);
    }
    private static byte[] Encode(IEnumerable<string> args) => Encoding.UTF8.GetBytes(string.Join('\0', args) + "\0");
    private static byte[] EncodeEnvironment(IReadOnlyDictionary<string, string> env) => Encode(env.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));
    private static string[] PluginArgs(string[] args) => args.Select((a, i) => (a, i)).Where(x => x.a == "--plugin-dir").Select(x => args[x.i + 1]).ToArray();
}
