// Source: DroppyCode/App/Views/Terminal/TerminalStore.swift (B3 52bc792), :1-176.
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Terminal;

/// <summary>:4-12 A shell running in a pseudo terminal, kept alive while its thread is open.</summary>
public sealed class TerminalSession : MainActorModel
{
    // :8 let id = UUID()
    public Guid Id { get; } = Guid.NewGuid();

    // :9 let threadID: UUID
    public Guid ThreadID { get; }

    // :10 let directory: String
    public string Directory { get; }

    // :11 var title: String
    private string title;
    public string Title { get => Read(title); set => Set(ref title, value); }

    // :12 private(set) var isRunning = true
    private bool isRunning = true;
    public bool IsRunning { get => Read(isRunning); private set => Set(ref isRunning, value); }

    // :14 @ObservationIgnored let view: LocalProcessTerminalView -- Swift's default (module-wide) access, needed by
    // the future terminal-panel host view that embeds it (not part of this unit); R-NAME-6.
    // PORT-ONLY type: Views/Terminal/Port/TerminalHost.cs (SwiftTerm has no Windows twin).
    public TerminalHostView View { get; }

    // :15 @ObservationIgnored private var bridge: TerminalBridge?
    private TerminalBridge? bridge;

    // :17-41
    public TerminalSession(Guid threadID, string directory, string title, string? command)
    {
        ThreadID = threadID;
        Directory = directory;
        this.title = title;
        // :21 view = LocalProcessTerminalView(frame: NSRect(x: 0, y: 0, width: 640, height: 240))
        View = new TerminalHostView();
        // :22 view.font = TerminalFont.preferred()
        View.Font = TerminalFont.Preferred();
        // :23 view.optionAsMetaKey = true
        View.OptionAsMetaKey = true;
        // :24-26
        var newBridge = new TerminalBridge(this);
        bridge = newBridge;
        View.ProcessDelegate = newBridge;

        // :28 let shell = LoginEnvironment.userShell()
        // DEVIATION OD-0008 C5: WindowsShell.Resolve() (Views/Terminal/Port/TerminalHost.cs) replaces
        // LoginEnvironment.userShell(); see that type's header for the engineering decision on "installed".
        var shell = WindowsShell.Resolve();
        // :29 var environment = LoginEnvironment.current
        var environment = new Dictionary<string, string>(LoginEnvironment.Current);
        // :30
        environment["TERM"] = "xterm-256color";
        // :31
        environment["COLORTERM"] = "truecolor";
        // :32 environment["TERM_PROGRAM"] = "Droppy Code" -- DEVIATION OD-0008 B12: "Marrow".
        environment["TERM_PROGRAM"] = "Nth";
        // :33
        environment["DROPPY_CODE_PROJECT_ROOT"] = directory;
        // :34 let pairs = environment.map { "\($0.key)=\($0.value)" }
        // Flattening with no quoting or escaping (PR-TERM-041) happens inside ProcessLauncher.BuildEnvironmentBlock,
        // which StartProcess below hands the dictionary to.
        // :35 let shellName = "-" + (shell as NSString).lastPathComponent -- DEVIATION PR-TERM-042: dropped on
        // Windows (no hyphen argv[0] login semantics); TerminalHostView.StartProcess ignores execName.
        var shellName = "-" + SwiftPath.LastPathComponent(shell);
        var arguments = ArgumentsForCommand(command); // :36-39; ED-222
        // :40
        View.StartProcess(shell, arguments, environment, shellName, directory);
    }

    // :36-39; DEVIATION OD-0008 C5 / ED-222. The POSIX vector and trailing exec become exactly three
    // PowerShell arguments: run the command verbatim, then stay interactive in the same process regardless
    // of its success. Pure extraction of the existing vector so contract tests never need to start a shell.
    internal static IReadOnlyList<string> ArgumentsForCommand(string? command) =>
        command is null ? [] : ["-NoExit", "-Command", command];

    // :44-45
    private bool? appliedDark;

    // :47-57
    public void ApplyAppearance(bool isDark)
    {
        // :48
        if (appliedDark == isDark) return;
        // :49
        appliedDark = isDark;
        // :50-51
        View.NativeBackgroundColor = isDark ? new SwiftColor(0.08, 1) : new SwiftColor(0.985, 1);
        View.NativeForegroundColor = isDark ? new SwiftColor(0.9, 1) : new SwiftColor(0.12, 1);
        // :54 let accent = Chrome.accentNSColor
        var accent = Chrome.AccentNSColor;
        // :55
        View.CaretColor = accent;
        // :56
        View.SelectedTextBackgroundColor = accent.Opacity(0.3);
    }

    // :59-63
    public void Terminate()
    {
        // :60
        if (!IsRunning) return;
        // :61
        View.Terminate();
        // :62
        IsRunning = false;
    }

    // :65-76 Whether the shell owns the foreground of its terminal: true at the prompt, false while a job it
    // started (vim, claude, less) has the terminal.
    // PLATFORM BOUNDARY (accepted deviation OD-0031 / ED-046 -- see Port/ProcessDescendants.cs header):
    // ConPTY has no process groups and no tpgid; approximated by whether the shell has a live descendant process.
    public static bool ForegroundJobIsTheShell(int shell)
    {
        // :70
        if (shell <= 0) return false;
        // :74-75 fail closed: "cannot tell" (null: no snapshot, timeout, shell not in the table) answers false, as the
        // source's failed sysctl does, so Escape goes to the terminal; only an explicit "no live child" answers true.
        return ProcessDescendants.HasLiveDescendant(shell) is false;
    }

    // :78-80 fileprivate func processEnded() -- fileprivate crosses into TerminalBridge below, in the same Swift
    // file; R-NAME-6 has no C# access level for "visible to another top-level type in this file only", so this is
    // internal (the narrowest level TerminalBridge, also internal, can call).
    internal void ProcessEnded()
    {
        // :79
        IsRunning = false;
    }
}

/// <summary>:83 Receives SwiftTerm callbacks, which arrive on the main thread.</summary>
internal sealed class TerminalBridge : ITerminalProcessDelegate
{
    // :85 weak var session: TerminalSession?
    private readonly WeakReference<TerminalSession> session;

    // :87-89
    public TerminalBridge(TerminalSession session)
    {
        this.session = new WeakReference<TerminalSession>(session);
    }

    // :91 func sizeChanged(source:newCols:newRows:) {}
    public void SizeChanged(int newCols, int newRows)
    {
    }

    // :93 func setTerminalTitle(source:title:) {}
    public void SetTerminalTitle(string? title)
    {
    }

    // :95 func hostCurrentDirectoryUpdate(source:directory:) {}
    public void HostCurrentDirectoryUpdate(string? directory)
    {
    }

    // :97-100 processTerminated(source:exitCode:) -- captures the session and runs
    // Task { @MainActor in session?.processEnded() }. exitCode is received and deliberately discarded (PR-TERM-047).
    public void ProcessTerminated(int? exitCode)
    {
        session.TryGetTarget(out var terminalSession);
        MainActor.Spawn(() =>
        {
            terminalSession?.ProcessEnded();
            return Task.CompletedTask;
        });
    }
}

/// <summary>:103-176</summary>
public sealed class TerminalStore : MainActorModel
{
    // :106 private(set) var sessions: [UUID: [TerminalSession]] -- exposed only through Sessions(threadID) (:109);
    // the stored property and the method cannot share one C# name (R-NAME-2).
    private ValueMap<Guid, ValueList<TerminalSession>> sessionsByThread = ValueMap<Guid, ValueList<TerminalSession>>.Empty;
    private ValueMap<Guid, ValueList<TerminalSession>> SessionsByThread
    {
        get => Read(sessionsByThread, nameof(Sessions));
        set => Set(ref sessionsByThread, value, property: nameof(Sessions));
    }

    // :107 var selection: [UUID: UUID]
    private ValueMap<Guid, Guid> selection = ValueMap<Guid, Guid>.Empty;
    public ValueMap<Guid, Guid> Selection { get => Read(selection); set => Set(ref selection, value); }

    // :109-111
    public IReadOnlyList<TerminalSession> Sessions(Guid threadID) =>
        SessionsByThread.TryGetValue(threadID, out var list) ? list : ValueList<TerminalSession>.Empty;

    // :113-116
    public TerminalSession? Selected(Guid threadID)
    {
        var list = Sessions(threadID);
        var selectedId = Selection.TryGetValue(threadID, out var id) ? id : (Guid?)null;
        foreach (var session in list)
            if (session.Id == selectedId) return session;
        return list.Count > 0 ? list[^1] : null;
    }

    // :118-125
    public TerminalSession Open(Guid threadID, string directory, string? title = null, string? command = null)
    {
        // :120
        var number = Sessions(threadID).Count + 1;
        // :121
        var session = new TerminalSession(threadID, directory, title ?? $"Terminal {number}", command);
        // :122
        SessionsByThread = SessionsByThread.Mutating(map =>
        {
            var list = map.TryGetValue(threadID, out var existing) ? existing : ValueList<TerminalSession>.Empty;
            map[threadID] = list.Appending(session);
        });
        // :123
        Selection = Selection.Mutating(map => map[threadID] = session.Id);
        // :124
        return session;
    }

    // :127-129
    public void Run(ProjectScript script, Guid threadID, string directory) =>
        Open(threadID, directory, title: script.Name, command: script.Command);

    // :131-135
    public void EnsureTerminal(Guid threadID, string directory)
    {
        // :132
        if (Sessions(threadID).Count == 0) Open(threadID, directory);
    }

    // :139-155 Replaces an exited shell with a fresh one in the same place in the row, keeping the tab's name and
    // its directory, so a shell that quit is one click from working again.
    public TerminalSession Restart(TerminalSession session)
    {
        // :141
        session.Terminate();
        // :142-147
        var fresh = new TerminalSession(session.ThreadID, session.Directory, session.Title, command: null);
        // :148-152
        SessionsByThread = SessionsByThread.Mutating(map =>
        {
            var list = map.TryGetValue(session.ThreadID, out var existing) ? existing : ValueList<TerminalSession>.Empty;
            var index = -1;
            for (var i = 0; i < list.Count; i++)
                if (list[i].Id == session.Id) { index = i; break; }
            map[session.ThreadID] = index >= 0 ? list.SetItem(index, fresh) : list.Appending(fresh);
        });
        // :153
        Selection = Selection.Mutating(map => map[session.ThreadID] = fresh.Id);
        // :154
        return fresh;
    }

    // :157-163
    public void Close(TerminalSession session)
    {
        // :158
        session.Terminate();
        // :159
        SessionsByThread = SessionsByThread.Mutating(map =>
        {
            if (!map.TryGetValue(session.ThreadID, out var list)) return;
            map[session.ThreadID] = new ValueList<TerminalSession>(list.Where(s => s.Id != session.Id));
        });
        // :160-162
        if (Selection.TryGetValue(session.ThreadID, out var selected) && selected == session.Id)
        {
            var remaining = Sessions(session.ThreadID);
            Selection = remaining.Count > 0
                ? Selection.Mutating(map => map[session.ThreadID] = remaining[^1].Id)
                : Selection.Mutating(map => map.Remove(session.ThreadID));
        }
    }

    // :165-169
    public void CloseAll(Guid threadID)
    {
        // :166
        foreach (var session in Sessions(threadID)) session.Terminate();
        // :167
        SessionsByThread = SessionsByThread.Mutating(map => map.Remove(threadID));
        // :168
        Selection = Selection.Mutating(map => map.Remove(threadID));
    }

    // :171-175
    public void TerminateAll()
    {
        // :172-174
        foreach (var list in SessionsByThread.Values)
            foreach (var session in list)
                session.Terminate();
    }
}
