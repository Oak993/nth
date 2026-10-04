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
using Marrow.Services.Tests.Fixtures;
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
        runtime.Loop.Run(() => legacy.Send(new TurnInput { Images = [], Text = "fixture", RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build }), TimeSpan.FromSeconds(5));
        legacy.Stop();
        var current = new ClaudeSession(config, () => null);
        runtime.Loop.Run(() => current.Start(), TimeSpan.FromSeconds(5));
        runtime.Loop.Run(() => current.Send(new TurnInput { Images = [], Text = "fixture", RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build }), TimeSpan.FromSeconds(5));
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
        var gen = service.Start("claude", "fixture-chat", [item, plugin], f.Switches((item, true), (plugin, true)).WithPlugin("A3", true), f.Budget, []);
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var events = new List<ProviderEvent>(); var s = new ClaudeSession(Config(), () => gen) { OnEvent = events.Add };
        runtime.Loop.Run(() => s.Start(), TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { gen.Folder }.Concat(gen.PluginFolders), PluginArgs(call!.Arguments));
        Assert.Contains(' ', gen.Folder);
        Assert.Contains("\"name\":\"marrow\"", File.ReadAllText(Path.Combine(gen.Folder, ".claude-plugin", "plugin.json")), StringComparison.Ordinal);
        Assert.Contains("\"version\":\"fixture\"", File.ReadAllText(Path.Combine(gen.Folder, ".claude-plugin", "plugin.json")), StringComparison.Ordinal);
        Assert.Equal(new[] { "pdf", "marrow:pdf" }, Assert.Single(events.OfType<ProviderEvent.Commands>()).Value.Select(x => x.Name));
        s.Stop(); Drain(runtime); Assert.False(gen.Verify());
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
    [Fact, Trait("Rule", "WU-6")]
    public void Handshake_passes_verified_listing_and_plugin_folders_and_releases_its_lease()
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder(); using var help = Help();
        using var f = new GenerationFixture(); var item = f.Add("A1", "pdf"); var plugin = f.Add("A3", "sales", plugin: true); var service = f.Open();
        var switches = f.Switches((item, true), (plugin, true)).WithPlugin("A3", true);
        var probe = new ClaudeSkillProbe(() => service.Listing("claude", [item, plugin], switches, f.Budget, []), [item, plugin], switches);
        var listingFolder = probe.Folder;
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var c = Config();
        var result = runtime.Loop.Run(() => ClaudeSession.Handshake(c.Executable!, c.WorkingDirectory, c.Environment, () => probe), TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { listingFolder }.Concat(probe.PluginFolders), PluginArgs(call!.Arguments));
        Assert.Equal(new[] { "pdf", "marrow:pdf" }, result.Commands.Select(x => x.Name));
        Assert.Throws<ObjectDisposedException>(() => { _ = probe.Folder; });
    }

    [Fact, Trait("Rule", "WU-6-resume")]
    public void Resume_after_GC_and_coordinator_restart_keeps_argv_root_and_transcript_skill_directory_verified()
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder(); using var help = Help();
        using var f = new GenerationFixture(); var item = f.Add("A1", "pdf"); var service = f.Open();
        var switches = f.Switches((item, true));
        var first = service.Start("claude", "stable-chat", [item], switches, f.Budget, []);
        var calls = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => { var p = new RecordingClaudeProcess(args, env); calls.Add(p); return p; });
        var start = new ClaudeSession(Config(null), () => first);
        var id = runtime.Loop.Run(() => start.Start(), TimeSpan.FromSeconds(5));
        var transcript = File.ReadAllText(Fixture("claude-resume.jsonl")).Replace("<GEN>", first.Folder.Replace("\\", "/", StringComparison.Ordinal), StringComparison.Ordinal);
        var baseDirectory = JSONValue.Parse(transcript)!["message"]!["content"]!.JSONValueArray![0]["content"]!.JSONValueString!["Base directory for this skill: ".Length..];
        start.Stop(); Drain(runtime); service.Collect(); service.Dispose();
        var restarted = f.Open(); var next = restarted.Start("claude", "stable-chat", [item], switches, f.Budget, []);
        using var resumedTransport = ClaudeProviderRuntime.Process.Override((_, args, _, env) =>
        {
            var p = new RecordingClaudeProcess(args, env) { OnStart = () => { Assert.True(next.Verify()); Assert.True(File.Exists(Path.Combine(baseDirectory, "SKILL.md"))); } };
            calls.Add(p); return p;
        });
        var resumed = new ClaudeSession(Config(id), () => next);
        runtime.Loop.Run(() => resumed.Start(), TimeSpan.FromSeconds(5));
        Assert.Equal(PluginArgs(calls[0].Arguments), PluginArgs(calls[1].Arguments)); Assert.Contains("--resume", calls[1].Arguments);
        resumed.Stop(); Drain(runtime);
    }

    [Fact, Trait("Rule", "WU-6-held-folder")]
    public void Held_old_generation_uses_new_root_and_exact_plan_moved_line_in_existing_prompt()
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder(); using var help = Help();
        using var f = new GenerationFixture(); var item = f.Add("A1", "pdf"); var service = f.Open(); var switches = f.Switches((item, true));
        var old = service.Start("claude", "held-chat", [item], switches, f.Budget, []); var path = old.Folder;
        old.Dispose(); f.Deletion.Held.Add(path); f.Store!.Retire(path);
        var next = service.Start("claude", "held-chat", [item], switches, f.Budget, []);
        Assert.NotEqual(path, next.Folder);
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var resumed = new ClaudeSession(Config(), () => next); runtime.Loop.Run(() => resumed.Start(), TimeSpan.FromSeconds(5));
        Assert.Contains(next.Folder, PluginArgs(call!.Arguments));
        var prompt = call.Arguments[Array.IndexOf(call.Arguments, "--append-system-prompt") + 1];
        Assert.Contains($"Marrow's skills for this chat are now in {next.Folder}. Paths under {path} earlier in this conversation no longer exist; use the same relative path under the new folder.", prompt, StringComparison.Ordinal);
        resumed.Stop(); Drain(runtime); f.Deletion.Held.Clear();
    }

    [Fact, Trait("Rule", "WU-6-integrity")]
    public void Tampered_generation_fails_before_any_provider_process_and_releases_lease()
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder(); using var help = Help();
        using var f = new GenerationFixture(); var item = f.Add("A1", "pdf"); var service = f.Open();
        var gen = service.Start("claude", "tamper-chat", [item], f.Switches((item, true)), f.Budget, []);
        f.Trees.Unlock(gen.Folder); File.WriteAllText(Path.Combine(gen.Folder, "extra.py"), "fixture"); f.Trees.Lock(gen.Folder);
        using var transport = ClaudeProviderRuntime.Process.Override((_, _, _, _) => throw new InvalidOperationException("Must not spawn"));
        var s = new ClaudeSession(Config(), () => gen);
        Assert.Throws<IOException>(() => runtime.Loop.Run(() => s.Start(), TimeSpan.FromSeconds(5)));
        Assert.False(gen.Verify());
    }

    [Fact, Trait("Rule", "DL-12")]
    public void Edge_fixture_isolates_head_commands_and_ignores_unknown_malformed_noncommand_and_prose_events()
    {
        var events = new List<ToolEvent>(); var s = new ClaudeSession(Config()) { OnSkillEvent = events.Add };
        foreach (var line in File.ReadLines(Fixture("claude-edge-tools.jsonl"))) s.Handle(JSONValue.Parse(line)!);
        Assert.Equal(new ToolEvent[] {
            new CommandFinished("python report.py", null, true, "python: command not found"),
            new CommandFinished("soffice --headless", null, true, "CommandNotFoundException"), new SkillUsed("pdf"), new CommandHeld("soffice --headless") }, events);
    }

    [Fact, Trait("Rule", "DL-12-observer")]
    public void Observer_failure_is_reported_without_changing_approval_or_provider_events()
    {
        var actual = new List<ProviderEvent>(); var expected = new List<ProviderEvent>();
        var failure = new InvalidOperationException("Fixture observer failure");
        var old = new LegacyClaudeSession(Config()) { OnEvent = expected.Add };
        var current = new ClaudeSession(Config()) { OnEvent = actual.Add, OnSkillEvent = _ => throw failure };
        foreach (var line in File.ReadLines(Fixture("claude-tools.jsonl"))) { var message = JSONValue.Parse(line)!; old.Handle(message); current.Handle(message); }
        Assert.Equal(expected, actual); Assert.Same(failure, current.LastSkillEventFailure);
    }

    [Theory, InlineData("allow"), InlineData("deny"), Trait("Rule", "DL-11-permissions")]
    public void Tap_keeps_real_permission_response_RPC_bytes_identical(string answer)
    {
        using var runtime = new TestRuntime(); using var scratch = new StorageScratchFolder(); using var help = Help();
        var calls = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => { var p = new RecordingClaudeProcess(args, env); calls.Add(p); return p; });
        var c = Config(); var old = new LegacyClaudeSession(c); var current = new ClaudeSession(c) { OnSkillEvent = _ => { } };
        runtime.Loop.Run(() => old.Start(), TimeSpan.FromSeconds(5)); runtime.Loop.Run(() => current.Start(), TimeSpan.FromSeconds(5));
        var held = JSONValue.Parse(File.ReadLines(Fixture("claude-tools.jsonl")).Skip(1).First())!;
        old.Handle(held); current.Handle(held); old.ResolveApproval("approval-1", answer); current.ResolveApproval("approval-1", answer);
        Assert.Equal(calls[0].Sent.Last(), calls[1].Sent.Last()); old.Stop(); current.Stop(); Drain(runtime);
    }

    private static void Drain(TestRuntime runtime) => runtime.Loop.RunUntilIdle();
    private static byte[] Encode(IEnumerable<string> args) => Encoding.UTF8.GetBytes(string.Join('\0', args) + "\0");
    private static byte[] EncodeEnvironment(IReadOnlyDictionary<string, string> env) => Encode(env.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));
    private static string[] PluginArgs(string[] args) => args.Select((a, i) => (a, i)).Where(x => x.a == "--plugin-dir").Select(x => args[x.i + 1]).ToArray();
}
