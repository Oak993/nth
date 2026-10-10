using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Marrow.Core.Port;

// Source: DroppyCode/Core/Support/Shell.swift (B2 1fbf81e), :1-462. Not ported, and why:
// - :372-385, read()'s login-shell capture (the marker, /usr/bin/env -0 and the parse): no login shell is spawned on
//   Windows and the capture is the app's own process environment (ED-028 (3), ED-170 (2)).
// - :406-412, developerGit's xcode-select probe and its developer-directory git: dead on Windows
//   (porting/dead_code.md entry 51), and no shim bypass replaces it (ED-174 (2)).
// - :429-439, userShell(): its callers are :372 (not ported, above) and TerminalStore.swift:28, which OD-0008 C5 gives
//   PowerShell 7 if installed, otherwise Windows PowerShell; PR-TERM-039's deviation states that the SHELL,
//   password-database and /bin/zsh chain is not ported.

namespace Marrow.Core.Support;

/// <summary>Shell.swift:4-20.</summary>
public sealed record ShellResult(int Status, byte[] Stdout, byte[] Stderr)
{
    // :9
    public bool Succeeded => Status == 0;

    // :10 String(decoding: stdout, as: UTF8.self)
    public string Output => SwiftText.DecodeUtf8Repairing(Stdout);

    // :11
    public string ErrorOutput => SwiftText.DecodeUtf8Repairing(Stderr);

    // :12 output.trimmed (Text.swift:132, whitespaces and newlines)
    public string TrimmedOutput => Output.Trimmed();

    /// <summary>:14-19 The most useful text to show when a command fails.</summary>
    public string FailureMessage
    {
        get
        {
            // :16
            var error = ErrorOutput.Trimmed();
            // :17
            var message = error.Length == 0 ? TrimmedOutput : error;
            // :18 "\(status)" is an Int32 in decimal with an ASCII hyphen, whatever the culture.
            return message.Length == 0
                ? "The command failed with exit code " + Status.ToString(CultureInfo.InvariantCulture) + "."
                : message;
        }
    }
}

/// <summary>
/// Shell.swift:22-29. The Swift <c>message</c> (:23) is read through <see cref="Exception.Message"/>, which
/// <see cref="SwiftError"/> answers with <see cref="ErrorDescription"/>.
/// </summary>
public sealed class ShellError(string message) : SwiftError
{
    // :24
    public override string? ErrorDescription => message;
}

/// <summary>Shell.swift:31-134.</summary>
public static class Shell
{
    // PORT-ONLY: ED-367, async-local offline process boundary.
    internal static Ambient<Func<ShellInvocation, Task<ShellResult>>> RunForTesting { get; } = new((Func<ShellInvocation, Task<ShellResult>>)null!);

    /// <summary>
    /// :31-108 Runs a process to completion off the main actor and collects its output. With an
    /// <paramref name="outputLimit"/>, only that many bytes of each stream's tail are kept (a command a model runs can
    /// print without end for the whole timeout; the app's own commands keep it all). <c>@concurrent</c> is Task.Run at
    /// the boundary (R-ISO-2).
    /// </summary>
    public static Task<ShellResult> Run(string executable, IReadOnlyList<string> arguments, string? @in = null,
        IReadOnlyDictionary<string, string>? environment = null, byte[]? input = null, double timeout = 120, int? outputLimit = null)
    {
        // PORT-ONLY (ED-207): a test's observer sees the call as its site made it; the app installs none.
        ShellObserver.Current?.Invoke(new ShellInvocation(executable, [.. arguments], @in, environment, input, timeout, outputLimit));
        if (RunForTesting.Current is { } offline) return offline(new ShellInvocation(executable, [.. arguments], @in, environment, input, timeout, outputLimit));
        return Task.Run(() => RunBody(executable, arguments, @in, environment, input, timeout, outputLimit));
    }

    private static async Task<ShellResult> RunBody(string executable, IReadOnlyList<string> arguments, string? directory,
        IReadOnlyDictionary<string, string>? environment, byte[]? input, double timeout, int? outputLimit)
    {
        // :45-49. The child gets its own job and process group (ED-047 (a)).
        var process = new ProcessLauncher
        {
            ExecutableURL = executable,
            Arguments = arguments,
        };
        if (directory is { } given) process.CurrentDirectoryURL = given;
        process.Environment = environment ?? LoginEnvironment.Current;

        // :51-56. No stdin pipe means FileHandle.nullDevice: the launcher hands the child NUL (R-PROC-1).
        var stdout = new ProcessPipe();
        var stderr = new ProcessPipe();
        var stdin = input is null ? null : new ProcessPipe();
        process.StandardOutput = stdout;
        process.StandardError = stderr;
        process.StandardInput = stdin;

        // :58-60
        var collector = new OutputCollector(outputLimit);
        stdout.ReadabilityHandler = collector.Reader(OutputCollector.Stream.Stdout, stdout);
        stderr.ReadabilityHandler = collector.Reader(OutputCollector.Stream.Stderr, stderr);

        // :62-63
        var cancelled = new Mutex<bool>(false);
        TaskHandle? deadline = null;
        var launched = false;
        try
        {
            // :69-102
            var status = await SwiftTask.WithCancellationHandler(async () =>
            {
                // :70
                SwiftTask.CheckCancellation();
                // :71-96
                return await CheckedContinuation<int>.With(continuation =>
                {
                    // :72-75
                    var once = new OnceContinuation<int>(continuation);
                    process.TerminationHandler = exited => once.Resume(exited.TerminationStatus);
                    try
                    {
                        // :77-80
                        cancelled.WithLock((ref bool isCancelled) =>
                        {
                            if (isCancelled) throw new CancellationError();
                            process.Run();
                        });
                        launched = true;
                        // :81-85. A Task created in nonisolated code: detached, never cancelled by this task (R-TASK-1).
                        deadline = TaskHandle.Detached(async () =>
                        {
                            try { await SwiftTask.Sleep(SwiftTask.Seconds(timeout)); }
                            catch (CancellationError) { return; }
                            Terminate(process);
                        });
                    }
                    catch (Exception error) when (SwiftTry.IsThrownError(error))
                    {
                        // :86-88
                        once.Resume(error);
                        return;
                    }
                    // :90-95
                    if (input is { } data && stdin is { } handle)
                    {
                        GlobalQueue.Async(() =>
                        {
                            SwiftTry.Ignore(() => handle.Write(data));
                            SwiftTry.Ignore(handle.CloseWriting);
                        });
                    }
                });
            }, () =>
            {
                // :97-102
                cancelled.WithLock((ref bool isCancelled) =>
                {
                    isCancelled = true;
                    Terminate(process);
                });
            });
            // :103
            deadline?.Cancel();
            // :104
            SwiftTask.CheckCancellation();
            // :105
            var output = await collector.Finished(within: 3);
            // :106
            SwiftTask.CheckCancellation();
            // :107
            return new ShellResult(status, output.Stdout, output.Stderr);
        }
        finally
        {
            // :64-68
            deadline?.Cancel();
            stdout.ReadabilityHandler = null;
            stderr.ReadabilityHandler = null;
            // Swift releases the Pipe objects here, and ARC closes their handles. A process that never started never
            // closed the parent's writing ends, so the port closes them, and the readers reach their end of file.
            if (!launched)
            {
                stdout.CloseWriting();
                stderr.CloseWriting();
            }
        }
    }

    /// <summary>
    /// :110-118. process.terminate(), then one second later, if still running, SIGKILL to the group: on Windows
    /// CTRL_BREAK_EVENT to the child's own process group, then TerminateJobObject (ED-047 (a) as ED-126 applies it).
    /// </summary>
    private static void Terminate(ProcessLauncher process) => TerminationLadder.Shell(process);

    /// <summary>
    /// :120-133 Runs a named tool found on the login PATH. Swift's <c>run(tool:_:in:environment:input:timeout:)</c>: C#
    /// cannot overload <see cref="Run"/> on the argument label alone, so the label is in the name. It takes no
    /// outputLimit, so a tool run by name keeps its whole output (PR-TERM-091).
    /// </summary>
    public static async Task<ShellResult> RunTool(string tool, IReadOnlyList<string> arguments, string? @in = null,
        IReadOnlyDictionary<string, string>? environment = null, byte[]? input = null, double timeout = 120)
    {
        // :129-131. The tool is looked up with no environment, so on current's PATH.
        if (LoginEnvironment.Which(tool) is not { } executable)
            throw new ShellError(tool + " was not found on your PATH.");
        // :132
        return await Run(executable, arguments, @in, environment, input, timeout);
    }
}

/// <summary>
/// Shell.swift:136-216. Whether a shell command could change a file. Agents edit from the shell often enough that a
/// command's timeline row takes its edits from a snapshot of the working tree before and after it, and each of those
/// snapshots costs several git processes over the whole checkout. A command that only reads is not worth them. The list
/// below is what is known to read; anything else counts as writing, so an edit is never missed.
/// </summary>
public static class ShellCommandKind
{
    /// <summary>:142-151 Tools that only ever read.</summary>
    private static readonly HashSet<string> Readers = new(SwiftStringComparer.Instance)
    {
        "ls", "cat", "bat", "head", "tail", "wc", "grep", "egrep", "fgrep", "rg", "tree",
        "pwd", "echo", "printf", "which", "command", "type", "stat", "file", "du", "df",
        "ps", "printenv", "date", "whoami", "hostname", "uname", "basename", "dirname",
        "realpath", "readlink", "column", "uniq", "cut", "tr", "nl", "diff", "cmp", "jq",
        "yq", "xxd", "od", "md5", "shasum", "cksum", "man", "true", "false", "sleep",
        "cd", "pushd", "popd",
    };

    /// <summary>:153-159 Git subcommands that only report. Anything else git can do stays a writer.</summary>
    private static readonly HashSet<string> GitReaders = new(SwiftStringComparer.Instance)
    {
        "log", "status", "diff", "show", "branch", "rev-parse", "rev-list", "ls-files",
        "ls-tree", "blame", "remote", "describe", "shortlog", "cat-file", "tag", "config",
        "grep", "whatchanged", "reflog", "for-each-ref", "symbolic-ref", "count-objects",
        "var", "version", "help",
    };

    /// <summary>:161-162 Searches that run something of their own, which may well write.</summary>
    private static readonly HashSet<string> SearchActions = new(SwiftStringComparer.Instance)
    {
        "-delete", "-exec", "-execdir", "-ok", "-okdir", "-x", "-X",
    };

    // DEVIATION ED-060 (1): find's write actions that SearchActions lacks at B2.
    private static readonly HashSet<string> FindWriteActions = new(SwiftStringComparer.Instance)
    {
        "-fprint", "-fprint0", "-fprintf", "-fls",
    };

    // DEVIATION ED-060 (2): fd's long spellings of -x and -X.
    private static readonly HashSet<string> FdWriteActions = new(SwiftStringComparer.Instance)
    {
        "--exec", "--exec-batch",
    };

    // DEVIATION ED-060 (4): the read forms of git config. The key or pattern a getter takes belongs to it.
    private static readonly HashSet<string> GitConfigGetters = new(SwiftStringComparer.Instance)
    {
        "--get", "--get-all", "--get-regexp", "--get-urlmatch",
    };

    private static readonly HashSet<string> GitConfigReadFlags = new(SwiftStringComparer.Instance)
    {
        "--list", "-l", "--show-origin", "--show-scope", "--name-only", "--null", "-z",
        "--global", "--system", "--local", "--worktree",
    };

    private static readonly HashSet<string> GitConfigFileFlags = new(SwiftStringComparer.Instance) { "--file", "-f" };

    // DEVIATION ED-060 (5): the flags that put git tag in list mode, alone or in their --flag=value spelling.
    private static readonly string[] GitTagListFlags = ["--contains", "--points-at", "--merged", "--no-merged"];

    // DEVIATION ED-060 (6): the git remote subcommands that write.
    private static readonly HashSet<string> GitRemoteWriters = new(SwiftStringComparer.Instance)
    {
        "add", "remove", "rm", "rename", "set-url", "set-head", "set-branches", "prune", "update",
    };

    /// <summary>:164-172</summary>
    public static bool MayWriteFiles(string text)
    {
        // :165-166
        var command = text.Trimmed();
        if (command.Length == 0) return false;
        // :167-169 Redirection writes whatever the tool in front of it does, and a substitution hides a command this
        // cannot see. The substring tests here and the separator replacement in Parts are ordinal. Swift's String and
        // Foundation searches compare canonically, so they may also match the two code points whose canonical form is
        // one of these ASCII characters, U+1FEF (a backtick) and U+037E (a semicolon); ordinal matching sees neither.
        // The literal comparisons elsewhere in this class hold no character with such a singleton equivalent.
        if (command.Contains('>', StringComparison.Ordinal) || command.Contains("$(", StringComparison.Ordinal) ||
            command.Contains('`', StringComparison.Ordinal)) return true;
        // :170
        foreach (var part in Parts(command))
            if (!OnlyReads(part)) return true;
        // :171
        return false;
    }

    /// <summary>
    /// :174-183 A command line as the separate commands it runs. Quoting is not taken apart: a separator inside a
    /// quoted argument splits the line into parts that read as unfamiliar tools, which is the safe answer anyway.
    /// </summary>
    private static List<string> Parts(string command)
    {
        // :178-181
        var text = command;
        foreach (var separator in (string[])["&&", "||", ";", "|", "\n", "&"])
            text = text.Replace(separator, "\U00000001", StringComparison.Ordinal); // U+0001 START OF HEADING
        // :182 split on the one-Character separator, trim .whitespaces, drop empties
        var parts = new List<string>();
        foreach (var piece in SwiftString.Split(text, "\U00000001")) // U+0001 START OF HEADING
        {
            var part = FoundationText.Trim(piece);
            if (part.Length > 0) parts.Add(part);
        }
        return parts;
    }

    /// <summary>:185-215</summary>
    private static bool OnlyReads(string part)
    {
        // :186 split(whereSeparator: \.isWhitespace) over Characters
        var tokens = Tokens(part);
        // :187
        if (tokens.Count == 0) return true;
        // :188 (first as NSString).lastPathComponent, with both Windows separators (PR-TERM-095 windows_path)
        var tool = SwiftPath.LastPathComponent(tokens[0]);
        // :189
        var arguments = tokens.GetRange(1, tokens.Count - 1);
        switch (tool)
        {
            case "sed":
            {
                // :192-194
                var prints = arguments.Any(a => SwiftString.HasPrefix(a, "-") && !SwiftString.HasPrefix(a, "--") &&
                    a.Contains('n', StringComparison.Ordinal));
                var inPlace = arguments.Any(a => SwiftString.HasPrefix(a, "-i") || SwiftString.HasPrefix(a, "--in-place"));
                return prints && !inPlace;
            }
            case "awk" or "gawk" or "mawk":
                // :196-197 Redirection is already ruled out; awk can still shell out through system().
                return !part.Contains("system(", StringComparison.Ordinal);
            case "find" or "fd":
            {
                // :198-199
                if (arguments.Any(SearchActions.Contains)) return false;
                // DEVIATION OD-0008 A8, ED-060 (1)-(2): each tool's own further write actions, by exact equality.
                var further = SwiftString.Equal(tool, "find") ? FindWriteActions : FdWriteActions;
                return !arguments.Any(further.Contains);
            }
            case "sort":
                // :200-201. DEVIATION OD-0008 A8, ED-060 (3): also an argument that is --output or starts with --output=
                // (PR-TERM-119 names only these two forms).
                return !arguments.Any(a => SwiftString.HasPrefix(a, "-o") || SwiftString.Equal(a, "--output") || SwiftString.HasPrefix(a, "--output="));
            case "git":
            {
                // :203
                var index = arguments.FindIndex(a => !SwiftString.HasPrefix(a, "-"));
                if (index < 0) return true;
                var subcommand = arguments[index];
                // :204
                if (!GitReaders.Contains(subcommand)) return false;
                // DEVIATION OD-0008 A8, ED-060 (4)-(6): upstream stops at :204, so config, tag and remote always read.
                var rest = arguments.GetRange(index + 1, arguments.Count - index - 1);
                return subcommand switch
                {
                    "config" => GitConfigOnlyReads(rest),
                    "tag" => GitTagOnlyLists(rest),
                    "remote" => GitRemoteOnlyReads(rest),
                    _ => true,
                };
            }
            case "xcodebuild":
                // :206
                return arguments.Any(a => a is "-showBuildSettings" or "-list" or "-version" or "-showsdks");
            case "swift" or "swiftc" or "node" or "npm" or "python" or "python3":
                // :208
                return arguments is ["--version"] or ["-version"] or ["-v"];
            case "env":
                // :210-211 Bare env prints the environment; with a command after it, that command runs.
                return arguments.All(a => SwiftString.HasPrefix(a, "-"));
            default:
                // :212-213
                return Readers.Contains(tool);
        }
    }

    /// <summary>:186 <c>part.split(whereSeparator: \.isWhitespace)</c>: runs of non-whitespace Characters.</summary>
    private static List<string> Tokens(string part)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var character in SwiftString.Characters(part))
        {
            if (SwiftCharacter.IsWhitespace(character))
            {
                if (current.Length > 0) tokens.Add(current.ToString());
                current.Clear();
            }
            else current.Append(character);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// DEVIATION ED-060 (4): git config is a writer unless every argument is a read form: a getter (--get, --get-all,
    /// --get-regexp, --get-urlmatch) with the operands that follow it, --list / -l, --show-origin, --show-scope,
    /// --name-only, --null / -z, a scope (--global, --system, --local, --worktree), or --file / -f with its path.
    /// </summary>
    private static bool GitConfigOnlyReads(List<string> arguments)
    {
        var index = 0;
        while (index < arguments.Count)
        {
            var argument = arguments[index];
            if (GitConfigGetters.Contains(argument))
            {
                index++;
                while (index < arguments.Count && !SwiftString.HasPrefix(arguments[index], "-")) index++;
            }
            else if (GitConfigFileFlags.Contains(argument)) index += 2;
            else if (GitConfigReadFlags.Contains(argument)) index++;
            else return false;
        }
        return true;
    }

    /// <summary>
    /// DEVIATION ED-060 (5): git tag is a writer unless it only lists. Every dash argument must be a listing form (-l,
    /// --list, -n or -n with a count, --contains, --points-at, --merged, --no-merged, each also as --flag=value, and
    /// --sort or --format, alone or as --sort= and --format=). A non-dash argument is a pattern or a flag's value only
    /// in list mode (a listing form other than --sort and --format is present); otherwise it names a tag to create.
    /// </summary>
    private static bool GitTagOnlyLists(List<string> arguments)
    {
        var listMode = false;
        var positional = false;
        foreach (var argument in arguments)
        {
            if (!SwiftString.HasPrefix(argument, "-")) positional = true;
            else if (argument is "-l" or "--list" or "-n" || IsCountFlag(argument) || IsFlag(argument, GitTagListFlags)) listMode = true;
            else if (!IsFlag(argument, ["--sort", "--format"])) return false;
        }
        return listMode || !positional;
    }

    /// <summary>"-n" followed by one or more ASCII digits (git tag -n5).</summary>
    private static bool IsCountFlag(string argument) =>
        argument.Length > 2 && SwiftString.HasPrefix(argument, "-n") && argument.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;

    /// <summary>The flag itself or its "--flag=value" spelling.</summary>
    private static bool IsFlag(string argument, string[] flags) =>
        flags.Any(flag => argument == flag || SwiftString.HasPrefix(argument, flag + "="));

    /// <summary>
    /// DEVIATION ED-060 (6): git remote is a writer when its first non-dash argument is add, remove, rm, rename,
    /// set-url, set-head, set-branches, prune or update; bare git remote, -v, show and get-url stay readers.
    /// </summary>
    private static bool GitRemoteOnlyReads(List<string> arguments)
    {
        var index = arguments.FindIndex(a => !SwiftString.HasPrefix(a, "-"));
        return index < 0 || !GitRemoteWriters.Contains(arguments[index]);
    }
}

/// <summary>
/// Shell.swift:218-243. Resumes a continuation exactly once, however many callbacks race to finish it. It is taken
/// out under the lock and resumed outside it, since a resume runs whatever the awaiting task does next and
/// <c>withLock</c> is non-reentrant.
/// </summary>
internal sealed class OnceContinuation<TValue>
{
    // :223
    private readonly Mutex<CheckedContinuation<TValue>?> continuation;

    // :225-227
    public OnceContinuation(CheckedContinuation<TValue> continuation) =>
        this.continuation = new Mutex<CheckedContinuation<TValue>?>(continuation);

    // :229-231
    public void Resume(TValue value) => Take()?.Resume(value);

    // :233-235
    public void Resume(Exception error) => Take()?.Resume(error);

    // :237-242
    private CheckedContinuation<TValue>? Take() =>
        continuation.WithLock((ref CheckedContinuation<TValue>? stored) =>
        {
            try { return stored; }
            finally { stored = null; }
        });
}

/// <summary>
/// Shell.swift:245-325 (a private class in Swift; internal here so the tests reach it, R-NAME-6).
/// </summary>
internal sealed class OutputCollector
{
    // :246-249
    internal enum Stream
    {
        Stdout,
        Stderr,
    }

    /// <summary>:251-256, guarded by <see cref="state"/>.</summary>
    private sealed class State
    {
        public List<byte> Stdout { get; } = [];

        public List<byte> Stderr { get; } = [];

        public HashSet<Stream> Closed { get; } = [];

        // Swift's Void is the empty tuple, ValueTuple.
        public List<OnceContinuation<ValueTuple>> Waiters { get; set; } = [];
    }

    // :258
    private readonly Mutex<State> state = new(new State());

    // :259
    private readonly int? limit;

    // :261-263
    public OutputCollector(int? limit) => this.limit = limit;

    /// <summary>
    /// :265-280. The readability handler for one stream; <paramref name="handle"/> is the pipe it is installed on
    /// (Foundation passes the handle to the handler, the port's pipe passes the bytes).
    /// </summary>
    public Action<byte[]> Reader(Stream stream, ProcessPipe handle) => data =>
    {
        // :267-268
        if (data.Length == 0)
        {
            // :269-270
            handle.ReadabilityHandler = null;
            Close(stream);
        }
        else
        {
            // :272-277
            state.WithLock((ref State current) =>
            {
                switch (stream)
                {
                    case Stream.Stdout: Append(data, current.Stdout); break;
                    case Stream.Stderr: Append(data, current.Stderr); break;
                }
            });
        }
    };

    /// <summary>
    /// :282-288 Keeps the tail within the limit, trimming by whole chunks once the buffer holds twice the limit, so a
    /// stream that never ends costs O(limit) memory and amortized O(1) per byte.
    /// </summary>
    private void Append(byte[] data, List<byte> buffer)
    {
        buffer.AddRange(data);
        if (limit is not { } bound || buffer.Count <= (long)bound * 2) return;
        buffer.RemoveRange(0, buffer.Count - bound);
    }

    /// <summary>:290-314</summary>
    public async Task<(byte[] Stdout, byte[] Stderr)> Finished(double within)
    {
        // :291
        var ready = state.WithLock<(byte[] Stdout, byte[] Stderr)?>((ref State current) =>
            current.Closed.Count == 2 ? (current.Stdout.ToArray(), current.Stderr.ToArray()) : null);
        if (ready is { } output) return output;
        // :292-297. A Task created in nonisolated code: detached (R-TASK-1).
        var deadline = TaskHandle.Detached(async () =>
        {
            try { await SwiftTask.Sleep(SwiftTask.Seconds(within)); }
            catch (CancellationError) { return; }
            Close(Stream.Stdout);
            Close(Stream.Stderr);
        });
        try
        {
            // :299-312
            await SwiftTask.WithCancellationHandler(() => SwiftTry.IgnoreAsync(() => CheckedContinuation<ValueTuple>.With(continuation =>
            {
                // :301-307
                var once = new OnceContinuation<ValueTuple>(continuation);
                var isDone = state.WithLock((ref State current) =>
                {
                    if (current.Closed.Count == 2) return true;
                    current.Waiters.Add(once);
                    return false;
                });
                if (isDone) once.Resume(default(ValueTuple));
            })), () =>
            {
                // :309-312
                Close(Stream.Stdout);
                Close(Stream.Stderr);
            });
            // :313
            return state.WithLock((ref State current) => (current.Stdout.ToArray(), current.Stderr.ToArray()));
        }
        finally
        {
            // :298
            deadline.Cancel();
        }
    }

    /// <summary>:316-324 Only the transition that completes the second stream takes the waiters, and wakes them after the lock.</summary>
    private void Close(Stream stream)
    {
        var ready = state.WithLock<List<OnceContinuation<ValueTuple>>>((ref State current) =>
        {
            if (!current.Closed.Add(stream) || current.Closed.Count != 2) return [];
            var waiters = current.Waiters;
            current.Waiters = [];
            return waiters;
        });
        foreach (var waiter in ready) waiter.Resume(default(ValueTuple));
    }
}

/// <summary>
/// Shell.swift:327-462. The user's login shell environment. Apps launched from Finder inherit a minimal PATH that cannot
/// find provider CLIs, so this is captured once at launch. On Windows no login shell is spawned: the capture is the app's
/// own process environment, so read() gives what fallback() gives (ED-028 (3), ED-170 (2)). Environments are
/// case-insensitive dictionaries, and copies keep that comparer (ED-128 (1)).
/// </summary>
public static class LoginEnvironment
{
    // :335-339
    private static readonly HashSet<string> ExcludedKeys = new(SwiftStringComparer.Instance)
    {
        "_", "SHLVL", "PWD", "OLDPWD", "TERM", "TERM_PROGRAM", "TERM_PROGRAM_VERSION",
        "TERM_SESSION_ID", "COLORTERM", "ITERM_SESSION_ID", "XPC_SERVICE_NAME",
        "__CFBundleIdentifier", "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT",
    };

    // ED-171: the extensions which uses when neither the searched environment nor the process has a PATHEXT.
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";

    // :330-333, :347 and :402: the process's one Loader, made on first use as the Swift statics are.
    private static readonly Lazy<Loader> shared = new(() => new Loader(Fallback, AugmentedPath,
        environment => DeveloperGit(environment, HomeDirectory)), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>:341-343</summary>
    // PORT-ONLY R-TEST-10 boundary; empty in production. Both credential home paths share the scoped override.
    internal static Ambient<string> HomeForTesting { get; } = new("");

    public static ValueMap<string, string> Current => HomeForTesting.Current is { Length: > 0 } home
        ? shared.Value.Current.Mutating(environment => environment["HOME"] = home) : shared.Value.Current;

    /// <summary>:349-351</summary>
    public static bool IsLoaded => shared.Value.IsLoaded;

    /// <summary>:353-355 <c>FileManager.default.homeDirectoryForCurrentUser.path</c>: the user's profile folder.</summary>
    public static string HomeDirectory => HomeForTesting.Current is { Length: > 0 } home
        ? home : System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

    // PORT-ONLY test seam (PR-RUNTIME-025): lets a test hold or observe the load, to see that a caller awaits it before it
    // releases a session. The process default runs the shared Loader exactly as the Swift static does; no production code overrides it.
    internal static Ambient<Func<Task>> LoadForTesting { get; } = new(() => shared.Value.Load());

    /// <summary>:357-365</summary>
    public static Task Load() => LoadForTesting.Current();

    /// <summary>
    /// :393-400 The git binary every Git call runs, found once: the value the load stored, else the lookup over
    /// current. PATH usually gives Git for Windows' git.exe; without git the value still names Git for Windows' folder,
    /// so a Git call fails at launch (ED-174).
    /// </summary>
    public static string Git => shared.Value.Git;

    /// <summary>
    /// :404-413 over <paramref name="environment"/>, with <paramref name="home"/> for which. DEVIATION ED-174: which("git")
    /// in that environment, else the first of %ProgramFiles%\Git\cmd\git.exe, %ProgramFiles(x86)%\Git\cmd\git.exe and
    /// %LOCALAPPDATA%\Programs\Git\cmd\git.exe that exists as a file, else %ProgramFiles%\Git\cmd\git.exe, where the
    /// Swift falls back to /usr/bin/git. No xcode-select probe and no shim bypass (ED-174 (2)).
    /// </summary>
    internal static string DeveloperGit(IReadOnlyDictionary<string, string> environment, string home)
    {
        // :405
        if (Which("git", environment, home) is { } onPath) return onPath;
        // :406-412 not ported (ED-174 (2)). The fallback, DEVIATION ED-174 (1):
        var programFiles = InstallFolder(environment, "ProgramFiles", @"Git\cmd\git.exe");
        foreach (var candidate in (string?[])[
                     programFiles,
                     InstallFolder(environment, "ProgramFiles(x86)", @"Git\cmd\git.exe"),
                     InstallFolder(environment, "LOCALAPPDATA", @"Programs\Git\cmd\git.exe"),
                 ])
        {
            if (candidate is not null && File.Exists(candidate)) return candidate;
        }
        // The never-optional value. With no ProgramFiles in the environment the reference stays unexpanded, as
        // ExpandEnvironmentStrings leaves an undefined variable.
        return programFiles ?? @"%ProgramFiles%\Git\cmd\git.exe";
    }

    /// <summary>ED-174: <paramref name="relative"/> under the folder the variable names, or null when it names none.</summary>
    private static string? InstallFolder(IReadOnlyDictionary<string, string> environment, string variable, string relative) =>
        ValueIgnoringCase(environment, variable) is { Length: > 0 } folder ? Path.Join(folder, relative) : null;

    // PORT-ONLY R-TEST-10 boundary (PR-GIT-051); null in production. A test sets it (an empty string is a PATH with nothing on it) so that
    // a lookup over current's PATH (which(name), Shell.run(tool:)) finds nothing the owner's machine or a test stand-in put there. It
    // changes what is searched only, never the environment a child process is given.
    internal static Ambient<string> PathForTesting { get; } = new((string)null!); // Current is null while no test overrides it

    private static IReadOnlyDictionary<string, string> SearchedCurrent() =>
        PathForTesting.Current is { } path ? Current.Mutating(environment => environment["PATH"] = path) : Current;

    /// <summary>:415-427</summary>
    public static string? Which(string name, IReadOnlyDictionary<string, string>? environment = null) =>
        Which(name, environment, HomeDirectory);

    /// <summary>
    /// :415-427 with <paramref name="home"/> as the home folder, both for augmentedPath(nil) and for a leading '~'
    /// (ED-130). The tests' seam; the public overload passes <see cref="HomeDirectory"/>.
    /// </summary>
    internal static string? Which(string name, IReadOnlyDictionary<string, string>? environment, string home)
    {
        IReadOnlyDictionary<string, string> searched = environment ?? SearchedCurrent();
        // ED-171: the PATHEXT that stands in for Darwin's executable test, on both branches.
        var pathExt = PathExt(searched);
        // :421 with ED-170 (3)
        var path = searched.TryGetValue("PATH", out var value) ? value : AugmentedPath(null, home);
        // :416-420 and :422-426 on Windows: a name holding '/' or '\', or drive-qualified, is a path, tilde-expanded and
        // tested itself; any other name is tried in each ';'-separated PATH directory, first match wins (ED-031, ED-129,
        // ED-130, ED-131).
        return ProcessLauncher.Which(name, path, pathExt, home);
    }

    /// <summary>
    /// PORT-ONLY (owner PC, 2026-10-08): <see cref="Which(string, IReadOnlyDictionary{string, string}?)"/> for a program
    /// the port itself launches through ProcessLauncher, whose child joins its job in the attribute list at creation
    /// (ADR-0002, R-PROC-1). Windows refuses that attribute for a Microsoft Store app: its execution alias
    /// (%LOCALAPPDATA%\Microsoft\WindowsApps, a reparse point) and its packaged executable (Program Files\WindowsApps)
    /// both fail with ERROR_ACCESS_DENIED, which left the terminal and run_command unable to start on a PC whose
    /// PowerShell 7 came from the Store. Such entries are skipped and the next match on PATH wins.
    /// </summary>
    public static string? WhichLaunchable(string name, IReadOnlyDictionary<string, string>? environment = null)
    {
        IReadOnlyDictionary<string, string> searched = environment ?? SearchedCurrent();
        var pathExt = PathExt(searched);
        var path = searched.TryGetValue("PATH", out var value) ? value : AugmentedPath(null, HomeDirectory);
        foreach (var folder in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ProcessLauncher.Which(name, folder, pathExt, HomeDirectory) is { } found && !IsStorePackaged(found)) return found;
        }
        return null;
    }

    /// <summary>A Store app's execution alias or packaged executable (see <see cref="WhichLaunchable"/>).</summary>
    public static bool IsStorePackaged(string executable)
    {
        try
        {
            var full = Path.GetFullPath(executable);
            if (full.Contains(Path.DirectorySeparatorChar + "WindowsApps" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            return File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)
                && (Path.GetDirectoryName(full) ?? "").EndsWith(Path.Join("Microsoft", "WindowsApps"), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// ED-171: the PATHEXT of the environment which searches, its name looked up case-insensitively; when that is
    /// absent or empty, the app process's own PATHEXT; when that is absent or empty too, ".COM;.EXE;.BAT;.CMD".
    /// </summary>
    private static string PathExt(IReadOnlyDictionary<string, string> environment)
    {
        if (ValueIgnoringCase(environment, "PATHEXT") is { Length: > 0 } searched) return searched;
        if (System.Environment.GetEnvironmentVariable("PATHEXT") is { Length: > 0 } own) return own;
        return DefaultPathExt;
    }

    /// <summary>The value of <paramref name="name"/> in any letter case, also from a dictionary that compares exactly.</summary>
    private static string? ValueIgnoringCase(IReadOnlyDictionary<string, string> environment, string name)
    {
        if (environment.TryGetValue(name, out var value)) return value;
        foreach (var (key, candidate) in environment)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        return null;
    }

    /// <summary>:441-453</summary>
    public static string AugmentedPath(string? path) => AugmentedPath(path, HomeDirectory);

    /// <summary>
    /// :441-453 with <paramref name="home"/> as the home folder (the tests' seam). DEVIATION ED-170 (1): PATH is split
    /// and joined on ';', and only the five entries under the home folder are appended, in the Swift order, each unless
    /// an entry already equals it ignoring letter case after one trailing separator is trimmed. The seven absolute macOS
    /// locations (/opt/homebrew/bin, /opt/homebrew/sbin, /usr/local/bin, /usr/bin, /bin, /usr/sbin, /sbin) are not
    /// appended.
    /// </summary>
    internal static string AugmentedPath(string? path, string home)
    {
        // :443. split(separator:) drops empty pieces.
        var entries = new List<string>(SwiftString.Split(path ?? "", ";"));
        // :444-448, the five home entries as ED-170 (1) spells them (Path.Join, backslash separators).
        string[] common =
        [
            Path.Join(home, ".local", "bin"), Path.Join(home, ".bun", "bin"), Path.Join(home, ".cargo", "bin"),
            Path.Join(home, ".npm-global", "bin"), Path.Join(home, ".volta", "bin"),
        ];
        // :449-451
        foreach (var directory in common)
        {
            if (!entries.Any(entry => SameEntry(entry, directory))) entries.Add(directory);
        }
        // :452
        return string.Join(';', entries);
    }

    /// <summary>
    /// ED-170 (1): <paramref name="entry"/>, less one trailing '\' or '/', equals <paramref name="directory"/> ignoring
    /// letter case. Letter case is compared as the port compares paths (ED-137): with the Windows ordinal table, never
    /// OrdinalIgnoreCase, which is wider than the file system.
    /// </summary>
    private static bool SameEntry(string entry, string directory)
    {
        var trimmed = entry.Length > 0 && entry[^1] is '\\' or '/' ? entry[..^1] : entry;
        if (trimmed.Length != directory.Length) return false;
        if (trimmed.Length == 0) return true;
        // PORT-ONLY test-host portability: kernel32 exists only on Windows. Linux test hosts (cloud workers, CI) compare with
        // OrdinalIgnoreCase, which no shipped build ever runs; Windows always takes the ED-137 path below.
        if (!OperatingSystem.IsWindows()) return string.Equals(trimmed, directory, StringComparison.OrdinalIgnoreCase);
        var result = CompareStringOrdinal(trimmed, trimmed.Length, directory, directory.Length, bIgnoreCase: true);
        if (result == 0) throw new InvalidOperationException($"CompareStringOrdinal failed with error {Marshal.GetLastPInvokeError()}.");
        return result == CstrEqual;
    }

    private const int CstrEqual = 2;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CompareStringOrdinal(string lpString1, int cchCount1, string lpString2, int cchCount2,
        [MarshalAs(UnmanagedType.Bool)] bool bIgnoreCase);

    /// <summary>:455-461</summary>
    private static ValueMap<string, string> Fallback() => Fallback(ProcessEnvironment(), HomeDirectory);

    /// <summary>:455-461 over the given process environment and home folder (the tests' seam).</summary>
    internal static ValueMap<string, string> Fallback(IEnumerable<KeyValuePair<string, string>> processEnvironment, string home)
    {
        // :456 ProcessInfo.processInfo.environment, in a case-insensitive dictionary (ED-128 (1)): a name set again in
        // another case keeps the first spelling and takes the last value.
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in processEnvironment) environment[key] = value;
        // :457. The dictionary compares names as Windows does, so each removal is case-insensitive (PR-TERM-102).
        foreach (var key in ExcludedKeys) environment.Remove(key);
        // :458
        environment["PATH"] = AugmentedPath(environment.TryGetValue("PATH", out var path) ? path : null, home);
        // :459
        if (!environment.ContainsKey("LANG")) environment["LANG"] = "en_US.UTF-8";
        // :460
        return new ValueMap<string, string>(environment, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The app process's own environment block.</summary>
    private static IEnumerable<KeyValuePair<string, string>> ProcessEnvironment()
    {
        foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            yield return new KeyValuePair<string, string>((string)entry.Key, (string?)entry.Value ?? "");
    }

    /// <summary>
    /// The state Swift keeps in the statics <c>cached</c>, <c>loading</c>, <c>fallbackEnvironment</c> and <c>resolvedGit</c>
    /// (:330-351, :402), with the load, read and git over it (:357-400). The static members use one per process; a test
    /// makes its own, so no test changes the process's (R-TEST-5).
    /// </summary>
    internal sealed class Loader(Func<ValueMap<string, string>> fallback, Func<string?, string> augmentedPath,
        Func<IReadOnlyDictionary<string, string>, string> developerGit)
    {
        // :330
        private readonly Mutex<ValueMap<string, string>?> cached = new(null);

        // :331-333 The one read of the login shell. Launch starts it; anything that resolves a CLI before it lands
        // waits on the same task, so a codex under nvm is never reported missing.
        private readonly Mutex<TaskHandle?> loading = new(null);

        // :345-347 The process's own environment, for the moments before the login shell has answered: built once,
        // since every reader in that window asked for a fresh copy.
        private readonly Lazy<ValueMap<string, string>> fallbackEnvironment = new(fallback, LazyThreadSafetyMode.ExecutionAndPublication);

        // :341-343
        public ValueMap<string, string> Current =>
            cached.WithLock((ref ValueMap<string, string>? value) => value) ?? fallbackEnvironment.Value;

        // :349-351
        public bool IsLoaded => cached.WithLock((ref ValueMap<string, string>? value) => value is not null);

        // :402
        private readonly Mutex<string?> resolvedGit = new(null);

        // :398-400. which("git") ?? the fallback is developerGit over current once the probe is gone (ED-174 (1), (3)).
        public string Git => resolvedGit.WithLock((ref string? value) => value) ?? developerGit(Current);

        // :357-365
        public async Task Load() => await Loading().Completion;

        /// <summary>:358-363 The stored task, started by the first caller.</summary>
        internal TaskHandle Loading() => loading.WithLock((ref TaskHandle? current) =>
        {
            if (current is { } existing) return existing;
            // :360. A Task created in nonisolated code: detached (R-TASK-1); read() is @concurrent.
            var started = TaskHandle.Detached(Read);
            current = started;
            return started;
        });

        // :367-391
        private Task Read()
        {
            // :371
            var environment = fallbackEnvironment.Value;
            // :372-385 not ported: no login shell is spawned (ED-028 (3), ED-170 (2)).
            // :386
            environment = environment.Mutating(values =>
                values["PATH"] = augmentedPath(values.TryGetValue("PATH", out var path) ? path : null));
            // :387-388
            var resolved = environment;
            cached.WithLock((ref ValueMap<string, string>? value) => { value = resolved; });
            // :389-390
            var git = developerGit(resolved);
            resolvedGit.WithLock((ref string? value) => { value = git; });
            return Task.CompletedTask;
        }
    }
}
