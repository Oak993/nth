// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). The words of Settings > MCP > Custom > "Import from other apps": plain, sentence case, in the voice of "Import skills" (ImportWords, SK-18).
// There is no design page for these, so every string here is an ENGINEERING DECISION reported to the owner with the ED entry. App names are plain text only.
namespace Marrow.Core.Port.MCP.Import;

public static class McpImportWords
{
    // ---- the Custom section
    public const string Action = "Import from other apps";

    // ---- the sheet
    public const string Title = "Import servers";
    public const string Sub = "Looking where other AI apps keep their MCP servers on this PC.";
    public const string Scanning = "Looking…";
    public const string NothingFound = "No servers found in other apps on this PC.";
    public const string Nothing = "Nothing in those apps is changed.";
    public const string SecretsNote = "Values that look like keys or tokens go to the Credential Manager.";
    public const string DuplicateHeading = "If a server is already in nth";
    public const string AlreadyIn = "Already in nth";
    public const string NotRead = "Couldn’t read";
    public const string Cancel = "Cancel";
    public const string Close = "Close";
    public const string ImportAll = "Import all";
    public const string KeepBoth = "Keep both";
    public const string Replace = "Replace";
    public const string Skip = "Skip";
    public static string ImportSelected(int count) => count == 0 ? "Import" : "Import " + count;
    public static string FoundCount(int servers, int apps) => servers + (servers == 1 ? " server" : " servers") + " in " + apps + (apps == 1 ? " app." : " apps.");
    public static string NotReadCount(int count) => count + (count == 1 ? " file couldn’t be read and is left out." : " files couldn’t be read and are left out.");
    public static string DuplicateNote(string choice) => choice switch
    {
        Replace => "The server in nth takes the new command or address and values.",
        KeepBoth => "The new one is added under a new name.",
        _ => "The server in nth stays as it is.",
    };

    // ---- servers nth can't take as they are (the row says so in place of where it came from; the tip says why)
    public const string CantImport = "Can’t import";
    public const string NeedsValueRow = "Needs a value";
    public const string LegacySse = "Uses the older SSE connection, which nth doesn’t speak. Ask the server for its streamable HTTP address (often /mcp) and add it with Add.";
    public static string LeftOut(int count) => count + (count == 1 ? " server can’t be imported and is left out." : " servers can’t be imported and are left out.");
    public static string Placeholders(IReadOnlyList<string> names) => "Uses " + string.Join(", ", names) + ", which nth can’t fill in here. Add it with Add and set the value.";
    public static string NeedsValueTip(IReadOnlyList<string> keys) => "Takes its value from something nth can’t read (" + string.Join(", ", keys) + "). It comes in empty; set it in the server’s editor.";
    public static string NeedsValueLine(string name, IReadOnlyList<string> keys) => name + ": set " + string.Join(", ", keys) + " in the server’s editor.";
    public static string Origin(string path, string project) => project.Length > 0 ? path + " (" + project + ")" : path;

    // ---- the result
    public const string Importing = "Importing…";
    public static string ImportingCount(int done, int total) => "Importing " + Math.Min(done + 1, total) + " of " + total + "…";
    public static string Done(int imported, int replaced, int skipped, int failed, int needValue = 0)
    {
        var parts = new List<string>();
        if (imported > 0) parts.Add(imported + " imported");
        if (replaced > 0) parts.Add(replaced + " replaced");
        if (skipped > 0) parts.Add(skipped + " skipped");
        if (failed > 0) parts.Add(failed + " couldn’t be imported");
        if (needValue > 0) parts.Add(needValue + (needValue == 1 ? " needs a value" : " need a value"));
        return parts.Count == 0 ? "Nothing was imported." : string.Join(", ", parts) + ".";
    }
    public const string ValuesNotSaved = "The Credential Manager could not save a value.";

    // ---- the apps (plain text)
    public const string ClaudeCode = "Claude Code";
    public const string ClaudeDesktop = "Claude Desktop";
    public const string Codex = "Codex";
    public const string Cursor = "Cursor";
    public const string VSCode = "VS Code";
    public const string Windsurf = "Windsurf";
    public const string Gemini = "Gemini CLI and Antigravity";

    // ---- where in an app (only for apps with more than one place)
    public const string User = "User";
    public const string Settings = "Settings";
    public const string Extension = "Extension";
}
