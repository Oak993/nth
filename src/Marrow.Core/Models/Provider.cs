using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Provider.swift (B2 1fbf81e), :1-351.
// SKELETON (cycle c4). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Codable conformance (the synthesized coding and :244-253 init(from:)) is left to the implementing unit.

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
        public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:19");
        public string DisplayName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:21");
        public string ExecutableName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:40");
        public string IconName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:60");
        public string LoginCommand => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:62");
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

    // :301 static func name(in:). C# cannot give a static method the name of the property Name, so it is NameIn.
    public static string? NameIn(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:301");

    public static ValueList<SlashCommand> Suggestions(IReadOnlyList<SlashCommand> commands, string matching,
        IReadOnlyList<string> recent) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:306");

    public ValueList<JSONValue> CodexInput(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:325");

    public static ValueList<SlashCommand> ClaudeCommands(IReadOnlyList<JSONValue> entries) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:335");

    public static ValueList<SlashCommand> CodexSkills(JSONValue result) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Provider.swift:342");
}
