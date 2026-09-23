using System.Globalization;
using Marrow.Core.Port;

// Source: DroppyCode/Core/Support/Shell.swift (B2 1fbf81e), :1-325. Not ported yet, and why: the nil environment
// default of :40 and :49 (`environment ?? LoginEnvironment.current`), run(tool:) at :120-133 and LoginEnvironment at
// :327-462. LoginEnvironment.current, which, read and fallback all rest on augmentedPath (:441-453), whose Windows
// directory list no decision settles (PR-TERM-103 open question; STOP Core.Support.Shell part 1 #1), and which's
// PATHEXT source is not decided (#2). Until then Run takes the environment as a required argument.

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
    /// <summary>
    /// :31-108 Runs a process to completion off the main actor and collects its output. With an
    /// <paramref name="outputLimit"/>, only that many bytes of each stream's tail are kept (a command a model runs can
    /// print without end for the whole timeout; the app's own commands keep it all). <c>@concurrent</c> is Task.Run at
    /// the boundary (R-ISO-2). The environment has no default yet: see the file header (STOP #1).
    /// </summary>
    public static Task<ShellResult> Run(string executable, IReadOnlyList<string> arguments, string? @in,
        IReadOnlyDictionary<string, string> environment, byte[]? input = null, double timeout = 120, int? outputLimit = null) =>
        Task.Run(() => RunBody(executable, arguments, @in, environment, input, timeout, outputLimit));

    private static async Task<ShellResult> RunBody(string executable, IReadOnlyList<string> arguments, string? directory,
        IReadOnlyDictionary<string, string> environment, byte[]? input, double timeout, int? outputLimit)
    {
        // :45-49. The child gets its own job and process group (ED-047 (a)).
        var process = new ProcessLauncher
        {
            ExecutableURL = executable,
            Arguments = arguments,
        };
        if (directory is { } given) process.CurrentDirectoryURL = given;
        process.Environment = environment;

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

    /// <summary>
    /// :161-162 Searches that run something of their own, which may well write.
    /// DEVIATION ED-060 (1)-(2): OD-0008 A8 adds find's -fprint, -fprint0, -fprintf and -fls and fd's --exec and
    /// --exec-batch to the seven upstream entries.
    /// </summary>
    private static readonly HashSet<string> SearchActions = new(SwiftStringComparer.Instance)
    {
        "-delete", "-exec", "-execdir", "-ok", "-okdir", "-x", "-X",
        "-fprint", "-fprint0", "-fprintf", "-fls", "--exec", "--exec-batch",
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
                // :198-199
                return !arguments.Any(SearchActions.Contains);
            case "sort":
                // :200-201. DEVIATION ED-060 (3): -o in any spelling, --output and --output= included.
                return !arguments.Any(a => SwiftString.HasPrefix(a, "-o") || SwiftString.HasPrefix(a, "--output"));
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
