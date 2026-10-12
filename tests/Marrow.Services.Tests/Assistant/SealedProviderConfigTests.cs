using System.Reflection;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 12.1a "Mission chats are sealed"; ED-NEW-dextra-as5-3): every chat of a mission launches
// in the ask mode with none of the owner's allow rules, hooks, bypasses or unlisted MCP servers, on every provider nth
// can seal; the others refuse mission work. The sessions honour the seal themselves, so no caller can launch a sealed
// chat in Full access by passing another mode.
public sealed class SealedProviderConfigTests
{
    private static readonly SealedLaunch Seal = new(Guid.Parse("6d1f8d0e-5e0a-4a51-9a35-0b2a1c3e4f50")) { Connections = new(["GitHub"]) };

    private static readonly ProviderKind[] Sealable =
        [ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Local, ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai, ProviderKind.OpenRouter];

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void Every_mode_is_capped_to_the_ask_mode_and_every_other_mode_counts_as_a_bypass()
    {
        foreach (var mode in Enum.GetValues<RuntimeMode>())
        {
            Assert.Equal(RuntimeMode.Supervised, SealedProviderConfig.Cap(mode));
            Assert.Equal(mode != RuntimeMode.Supervised, SealedProviderConfig.Bypasses(mode));
        }
        Assert.Contains(RuntimeMode.FullAccess, Enum.GetValues<RuntimeMode>());
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void Every_provider_either_seals_into_the_ask_mode_or_refuses_mission_work()
    {
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var expected = Sealable.Contains(provider);
            Assert.Equal(expected, SealedProviderConfig.CanSeal(provider));
            foreach (var mode in Enum.GetValues<RuntimeMode>())
            {
                var configuration = BuiltInServerFixtures.Config(provider) with { RuntimeMode = mode };
                if (expected)
                {
                    var sealedConfiguration = SealedProviderConfig.Seal(configuration, Seal);
                    Assert.Equal(RuntimeMode.Supervised, sealedConfiguration.RuntimeMode);
                    Assert.Same(Seal, sealedConfiguration.Sealed);
                    Assert.Null(SealedProviderConfig.Refusal(provider));
                }
                else
                {
                    var refused = Assert.Throws<ProviderError>(() => SealedProviderConfig.Seal(configuration, Seal));
                    Assert.Contains(provider.DisplayName, refused.Message, StringComparison.Ordinal);
                    Assert.Equal($"{provider.DisplayName} cannot run mission work yet: nth cannot switch off its own allow rules. Use Claude, Codex or an API provider.",
                        SealedProviderConfig.Refusal(provider));
                }
            }
        }
        var turn = new TurnInput { Text = "go", Images = ValueList<Attachment>.Empty, RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build };
        Assert.Equal(RuntimeMode.Supervised, SealedProviderConfig.Seal(turn).RuntimeMode);
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void A_sealed_answer_is_always_the_providers_once_option()
    {
        var request = new ApprovalRequest("r1", ApprovalRequest.Kind.Command, "Run", new(
        [
            new ApprovalRequest.Option("always", "Always allow", ApprovalRequest.Option.Role.ApproveAlways),
            new ApprovalRequest.Option("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new ApprovalRequest.Option("cancel", "Cancel", ApprovalRequest.Option.Role.Cancel),
            new ApprovalRequest.Option("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));
        Assert.Equal("allow", SealedProviderConfig.OnceOption(request)!.Id);
        Assert.Equal("deny", SealedProviderConfig.DeclineOption(request)!.Id);
        var onlyAlways = request with { Options = new([request.Options[0], request.Options[2]]) };
        Assert.Null(SealedProviderConfig.OnceOption(onlyAlways));
        Assert.Equal("cancel", SealedProviderConfig.DeclineOption(onlyAlways)!.Id);

        Assert.Equal("accept", SealedProviderConfig.CodexOnce("acceptForSession"));
        Assert.Equal("accept", SealedProviderConfig.CodexOnce("amend"));
        Assert.Equal("turn", SealedProviderConfig.CodexOnce("session"));
        foreach (var id in new[] { "accept", "decline", "cancel", "turn" }) Assert.Equal(id, SealedProviderConfig.CodexOnce(id));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void Claude_launch_arguments_lose_every_settings_source_and_every_bypass()
    {
        string[] ordinary = ["-p", "--setting-sources", "user,project,local", "--allow-dangerously-skip-permissions", "--permission-mode", "bypassPermissions",
            "--dangerously-skip-permissions", "--model", "sonnet"];
        Assert.Equal(new[] { "-p", "--setting-sources", "", "--permission-mode", "default", "--model", "sonnet" }, SealedProviderConfig.ClaudeArguments(ordinary));
        Assert.Equal("plan", SealedProviderConfig.ClaudeArguments(["--permission-mode", "plan"])[1]);
        Assert.Equal("default", SealedProviderConfig.ClaudeArguments(["--permission-mode", "acceptEdits"])[1]);
        Assert.Equal("default", SealedProviderConfig.ClaudeArguments(["--permission-mode", "auto"])[1]);
        Assert.Equal(new[] { "--permission-mode" }, SealedProviderConfig.ClaudeArguments(["--permission-mode"]));
        Assert.Equal("plan", SealedProviderConfig.ClaudeMode("plan"));
        foreach (var mode in new[] { "bypassPermissions", "acceptEdits", "auto", "default", "dontAsk" }) Assert.Equal("default", SealedProviderConfig.ClaudeMode(mode));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void Claude_flag_settings_inherit_no_allow_rule_and_the_seal_wins_every_clash()
    {
        var others = JSONValue.Parse("""{"fastMode":true,"permissions":{"allow":["Bash(*)"],"defaultMode":"bypassPermissions"},"disableAllHooks":false,"hooks":{"PreToolUse":[]}}""")!.JSONValueObject!;
        var settings = JSONValue.Parse(SealedProviderConfig.ClaudeFlagSettings(others))!;
        Assert.True(settings["fastMode"]!.JSONValueBool);
        var permissions = settings["permissions"]!;
        foreach (var key in new[] { "allow", "deny", "ask", "additionalDirectories" }) Assert.Empty(permissions[key]!.JSONValueArray!);
        Assert.Equal(("default", "disable"), (permissions["defaultMode"]!.JSONValueString, permissions["disableBypassPermissionsMode"]!.JSONValueString));
        Assert.True(settings["disableAllHooks"]!.JSONValueBool);
        Assert.False(settings["enableAllProjectMcpServers"]!.JSONValueBool);
        Assert.Empty(settings["enabledMcpjsonServers"]!.JSONValueArray!);
        Assert.Equal(SealedProviderConfig.ClaudeFlagSettings(), SealedProviderConfig.ClaudeFlagSettings(new Dictionary<string, JSONValue>()));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void Only_the_servers_the_profile_lists_reach_a_mission_chat_and_nth_entries_replace_impostors()
    {
        var connected = JSONValue.Parse("""{"github":{"url":"https://example.test/gh"},"slack":{"url":"https://example.test/slack"},"nth-advisor":{"url":"https://imposter.example"}}""");
        var claude = SealedProviderConfig.ClaudeServers(connected, Seal, [BuiltInServerFixtures.Advisor]).JSONValueObject!;
        Assert.Equal(new HashSet<string> { "github", "nth-advisor" }, claude.Keys.ToHashSet());
        Assert.Equal("C:/nth/nth.exe", claude["nth-advisor"]["command"]!.JSONValueString);
        Assert.Equal(new[] { "nth-advisor" }, SealedProviderConfig.ClaudeServers(null, new SealedLaunch(Guid.NewGuid()), [BuiltInServerFixtures.Advisor]).JSONValueObject!.Keys);

        var codex = SealedProviderConfig.CodexServers(connected, ["personal", "github"], Seal, [BuiltInServerFixtures.Advisor]).JSONValueObject!;
        Assert.Equal(new HashSet<string> { "github", "slack", "personal", "nth-advisor" }, codex.Keys.ToHashSet());
        Assert.Equal("https://example.test/gh", codex["github"]["url"]!.JSONValueString);
        Assert.False(codex["slack"]["enabled"]!.JSONValueBool);
        Assert.False(codex["personal"]["enabled"]!.JSONValueBool);
        Assert.Equal("C:/nth/nth.exe", codex["nth-advisor"]["command"]!.JSONValueString);
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void A_reported_allow_rule_or_bypass_breaks_the_seal_and_a_silent_cli_does_not()
    {
        Assert.Null(SealedProviderConfig.ClaudeSealBroken(null));
        Assert.Null(SealedProviderConfig.ClaudeSealBroken(JSONValue.Parse("""{"other":1}""")));
        Assert.Null(SealedProviderConfig.ClaudeSealBroken(JSONValue.Parse("""{"effective":{"permissions":{"allow":[],"defaultMode":"default"}}}""")));
        Assert.Equal("Claude Code still applies 2 allow rule(s) to this chat.",
            SealedProviderConfig.ClaudeSealBroken(JSONValue.Parse("""{"effective":{"permissions":{"allow":["Bash(*)","Edit"]}}}""")));
        Assert.Equal("Claude Code would run this chat with permissions bypassed.",
            SealedProviderConfig.ClaudeSealBroken(JSONValue.Parse("""{"effective":{"permissions":{"defaultMode":"bypassPermissions"}}}""")));
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void Codex_always_allow_rules_are_found_wherever_codex_home_points()
    {
        using var scratch = new ScratchFolder();
        var home = scratch.Child("codex-home");
        var env = new Dictionary<string, string> { ["CODEX_HOME"] = home };
        Assert.Equal(home, SealedProviderConfig.CodexHome(env, "C:/Users/someone"));
        Assert.Equal(Path.Combine("C:/Users/someone", ".codex"), SealedProviderConfig.CodexHome(new Dictionary<string, string>(), "C:/Users/someone"));
        Assert.Equal(Path.Combine("C:/Users/someone", ".codex"), SealedProviderConfig.CodexHome(new Dictionary<string, string> { ["CODEX_HOME"] = "  " }, "C:/Users/someone"));
        Assert.Empty(SealedProviderConfig.CodexAllowRules(home));
        Assert.Null(SealedProviderConfig.CodexRefusal(env, "C:/unused"));

        Directory.CreateDirectory(Path.Combine(home, "rules", "nested"));
        File.WriteAllText(Path.Combine(home, "rules", "ask.rules"), "prefix_rule(pattern=[\"git\"], decision=\"prompt\")\n");
        File.WriteAllText(Path.Combine(home, "rules", "notes.txt"), "decision=\"allow\"\n");
        Assert.Empty(SealedProviderConfig.CodexAllowRules(home));

        File.WriteAllText(Path.Combine(home, "rules", "nested", "default.rules"), "prefix_rule(pattern=[\"rm\"], decision = 'Allow')\n");
        var found = Assert.Single(SealedProviderConfig.CodexAllowRules(home));
        Assert.EndsWith("default.rules", found, StringComparison.Ordinal);
        Assert.Equal("Codex keeps \"Always allow\" rules (default.rules), which would run commands in a mission without asking. "
            + "Remove them, or run the mission on another account or provider.", SealedProviderConfig.CodexRefusal(env, "C:/unused"));
    }

    // ---- The sessions honour the seal ----

    private static (SealedClaudeProcess Process, ClaudeSession Session, Exception? Failure) LaunchSealedClaude(
        TestRuntime runtime, Action<SealedClaudeProcess> arrange, RuntimeMode mode = RuntimeMode.FullAccess, bool fast = false)
    {
        SealedClaudeProcess? process = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, _) => { process = new(args); arrange(process); return process; });
        var configuration = BuiltInServerFixtures.Config(ProviderKind.Claude, Guid.NewGuid()) with
        {
            Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!, WorkingDirectory = SwiftURL.Create("file:///C:/dextra-test")!,
            Model = "sonnet", RuntimeMode = mode, FastMode = fast, Sealed = Seal,
        };
        var session = new ClaudeSession(configuration, () => null);
        Exception? failure = null;
        runtime.Loop.Run(async () =>
        {
            try { await session.Start(); }
            catch (ProviderError error) { failure = error; }
        }, TimeSpan.FromSeconds(10));
        return (process!, session, failure);
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void A_sealed_claude_chat_launches_in_the_ask_mode_with_no_settings_file_strict_servers_and_a_checked_seal()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--system-prompt-snapshot"), [])));
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll([BuiltInServerFixtures.Connected, new MCPResolvedServer("slack", "slack") { Url = "https://example.test/slack" }]);

        foreach (var mode in Enum.GetValues<RuntimeMode>())
        {
            var (process, session, failure) = LaunchSealedClaude(runtime, _ => { }, mode, fast: mode == RuntimeMode.FullAccess);
            Assert.Null(failure);
            var arguments = process.Arguments;
            Assert.DoesNotContain("--allow-dangerously-skip-permissions", arguments);
            Assert.DoesNotContain("--dangerously-skip-permissions", arguments);
            Assert.Equal("", arguments[Array.IndexOf(arguments, "--setting-sources") + 1]);
            Assert.Equal("default", arguments[Array.IndexOf(arguments, "--permission-mode") + 1]);
            Assert.Single(arguments, a => a == "--settings");
            var settings = JSONValue.Parse(arguments[Array.IndexOf(arguments, "--settings") + 1])!;
            Assert.Empty(settings["permissions"]!["allow"]!.JSONValueArray!);
            Assert.True(settings["disableAllHooks"]!.JSONValueBool);
            if (mode == RuntimeMode.FullAccess) Assert.True(settings["fastMode"]!.JSONValueBool);
            var at = Array.IndexOf(arguments, "--mcp-config");
            Assert.Equal("--strict-mcp-config", arguments[at + 2]);
            var servers = JSONValue.Parse(File.ReadAllBytes(arguments[at + 1]))!["mcpServers"]!.JSONValueObject!;
            Assert.Equal(new[] { "github" }, servers.Keys);
            Assert.Contains(process.Sent, m => m["request"]?["subtype"]?.JSONValueString == "get_settings");
            session.Stop();
        }
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void A_sealed_claude_chat_stops_when_the_cli_reports_a_broken_seal_or_never_answers_and_goes_on_when_it_cannot_say()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--system-prompt-snapshot"), [])));
        using var storage = new StorageScratchFolder();

        var broken = LaunchSealedClaude(runtime, p => p.Settings = JSONValue.Parse("""{"effective":{"permissions":{"allow":["Bash(*)"]}}}"""));
        Assert.Equal("This mission chat could not be sealed: Claude Code still applies 1 allow rule(s) to this chat.", broken.Failure?.Message);
        broken.Session.Stop();

        var older = LaunchSealedClaude(runtime, p => p.SettingsError = true);
        Assert.Null(older.Failure);
        older.Session.Stop();

        var timeout = ClaudeSession.SealCheckTimeout;
        ClaudeSession.SealCheckTimeout = TimeSpan.FromMilliseconds(200);
        try
        {
            var silent = LaunchSealedClaude(runtime, p => p.Settings = null);
            Assert.Equal("Claude Code did not confirm this mission chat's seal.", silent.Failure?.Message);
            silent.Session.Stop();
        }
        finally { ClaudeSession.SealCheckTimeout = timeout; }
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void A_sealed_claude_chat_answers_always_as_once_and_keeps_no_rule()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--system-prompt-snapshot"), [])));
        using var storage = new StorageScratchFolder();
        var (process, session, failure) = LaunchSealedClaude(runtime, _ => { });
        Assert.Null(failure);
        var suggestion = JSONValue.FromDictionaryLiteral(("type", "addRules"), ("behavior", "allow"), ("destination", "userSettings"));
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(("request_id", "r1"), ("request", JSONValue.FromDictionaryLiteral(
            ("subtype", "can_use_tool"), ("tool_name", "Bash"), ("input", JSONValue.FromDictionaryLiteral(("command", "ls"))),
            ("permission_suggestions", JSONValue.FromArrayLiteral(suggestion))))));
        var before = process.Sent.Count;
        session.ResolveApproval("r1", "always", null);
        var reply = process.Sent[before]["response"]!["response"]!;
        Assert.Equal("allow", reply["behavior"]!.JSONValueString);
        Assert.Null(reply["updatedPermissions"]);
        session.Stop();
    }

    private static CodexSession SealedCodex(string codexHome, RuntimeMode mode) => new(new SessionConfiguration
    {
        Provider = ProviderKind.Codex, WorkingDirectory = SwiftURL.Create("file:///offline")!, Executable = SwiftURL.Create("/offline/codex")!,
        Environment = new ValueMap<string, string>(new Dictionary<string, string> { ["CODEX_HOME"] = codexHome }),
        RuntimeMode = mode, InteractionMode = InteractionMode.Build, Sealed = Seal,
    });

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public void A_sealed_codex_thread_asks_for_everything_and_sees_only_the_listed_servers()
    {
        using var storage = new StorageScratchFolder();
        using var scratch = new ScratchFolder();
        var home = scratch.Child("codex-home");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.toml"), "[mcp_servers.personal]\ncommand = \"personal-mcp\"\n");
        MCPProviderConfig.WriteAll([BuiltInServerFixtures.Connected, new MCPResolvedServer("slack", "slack") { Url = "https://example.test/slack" }]);
        foreach (var mode in Enum.GetValues<RuntimeMode>())
        {
            var session = SealedCodex(home, mode);
            var parameters = (Dictionary<string, JSONValue>)typeof(CodexSession).GetProperty("ThreadParameters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.Equal(("untrusted", "read-only", "user"),
                (parameters["approvalPolicy"].JSONValueString, parameters["sandbox"].JSONValueString, parameters["approvalsReviewer"].JSONValueString));
            var servers = parameters["config"]["mcp_servers"]!.JSONValueObject!;
            Assert.Equal("https://example.test/mcp", servers["github"]["url"]!.JSONValueString);
            Assert.False(servers["slack"]["enabled"]!.JSONValueBool);
            Assert.False(servers["personal"]["enabled"]!.JSONValueBool);
        }
    }

    [Fact]
    [Trait("Feature", "AS-5")]
    [Trait("Decision", "ED-NEW-dextra-as5-3")]
    public async Task A_sealed_codex_chat_does_not_start_beside_always_allow_rules()
    {
        using var scratch = new ScratchFolder();
        var home = scratch.Child("codex-home");
        Directory.CreateDirectory(Path.Combine(home, "rules"));
        File.WriteAllText(Path.Combine(home, "rules", "default.rules"), "prefix_rule(pattern=[\"rm\"], decision=\"allow\")\n");
        var refused = await Assert.ThrowsAsync<ProviderError>(() => SealedCodex(home, RuntimeMode.Supervised).Start());
        Assert.StartsWith("Codex keeps \"Always allow\" rules (default.rules)", refused.Message, StringComparison.Ordinal);
    }
}
