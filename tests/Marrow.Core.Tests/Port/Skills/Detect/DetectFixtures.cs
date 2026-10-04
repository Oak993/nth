// PORT-ONLY: shared helpers of the SK-10 tests (first-release feature unit SK-10, DL-10 of porting/first-release/skills-ONDEMAND-PLAN.md section 7).
// The detector is pure: nothing here probes PATH, starts a process, touches a folder or opens the network. "Installed" and "found on this PC" come only
// from the AddonsState the test builds. The provider-shape events below are hand-written to the shapes ONDEMAND-PLAN.md 5.1 and 5.2 describe (a command
// line, an exit code where the provider has one, an error flag and the output text); they are not recordings of real runs, which need the real CLIs
// (SK-11's adapters and DL-20 check the real shapes). The Windows message texts are written from memory of the messages and are UNVERIFIED as exact
// wording; the detector's rules do not rest on the wording (exit codes, the identifier CommandNotFoundException, and the program's own name).
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Tests.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// An event type the detector has never heard of: it must ignore it (an event it cannot read is no signal).
internal sealed record ProseEvent(string Text) : ToolEvent;

internal static class DetectFixtures
{
    public static SkillsCatalogue Catalogue => CatalogueFixtures.Real;

    public static AddonsState State(params ItemRecord[] items) => AddonsState.Restore(items, [], [], []);

    public static ItemRecord Has(string id, string pin = "pin") => new(id, ItemState.Installed, pin, null, null);

    public static DetectionContext Ctx(AddonsState? state = null, string chat = "chat-1", params string[] offered) =>
        new(Catalogue, state ?? AddonsState.Empty, chat, new HashSet<string>(offered, StringComparer.Ordinal));

    public static string[] Ids(Detection detection, FindingVerdict verdict) =>
        detection.Findings.Where(f => f.Verdict == verdict).Select(f => f.ItemId).ToArray();

    public static string[] Offered(Detection detection) => Ids(detection, FindingVerdict.Offer);

    public static Detection Run(DetectionContext context, params ToolEvent[] events) => MissingToolDetector.Detect(context, events);

    public static CommandFinished Failed(string command, int? exit, string output, bool isError = true) => new(command, exit, isError, output);

    // The same failure as each provider shape delivers it. Claude's tool_result carries an error flag and text, no exit code (5.1 S3); Codex's
    // commandExecution item carries exitCode and aggregatedOutput; the others are written with and without an exit code.
    public static IEnumerable<(string Shape, ToolEvent Event)> MissingSoffice()
    {
        yield return ("Claude Bash", Failed("soffice --headless --convert-to pdf report.docx", null, "Exit code 127\nbash: line 1: soffice: command not found"));
        yield return ("Claude PowerShell, English", Failed("soffice --headless --convert-to pdf report.docx", null,
            "soffice : The term 'soffice' is not recognized as the name of a cmdlet, function, script file, or operable program.\n    + FullyQualifiedErrorId : CommandNotFoundException"));
        yield return ("Claude PowerShell, German", Failed("soffice --headless --convert-to pdf report.docx", null,
            "soffice : Die Benennung \"soffice\" wurde nicht als Name eines Cmdlet, einer Funktion, einer Skriptdatei oder eines ausführbaren Programms erkannt.\n    + FullyQualifiedErrorId : CommandNotFoundException"));
        yield return ("Codex cmd, English", Failed("soffice --headless --convert-to pdf report.docx", 9009,
            "'soffice' is not recognized as an internal or external command,\noperable program or batch file.", false));
        yield return ("Codex cmd, German", Failed("soffice --headless --convert-to pdf report.docx", 9009,
            "Der Befehl \"soffice\" ist entweder falsch geschrieben oder\nkonnte nicht gefunden werden.", false));
        yield return ("Codex cmd, German, other wording", Failed("soffice --headless --convert-to pdf report.docx", 9009,
            "\"soffice\" wird nicht als interner oder externer Befehl, ausführbares Programm oder Batchdatei erkannt.", false));
        yield return ("Copilot, error flag only", Failed("soffice --headless --convert-to pdf report.docx", null, "/usr/bin/bash: line 1: soffice: command not found"));
        yield return ("ACP, bash exit 127", Failed("soffice --headless --convert-to pdf report.docx", 127, "bash: soffice: command not found", false));
        yield return ("Pi, bash exit 127, German", Failed("soffice --headless --convert-to pdf report.docx", 127, "bash: soffice: Befehl nicht gefunden", false));
        yield return ("Command Code, quoted path", Failed("\"C:\\Program Files\\LibreOffice\\program\\soffice.exe\" --headless a.docx", 9009,
            "'C:\\Program Files\\LibreOffice\\program\\soffice.exe' is not recognized as an internal or external command,\noperable program or batch file.", false));
        yield return ("native run_command", Failed("& 'C:\\Program Files\\LibreOffice\\program\\soffice.exe' --headless a.docx", 1,
            "& : The term 'C:\\Program Files\\LibreOffice\\program\\soffice.exe' is not recognized as the name of a cmdlet.\nFullyQualifiedErrorId : CommandNotFoundException", false));
    }
}
