// PORT-ONLY: first-release feature unit SK-10. The normalized events of ONDEMAND-PLAN.md 5.2: what the detector reads, never provider JSON.
namespace Marrow.Core.Port.Skills.Detect;

/// <summary>
/// "(ED) A small adapter per session turns them into one normalized event (CommandHeld, CommandFinished, SkillUsed, ToolRequested) that the detector reads; the
/// detector never sees provider JSON" (ONDEMAND-PLAN.md 5.2). The adapters are DL-12's. An event of any other type is ignored.
/// </summary>
public abstract record ToolEvent;

/// <summary>S2: a command waiting for approval and not run yet.</summary>
public sealed record CommandHeld(string CommandLine) : ToolEvent;

/// <summary>
/// S3: a command that ended. <see cref="ExitCode"/> is null where the provider reports none (Claude's tool result carries an error flag and text); <see cref="IsError"/>
/// is the provider's own error flag and counts only when there is no exit code.
/// </summary>
public sealed record CommandFinished(string CommandLine, int? ExitCode, bool IsError, string Output) : ToolEvent;

/// <summary>S1: the model or the user used a skill by its folder name; <see cref="ItemId"/> names the set when the provider knows it.</summary>
public sealed record SkillUsed(string Skill, string? ItemId = null) : ToolEvent;

/// <summary>S4: the model asked for a tool (request_tool).</summary>
public sealed record ToolRequested(string Name, string Reason) : ToolEvent;
