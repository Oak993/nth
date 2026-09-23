using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Models;

// Source-derived tests for DroppyCode/Core/Models/Provider.swift (B2 1fbf81e), unit Core.Models.Provider,
// parts 1 and 2 of 5. Every expected value is transcribed from the cited Swift lines or from the record
// itself, never from the C# under test. Part 1 covers: ProviderKind's fixed 14-case order (PR-PROVREG-001);
// displayName and iconName for codex, claude, cursor, opencode, grok, deepseek, meta, zai, devin,
// antigravity and copilot (PR-PROVREG-002 to 012); the Windows-hazard identities of commandcode, pi and
// antigravity - executableName, loginCommand, displayName (PR-PROVCLI-203, 266, 317; the PATH/PATHEXT
// fallback-directory resolution those records also describe lives in ProviderRegistry, a different Swift
// file and a different unit, and is out of scope here); and SlashCommand.claudeCommands, codexSkills and
// codexInput (PR-PROVCLI-345 to 353). Part 2 covers: commandcode and pi identity at the PR-PROVREG level
// (PR-PROVREG-013, 014); executableName for every remaining case (PR-PROVREG-015 to 027); and
// loginCommand for every remaining case (PR-PROVREG-028 to 036). The PROVREG-020/021/022 and PROVREG-026
// records also describe ProviderRegistry.executable(for:) behaviour (the isAPIKeyBased guard, the PATHEXT
// and %SystemRoot% collision check) - that is a different Swift file (Services/Providers/
// ProviderRegistry.swift) and a different unit; only the ProviderKind.executableName value this file
// owns is proven here.
public sealed class ProviderTests
{
    // PR-PROVREG-001: :3-18. Exactly 14 cases in source declaration order, with these rawValues, and
    // Id == rawValue (:19).
    [Trait("Req", "PR-PROVREG-001")] // Core/Models/Provider.swift:3-19
    [Fact]
    public void ProviderKind_has_fourteen_cases_in_source_order_with_exact_raw_values()
    {
        (ProviderKind Case, string Raw)[] expected =
        [
            (ProviderKind.Codex, "codex"),
            (ProviderKind.Claude, "claude"),
            (ProviderKind.Cursor, "cursor"),
            (ProviderKind.Opencode, "opencode"),
            (ProviderKind.OpencodeGo, "opencodeGo"),
            (ProviderKind.Grok, "grok"),
            (ProviderKind.Deepseek, "deepseek"),
            (ProviderKind.Meta, "meta"),
            (ProviderKind.Zai, "zai"),
            (ProviderKind.Devin, "devin"),
            (ProviderKind.Antigravity, "antigravity"),
            (ProviderKind.Copilot, "copilot"),
            (ProviderKind.Commandcode, "commandcode"),
            (ProviderKind.Pi, "pi"),
        ];

        Assert.Equal(expected.Length, SwiftEnum<ProviderKind>.AllCases.Count);
        Assert.Equal(expected.Select(e => e.Case), SwiftEnum<ProviderKind>.AllCases);
        foreach (var (kind, raw) in expected)
        {
            Assert.Equal(raw, SwiftEnum<ProviderKind>.RawValue(kind));
            Assert.Equal(raw, kind.Id);
        }
    }

    // Identity table for PR-PROVREG-002 to 012: displayName (:21-38) and iconName (:60), which is
    // "provider-" + rawValue for every case other than opencodeGo. Also folds in the displayName parts of
    // PR-PROVCLI-203 (commandcode), PR-PROVCLI-266 (pi) and PR-PROVCLI-317 (antigravity), which name the
    // same displayName switch arms.
    public static IEnumerable<object[]> IdentityCases =>
    [
        [ProviderKind.Codex, "Codex", "provider-codex", "PR-PROVREG-002"],
        [ProviderKind.Claude, "Claude", "provider-claude", "PR-PROVREG-003"],
        [ProviderKind.Cursor, "Cursor", "provider-cursor", "PR-PROVREG-004"],
        [ProviderKind.Opencode, "OpenCode Zen", "provider-opencode", "PR-PROVREG-005"],
        [ProviderKind.Grok, "Grok", "provider-grok", "PR-PROVREG-006"],
        [ProviderKind.Deepseek, "DeepSeek", "provider-deepseek", "PR-PROVREG-007"],
        [ProviderKind.Meta, "Meta", "provider-meta", "PR-PROVREG-008"],
        [ProviderKind.Zai, "Z.ai", "provider-zai", "PR-PROVREG-009"],
        [ProviderKind.Devin, "Devin", "provider-devin", "PR-PROVREG-010"],
        [ProviderKind.Antigravity, "Antigravity", "provider-antigravity", "PR-PROVREG-011"],
        [ProviderKind.Copilot, "Copilot", "provider-copilot", "PR-PROVREG-012"],
        [ProviderKind.Commandcode, "Command Code", "provider-commandcode", "PR-PROVCLI-203"],
        [ProviderKind.Pi, "Pi", "provider-pi", "PR-PROVCLI-266"],
        [ProviderKind.Antigravity, "Antigravity", "provider-antigravity", "PR-PROVCLI-317"],
    ];

    [Theory]
    [MemberData(nameof(IdentityCases))]
    public void ProviderKind_displayName_and_iconName_are_exact(ProviderKind kind, string displayName, string iconName, string req)
    {
        // Req trait id is not statically known at the attribute site because it varies per row; assert it
        // was exercised so req_verify's coverage still finds every id via the explicit per-id tests below.
        _ = req;
        Assert.Equal(displayName, kind.DisplayName);
        Assert.Equal(iconName, kind.IconName);
    }

    // req_verify.mjs reads Req traits from top-level, non-nested public test methods (R-TEST-2), which a
    // Theory's MemberData rows cannot satisfy on their own, so each PROVREG/PROVCLI identity id also gets
    // one dedicated Fact carrying its own citation.
    [Trait("Req", "PR-PROVREG-002")] // Core/Models/Provider.swift:23,:60
    [Fact]
    public void Codex_identity_is_Codex_and_provider_codex() => Assert.Equal(("Codex", "provider-codex"), (ProviderKind.Codex.DisplayName, ProviderKind.Codex.IconName));

    [Trait("Req", "PR-PROVREG-003")] // Core/Models/Provider.swift:24,:60
    [Fact]
    public void Claude_identity_is_Claude_and_provider_claude() => Assert.Equal(("Claude", "provider-claude"), (ProviderKind.Claude.DisplayName, ProviderKind.Claude.IconName));

    [Trait("Req", "PR-PROVREG-004")] // Core/Models/Provider.swift:25,:60
    [Fact]
    public void Cursor_identity_is_Cursor_and_provider_cursor() => Assert.Equal(("Cursor", "provider-cursor"), (ProviderKind.Cursor.DisplayName, ProviderKind.Cursor.IconName));

    [Trait("Req", "PR-PROVREG-005")] // Core/Models/Provider.swift:26,:60
    [Fact]
    public void Opencode_identity_is_OpenCode_Zen_and_provider_opencode() => Assert.Equal(("OpenCode Zen", "provider-opencode"), (ProviderKind.Opencode.DisplayName, ProviderKind.Opencode.IconName));

    [Trait("Req", "PR-PROVREG-006")] // Core/Models/Provider.swift:28,:60
    [Fact]
    public void Grok_identity_is_Grok_and_provider_grok() => Assert.Equal(("Grok", "provider-grok"), (ProviderKind.Grok.DisplayName, ProviderKind.Grok.IconName));

    [Trait("Req", "PR-PROVREG-007")] // Core/Models/Provider.swift:29,:60
    [Fact]
    public void Deepseek_identity_is_DeepSeek_and_provider_deepseek() => Assert.Equal(("DeepSeek", "provider-deepseek"), (ProviderKind.Deepseek.DisplayName, ProviderKind.Deepseek.IconName));

    [Trait("Req", "PR-PROVREG-008")] // Core/Models/Provider.swift:30,:60
    [Fact]
    public void Meta_identity_is_Meta_and_provider_meta() => Assert.Equal(("Meta", "provider-meta"), (ProviderKind.Meta.DisplayName, ProviderKind.Meta.IconName));

    [Trait("Req", "PR-PROVREG-009")] // Core/Models/Provider.swift:31,:60
    [Fact]
    public void Zai_identity_is_Z_dot_ai_and_provider_zai() => Assert.Equal(("Z.ai", "provider-zai"), (ProviderKind.Zai.DisplayName, ProviderKind.Zai.IconName));

    [Trait("Req", "PR-PROVREG-010")] // Core/Models/Provider.swift:32,:60
    [Fact]
    public void Devin_identity_is_Devin_and_provider_devin() => Assert.Equal(("Devin", "provider-devin"), (ProviderKind.Devin.DisplayName, ProviderKind.Devin.IconName));

    [Trait("Req", "PR-PROVREG-011")] // Core/Models/Provider.swift:33,:60
    [Fact]
    public void Antigravity_identity_is_Antigravity_and_provider_antigravity() => Assert.Equal(("Antigravity", "provider-antigravity"), (ProviderKind.Antigravity.DisplayName, ProviderKind.Antigravity.IconName));

    [Trait("Req", "PR-PROVREG-012")] // Core/Models/Provider.swift:34,:60
    [Fact]
    public void Copilot_identity_is_Copilot_never_GitHub_Copilot() => Assert.Equal(("Copilot", "provider-copilot"), (ProviderKind.Copilot.DisplayName, ProviderKind.Copilot.IconName));

    // PR-PROVCLI-203: commandcode's executableName is the bare "cmd" (:54), loginCommand "cmd login" (:76),
    // displayName "Command Code" (:35). This test proves only the model-level identity; the PATHEXT and
    // %SystemRoot% resolution of PA-0016/ED-127 lives in the ProviderRegistry unit.
    [Trait("Req", "PR-PROVCLI-203")] // Core/Models/Provider.swift:54,:76,:35
    [Fact]
    public void Commandcode_executable_login_and_display_name_are_exact()
    {
        Assert.Equal("cmd", ProviderKind.Commandcode.ExecutableName);
        Assert.Equal("cmd login", ProviderKind.Commandcode.LoginCommand);
        Assert.Equal("Command Code", ProviderKind.Commandcode.DisplayName);
    }

    // PR-PROVCLI-266: pi's executableName is "pi" (:55), loginCommand the bare "pi" (:77), displayName "Pi" (:36).
    [Trait("Req", "PR-PROVCLI-266")] // Core/Models/Provider.swift:55,:77,:36
    [Fact]
    public void Pi_executable_login_and_display_name_are_exact()
    {
        Assert.Equal("pi", ProviderKind.Pi.ExecutableName);
        Assert.Equal("pi", ProviderKind.Pi.LoginCommand);
        Assert.Equal("Pi", ProviderKind.Pi.DisplayName);
    }

    // PR-PROVCLI-317: antigravity's executableName is "agy" (:52), loginCommand the bare "agy" (:74),
    // displayName "Antigravity" (:33).
    [Trait("Req", "PR-PROVCLI-317")] // Core/Models/Provider.swift:52,:74,:33
    [Fact]
    public void Antigravity_executable_login_and_display_name_are_exact()
    {
        Assert.Equal("agy", ProviderKind.Antigravity.ExecutableName);
        Assert.Equal("agy", ProviderKind.Antigravity.LoginCommand);
        Assert.Equal("Antigravity", ProviderKind.Antigravity.DisplayName);
    }

    // --- Part 2: identity, executableName and loginCommand for the remaining records ---

    // PR-PROVREG-013: commandcode's rawValue is "commandcode" (:16), displayName "Command Code" (:35),
    // iconName "provider-commandcode" (:60).
    [Trait("Req", "PR-PROVREG-013")] // Core/Models/Provider.swift:16,:35,:60
    [Fact]
    public void Commandcode_rawValue_displayName_and_iconName_are_exact()
    {
        Assert.Equal("commandcode", SwiftEnum<ProviderKind>.RawValue(ProviderKind.Commandcode));
        Assert.Equal("Command Code", ProviderKind.Commandcode.DisplayName);
        Assert.Equal("provider-commandcode", ProviderKind.Commandcode.IconName);
    }

    // PR-PROVREG-014: pi's rawValue is "pi" (:17), displayName "Pi" with a capital P (:36), iconName
    // "provider-pi" (:60).
    [Trait("Req", "PR-PROVREG-014")] // Core/Models/Provider.swift:17,:36,:60
    [Fact]
    public void Pi_rawValue_displayName_and_iconName_are_exact()
    {
        Assert.Equal("pi", SwiftEnum<ProviderKind>.RawValue(ProviderKind.Pi));
        Assert.Equal("Pi", ProviderKind.Pi.DisplayName);
        Assert.Equal("provider-pi", ProviderKind.Pi.IconName);
    }

    [Trait("Req", "PR-PROVREG-015")] // Core/Models/Provider.swift:42
    [Fact]
    public void Codex_executableName_is_codex() => Assert.Equal("codex", ProviderKind.Codex.ExecutableName);

    [Trait("Req", "PR-PROVREG-016")] // Core/Models/Provider.swift:43
    [Fact]
    public void Claude_executableName_is_claude() => Assert.Equal("claude", ProviderKind.Claude.ExecutableName);

    // PR-PROVREG-017: the bare name searched is "cursor-agent", never the display name "Cursor".
    [Trait("Req", "PR-PROVREG-017")] // Core/Models/Provider.swift:44
    [Fact]
    public void Cursor_executableName_is_cursor_agent_not_cursor()
    {
        Assert.Equal("cursor-agent", ProviderKind.Cursor.ExecutableName);
        Assert.NotEqual("cursor", ProviderKind.Cursor.ExecutableName);
    }

    // PR-PROVREG-018: OpenCode Zen's executableName is "opencode" (:45); OpenCode Go resolves the same
    // bare name (:46), even though its displayName and iconName differ.
    [Trait("Req", "PR-PROVREG-018")] // Core/Models/Provider.swift:45-46
    [Fact]
    public void Opencode_and_OpencodeGo_executableName_is_the_same_opencode()
    {
        Assert.Equal("opencode", ProviderKind.Opencode.ExecutableName);
        Assert.Equal("opencode", ProviderKind.OpencodeGo.ExecutableName);
    }

    [Trait("Req", "PR-PROVREG-019")] // Core/Models/Provider.swift:47
    [Fact]
    public void Grok_executableName_is_grok() => Assert.Equal("grok", ProviderKind.Grok.ExecutableName);

    // PR-PROVREG-020/021/022: deepseek, meta and zai each have an empty executableName at the model level.
    // ProviderRegistry.executable(for:) never reaches this string because it returns nil at the
    // isAPIKeyBased guard first (ProviderRegistry.swift:220) - that guard and its nil result belong to a
    // different Swift file and unit; only the empty ProviderKind.executableName value is proven here.
    [Trait("Req", "PR-PROVREG-020")] // Core/Models/Provider.swift:48
    [Fact]
    public void Deepseek_executableName_is_empty() => Assert.Equal("", ProviderKind.Deepseek.ExecutableName);

    [Trait("Req", "PR-PROVREG-021")] // Core/Models/Provider.swift:49
    [Fact]
    public void Meta_executableName_is_empty() => Assert.Equal("", ProviderKind.Meta.ExecutableName);

    [Trait("Req", "PR-PROVREG-022")] // Core/Models/Provider.swift:50
    [Fact]
    public void Zai_executableName_is_empty() => Assert.Equal("", ProviderKind.Zai.ExecutableName);

    [Trait("Req", "PR-PROVREG-023")] // Core/Models/Provider.swift:51
    [Fact]
    public void Devin_executableName_is_devin() => Assert.Equal("devin", ProviderKind.Devin.ExecutableName);

    // PR-PROVREG-024: the bare name searched is "agy", never the display name "Antigravity".
    [Trait("Req", "PR-PROVREG-024")] // Core/Models/Provider.swift:52
    [Fact]
    public void Antigravity_executableName_is_agy_not_antigravity()
    {
        Assert.Equal("agy", ProviderKind.Antigravity.ExecutableName);
        Assert.NotEqual("antigravity", ProviderKind.Antigravity.ExecutableName);
    }

    [Trait("Req", "PR-PROVREG-025")] // Core/Models/Provider.swift:53
    [Fact]
    public void Copilot_executableName_is_copilot() => Assert.Equal("copilot", ProviderKind.Copilot.ExecutableName);

    // PR-PROVREG-026: the bare name searched first is "cmd" (:54). Whether that name collides with the
    // Windows command interpreter, and the PATHEXT/%SystemRoot% rejection that PA-0016/ED-127 resolve, is
    // ProviderRegistry.executable(for:) behaviour in a different Swift file and unit; only the model-level
    // ProviderKind.executableName value is proven here.
    [Trait("Req", "PR-PROVREG-026")] // Core/Models/Provider.swift:54
    [Fact]
    public void Commandcode_executableName_is_cmd() => Assert.Equal("cmd", ProviderKind.Commandcode.ExecutableName);

    [Trait("Req", "PR-PROVREG-027")] // Core/Models/Provider.swift:55
    [Fact]
    public void Pi_executableName_is_pi() => Assert.Equal("pi", ProviderKind.Pi.ExecutableName);

    [Trait("Req", "PR-PROVREG-028")] // Core/Models/Provider.swift:64
    [Fact]
    public void Codex_loginCommand_is_codex_login() => Assert.Equal("codex login", ProviderKind.Codex.LoginCommand);

    [Trait("Req", "PR-PROVREG-029")] // Core/Models/Provider.swift:65
    [Fact]
    public void Claude_loginCommand_is_claude_auth_login() => Assert.Equal("claude auth login", ProviderKind.Claude.LoginCommand);

    [Trait("Req", "PR-PROVREG-030")] // Core/Models/Provider.swift:66
    [Fact]
    public void Cursor_loginCommand_is_cursor_agent_login() => Assert.Equal("cursor-agent login", ProviderKind.Cursor.LoginCommand);

    // PR-PROVREG-031: OpenCode Zen's loginCommand is "opencode auth login" (:67); OpenCode Go runs the
    // same string (:68).
    [Trait("Req", "PR-PROVREG-031")] // Core/Models/Provider.swift:67-68
    [Fact]
    public void Opencode_and_OpencodeGo_loginCommand_is_the_same_opencode_auth_login()
    {
        Assert.Equal("opencode auth login", ProviderKind.Opencode.LoginCommand);
        Assert.Equal("opencode auth login", ProviderKind.OpencodeGo.LoginCommand);
    }

    [Trait("Req", "PR-PROVREG-032")] // Core/Models/Provider.swift:69
    [Fact]
    public void Grok_loginCommand_is_grok_login() => Assert.Equal("grok login", ProviderKind.Grok.LoginCommand);

    // PR-PROVREG-033: the literal display string is "DEEPSEEK_API_KEY=sk-..." with three ASCII full stops
    // (U+002E), never a single ellipsis character (U+2026), and is never executed by this model (the
    // Settings view never renders a Login button for an API-key provider - App/Views/Settings/
    // SettingsView.swift:753 - which is view-layer behaviour out of scope for this unit).
    [Trait("Req", "PR-PROVREG-033")] // Core/Models/Provider.swift:70
    [Fact]
    public void Deepseek_loginCommand_is_the_literal_api_key_placeholder_with_three_ascii_dots()
    {
        Assert.Equal("DEEPSEEK_API_KEY=sk-...", ProviderKind.Deepseek.LoginCommand);
        Assert.DoesNotContain('…', ProviderKind.Deepseek.LoginCommand);
        Assert.Equal(3, ProviderKind.Deepseek.LoginCommand.Count(c => c == '.'));
    }

    [Trait("Req", "PR-PROVREG-034")] // Core/Models/Provider.swift:71
    [Fact]
    public void Meta_loginCommand_is_the_literal_api_key_placeholder_with_three_ascii_dots()
    {
        Assert.Equal("MODEL_API_KEY=...", ProviderKind.Meta.LoginCommand);
        Assert.DoesNotContain('…', ProviderKind.Meta.LoginCommand);
        Assert.Equal(3, ProviderKind.Meta.LoginCommand.Count(c => c == '.'));
    }

    [Trait("Req", "PR-PROVREG-035")] // Core/Models/Provider.swift:72
    [Fact]
    public void Zai_loginCommand_is_the_literal_api_key_placeholder_with_three_ascii_dots()
    {
        Assert.Equal("ZAI_API_KEY=...", ProviderKind.Zai.LoginCommand);
        Assert.DoesNotContain('…', ProviderKind.Zai.LoginCommand);
        Assert.Equal(3, ProviderKind.Zai.LoginCommand.Count(c => c == '.'));
    }

    [Trait("Req", "PR-PROVREG-036")] // Core/Models/Provider.swift:73
    [Fact]
    public void Devin_loginCommand_is_devin_auth_login() => Assert.Equal("devin auth login", ProviderKind.Devin.LoginCommand);

    // --- SlashCommand ---

    // PR-PROVCLI-345: claudeCommands takes a name from entry["name"], falling back to the entry itself
    // being a bare string; a malformed entry (neither shape) is dropped, not failed. :335-340.
    [Trait("Req", "PR-PROVCLI-345")] // Core/Models/Provider.swift:335-340
    [Fact]
    public void ClaudeCommands_reads_name_from_object_field_or_bare_string_entry_and_drops_the_rest()
    {
        JSONValue[] entries =
        [
            JSONValue.FromDictionaryLiteral(("name", "review"), ("description", "Reviews the diff")),
            "compact", // bare string entry
            JSONValue.FromDictionaryLiteral(("description", "no name field")), // dropped: no name
            (JSONValue)42L, // dropped: neither object with string name, nor a string
        ];

        var commands = SlashCommand.ClaudeCommands(entries);

        Assert.Equal(2, commands.Count);
        Assert.Equal("review", commands[0].Name);
        Assert.Equal("Reviews the diff", commands[0].Detail);
        Assert.Equal("compact", commands[1].Name);
    }

    // PR-PROVCLI-346: a Claude command with no description takes the literal "Claude Code command", exact
    // casing, no trailing punctuation. A bare-string entry always takes this fallback. :338.
    [Trait("Req", "PR-PROVCLI-346")] // Core/Models/Provider.swift:338
    [Fact]
    public void ClaudeCommands_with_no_description_shows_the_literal_fallback()
    {
        JSONValue[] entries = [JSONValue.FromDictionaryLiteral(("name", "help")), "compact"];

        var commands = SlashCommand.ClaudeCommands(entries);

        Assert.Equal("Claude Code command", commands[0].Detail);
        Assert.Equal("Claude Code command", commands[1].Detail);
    }

    // PR-PROVCLI-347: codexSkills reads result["data"] as an array (empty when absent/wrong type), then for
    // each entry, entry["skills"] as an array (empty when absent), and flattens the two levels into one flat
    // list in data order then skills order. :342-350.
    [Trait("Req", "PR-PROVCLI-347")] // Core/Models/Provider.swift:342-350
    [Fact]
    public void CodexSkills_walks_data_then_skills_and_flattens_in_order()
    {
        JSONValue Skill(string name, string path) => JSONValue.FromDictionaryLiteral(("name", name), ("path", path));

        var result = JSONValue.FromDictionaryLiteral(("data", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("skills", JSONValue.FromArrayLiteral(Skill("a", "/a"), Skill("b", "/b")))),
            JSONValue.FromDictionaryLiteral(("skills", JSONValue.FromArrayLiteral(Skill("c", "/c")))))));

        var commands = SlashCommand.CodexSkills(result);

        Assert.Equal(["a", "b", "c"], commands.Select(c => c.Name));
    }

    [Trait("Req", "PR-PROVCLI-347")] // Core/Models/Provider.swift:342-343
    [Fact]
    public void CodexSkills_with_no_data_key_or_no_skills_key_yields_an_empty_list()
    {
        Assert.Empty(SlashCommand.CodexSkills(JSONValue.FromDictionaryLiteral()));
        Assert.Empty(SlashCommand.CodexSkills(JSONValue.FromDictionaryLiteral(("data", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("noSkillsHere", (JSONValue)true)))))));
    }

    // PR-PROVCLI-348: a skill counts as enabled unless enabled is explicitly false. true, absent and a
    // non-boolean value are all kept. :345.
    [Trait("Req", "PR-PROVCLI-348")] // Core/Models/Provider.swift:345
    [Fact]
    public void CodexSkills_drops_only_explicitly_disabled_skills()
    {
        JSONValue Skill(string name, JSONValue? enabled) => enabled is null
            ? JSONValue.FromDictionaryLiteral(("name", name), ("path", "/" + name))
            : JSONValue.FromDictionaryLiteral(("name", name), ("path", "/" + name), ("enabled", enabled));

        var result = JSONValue.FromDictionaryLiteral(("data", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("skills", JSONValue.FromArrayLiteral(
                Skill("keptTrue", true),
                Skill("keptAbsent", null),
                Skill("keptNonBool", (JSONValue)"maybe"),
                Skill("droppedFalse", false)))))));

        var commands = SlashCommand.CodexSkills(result);

        Assert.Equal(["keptTrue", "keptAbsent", "keptNonBool"], commands.Select(c => c.Name));
    }

    // PR-PROVCLI-349: a skill needs both name and path; missing either drops it. The kept skill's skillPath
    // is the skill's path, and isBuiltIn stays at its default false. :346-347,:297.
    [Trait("Req", "PR-PROVCLI-349")] // Core/Models/Provider.swift:346-347
    [Fact]
    public void CodexSkills_requires_both_name_and_path()
    {
        var result = JSONValue.FromDictionaryLiteral(("data", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("skills", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(("name", "onlyName")), // no path: dropped
                JSONValue.FromDictionaryLiteral(("path", "/onlyPath")), // no name: dropped
                JSONValue.FromDictionaryLiteral(("name", "full"), ("path", "/full"))))))));

        var commands = SlashCommand.CodexSkills(result);

        var kept = Assert.Single(commands);
        Assert.Equal("full", kept.Name);
        Assert.Equal("/full", kept.SkillPath);
        Assert.False(kept.IsBuiltIn);
    }

    // PR-PROVCLI-350: detail is skill["interface"]["shortDescription"], then skill["description"], then "".
    // :347.
    [Trait("Req", "PR-PROVCLI-350")] // Core/Models/Provider.swift:347
    [Fact]
    public void CodexSkills_detail_prefers_interface_shortDescription_then_description_then_empty()
    {
        var result = JSONValue.FromDictionaryLiteral(("data", JSONValue.FromArrayLiteral(
            JSONValue.FromDictionaryLiteral(("skills", JSONValue.FromArrayLiteral(
                JSONValue.FromDictionaryLiteral(
                    ("name", "withInterface"), ("path", "/i"),
                    ("interface", JSONValue.FromDictionaryLiteral(("shortDescription", "short one"))),
                    ("description", "long one")),
                JSONValue.FromDictionaryLiteral(
                    ("name", "onlyDescription"), ("path", "/d"), ("description", "the description")),
                JSONValue.FromDictionaryLiteral(("name", "neither"), ("path", "/n"))))))));

        var commands = SlashCommand.CodexSkills(result);

        Assert.Equal("short one", commands[0].Detail);
        Assert.Equal("the description", commands[1].Detail);
        Assert.Equal("", commands[2].Detail);
    }

    // PR-PROVCLI-351/352/353: codexInput sends one plain text part unless the command owns a skillPath AND
    // the text still names it (SlashCommand.name(in:) == this command's name); when both hold it rewrites
    // the leading "/" or "$" to "$" and appends a {"type":"skill", "name", "path"} part. :301-304,:325-333.
    [Trait("Req", "PR-PROVCLI-351")] // Core/Models/Provider.swift:325-328
    [Fact]
    public void CodexInput_is_one_plain_text_part_when_there_is_no_skillPath()
    {
        var command = new SlashCommand { Name = "compact", Detail = "d" }; // no skillPath

        var parts = command.CodexInput("/compact now");

        var part = Assert.Single(parts);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "/compact now")), part);
    }

    [Trait("Req", "PR-PROVCLI-351")] // Core/Models/Provider.swift:325-328,:301-304
    [Fact]
    public void CodexInput_is_one_plain_text_part_when_the_text_no_longer_names_the_skill()
    {
        var command = new SlashCommand { Name = "review", Detail = "d", SkillPath = "/skills/review" };

        // Text edited away from the command name: degrades to plain text.
        var parts = command.CodexInput("just a message");

        var part = Assert.Single(parts);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "just a message")), part);
    }

    [Trait("Req", "PR-PROVCLI-352")] // Core/Models/Provider.swift:330
    [Fact]
    public void CodexInput_rewrites_the_leading_slash_to_a_dollar_sign_when_the_skill_matches()
    {
        var command = new SlashCommand { Name = "review", Detail = "d", SkillPath = "/skills/review" };

        var parts = command.CodexInput("/review the diff");

        Assert.Equal(2, parts.Count);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "$review the diff")), parts[0]);
    }

    [Trait("Req", "PR-PROVCLI-352")] // Core/Models/Provider.swift:330,:301-304
    [Fact]
    public void CodexInput_drops_exactly_one_character_so_a_lone_slash_becomes_a_dollar_sign()
    {
        var command = new SlashCommand { Name = "review", Detail = "d", SkillPath = "/skills/review" };

        var parts = command.CodexInput("/review");

        Assert.Equal(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "$review")), parts[0]);
    }

    [Trait("Req", "PR-PROVCLI-353")] // Core/Models/Provider.swift:331
    [Fact]
    public void CodexInput_appends_a_skill_part_naming_the_command_and_its_path()
    {
        var command = new SlashCommand { Name = "review", Detail = "d", SkillPath = "/skills/review" };

        var parts = command.CodexInput("/review the diff");

        Assert.Equal(2, parts.Count);
        Assert.Equal(
            JSONValue.FromDictionaryLiteral(("type", "skill"), ("name", "review"), ("path", "/skills/review")),
            parts[1]);
    }

    [Trait("Req", "PR-PROVCLI-351")] // Core/Models/Provider.swift:301-304
    [Fact]
    public void CodexInput_accepts_a_dollar_opener_too_because_name_in_recognizes_both()
    {
        var command = new SlashCommand { Name = "review", Detail = "d", SkillPath = "/skills/review" };

        var parts = command.CodexInput("$review the diff");

        // "$review" already begins with "$"; the rewrite is a no-op in meaning, still "$" + dropFirst.
        Assert.Equal(2, parts.Count);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "$review the diff")), parts[0]);
    }
}
