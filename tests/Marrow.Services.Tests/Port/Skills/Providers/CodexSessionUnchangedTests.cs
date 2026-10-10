// PORT-ONLY: SK-11b / DL-11 hard rule. With nothing installed (no wiring, nothing bound), every argv, environment variable and RPC message CodexSession produces equals what the pinned Mac source produces.
// The Linux tests drive CodexSession's own members; the stand-in tests (Windows only, they need the csc-compiled CodexStandIn) record the real stdin lines.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Voice.Engine;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class CodexSessionUnchangedTests
{
    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-10")]
    public void The_app_server_argument_vector_is_exactly_app_server()
    {
        Assert.Equal(["app-server"], CodexWiringFixture.StaticProperty<IReadOnlyList<string>>("AppServerArguments"));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-10")]
    public void The_initialize_message_is_the_one_the_Mac_source_builds_with_the_Marrow_identity()
    {
        var parameters = (JSONValue)CodexWiringFixture.CallStatic("InitializeParameters")!;
        var expected = "{\"clientInfo\":{\"name\":\"nth\",\"title\":\"Nth\",\"version\":" +
            System.Text.Json.JsonSerializer.Serialize(AppInfo.Current.Version) + "},\"capabilities\":{\"experimentalApi\":true}}";
        Assert.Equal(CodexWiringFixture.Bytes(CodexWiringFixture.Json(expected)), CodexWiringFixture.Bytes(parameters));
        Assert.Equal("nth", parameters["clientInfo"]!["name"]!.JSONValueString);
        Assert.Equal("Nth", parameters["clientInfo"]!["title"]!.JSONValueString);
        Assert.True(parameters["capabilities"]!["experimentalApi"]!.JSONValueBool);
        Assert.Null(parameters["capabilities"]!["skills"]);
    }

    private static (GenerationFixture Fixture, Generation Chat, CodexSkillWiring Wiring) Wired(string chat = "chat-a")
    {
        var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var generation = f.Open().Start("codex", chat, [item], f.Switches((item, true)), f.Budget);
        return (f, generation, new CodexSkillWiring(generation, f.Paths));
    }

    private static Dictionary<string, JSONValue> Params() => CodexWiringFixture.Property<Dictionary<string, JSONValue>>(CodexWiringFixture.Session(), "ThreadParameters");

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void Thread_parameters_are_the_same_bytes_with_and_without_wiring()
    {
        using var storage = new StorageScratchFolder();
        var (f, chat, wiring) = Wired();
        using var _ = f;
        using var __ = chat;
        var bare = CodexWiringFixture.Property<Dictionary<string, JSONValue>>(CodexWiringFixture.Session(), "ThreadParameters");
        var wired = CodexWiringFixture.Property<Dictionary<string, JSONValue>>(CodexWiringFixture.Session(wiring), "ThreadParameters");
        Assert.Equal(bare.Keys, wired.Keys);
        foreach (var key in bare.Keys) Assert.Equal(CodexWiringFixture.Bytes(bare[key]), CodexWiringFixture.Bytes(wired[key]));
        Assert.DoesNotContain("skills", bare.Keys);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void Resume_parameters_equal_today_plus_the_thread_id_and_excludeTurns_when_nothing_moved()
    {
        using var storage = new StorageScratchFolder();
        var (f, chat, wiring) = Wired();
        using var _ = f;
        using var __ = chat;
        Assert.Null(chat.PreviousFolder);
        var bare = (Dictionary<string, JSONValue>)CodexWiringFixture.Call(CodexWiringFixture.Session(resumeId: "r1"), "ResumeParameters", Params(), "r1")!;
        var wired = (Dictionary<string, JSONValue>)CodexWiringFixture.Call(CodexWiringFixture.Session(wiring, resumeId: "r1"), "ResumeParameters", Params(), "r1")!;
        var threadStart = CodexWiringFixture.Property<Dictionary<string, JSONValue>>(CodexWiringFixture.Session(), "ThreadParameters");
        Assert.Equal(threadStart.Keys.Concat(["threadId", "excludeTurns"]).Order(), bare.Keys.Order());
        Assert.Equal("r1", bare["threadId"].JSONValueString);
        Assert.True(bare["excludeTurns"].JSONValueBool);
        foreach (var key in bare.Keys) Assert.Equal(CodexWiringFixture.Bytes(bare[key]), CodexWiringFixture.Bytes(wired[key]));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_moved_generation_adds_the_skills_moved_line_to_the_resume_instructions_and_nothing_else()
    {
        using var storage = new StorageScratchFolder();
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        var old = service.Start("codex", "one", [item], f.Switches((item, true)), f.Budget);
        f.Deletion.Held.Add(old.Folder); // the old folder is still held open, so the chat moves to a new path (skills-PLAN.md 2.1 "Resuming" step 3)
        var original = old.Folder;
        old.Dispose();
        using var moved = service.Start("codex", "one", [item], f.Switches((item, false)), f.Budget);
        Assert.Equal(original, moved.PreviousFolder);
        var wiring = new CodexSkillWiring(moved, f.Paths);
        var bare = (Dictionary<string, JSONValue>)CodexWiringFixture.Call(CodexWiringFixture.Session(resumeId: "r1"), "ResumeParameters", Params(), "r1")!;
        var withNote = (Dictionary<string, JSONValue>)CodexWiringFixture.Call(CodexWiringFixture.Session(wiring, resumeId: "r1"), "ResumeParameters", Params(), "r1")!;
        var line = "Nth's skills for this chat are now in " + moved.Folder + ". Paths under " + original + " earlier in this conversation no longer exist; use the same relative path under the new folder.";
        Assert.Equal(line, wiring.MovedNote);
        Assert.Equal(bare["developerInstructions"].JSONValueString + "\n\n" + line, withNote["developerInstructions"].JSONValueString);
        Assert.Equal(bare.Keys.Order(), withNote.Keys.Order());
        foreach (var key in bare.Keys.Where(k => k != "developerInstructions")) Assert.Equal(CodexWiringFixture.Bytes(bare[key]), CodexWiringFixture.Bytes(withNote[key]));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void The_skills_moved_line_is_exact_for_a_generation_that_moved()
    {
        var line = CodexSkillWiring.MovedLine("/new/chat-a-0123456789abcdef", "/old/chat-a");
        Assert.Equal("Nth's skills for this chat are now in /new/chat-a-0123456789abcdef. Paths under /old/chat-a earlier in this conversation no longer exist; use the same relative path under the new folder.", line);
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-9")]
    public void Without_wiring_and_for_a_command_with_no_skill_path_the_command_is_used_as_it_is()
    {
        var skill = new SlashCommand { Name = "pdf", Detail = "d", SkillPath = "/x/list/pdf/SKILL.md" };
        var plain = new SlashCommand { Name = "review", Detail = "d", IsBuiltIn = true };
        Assert.Same(skill, CodexWiringFixture.Call(CodexWiringFixture.Session(), "ResolveSkill", skill));
        var (f, chat, wiring) = Wired();
        using var _ = f;
        using var __ = chat;
        Assert.Same(plain, CodexWiringFixture.Call(CodexWiringFixture.Session(wiring), "ResolveSkill", plain));
    }

    [Fact]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_skill_command_from_the_listing_gets_the_path_under_the_session_generation_and_a_users_own_path_is_kept()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        using var listing = service.Listing("codex", [item], f.Switches((item, true)), f.Budget);
        using var chat = service.Start("codex", "chat-a", [item], f.Switches((item, true)), f.Budget);
        var wiring = new CodexSkillWiring(chat, f.Paths, listing.Skills);
        var command = new SlashCommand { Name = "pdf", Detail = "d", SkillPath = listing.Skills[0].File };
        var resolved = (SlashCommand)CodexWiringFixture.Call(CodexWiringFixture.Session(wiring), "ResolveSkill", command)!;
        Assert.Equal(chat.Skills[0].File, resolved.SkillPath);
        Assert.Equal(command with { SkillPath = chat.Skills[0].File }, resolved);
        var user = new SlashCommand { Name = "mine", Detail = "d", SkillPath = "/home/u/.agents/skills/mine/SKILL.md" };
        Assert.Equal(user, CodexWiringFixture.Call(CodexWiringFixture.Session(wiring), "ResolveSkill", user));
    }

    [WindowsOnlyFact("SlashCommand.CodexInput needs the ICU shim (icu.dll) that Windows has")]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-9")]
    public void The_skill_part_of_the_turn_input_carries_the_rewritten_path_and_the_text_part_is_unchanged()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        using var listing = service.Listing("codex", [item], f.Switches((item, true)), f.Budget);
        using var chat = service.Start("codex", "chat-a", [item], f.Switches((item, true)), f.Budget);
        var command = new SlashCommand { Name = "pdf", Detail = "d", SkillPath = listing.Skills[0].File };
        var wired = (SlashCommand)CodexWiringFixture.Call(CodexWiringFixture.Session(new CodexSkillWiring(chat, f.Paths, listing.Skills)), "ResolveSkill", command)!;
        var parts = wired.CodexInput("$pdf go");
        Assert.Equal(command.CodexInput("$pdf go")[0], parts[0]);
        Assert.Equal(chat.Skills[0].File, parts[1]["path"]!.JSONValueString);
        Assert.Equal("pdf", parts[1]["name"]!.JSONValueString);
    }

    // The thread/start parameters carry the working directory, so two sessions that are compared must share one (a script's own folder differs per script).
    private static CodexSession StandIn(CodexScript script, CodexSkillWiring? wiring, string? workingDirectory = null)
    {
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Codex,
            Executable = CodexWiringFixture.Url(script.ExecutablePath),
            WorkingDirectory = CodexWiringFixture.Url(workingDirectory ?? Path.GetDirectoryName(script.ExecutablePath)!),
            Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        return wiring is null ? new CodexSession(configuration) : new CodexSession(configuration, wiring);
    }

    private static void Drive(TestRuntime runtime, Task task)
    {
        for (var i = 0; i < 200 && !task.IsCompleted; i++)
        {
            runtime.RunUntilIdle();
            if (task.IsCompleted) break;
            SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromMilliseconds(25));
        }
        task.GetAwaiter().GetResult();
    }

    private static string Line(CodexScript script, int index) => CodexWiringFixture.Bytes(CodexWiringFixture.Json(script.CapturedLine(0, index)));

    [WindowsOnlyFact("the CodexStandIn is compiled with the .NET Framework csc.exe and spawned through CreateProcessW")]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-10")]
    public void Start_without_wiring_sends_exactly_initialize_initialized_thread_start_and_launches_app_server()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var script = new CodexScript("sk11b-bare");
        script.SetResponse("initialize", "{}");
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}");
        Drive(runtime, StandIn(script, null).Start());
        Assert.Equal(["app-server"], Assert.Single(script.Calls()).Arguments);
        Assert.Equal(3, script.MessageCount(0));
        var initialize = CodexWiringFixture.Json(script.CapturedLine(0, 0));
        Assert.Equal("initialize", initialize["method"]!.JSONValueString);
        Assert.Equal(CodexWiringFixture.Bytes((JSONValue)CodexWiringFixture.CallStatic("InitializeParameters")!), CodexWiringFixture.Bytes(initialize["params"]!));
        Assert.Equal("initialized", CodexWiringFixture.Json(script.CapturedLine(0, 1))["method"]!.JSONValueString);
        Assert.Equal("thread/start", CodexWiringFixture.Json(script.CapturedLine(0, 2))["method"]!.JSONValueString);
    }

    [WindowsOnlyFact("the CodexStandIn is compiled with the .NET Framework csc.exe and spawned through CreateProcessW")]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T11")]
    [Trait("Rule", "R-TEST-10")]
    public void The_child_environment_differs_from_today_only_by_addons_bin_in_PATH()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var path = @"C:\Windows\system32;C:\Users\x\AppData\Local\Microsoft\WindowsApps;C:\Windows";

        CodexScript Launch(string label, CodexChildRoot? root)
        {
            var script = new CodexScript(label);
            script.SetResponse("initialize", "{}");
            script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}");
            var configuration = new SessionConfiguration
            {
                Provider = ProviderKind.Codex,
                Executable = CodexWiringFixture.Url(script.ExecutablePath),
                WorkingDirectory = CodexWiringFixture.Url(Path.GetDirectoryName(script.ExecutablePath)!),
                Environment = new ValueMap<string, string>(new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value, ["PATH"] = path, ["SK11B_PROBE"] = "kept" }),
                RuntimeMode = RuntimeMode.Supervised,
                InteractionMode = InteractionMode.Build,
            };
            using (CodexChildEnvironment.Root.Override(root ?? new CodexChildRoot(() => null))) Drive(runtime, new CodexSession(configuration).Start());
            return script;
        }

        var today = Launch("sk11b-env-today", null).LaunchEnvironment(0);
        var dataRoot = Path.Combine(storage.Root, "env-root");
        var wired = Launch("sk11b-env-bin", new CodexChildRoot(() => dataRoot)).LaunchEnvironment(0);
        var bin = Path.Combine(dataRoot, "addons", "bin");
        Assert.Equal(path, today["PATH"]);
        Assert.Equal(@"C:\Windows\system32;" + bin + @";C:\Users\x\AppData\Local\Microsoft\WindowsApps;C:\Windows", wired["PATH"]);
        foreach (var (name, value) in today.Where(p => p.Key != "PATH" && p.Key != "CODEX_STANDIN_DIR")) Assert.Equal(value, wired[name]);
        Assert.Equal(today.Keys.Order(StringComparer.OrdinalIgnoreCase), wired.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("kept", wired["SK11B_PROBE"]);
    }

    [WindowsOnlyFact("the CodexStandIn is compiled with the .NET Framework csc.exe and spawned through CreateProcessW")]
    [Trait("Feature", "SK-11b")]
    [Trait("Spec", "SK-T7")]
    [Trait("Rule", "R-TEST-10")]
    public void Start_with_wiring_sends_skills_list_and_extraRoots_between_initialized_and_thread_start_and_changes_no_other_line()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var bare = new CodexScript("sk11b-bare2");
        bare.SetResponse("initialize", "{}");
        bare.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}");
        Drive(runtime, StandIn(bare, null).Start());

        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        using var chat = f.Open().Start("codex", "chat-a", [item], f.Switches((item, true)), f.Budget);
        var script = new CodexScript("sk11b-wired");
        script.SetResponse("initialize", "{}");
        script.SetResponse("skills/list", "{\"data\":[]}");
        script.SetResponse("skills/extraRoots/set", "{}");
        script.SetResponse("thread/start", "{\"thread\":{\"id\":\"t1\"}}");
        Drive(runtime, StandIn(script, new CodexSkillWiring(chat, f.Paths), Path.GetDirectoryName(bare.ExecutablePath)).Start());

        Assert.Equal(["app-server"], Assert.Single(script.Calls()).Arguments);
        Assert.Equal(5, script.MessageCount(0));
        var methods = Enumerable.Range(0, 5).Select(i => CodexWiringFixture.Json(script.CapturedLine(0, i))["method"]!.JSONValueString!).ToArray();
        Assert.Equal(["initialize", "initialized", "skills/list", "skills/extraRoots/set", "thread/start"], methods);
        Assert.Equal(Line(bare, 0), Line(script, 0));
        Assert.Equal(Line(bare, 1), Line(script, 1));
        Assert.Equal([chat.Folder], CodexWiringFixture.Json(script.CapturedLine(0, 3))["params"]!["extraRoots"]!.JSONValueArray!.Select(r => r.JSONValueString));
        Assert.Equal(CodexWiringFixture.Json(bare.CapturedLine(0, 2))["params"]!.ToString(), CodexWiringFixture.Json(script.CapturedLine(0, 4))["params"]!.ToString());
    }
}
