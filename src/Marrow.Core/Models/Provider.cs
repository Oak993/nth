using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Provider.swift (B3 52bc792), :1-351.
// Unit Core.Models.Provider part 1 of 5 implemented ProviderKind.Id, DisplayName, ExecutableName, IconName
// and LoginCommand (PR-PROVREG-001 to 012, PR-PROVCLI-203, 266, 317), and SlashCommand.NameIn,
// ClaudeCommands, CodexSkills and CodexInput (PR-PROVCLI-345 to 353). Part 1's switch statements already
// covered every ProviderKind case, so part 2 of 5 (PR-PROVREG-013 to 036: commandcode and pi identity, and
// executableName/loginCommand for every remaining case) adds no new arms, only proves the remaining record
// ids against DisplayName, ExecutableName, IconName and LoginCommand as part 1 wrote them. Part 3 (PR-
// PROVREG-037 to 060) and part 4 (PR-PROVREG-061 to 074, 157 to 161, 240 to 244) implemented InstallURL,
// IsAPIKeyBased, UsesACP, AcceptsAPIKey, ApiHost, ApiKeySource, ApiKeyEnvVar, SupportsRewind,
// SupportsCompact, SupportsImages and ModelOption in full. Part 5 of 5 (PR-PROVREG-245, UI-PROVACP-034)
// added no new SupportsCompact arms (already covered by part 4) and implemented RuntimeMode's Id, Title,
// Summary and Symbol. The follow-up ports SlashCommand.Suggestions for PR-COMPOSER-042 and the three
// remaining identity/title members directly from the Swift under R-FILE-1. No throwing stubs remain.

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
    // PORT-ONLY (OD-0049, OD-0060, ED-234): no Swift case. An API-key provider in the manner of deepseek, meta and zai. The
    // case name is the brand's own spelling (R-NAME-2 would give "Openrouter"; the raw value is the lowercase wire form).
    [RawValue("openrouter")] OpenRouter,
    // PORT-ONLY (OD-0114, ED-410): keyless OpenAI-compatible servers, each with its own model selection.
    [RawValue("local")] Local,
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
            ProviderKind.Commandcode => "Command Code", // :35 - PR-PROVREG-013
            ProviderKind.Pi => "Pi", // :36 - PR-PROVREG-014
            ProviderKind.OpenRouter => "OpenRouter", // ED-234
            ProviderKind.Local => "Local", // ED-410
            _ => throw new UnreachableException(),
        };

        // :40-57. deepseek, meta and zai are API-key providers with no local CLI, so the empty string.
        public string ExecutableName => kind switch
        {
            ProviderKind.Codex => "codex", // :42 - PR-PROVREG-015
            ProviderKind.Claude => "claude", // :43 - PR-PROVREG-016
            ProviderKind.Cursor => "cursor-agent", // :44 - PR-PROVREG-017
            ProviderKind.Opencode => "opencode", // :45 - PR-PROVREG-018
            ProviderKind.OpencodeGo => "opencode", // :46 - PR-PROVREG-018
            ProviderKind.Grok => "grok", // :47 - PR-PROVREG-019
            ProviderKind.Deepseek => "", // :48 - PR-PROVREG-020
            ProviderKind.Meta => "", // :49 - PR-PROVREG-021
            ProviderKind.Zai => "", // :50 - PR-PROVREG-022
            ProviderKind.Devin => "devin", // :51 - PR-PROVREG-023
            ProviderKind.Antigravity => "agy", // :52 - PR-PROVCLI-317, PR-PROVREG-024
            ProviderKind.Copilot => "copilot", // :53 - PR-PROVREG-025
            ProviderKind.Commandcode => "cmd", // :54 - PR-PROVCLI-203, PR-PROVREG-026. See PA-0016/ED-020/
                                                // ED-127 for the Windows resolver that keeps this literal
                                                // name from matching the command interpreter; that resolver
                                                // lives in the ProviderRegistry unit, not here.
            ProviderKind.Pi => "pi", // :55 - PR-PROVCLI-266, PR-PROVREG-027
            ProviderKind.OpenRouter => "", // ED-234: an API-key provider with no local CLI, like deepseek, meta and zai
            ProviderKind.Local => "", // ED-410
            _ => throw new UnreachableException(),
        };

        // :60. Both OpenCode plans are the one `opencode` CLI; the mark is the same.
        public string IconName => kind == ProviderKind.OpencodeGo ? "provider-opencode" : $"provider-{SwiftEnum<ProviderKind>.RawValue(kind)}";

        // :62-79.
        public string LoginCommand => kind switch
        {
            ProviderKind.Codex => "codex login", // :64 - PR-PROVREG-028
            ProviderKind.Claude => "claude auth login", // :65 - PR-PROVREG-029
            ProviderKind.Cursor => "cursor-agent login", // :66 - PR-PROVREG-030
            ProviderKind.Opencode => "opencode auth login", // :67 - PR-PROVREG-031
            ProviderKind.OpencodeGo => "opencode auth login", // :68 - PR-PROVREG-031
            ProviderKind.Grok => "grok login", // :69 - PR-PROVREG-032
            ProviderKind.Deepseek => "DEEPSEEK_API_KEY=sk-...", // :70 - PR-PROVREG-033
            ProviderKind.Meta => "MODEL_API_KEY=...", // :71 - PR-PROVREG-034
            ProviderKind.Zai => "ZAI_API_KEY=...", // :72 - PR-PROVREG-035
            ProviderKind.Devin => "devin auth login", // :73 - PR-PROVREG-036
            ProviderKind.Antigravity => "agy", // :74 - PR-PROVCLI-317
            ProviderKind.Copilot => "copilot login", // :75
            ProviderKind.Commandcode => "cmd login", // :76 - PR-PROVCLI-203
            ProviderKind.Pi => "pi", // :77 - PR-PROVCLI-266
            ProviderKind.OpenRouter => "OPENROUTER_API_KEY=sk-or-...", // ED-234
            ProviderKind.Local => "", // ED-410: no key or login
            _ => throw new UnreachableException(),
        };

        // :81-98. `URL(string:)!` force-unwraps: every literal below is a valid URL, so the bang never
        // traps; SwiftURL.Create mirrors that constructor and the "!" is SwiftTraps.Unwrap (R-NAME-5).
        public SwiftURL InstallURL => kind switch
        {
            ProviderKind.Codex => SwiftTraps.Unwrap(SwiftURL.Create("https://developers.openai.com/codex/cli")), // :83 - PR-PROVREG-041
            ProviderKind.Claude => SwiftTraps.Unwrap(SwiftURL.Create("https://claude.com/product/claude-code")), // :84 - PR-PROVREG-042
            ProviderKind.Cursor => SwiftTraps.Unwrap(SwiftURL.Create("https://cursor.com/cli")), // :85 - PR-PROVREG-043
            ProviderKind.Opencode => SwiftTraps.Unwrap(SwiftURL.Create("https://opencode.ai")), // :86 - PR-PROVREG-044
            ProviderKind.OpencodeGo => SwiftTraps.Unwrap(SwiftURL.Create("https://opencode.ai")), // :87
            ProviderKind.Grok => SwiftTraps.Unwrap(SwiftURL.Create("https://x.ai/cli")), // :88 - PR-PROVREG-045
            ProviderKind.Deepseek => SwiftTraps.Unwrap(SwiftURL.Create("https://platform.deepseek.com/api_keys")), // :89 - PR-PROVREG-046
            ProviderKind.Meta => SwiftTraps.Unwrap(SwiftURL.Create("https://dev.meta.ai/")), // :90 - PR-PROVREG-047
            ProviderKind.Zai => SwiftTraps.Unwrap(SwiftURL.Create("https://z.ai/manage-apikey/apikey-list")), // :91 - PR-PROVREG-048
            ProviderKind.Devin => SwiftTraps.Unwrap(SwiftURL.Create("https://cli.devin.ai")), // :92 - PR-PROVREG-049
            ProviderKind.Antigravity => SwiftTraps.Unwrap(SwiftURL.Create("https://antigravity.google/docs/cli/overview")), // :93 - PR-PROVREG-050
            ProviderKind.Copilot => SwiftTraps.Unwrap(SwiftURL.Create("https://github.com/github/copilot-cli")), // :94 - PR-PROVREG-051
            ProviderKind.Commandcode => SwiftTraps.Unwrap(SwiftURL.Create("https://commandcode.ai/docs/quickstart")), // :95 - PR-PROVREG-052
            ProviderKind.Pi => SwiftTraps.Unwrap(SwiftURL.Create("https://pi.dev/docs/latest")), // :96 - PR-PROVREG-053
            ProviderKind.OpenRouter => SwiftTraps.Unwrap(SwiftURL.Create("https://openrouter.ai/keys")), // ED-234: where a key is made
            ProviderKind.Local => SwiftTraps.Unwrap(SwiftURL.Create("https://ollama.com/")), // ED-410: server's own app
            _ => throw new UnreachableException(),
        };

        // :101 `self == .deepseek || self == .meta || self == .zai`, and OpenRouter (ED-234).
        public bool IsAPIKeyBased => kind is ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter; // PR-PROVREG-059, 060

        // :104 `self == .cursor || self == .opencode || self == .opencodeGo || self == .grok || self == .devin`.
        public bool UsesACP => kind is ProviderKind.Cursor or ProviderKind.Opencode or ProviderKind.OpencodeGo
            or ProviderKind.Grok or ProviderKind.Devin; // PR-PROVREG-056, 057, 058

        // :109 `isAPIKeyBased || self == .commandcode`.
        public bool AcceptsAPIKey => kind.IsAPIKeyBased || kind == ProviderKind.Commandcode; // PR-PROVREG-059

        // :112-119.
        public string? ApiHost => kind switch
        {
            ProviderKind.Deepseek => "api.deepseek.com", // :114 - PR-PROVREG-059
            ProviderKind.Meta => "api.meta.ai/v1", // :115 - PR-PROVREG-060
            ProviderKind.Zai => "api.z.ai/api/coding/paas/v4", // :116
            ProviderKind.OpenRouter => "openrouter.ai/api/v1", // ED-234
            _ => null, // :117
        };

        // :122-130.
        public string? ApiKeySource => kind switch
        {
            ProviderKind.Deepseek => "platform.deepseek.com", // :124 - PR-PROVREG-059
            ProviderKind.Meta => "dev.meta.ai", // :125 - PR-PROVREG-060
            ProviderKind.Zai => "z.ai/manage-apikey/apikey-list", // :126
            ProviderKind.Commandcode => "commandcode.ai/studio", // :127 - PR-PROVREG-039
            ProviderKind.OpenRouter => "openrouter.ai/keys", // ED-234
            _ => null, // :128
        };

        // :133-141.
        public string? ApiKeyEnvVar => kind switch
        {
            ProviderKind.Deepseek => "DEEPSEEK_API_KEY", // :135 - PR-PROVREG-059
            ProviderKind.Meta => "MODEL_API_KEY", // :136 - PR-PROVREG-060
            ProviderKind.Zai => "ZAI_API_KEY", // :137
            ProviderKind.Commandcode => "COMMAND_CODE_API_KEY", // :138
            ProviderKind.OpenRouter => "OPENROUTER_API_KEY", // ED-234: the variable PiCLI.swift:38 already passes through
            _ => null, // :139
        };

        // :144 `self == .codex || self == .claude || self == .copilot`.
        public bool SupportsRewind => kind is ProviderKind.Codex or ProviderKind.Claude or ProviderKind.Copilot; // PR-PROVREG-054, 055

        // :149-154.
        public bool SupportsCompact => kind switch
        {
            ProviderKind.Codex or ProviderKind.Claude or ProviderKind.Copilot or ProviderKind.Deepseek
                or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.Pi or ProviderKind.OpenRouter or ProviderKind.Local => true, // :151, ED-234, ED-410
            _ => false, // :152 - PR-PROVREG-057
        };

        // :158 `self != .grok && self != .antigravity`.
        public bool SupportsImages => kind != ProviderKind.Grok && kind != ProviderKind.Antigravity; // PR-PROVREG-058
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
        // :167 `var id: String { rawValue }`.
        public string Id => SwiftEnum<RuntimeMode>.RawValue(mode);

        // :169-176. UI-PROVACP-034: exact row titles, one hyphen and lowercase "e" in "Auto-accept edits".
        public string Title => mode switch
        {
            RuntimeMode.Supervised => "Supervised", // :171
            RuntimeMode.AutoAcceptEdits => "Auto-accept edits", // :172
            RuntimeMode.Auto => "Auto", // :173
            RuntimeMode.FullAccess => "Full access", // :174
            _ => throw new UnreachableException(),
        };

        // :178-185. UI-PROVACP-034: exact row summaries.
        public string Summary => mode switch
        {
            RuntimeMode.Supervised => "Asks before commands and file changes", // :180
            RuntimeMode.AutoAcceptEdits => "Edits files freely, asks before other actions", // :181
            RuntimeMode.Auto => "The provider's reviewer approves routine actions", // :182
            RuntimeMode.FullAccess => "Runs commands and edits without asking", // :183
            _ => throw new UnreachableException(),
        };

        // :187-194. UI-PROVACP-034: SF Symbol names, ported as data (never drawn as literal text); the
        // Windows icon substitution for each is a platform boundary handled elsewhere (UNK-0007/PA-0012).
        public string Symbol => mode switch
        {
            RuntimeMode.Supervised => "hand.raised", // :189
            RuntimeMode.AutoAcceptEdits => "pencil.line", // :190
            RuntimeMode.Auto => "wand.and.sparkles", // :191
            RuntimeMode.FullAccess => "lock.open", // :192
            _ => throw new UnreachableException(),
        };
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
        // :206 `var id: String { rawValue }`.
        public string Id => SwiftEnum<WorkspaceMode>.RawValue(mode);

        // :208-213.
        public string Title => mode switch
        {
            WorkspaceMode.Local => "Local", // :210
            WorkspaceMode.Worktree => "New worktree", // :211
            _ => throw new UnreachableException(),
        };
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
        Id = id; // :235
        Name = name; // :236
        Detail = detail; // :237
        Efforts = efforts ?? ValueList<string>.Empty; // :238
        DefaultEffort = defaultEffort; // :239
        IsDefault = isDefault; // :240
        FastTier = fastTier; // :241
    }

    // :244-253 `init(from decoder: Decoder)`. id and name are required and strictly typed (PR-PROVREG-157);
    // detail, defaultEffort and fastTier are optional; efforts defaults to the empty array and isDefault to
    // false when absent, so a row written by an older build without either still decodes.
    public static ModelOption Decode(JsonElement element) => new(
        id: SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadString), // :246
        name: SwiftDecoding.Decode(element, "name", SwiftDecoding.ReadString), // :247
        detail: SwiftDecoding.DecodeIfPresent(element, "detail", SwiftDecoding.ReadString), // :248
        efforts: SwiftDecoding.DecodeIfPresent(element, "efforts", e => SwiftDecoding.ReadArray(e, SwiftDecoding.ReadString))
            ?? ValueList<string>.Empty, // :249
        defaultEffort: SwiftDecoding.DecodeIfPresent(element, "defaultEffort", SwiftDecoding.ReadString), // :250
        isDefault: SwiftDecoding.DecodeIfPresentValue(element, "isDefault", SwiftDecoding.ReadBool) ?? false, // :251
        fastTier: SwiftDecoding.DecodeIfPresent(element, "fastTier", SwiftDecoding.ReadString)); // :252

    // :255 `fastTier != nil`. PR-PROVREG-158: true even when fastTier is the empty string.
    public bool SupportsFast => FastTier is not null;

    // :257-262. PR-PROVREG-159: drop a leading "gpt-" (case-insensitive test, exact 4-character count drop),
    // then turn every remaining hyphen into a space.
    public string ShortName
    {
        get
        {
            var trimmed = Name; // :259
            if (SwiftString.HasPrefix(SwiftString.Lowercased(trimmed), "gpt-")) trimmed = SwiftString.DropFirst(trimmed, 4); // :260
            return trimmed.Replace("-", " ", StringComparison.Ordinal); // :261 `replacingOccurrences(of: "-", with: " ")`
        }
    }

    // :264-282. PR-PROVREG-160: cut at the first of six separators (loop continues over all six, so later
    // separators still apply to the remainder), then drop the first matching vendor prefix among ten
    // (loop breaks on first match; the length guard keeps a text equal to the prefix untouched), trim, and
    // fall back to shortName when the result is empty.
    private static readonly string[] ChipNameSeparators = [" (", " · ", " — ", " – ", " - ", ": "]; // :270
    private static readonly string[] ChipNameVendorPrefixes =
        ["claude ", "gpt ", "gemini ", "deepseek ", "openai ", "anthropic ", "google ", "meta ", "muse ", "glm "]; // :274

    public string ChipName
    {
        get
        {
            var text = ShortName; // :269
            foreach (var separator in ChipNameSeparators) // :270-272
            {
                var index = text.IndexOf(separator, StringComparison.Ordinal);
                if (index >= 0) text = text[..index];
            }
            var lowered = SwiftString.Lowercased(text); // :273
            foreach (var vendor in ChipNameVendorPrefixes) // :274-279
            {
                if (SwiftString.HasPrefix(lowered, vendor) && SwiftString.Count(text) > SwiftString.Count(vendor))
                {
                    text = SwiftString.DropFirst(text, SwiftString.Count(vendor)); // :276
                    break; // :277
                }
            }
            var cleaned = FoundationText.Trim(text); // :280 `.trimmingCharacters(in: .whitespaces)`
            return cleaned.Length == 0 ? ShortName : cleaned; // :281
        }
    }

    // :284-290. PR-PROVREG-161: "xhigh" and "extra-high" both title as "Extra High"; the empty string as
    // "Default"; otherwise the first character upper-cased and the rest kept as is (NOT lowercased).
    public static string EffortTitle(string effort) => effort switch
    {
        "xhigh" or "extra-high" => "Extra High", // :286
        "" => "Default", // :287
        _ => SwiftString.Uppercased(SwiftString.Prefix(effort, 1)) + SwiftString.DropFirst(effort, 1), // :288 `effort.prefix(1).uppercased() + effort.dropFirst()`
    };
}

// :293-351
public sealed record SlashCommand
{
    public required string Name { get; init; }   // :294
    public required string Detail { get; init; } // :295
    public bool IsBuiltIn { get; init; } = false; // :296
    public string? SkillPath { get; init; }      // :297

    // :299 `var id: String { name }`.
    public string Id => Name;

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

    // :306-323. PR-COMPOSER-042: first name wins; prefix, earliest recent rank, then lowercased name.
    public static ValueList<SlashCommand> Suggestions(IReadOnlyList<SlashCommand> commands, string matching,
        IReadOnlyList<string> recent)
    {
        var needle = SwiftString.Lowercased(matching); // :307
        var ranks = new Dictionary<string, int>(SwiftStringComparer.Instance); // :308, R-STR-2
        for (var index = 0; index < recent.Count; index++)
            ranks.TryAdd(recent[index], index); // :308 `uniquingKeysWith: min`: ascending offsets keep the first.
        var seen = new HashSet<string>(SwiftStringComparer.Instance); // :309
        var matched = new List<(SlashCommand Command, string Name, bool Prefixed, int Rank)>(commands.Count); // :311-312
        foreach (var command in commands) // :313
        {
            if (!seen.Add(command.Name)) continue; // :313: insert before filtering, preserving the first command.
            var name = SwiftString.Lowercased(command.Name); // :314
            if (needle.Length != 0 && !SwiftString.Contains(name, needle)) continue; // :315
            matched.Add((command, name, SwiftString.HasPrefix(name, needle),
                ranks.GetValueOrDefault(command.Name, int.MaxValue))); // :316, Swift Int.max: unseen ranks last.
        }
        // :318-320. LINQ keeps equal keys in input order, matching Swift's stable sort (R-COLL-1).
        return new ValueList<SlashCommand>(matched.OrderByDescending(item => item.Prefixed)
            .ThenBy(item => item.Rank)
            .ThenBy(item => item.Name, SwiftStringComparer.Instance)
            .Select(item => item.Command)); // :322
    }

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
