// PORT-ONLY: ED-NEW-ignisadvisor-1 (owner 2026-10-11: the Claude advisor is a model choice in the chat's model picker). No Swift
// twin. The model-picker entries that run a Claude chat on a cheaper main model which asks a stronger model for advice through
// Claude Code's own advisor tool (code.claude.com/docs/en/advisor: `claude --advisor opus`), the launch decision (CLI version
// gate, the user's own off switch, the Anthropic-API-only rule) and the words the picker shows. Every model id lives here, as
// data; the views and the launch read it.
using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Advisor;

/// <summary>One picker entry: its id (what the chat stores as its model), its name and subtitle, the main model the CLI runs and the advisor it asks.</summary>
public sealed record ClaudeAdvisorModel(string Id, string Name, string Detail, string MainModel, ValueList<string> MainAliases, string Advisor);

/// <summary>Why a launch passes the advisor flag or not.</summary>
public enum ClaudeAdvisorGate
{
    /// <summary>The flag goes on the launch.</summary>
    Ready,
    /// <summary>The installed Claude Code is older than <see cref="ClaudeAdvisorModels.MinimumCli"/>.</summary>
    CliTooOld,
    /// <summary>The installed Claude Code's version could not be read.</summary>
    CliVersionUnknown,
    /// <summary>The user turned the advisor tool off (CLAUDE_CODE_DISABLE_ADVISOR_TOOL).</summary>
    TurnedOff,
    /// <summary>Claude runs on Amazon Bedrock, Google Cloud or Microsoft Foundry, where the advisor tool is not available.</summary>
    NotAnthropicApi,
}

/// <summary>What one launch of an advisor entry runs: the main model, and the advisor when the gate is <see cref="ClaudeAdvisorGate.Ready"/>.</summary>
public sealed record ClaudeAdvisorLaunch(string MainModel, string? Advisor, ClaudeAdvisorGate Gate);

public static class ClaudeAdvisorModels
{
    /// <summary>
    /// SOURCE FACT (code.claude.com/docs/en/changelog): Claude Sonnet 5.5 (`claude-sonnet-5-5`) arrived in Claude Code 2.1.284; the
    /// advisor tool and its `--advisor` launch flag are older (2.1.117 and later). The Sonnet 5.5 + Opus pairing needs both.
    /// </summary>
    public static readonly Version MinimumCli = new(2, 1, 284);

    /// <summary>SOURCE FACT (code.claude.com/docs/en/advisor): set, it turns the advisor tool off and `--advisor` has no effect.</summary>
    public const string DisableVariable = "CLAUDE_CODE_DISABLE_ADVISOR_TOOL";

    /// <summary>SOURCE FACT (same page): the advisor tool needs the Anthropic API; these select a cloud provider instead.</summary>
    public static IReadOnlyList<string> CloudProviderVariables { get; } = ["CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY"];

    /// <summary>The launch flag (SOURCE FACT: `claude --advisor opus`; not listed by `claude --help`).</summary>
    public const string Flag = "--advisor";

    /// <summary>
    /// The entries, in picker order. Sonnet 5.5 accepts Opus 5 or later as its advisor (docs, "Choose an advisor model"); "opus" is
    /// Claude Code's own alias for its current default Opus, and Opus is billed only for the calls it answers.
    /// </summary>
    public static IReadOnlyList<ClaudeAdvisorModel> All { get; } =
    [
        new("claude-sonnet-5-5+advisor-opus", "Sonnet 5.5 + Opus advisor", "Opus steps in only when Sonnet is stuck · about half the cost",
            "claude-sonnet-5-5", new ValueList<string>(["sonnet"]), "opus"),
    ];

    /// <summary>The entry whose id this is (ordinal); null for every other model.</summary>
    public static ClaudeAdvisorModel? Find(string? id) => id is null ? null : All.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    /// <summary>The model the CLI runs for a chat's model: an entry's main model, else the model itself.</summary>
    public static string? MainModel(string? id) => Find(id)?.MainModel ?? id;

    /// <summary>
    /// An entry's picker option: its name and subtitle, the efforts of its main model's option when the catalogue has it (else none),
    /// no fast tier (fast mode belongs to Opus).
    /// </summary>
    public static ModelOption Option(ClaudeAdvisorModel entry, ModelOption? main) =>
        new(entry.Id, entry.Name, detail: entry.Detail, efforts: main?.Efforts, defaultEffort: main?.DefaultEffort);

    /// <summary>Whether a catalogue option is an entry's main model (its id or an alias), so the entry goes right after it.</summary>
    public static bool IsMainOf(ClaudeAdvisorModel entry, string optionID) =>
        string.Equals(entry.MainModel, optionID, StringComparison.Ordinal) || entry.MainAliases.Contains(optionID, StringComparer.Ordinal);

    /// <summary>The CLI version gate alone (the picker's note): Ready, too old, or unknown.</summary>
    public static ClaudeAdvisorGate VersionGate(string? cliVersion)
    {
        if (ParseVersion(cliVersion) is not { } version) return ClaudeAdvisorGate.CliVersionUnknown;
        return version < MinimumCli ? ClaudeAdvisorGate.CliTooOld : ClaudeAdvisorGate.Ready;
    }

    /// <summary>
    /// What one launch runs: always the entry's main model; the advisor only when the CLI is new enough, the user has not turned the
    /// tool off and Claude runs on the Anthropic API. An unknown version never gets the flag (an unknown flag must not stop a launch).
    /// </summary>
    public static ClaudeAdvisorLaunch Launch(ClaudeAdvisorModel entry, string? cliVersion, IReadOnlyDictionary<string, string> environment)
    {
        var gate = VersionGate(cliVersion);
        if (gate == ClaudeAdvisorGate.Ready && IsSet(environment, DisableVariable)) gate = ClaudeAdvisorGate.TurnedOff;
        if (gate == ClaudeAdvisorGate.Ready && CloudProviderVariables.Any(name => IsSet(environment, name))) gate = ClaudeAdvisorGate.NotAnthropicApi;
        return new ClaudeAdvisorLaunch(entry.MainModel, gate == ClaudeAdvisorGate.Ready ? entry.Advisor : null, gate);
    }

    /// <summary>"2.1.296" from any text that carries it ("2.1.296 (Claude Code)"); null when none.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (text is null) return null;
        var match = Regex.Match(text, @"(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        try
        {
            return new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }
        catch (OverflowException) { return null; }
    }

    // A variable counts as set when present (any letter case) with a value other than empty, "0" or "false".
    private static bool IsSet(IReadOnlyDictionary<string, string> environment, string name) =>
        environment.Any(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
            && pair.Value.Trim() is { Length: > 0 } value && value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));

    // ---- words ----

    /// <summary>The picker row's note while the installed Claude Code cannot use the advisor (owner's words).</summary>
    public const string TooOldNote = "Your Claude Code is too old for this. Update it to use it.";

    /// <summary>The note when the version could not be read.</summary>
    public const string VersionUnknownNote = "Claude Code’s version is unknown. Update it to use it.";

    /// <summary>The row's action that opens Settings › Updates, where Claude Code is updated.</summary>
    public const string UpdateAction = "Update";

    /// <summary>The Update action's accessible name.</summary>
    public const string UpdateActionAccessibleName = "Update Claude Code";

    /// <summary>The note for a gate, or null when the advisor is used.</summary>
    public static string? Note(ClaudeAdvisorGate gate) => gate switch
    {
        ClaudeAdvisorGate.CliTooOld => TooOldNote,
        ClaudeAdvisorGate.CliVersionUnknown => VersionUnknownNote,
        _ => null,
    };
}
