// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-2). The words of Settings > Plugins > "Import": plain, sentence case, in the voice of "Import skills" (ImportWords, SK-18), whose shared
// words (Cancel, Close, Choose a folder, Import all, Skip, Replace, the copy note) it reuses. Every string is an ENGINEERING DECISION reported to the owner with the ED entry.
namespace Marrow.Core.Port.Skills.Import;

public static class PluginImportWords
{
    public const string Title = "Import plugins";
    public const string Sub = "Looking where other AI apps keep the plugins they installed on this PC.";
    public const string NothingFound = "No plugins found in the usual places. You can choose a folder to look in.";
    public const string AlreadyAdded = "Already in nth";
    public const string DuplicateHeading = "If a plugin is already in nth";
    public const string SkipNote = "The plugin in nth stays as it is.";
    public const string ReplaceNote = "The plugin in nth is removed and the new one is added.";
    public const string CopyNote = "nth keeps its own copy and checks it each time it is used. Later changes to the original are not picked up.";
    public const string ClaudeCode = "Claude Code";
    public const string Codex = "Codex";
    public const string Folders = "Folders you chose";
    public const string NotFound = "No plugin manifest was found there.";
    public static string FoundCount(int plugins, int places) => plugins + (plugins == 1 ? " plugin" : " plugins") + " in " + places + (places == 1 ? " place." : " places.");
}
