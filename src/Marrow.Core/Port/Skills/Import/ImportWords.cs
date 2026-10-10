// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The words of "Import skills": plain, in the voice of the Skills page (LibraryWords). There is no design page for these yet, so every string here is an
// ENGINEERING DECISION that goes to the owner with the unit's report (ED-381). App names are plain text only (skills-PLAN.md 3.1): no logo, no image.
namespace Marrow.Core.Port.Skills.Import;

public static class ImportWords
{
    // ---- the page
    public const string Action = "Import skills";
    public const string SectionHeading = "Imported skills";
    public const string SectionNote = "Skills you brought in from other apps on this PC. Nth gives them to your chats like any other skill.";
    public const string ActionNote = "Bring in skills that other AI apps keep on this PC. Nothing in those apps is changed.";

    // ---- the quiet line (OD-0118 addendum: found by itself)
    public const string FoundImport = "Import";
    public const string FoundDismiss = "Dismiss";
    public static string Found(int skills, IReadOnlyList<string> apps) =>
        "Found " + skills + (skills == 1 ? " skill" : " skills") + " in " + (apps.Count <= 1 ? string.Concat(apps) : string.Join(", ", apps.Take(apps.Count - 1)) + " and " + apps[^1]);

    // ---- the sheet
    public const string Title = "Import skills";
    public const string Sub = "Looking where other AI apps keep their skills on this PC.";
    public const string Scanning = "Looking…";
    public const string NothingFound = "No skills found in the usual places. You can choose a folder to look in.";
    public const string Nothing = "Nothing in those apps is moved or changed.";
    public const string ModeHeading = "How to bring them in";
    public const string Copy = "Copy";
    public const string Link = "Link";
    public const string CopyNote = "Nth keeps its own copy. Later changes to the original are not picked up.";
    public const string LinkNote = "Nth reads the original each time, so changes to it are picked up. If the original goes away, the skill pauses.";
    public const string ChooseFolder = "Choose a folder…";
    public const string Cancel = "Cancel";
    public const string Close = "Close";
    public const string ImportAll = "Import all";
    public const string AlreadyImported = "Already imported";
    public const string NotRead = "Couldn’t read";
    public static string ImportSelected(int count) => count == 0 ? "Import" : "Import " + count;
    public static string FoundCount(int skills, int apps) => skills + (skills == 1 ? " skill" : " skills") + " in " + apps + (apps == 1 ? " place." : " places.");
    public static string NotReadCount(int count) => count + (count == 1 ? " item couldn’t be read and is left out." : " items couldn’t be read and are left out.");

    // ---- duplicates
    public static string Duplicate(string name) => "“" + name + "” is already imported.";
    public const string KeepBoth = "Keep both";
    public const string Replace = "Replace";
    public const string Skip = "Skip";
    public const string ApplyToRest = "Do this for the rest";
    /// <summary>Lead-proposed (pre-merge review M2): shown under the imported skills when a newer Marrow wrote them.</summary>
    public const string ReadOnlyNote = "These skills were saved by a newer Nth, so they can\u2019t be changed here.";
    public const string CouldNotSave = "Nth couldn\u2019t save the imported skills. Nothing was changed.";

    // ---- the result
    public const string Importing = "Importing…";
    public static string Done(int imported, int replaced, int skipped, int failed)
    {
        var parts = new List<string>();
        if (imported > 0) parts.Add(imported + " imported");
        if (replaced > 0) parts.Add(replaced + " replaced");
        if (skipped > 0) parts.Add(skipped + " skipped");
        if (failed > 0) parts.Add(failed + " couldn’t be imported");
        return parts.Count == 0 ? "Nothing was imported." : string.Join(", ", parts) + ".";
    }

    // ---- an imported skill's row
    public static string From(string app) => "from " + app;
    public const string Remove = "Remove";
    public const string RemoveAsk = "Remove?";
    public const string OriginalMissing = "Original missing";
    public const string Linked = "Linked";
    public const string Copied = "Copied";

    // ---- the apps (plain text)
    public const string ClaudeCode = "Claude Code";
    public const string Codex = "Codex";
    public const string Antigravity = "Antigravity";
    public const string Cursor = "Cursor";
    public const string Folders = "Folders you chose";
}
