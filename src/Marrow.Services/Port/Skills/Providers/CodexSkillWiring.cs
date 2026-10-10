// PORT-ONLY: SK-11b / WU-7. What one Codex chat needs from SK-9's generations: its own generation (leased by the caller, who disposes it at session end), Marrow's views root, and what the listing probe reported.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class CodexSkillWiring(Generation chat, StorePaths paths, IReadOnlyList<PublishedSkill>? listed = null)
{
    public Generation Chat { get; } = chat;
    public string ViewsRoot { get; } = paths.Views;

    // The folder Codex is told about: a flat list of <skill>\SKILL.md folders (research-loading.md 3.2).
    public string ExtraRoot => Chat.Folder;

    // The skills the listing probe reported, as they were when it ran; paths in the slash list point there.
    public IReadOnlyList<PublishedSkill>? Listed { get; } = listed;

    public string ResolveSkillPath(string listedPath) => CodexSkillPaths.Rewrite(listedPath, Listed, Chat);

    // skills-PLAN.md 2.1 "Resuming" step 3: only when the old folder was still held and the chat moved.
    public string? MovedNote => Chat.PreviousFolder is { } previous ? MovedLine(Chat.Folder, previous) : null;

    public static string MovedLine(string folder, string previous) =>
        "Nth's skills for this chat are now in " + folder + ". Paths under " + previous + " earlier in this conversation no longer exist; use the same relative path under the new folder.";
}
