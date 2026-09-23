using System.Diagnostics;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Provider.swift (B2 1fbf81e), :1-351.
// SKELETON (cycle c4). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Codable conformance (the synthesized coding and :244-253 init(from:)) is left to the implementing unit.
// Unit Core.Models.Provider part 1 of 5 implements: ProviderKind.Id, DisplayName, ExecutableName, IconName
// and LoginCommand (PR-PROVREG-001 to 012, PR-PROVCLI-203, 266, 317), and SlashCommand.NameIn,
// ClaudeCommands, CodexSkills and CodexInput (PR-PROVCLI-345 to 353). Every other member in this file stays
// MARROW_STUB for a later part.

// :3-18
public enum ProviderKind
{
    [RawValue("codex")] Codex,
    [RawValue("claude")] Claude,
    [RawValue("cursor")] Cursor,
    [RawValue("opencode")] Opencode,
    [RawValue("opencodeGo")] OpencodeGo,
    [RawValue("grok")] Grok,
    [RawValue("deepseek")] Deepseek,
    [RawValue("meta")] Meta,
    [RawValue("zai")] Zai,
    [RawValue("devin")] Devin,
    [RawValue("antigravity")] Antigravity,
    [RawValue("copilot")] Copilot,
    [RawValue("commandcode")] Commandcode,
    [RawValue("pi")] Pi,
}

public static class ProviderKindExtensions
{
    extension(ProviderKind kind)
    {
        // :19 `var id: String { rawValue }`.
        public string Id => SwiftEnum<ProviderKind>.RawValue(kind);

        // :21-38.
        public string DisplayName => kind switch
        {
            ProviderKind.Codex => "Codex", // :23
            ProviderKind.Claude => "Claude", // :24
            ProviderKind.Cursor => "Cursor", // :25
            ProviderKind.Opencode => "OpenCode Zen", // :26
            ProviderKind.OpencodeGo => "OpenCode Go", // :27
            ProviderKind.Grok => "Grok", // :28
            ProviderKind.Deepseek => "DeepSeek", // :29
            ProviderKind.Meta => "Meta", // :30
            ProviderKind.Zai => "Z.ai", // :31
            ProviderKind.Devin => "Devin", // :32
            ProviderKind.Antigravity => "Antigravity", // :33
            ProviderKind.Copilot => "Copilot", // :34
            ProviderKind.Commandcode => "Command Code", // :35
            ProviderKind.Pi => "Pi", // :36
            _ => throw new UnreachableException(),
        };

        // :40-57. deepseek, meta and zai are API-key providers with no local CLI, so the empty string.
        public string ExecutableName => kind switch
        {
            ProviderKind.Codex => "codex", // :42
            ProviderKind.Claude => "claude", // :43
            ProviderKind.Cursor => "cursor-agent", // :44
            ProviderKind.Opencode => "opencode", // :45
            ProviderKind.OpencodeGo => "opencode", // :46
            ProviderKind.Grok => "grok", // :47
            ProviderKind.Deepseek => "", // :48
            ProviderKind.Meta => "", // :49
            ProviderKind.Zai => "", // :50
            ProviderKind.Devin => "devin", // :51
            ProviderKind.Antigravity => "agy", // :52 - PR-PROVCLI-317
            ProviderKind.Copilot => "copilot", // :53
            ProviderKind.Commandcode => "cmd", // :54 - PR-PROVCLI-203. See PA-0016/ED-020/ED-127 for the
                                                // Windows resolver that keeps this literal name from matching
                                                // the command interpreter; that resolver lives in the
                                                // ProviderRegistry unit, not here.
            ProviderKind.Pi => "pi", // :55 - PR-PROVCLI-266
            _ => throw new UnreachableException(),
        };

        // :60. Both OpenCode plans are the one `opencode` CLI; the mark is the same.
        public string IconName => kind == ProviderKind.OpencodeGo ? "provider-opencode" : $"provider-{SwiftEnum<ProviderKind>.RawValue(kind)}";

        // :62-79.
        public string LoginCommand => kind switch
        {
            ProviderKind.Codex => "codex login", // :64
            ProviderKind.Claude => "claude auth login", // :65
            ProviderKind.Cursor => "cursor-agent login", // :66
            ProviderKind.Opencode => "opencode auth login", // :67
            ProviderKind.OpencodeGo => "opencode auth login", // :68
            ProviderKind.Grok => "grok login", // :69
            ProviderKind.Deepseek => "DEEPSEEK_API_KEY=sk-...", // :70
            ProviderKind.Meta => "MODEL_API_KEY=...", // :71
            ProviderKind.Zai => "ZAI_API_KEY=...", // :72
            ProviderKind.Devin => "devin auth login", // :73
            ProviderKind.Antigravity => "agy", // :74 - PR-PROVCLI-317
            ProviderKind.Copilot => "copilot login", // :75
            ProviderKind.Commandcode => "cmd login", // :76 - PR-PROVCLI-203
            ProviderKind.Pi => "pi", // :77 - PR-PROVCLI-266
            _ => throw new UnreachableException(),
        };

        public SwiftURL InstallURL => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:81");
        public bool IsAPIKeyBased => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:101");
        public bool UsesACP => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:104");
        public bool AcceptsAPIKey => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:109");
        public string? ApiHost => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:112");
        public string? ApiKeySource => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:122");
        public string? ApiKeyEnvVar => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:133");
        public bool SupportsRewind => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:144");
        public bool SupportsCompact => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:149");
        public bool SupportsImages => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:158");
    }
}

// :161-165
public enum RuntimeMode
{
    [RawValue("supervised")] Supervised,
    [RawValue("autoAcceptEdits")] AutoAcceptEdits,
    [RawValue("auto")] Auto,
    [RawValue("fullAccess")] FullAccess,
}

public static class RuntimeModeExtensions
{
    extension(RuntimeMode mode)
    {
        public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:167");
        public string Title => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:169");
        public string Summary => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:178");
        public string Symbol => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:187");
    }
}

// :197-200
public enum InteractionMode
{
    [RawValue("build")] Build,
    [RawValue("plan")] Plan,
}

// :202-204
public enum WorkspaceMode
{
    [RawValue("local")] Local,
    [RawValue("worktree")] Worktree,
}

public static class WorkspaceModeExtensions
{
    extension(WorkspaceMode mode)
    {
        public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:206");
        public string Title => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:208");
    }
}

// :216-291
public sealed record ModelOption
{
    public string Id { get; init; }                 // :217
    public string Name { get; init; }               // :218
    public string? Detail { get; init; }            // :219
    public ValueList<string> Efforts { get; init; } // :220
    public string? DefaultEffort { get; init; }     // :221
    public bool IsDefault { get; init; }            // :222
    public string? FastTier { get; init; }          // :224

    // :226-242. The Swift default `efforts: [String] = []` is a null default here.
    public ModelOption(string id, string name, string? detail = null, ValueList<string>? efforts = null,
        string? defaultEffort = null, bool isDefault = false, string? fastTier = null)
    {
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:226");
    }

    public bool SupportsFast => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:255");
    public string ShortName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:258");
    public string ChipName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:268");

    public static string EffortTitle(string effort) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:284");
}

// :293-351
public sealed record SlashCommand
{
    public required string Name { get; init; }   // :294
    public required string Detail { get; init; } // :295
    public bool IsBuiltIn { get; init; } = false; // :296
    public string? SkillPath { get; init; }      // :297

    public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:299");

    // :301-304 `static func name(in text: String) -> String?`. C# cannot give a static method the name of
    // the property Name, so it is NameIn. Requires text to open with "/" or "$", then the first whitespace-
    // separated token of everything after that first character (Character.isWhitespace, R-STR-3), or nil
    // when nothing but whitespace follows (Swift's `split(whereSeparator:)` omits empty subsequences and
    // `.first` on an empty result is nil).
    public static string? NameIn(string @in)
    {
        if (@in.Length == 0 || (@in[0] != '/' && @in[0] != '$')) return null; // :302
        return FirstWhitespaceSeparatedToken(SwiftString.DropFirst(@in)); // :303
    }

    private static string? FirstWhitespaceSeparatedToken(string text)
    {
        var token = new StringBuilder();
        var started = false;
        foreach (var character in SwiftString.Characters(text))
        {
            if (SwiftCharacter.IsWhitespace(character))
            {
                if (started) break;
                continue;
            }
            started = true;
            token.Append(character);
        }
        return started ? token.ToString() : null;
    }

    public static ValueList<SlashCommand> Suggestions(IReadOnlyList<SlashCommand> commands, string matching,
        IReadOnlyList<string> recent) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:306");

    // :325-333. PR-PROVCLI-351/352/353. One plain text part unless both hold: this command owns a
    // skillPath, AND the text still names it (NameIn(text) == Name). Then two parts: the rewritten text
    // ("$" + everything but the first character of the ORIGINAL text) and a skill part naming this command
    // and its path.
    public ValueList<JSONValue> CodexInput(string text)
    {
        if (SkillPath is not { } skillPath || NameIn(text) != Name) // :326
            return new ValueList<JSONValue>([JSONValue.FromDictionaryLiteral(("type", "text"), ("text", text))]); // :327

        return new ValueList<JSONValue>(
        [
            JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "$" + SwiftString.DropFirst(text))), // :330
            JSONValue.FromDictionaryLiteral(("type", "skill"), ("name", Name), ("path", skillPath)), // :331
        ]);
    }

    // :335-340. PR-PROVCLI-345/346. Name from entry["name"] (object shape) or the entry itself (bare
    // string); a malformed entry (neither) is dropped, never fails the whole list. Detail from
    // entry["description"], else the literal "Claude Code command".
    public static ValueList<SlashCommand> ClaudeCommands(IReadOnlyList<JSONValue> entries)
    {
        var result = new List<SlashCommand>();
        foreach (var entry in entries)
        {
            var name = entry["name"]?.JSONValueString ?? entry.JSONValueString; // :337
            if (name is null) continue;
            result.Add(new SlashCommand
            {
                Name = name,
                Detail = entry["description"]?.JSONValueString ?? "Claude Code command", // :338
            });
        }
        return new ValueList<SlashCommand>(result);
    }

    // :342-350. PR-PROVCLI-347/348/349/350. Two levels, result["data"] then each entry's "skills", both
    // defaulting to empty and flattened with flatMap into one flat list, data order then skills order. A
    // skill needs enabled != false (kept: true, absent, non-boolean; dropped: explicitly false), and both a
    // name and a path (skillPath). Detail: interface.shortDescription, then description, then "".
    public static ValueList<SlashCommand> CodexSkills(JSONValue result)
    {
        var commands = new List<SlashCommand>();
        var data = result["data"]?.JSONValueArray ?? []; // :343
        foreach (var entry in data)
        {
            var skills = entry["skills"]?.JSONValueArray ?? []; // :344
            foreach (var skill in skills)
            {
                if (skill["enabled"]?.JSONValueBool == false) continue; // :345
                var name = skill["name"]?.JSONValueString; // :346
                var path = skill["path"]?.JSONValueString; // :346
                if (name is null || path is null) continue; // :346-347
                var detail = skill["interface"]?["shortDescription"]?.JSONValueString // :347
                    ?? skill["description"]?.JSONValueString
                    ?? "";
                commands.Add(new SlashCommand { Name = name, Detail = detail, SkillPath = path });
            }
        }
        return new ValueList<SlashCommand>(commands);
    }
}
