// PORT-ONLY: SK-11d / WU-9, skills-PLAN.md 2.2 "OpenCode config merge". Everything one OpenCode launch needs to wire a locked generation.
namespace Marrow.Services.Port.Skills.Providers;

/// <summary>The trusted inputs of one OpenCode launch. A null launch means "no Marrow skills": the environment stays exactly today's.</summary>
/// <param name="GenerationFolder">The chat's locked generation folder (views\opencode\chat-id), absolute, appended as Marrow's own entry.</param>
/// <param name="SessionFolder">The folder the session runs in (opencode's session directory).</param>
/// <param name="DataRoot">The Marrow Code data root whose addons\bin goes on PATH (DL-6).</param>
/// <param name="Home">The user's home folder (opencode's home).</param>
/// <param name="WorktreeRoot">The project's worktree root; null makes the resolver look for a .git entry upward.</param>
public sealed record OpenCodeSkillLaunch(string GenerationFolder, string SessionFolder, string DataRoot, string Home, string? WorktreeRoot = null);
