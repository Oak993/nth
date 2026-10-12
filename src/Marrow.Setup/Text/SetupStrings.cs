using System.Globalization;

namespace Marrow.Setup.Text;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Text"; unit U1, ED-NEW-inst-u1-1). One language's words: "key = value" lines from
/// an embedded table (<c>SetupStrings.&lt;language&gt;</c>), so the window can be translated without code changes. Blank
/// lines and lines starting with '#' are skipped; a key appears once.
/// </summary>
public sealed class StringTable
{
    private readonly Dictionary<string, string> entries;

    private StringTable(Dictionary<string, string> entries, string language)
    {
        this.entries = entries;
        Language = language;
    }

    /// <summary>The language this table is for ("en").</summary>
    public string Language { get; }

    /// <summary>Every key, in no particular order.</summary>
    public IReadOnlyCollection<string> Keys => entries.Keys;

    /// <summary>The words for <paramref name="key"/>; a missing key is a build error, so it throws.</summary>
    public string this[string key] =>
        entries.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"The setup's string table has no '{key}'.");

    public bool Contains(string key) => entries.ContainsKey(key);

    /// <summary>The words for <paramref name="key"/> with {0}, {1} filled in (invariant culture: numbers are plain).</summary>
    public string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, this[key], values);

    /// <summary>The form of <paramref name="key"/> for <paramref name="count"/> (<c>key.one</c> for 1, else <c>key.other</c>), with the count as {0}.</summary>
    public string Count(string key, long count, params object[] more) =>
        string.Format(CultureInfo.InvariantCulture, this[key + (count == 1 ? ".one" : ".other")], [count, .. more]);

    /// <summary>Reads a table's text. Throws <see cref="FormatException"/> on a line without '=', an empty key or a key given twice.</summary>
    public static StringTable Parse(string text, string language)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0) throw new FormatException($"Line {number} of the {language} string table has no 'key = value'.");
            var key = line[..equals].Trim();
            if (key.Length == 0) throw new FormatException($"Line {number} of the {language} string table has no key.");
            if (!entries.TryAdd(key, line[(equals + 1)..].Trim())) throw new FormatException($"The {language} string table gives '{key}' twice.");
        }
        return new StringTable(entries, language);
    }

    /// <summary>The embedded table for <paramref name="language"/>, or the English one when there is none.</summary>
    public static StringTable Load(string language)
    {
        var assembly = typeof(StringTable).Assembly;
        var stream = assembly.GetManifestResourceStream("SetupStrings." + language);
        if (stream is null)
        {
            language = "en";
            stream = assembly.GetManifestResourceStream("SetupStrings.en") ?? throw new InvalidOperationException("nth Setup was built without its English string table.");
        }
        using (stream)
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
            return Parse(reader.ReadToEnd(), language);
    }
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Text"; unit U1, ED-NEW-inst-u1-1). The setup window's words, by name. Every
/// string a screen shows is read here, from <see cref="Table"/>; the names follow the table's keys.
/// </summary>
public static class SetupStrings
{
    /// <summary>The table in use. English ships first; a translation is another embedded table with the same keys.</summary>
    public static StringTable Table { get; set; } = StringTable.Load("en");

    private static string S(string key) => Table[key];

    private static string F(string key, params object[] values) => Table.Format(key, values);

    // The caption and Narrator names
    public static string CaptionSetup => S("caption.setup");
    public static string CaptionUninstall => S("caption.uninstall");
    public static string CaptionMinimise => S("caption.minimise");
    public static string CaptionClose => S("caption.close");
    public static string Dot => S("common.dot");

    // Screen names (Narrator)
    public static string ScreenWelcome => S("screen.welcome");
    public static string ScreenOptions => S("screen.options");
    public static string ScreenInstalling => S("screen.installing");
    public static string ScreenReady => S("screen.ready");
    public static string ScreenUpdate => S("screen.update");
    public static string ScreenCurrent => S("screen.current");
    public static string ScreenProblem => S("screen.problem");
    public static string ScreenUninstall => S("screen.uninstall");

    // Welcome
    public static string WelcomeTitle => S("welcome.title");
    public static string WelcomeBy(string publisher) => F("welcome.by", publisher);
    public static string WelcomeMeta(string version, long megabytes) => F("welcome.meta", version, megabytes);
    public static string WelcomeSigned(string publisher) => F("welcome.signed", publisher);
    public static string WelcomeUnsigned => S("welcome.unsigned");
    public static string WelcomeForYou => S("welcome.forYou");
    public static string WelcomeOffline => S("welcome.offline");
    public static string WelcomeInstall => S("welcome.install");
    public static string WelcomeInstallsTo(string path) => F("welcome.installsTo", path);
    public static string WelcomeOptions => S("welcome.options");
    public static string WelcomeAddsBoth => S("welcome.adds.both");
    public static string WelcomeAddsStart => S("welcome.adds.start");
    public static string WelcomeDesktopLeaveOut => S("welcome.desktop.leaveOut");
    public static string WelcomeDesktopAdd => S("welcome.desktop.add");
    public static string WelcomeLicence => S("welcome.licence");
    public static string WelcomeLicenceView => S("welcome.licence.view");

    // Options
    public static string OptionsTitle => S("options.title");
    public static string OptionsSubtitle => S("options.subtitle");
    public static string OptionsLocation => S("options.location");
    public static string OptionsChange => S("options.change");
    public static string OptionsDesktop => S("options.desktop");
    public static string OptionsDesktopHint => S("options.desktop.hint");
    public static string OptionsSignIn => S("options.signIn");
    public static string OptionsDone => S("options.done");

    // Installing and the steps
    public static string InstallingTitle => S("installing.title");
    public static string InstallingSubtitle => S("installing.subtitle");
    public static string InstallingSlow => S("installing.slow");
    public static string InstallingHold => S("installing.hold");
    public static string StepChecking => S("step.checking");
    public static string StepPreparing => S("step.preparing");
    public static string StepClosing => S("step.closing");
    public static string StepCopying => S("step.copying");
    public static string StepCopyingNew => S("step.copyingNew");
    public static string StepAddingBoth => S("step.adding.both");
    public static string StepAddingStart => S("step.adding.start");
    public static string StepFinishing => S("step.finishing");
    public static string StepStopping(int threads) => Table.Count("step.stopping", threads);
    public static string StepWaiting(int threads) => Table.Count("step.waiting", threads);
    public static string ProgressBytes(long done, long total) => F("progress.bytes", done, total);
    public static string ProgressPercent(int percent) => F("progress.percent", percent);

    // Ready
    public static string ReadyTitle => S("ready.title");
    public static string ReadyTitleUpdated => S("ready.title.updated");
    public static string ReadyBoth => S("ready.both");
    public static string ReadyStart => S("ready.start");
    public static string ReadyUpdated(string version) => F("ready.updated", version);
    public static string ReadyNameTaken => S("ready.nameTaken");
    public static string ReadyFailed => S("ready.failed");
    public static string ReadyHidden => S("ready.hidden");
    public static string ReadyClose => S("ready.close");
    public static string ReadyOpen => S("ready.open");
    public static string ReadyOpening => S("ready.opening");

    // Update and a running nth
    public static string UpdateTitle => S("update.title");
    public static string UpdateSubtitle(string installed, string next) => F("update.subtitle", installed, next);
    public static string UpdateWhatsNew(string version) => F("update.whatsNew", version);
    public static string UpdateKeep => S("update.keep");
    public static string UpdateKeepOpen => S("update.keep.open");
    public static string UpdateLater => S("update.later");
    public static string UpdateGo => S("update.go");
    public static string UpdateBusyTitle => S("update.busy.title");
    public static string UpdateBusySubtitle(int threads) => Table.Count("update.busy.subtitle", threads);
    public static string UpdateBusyRunning => S("update.busy.running");
    public static string UpdateBusyRow(string thread, int minutes) => F("update.busy.row", thread, minutes);
    public static string UpdateBusyNote => S("update.busy.note");
    public static string UpdateBusyStop(int threads) => Table.Count("update.busy.stop", threads);
    public static string UpdateBusyWait(int threads) => Table.Count("update.busy.wait", threads);
    public static string UpdateWaitTitle => S("update.wait.title");
    public static string UpdateWaitSubtitle(int threads) => Table.Count("update.wait.subtitle", threads);
    public static string UpdateWaitStop => S("update.wait.stop");
    public static string UpdateRunningTitle(string version) => F("update.running.title", version);
    public static string UpdateHold => S("update.hold");
    public static string UpdateNotResponding => S("update.notResponding");

    // Up to date and Newer installed (OD-0072)
    public static string CurrentTitle => S("current.title");
    public static string CurrentSubtitle(string version) => F("current.subtitle", version);
    public static string NewerTitle => S("newer.title");
    public static string NewerSubtitle(string installedVersion) => F("newer.subtitle", installedVersion);

    // Problem
    public static string ProblemTitle => S("problem.title");
    public static string ProblemSpace(string drive, long neededMegabytes, long freeMegabytes) => F("problem.space", drive, neededMegabytes, freeMegabytes);
    public static string ProblemInUse => S("problem.inUse");
    public static string ProblemBlocked => S("problem.blocked");
    public static string ProblemOther => S("problem.other");
    public static string ProblemChanged => S("problem.changed");
    public static string ProblemSignature => S("problem.signature");
    public static string ProblemSafe => S("problem.safe");
    public static string ProblemLeft => S("problem.left");
    public static string ProblemCopy => S("problem.copy");
    public static string ProblemCopied => S("problem.copied");
    public static string ProblemRetry => S("problem.retry");
    public static string ProblemShow => S("problem.show");
    public static string ProblemHide => S("problem.hide");
    public static string ProblemHeader(string setupVersion, string windows, int build, string architecture) => F("problem.header", setupVersion, windows, build, architecture);

    // Uninstall
    public static string UninstallTitle => S("uninstall.title");
    public static string UninstallSubtitle => S("uninstall.subtitle");
    public static string UninstallWipe => S("uninstall.wipe");
    public static string UninstallWipeHint => S("uninstall.wipe.hint");
    public static string UninstallWarning => S("uninstall.warning");
    public static string UninstallCancel => S("uninstall.cancel");
    public static string UninstallGo => S("uninstall.go");
    public static string UninstallRemoving => S("uninstall.removing");
    public static string UninstallRemovingWipe => S("uninstall.removing.wipe");
    public static string UninstallRemovingKeep => S("uninstall.removing.keep");
    public static string UninstallHold => S("uninstall.hold");
    public static string UninstallRemoved => S("uninstall.removed");
    public static string UninstallRemovedWipe => S("uninstall.removed.wipe");
    public static string UninstallRemovedKeep => S("uninstall.removed.keep");
    public static string UninstallClose => S("uninstall.close");

    // The desktop previews
    public static string DesktopRecycleBin => S("desktop.recycleBin");
    public static string DesktopNth => S("desktop.nth");

    // Sign-out and shutdown held
    public static string ShutdownInstalling => S("shutdown.installing");
}
