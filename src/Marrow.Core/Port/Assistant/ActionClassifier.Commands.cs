// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a; unit AS-1; ED-NEW-dextracore-2). No Swift twin.
// The rule tables: what each known program does with its arguments. A program missing from the tables is Unknown; an
// Unknown program on the allowlist is Run. A program's stricter reading always wins over the allowlist: an owner's
// "npm publish" entry still asks, because publishing is Outward and no standing rule covers an irreversible step.
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public static partial class ActionClassifier
{
    private delegate Classification Handler(Call call);

    /// <summary>How a program reads arguments that start with a slash.</summary>
    private enum Slash { Path, Flag, FlagUnderCmd }

    /// <summary>One simple command being classified: its name, its words and where it runs.</summary>
    private sealed class Call(string name, List<CommandWord> words, CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        public string Name { get; } = name;
        public List<CommandWord> Words { get; } = words;
        public List<CommandWord> Args { get; } = words.Skip(1).ToList();
        public CommandPart Part { get; } = part;
        public RunState State { get; } = state;
        public ClassifierContext Context { get; } = context;
        public int Depth { get; } = depth;
        public ShellKind Shell => Part.Shell;
        public string? Cwd => State.Cwd;

        /// <summary>The first positional argument (the subcommand), lower case, or "".</summary>
        public string Sub(int index = 0)
        {
            var positional = Args.Where(a => !a.Text.StartsWith('-')).ToList();
            return index < positional.Count ? positional[index].Text.ToLowerInvariant() : "";
        }

        public bool Has(params string[] flags) => Args.Any(a => flags.Contains(a.Text, StringComparer.Ordinal));

        public bool HasIgnoreCase(params string[] flags) => Args.Any(a => flags.Contains(a.Text, StringComparer.OrdinalIgnoreCase));

        public Classification Inner(List<CommandWord> words, string? cwd = null)
        {
            if (words.Count == 0) return new Classification(ActionClass.Work, "runs nothing");
            var state = cwd is null ? State : new RunState(cwd);
            return ClassifyWords(words, Part, state, Context, Depth + 1);
        }

        public Classification InnerShell(string code, ShellKind shell, string? cwd = null) =>
            ClassifyShell(code, shell, cwd ?? Cwd, Context, Depth + 1);

        public Classification Read(CommandWord word) => Paths.Read(word, Cwd, Context);

        public Classification Write(CommandWord word) => Paths.Write(word, Cwd, Context);

        public Classification Remove(CommandWord word) => Paths.Remove(word, Cwd, Context);

        public CommandWord CwdWord => new(Cwd ?? "", Cwd is null);

        public bool IsSlashFlag(string text, Slash slash) =>
            slash switch
            {
                Slash.Flag => SlashFlagShape(text),
                Slash.FlagUnderCmd => Shell == ShellKind.Cmd && SlashFlagShape(text),
                _ => false,
            };

        private static bool SlashFlagShape(string text) =>
            text.Length is >= 2 and <= 16 && text[0] == '/' && (char.IsAsciiLetter(text[1]) || text[1] == '?')
            && text.IndexOfAny(['/', '\\'], 1) < 0;

        /// <summary>
        /// Splits the arguments into positionals and named values. A word starting with - (or --) is a flag; when its
        /// name is in <paramref name="valueNames"/> (PowerShell style: any prefix of two letters or more) the next word
        /// (or the part after : or =) is its value. After -- every word is positional.
        /// </summary>
        public Arguments Parse(string[] valueNames, bool psPrefix = true, Slash slash = Slash.Path, int skip = 0)
        {
            var result = new Arguments();
            var args = Args.Skip(skip).ToList();
            var ended = false;
            for (var k = 0; k < args.Count; k++)
            {
                var w = args[k];
                var t = w.Text;
                if (!ended && t == "--" && !w.Quoted) { ended = true; continue; }
                if (!ended && !w.Quoted && t.Length > 1 && t[0] == '-' && !(t.Length > 1 && char.IsAsciiDigit(t[1])))
                {
                    var body = t.TrimStart('-');
                    string? inline = null;
                    var colon = psPrefix ? body.IndexOf(':', StringComparison.Ordinal) : -1;
                    var eq = t.StartsWith("--", StringComparison.Ordinal) ? body.IndexOf('=', StringComparison.Ordinal) : -1;
                    if (colon > 0) { inline = body[(colon + 1)..]; body = body[..colon]; }
                    else if (eq > 0) { inline = body[(eq + 1)..]; body = body[..eq]; }
                    var name = body.ToLowerInvariant();
                    var canonical = valueNames.FirstOrDefault(v => v == name)
                                    ?? (psPrefix && name.Length >= 2 ? valueNames.FirstOrDefault(v => v.StartsWith(name, StringComparison.Ordinal)) : null);
                    if (canonical is not null)
                    {
                        if (inline is not null) result.Named[canonical] = new CommandWord(inline, w.Dynamic);
                        else if (k + 1 < args.Count) result.Named[canonical] = args[++k];
                        else result.Named[canonical] = null;
                        result.NamedAll.Add((canonical, result.Named[canonical]));
                    }
                    else
                    {
                        result.Flags.Add(name + (inline is null ? "" : "=" + inline.ToLowerInvariant()));
                        if (inline is { Length: > 0 } && !inline.Equals("$true", StringComparison.OrdinalIgnoreCase)
                            && !inline.Equals("$false", StringComparison.OrdinalIgnoreCase) && !inline.Equals("$null", StringComparison.OrdinalIgnoreCase))
                            result.InlineValues.Add(new CommandWord(inline, w.Dynamic));
                    }
                    continue;
                }
                if (!ended && !w.Quoted && IsSlashFlag(t, slash)) { result.Flags.Add(t.ToLowerInvariant()); continue; }
                if (w.Dynamic && w.Text.StartsWith('@') && Shell == ShellKind.PowerShell) { result.Splatted = true; continue; }
                result.Positional.Add(w);
            }
            return result;
        }

        /// <summary>Words of an argument, split at commas when PowerShell reads it (a,b is an array there).</summary>
        public IEnumerable<CommandWord> Items(CommandWord word)
        {
            if (Shell != ShellKind.PowerShell || word.Quoted || !word.Text.Contains(',', StringComparison.Ordinal)) return [word];
            return word.Text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => word with { Text = t });
        }
    }

    private sealed class Arguments
    {
        public List<CommandWord> Positional { get; } = [];
        public Dictionary<string, CommandWord?> Named { get; } = [];
        public List<(string Name, CommandWord? Value)> NamedAll { get; } = [];
        public List<string> Flags { get; } = [];
        /// <summary>Values written into a flag that takes none of the listed names ("-Foo:value", "--foo=value").</summary>
        public List<CommandWord> InlineValues { get; } = [];
        public bool Splatted { get; set; }

        public bool Flag(params string[] names) => Flags.Any(f => names.Any(n => f == n || f.StartsWith(n + "=", StringComparison.Ordinal)));

        public IEnumerable<CommandWord> Values(params string[] names) =>
            NamedAll.Where(n => names.Contains(n.Name) && n.Value is not null).Select(n => n.Value!);
    }

    private static Classification Work(string reason) => new(ActionClass.Work, reason);

    private static Classification Leaf(ActionClass actionClass, string reason) => new(actionClass, reason);

    private static readonly HashSet<string> KnownTools = new(StringComparer.Ordinal)
    {
        "dotnet", "node", "npm", "pnpm", "yarn", "bun", "deno", "python", "python3", "py", "pip", "pip3", "git", "gh", "cargo",
        "rustc", "rustup", "go", "java", "javac", "mvn", "gradle", "pwsh", "powershell", "winget", "choco", "scoop", "docker",
        "kubectl", "az", "aws", "gcloud", "code", "msbuild", "cmake", "make", "ninja", "gcc", "g++", "clang", "tsc", "uv",
    };

    private static readonly HashSet<string> VersionWords = new(StringComparer.Ordinal)
        { "--version", "-v", "-V", "version", "--help", "-h", "-?", "/?", "help", "--info", "--list-sdks", "--list-runtimes" };

    private static Classification Command(string name, List<CommandWord> words, CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        var call = new Call(name, words, part, state, context, depth);
        Classification result;
        if (Table.TryGetValue(name, out var handlers)) result = Classification.Of(handlers.Select(h => h(call)));
        else if (KnownTools.Contains(name) && call.Args.Count == 1 && VersionWords.Contains(call.Args[0].Text)) result = Work("prints a version or help");
        else result = Unknown($"{name} is not a command the classifier knows");
        // The allowlist turns an Unknown into Run, never a stricter class into anything.
        if (result.Parts.Count == 0 && result.Class == ActionClass.Unknown && IsAllowlisted(words, state.Cwd, context))
            return new Classification(ActionClass.Run, $"an allowlisted command: {Join(words, part.Shell)}") { Target = Join(words, part.Shell) };
        return result;
    }

    private static readonly Dictionary<string, Handler[]> Table = BuildTable();

    private static Dictionary<string, Handler[]> BuildTable()
    {
        var table = new Dictionary<string, Handler[]>(StringComparer.Ordinal);
        void Add(Handler handler, params string[] names)
        {
            foreach (var n in names)
                table[n] = table.TryGetValue(n, out var existing) ? [.. existing, handler] : [handler];
        }

        Add(_ => Work("prints or computes"),
            "echo", "write-output", "write", "write-host", "write-error", "write-warning", "write-verbose", "write-debug",
            "write-information", "out-null", "out-string", "out-host", "out-default", "format-table", "ft", "format-list", "fl",
            "format-wide", "fw", "format-custom", "sort-object", "select-object", "select", "where-object", "?", "foreach-object",
            "foreach", "%", "measure-object", "measure", "group-object", "group", "convertto-json", "convertfrom-json",
            "convertto-csv", "convertfrom-csv", "convertto-html", "convertto-xml", "convertfrom-stringdata", "get-member", "gm",
            "get-variable", "gv", "get-location", "gl", "pwd", "get-date", "date", "get-host", "get-culture", "get-uiculture",
            "get-random", "get-command", "gcm", "get-help", "help", "man", "get-alias", "gal", "get-module", "get-psdrive", "gdr",
            "get-process", "gps", "ps", "get-service", "gsv", "get-history", "history", "get-computerinfo", "get-timezone",
            "get-uptime", "get-error", "get-job", "receive-job", "wait-job", "start-job", "start-threadjob", "start-sleep", "sleep",
            "new-guid", "new-timespan", "test-json", "clear-host", "cls", "clear", "exit", "true", "false", ":", "test", "[",
            "expr", "seq", "printf", "basename", "dirname", "yes", "wait", "whoami", "hostname", "id", "uname", "nproc", "arch",
            "ver", "vol", "systeminfo", "tasklist", "ipconfig", "netstat", "getmac", "setlocal", "endlocal", "title", "color",
            "chcp", "where", "which", "whereis", "hash", "shift", "read", "getopts", "umask", "ulimit", "jobs", "fg", "bg",
            "disown", "tput", "stty", "tty", "locale", "printenv", "df", "free", "uptime", "get-netadapter", "get-netipaddress",
            "get-netipconfiguration", "pause", "choice", "remove-variable", "rv", "clear-variable", "clv",
            "get-executionpolicy", "tr");

        Add(ReadFiles(Slash.Path),
            "get-content", "gc", "cat", "less", "head", "tail", "get-childitem", "gci", "ls", "ll", "select-string", "sls",
            "grep", "egrep", "fgrep", "rg", "ag", "ack", "wc", "cut", "uniq", "nl", "rev", "tac", "od", "xxd", "hexdump",
            "strings", "file", "stat", "md5sum", "sha1sum", "sha256sum", "sha512sum", "get-filehash", "test-path", "resolve-path",
            "rvpa", "split-path", "join-path", "get-item", "gi", "get-itemproperty", "gp", "get-itempropertyvalue", "gpv",
            "import-csv", "ipcsv", "import-clixml", "jq", "yq", "diff", "cmp", "comm", "compare-object", "compare", "realpath",
            "readlink", "du", "column", "base64", "iconv", "bat", "fd", "get-acl");
        Add(ReadFiles(Slash.FlagUnderCmd), "dir", "type", "more", "tree");
        Add(ReadFiles(Slash.Flag), "findstr", "fc", "comp");

        Add(ChangeFolder, "cd", "chdir", "set-location", "sl", "push-location", "pushd", "pop-location", "popd");
        Add(Network, "ping", "test-connection", "nslookup", "resolve-dnsname", "dig", "host", "tracert", "traceroute",
            "test-netconnection", "tnc", "pathping");

        Add(NewItem, "new-item", "ni");
        Add(WriteEachPositional(Slash.Path), "touch");
        Add(WriteEachPositional(Slash.FlagUnderCmd), "md", "mkdir");
        Add(SetContent, "set-content", "sc", "add-content", "ac", "out-file", "export-csv", "epcsv", "export-clixml", "tee-object");
        Add(Tee, "tee");
        Add(Copy, "copy-item", "cp", "copy", "cpi", "xcopy");
        Add(Robocopy, "robocopy");
        Add(MoveItem, "move-item", "mv", "move", "mi");
        Add(RenameItem, "rename-item", "ren", "rename", "rni");
        Add(RemoveItem, "remove-item", "rm", "del", "erase", "rd", "rmdir", "ri", "unlink", "clear-content", "clc", "shred", "truncate");
        Add(Find, "find");
        Add(Sed, "sed");
        Add(Sort, "sort");
        Add(DiskDump, "dd");
        Add(Rsync, "rsync");
        Add(Archive, "tar", "unzip", "zip", "7z", "7za", "compress-archive", "expand-archive");
        Add(ChangeMode, "chmod", "chown", "chgrp", "attrib");
        Add(_ => Unknown("makes a link, which can make a later delete reach outside the worktree"), "ln", "mklink");

        Add(_ => Leaf(ActionClass.Destructive, "ends other programs"),
            "taskkill", "tskill", "stop-process", "spps", "kill", "pkill", "killall");
        Add(_ => Leaf(ActionClass.Destructive, "shuts down, restarts or signs out of the PC"),
            "shutdown", "restart-computer", "stop-computer", "logoff");
        Add(_ => Leaf(ActionClass.Destructive, "erases a disk or partition"),
            "format", "format-volume", "diskpart", "clear-disk", "initialize-disk", "remove-partition", "fdisk", "mkfs", "cipher");

        Add(_ => Leaf(ActionClass.Outward, "connects to another computer and can send anything"),
            "ssh", "sftp", "ftp", "tftp", "telnet", "nc", "ncat", "netcat", "socat", "enter-pssession", "etsn", "new-pssession",
            "nsn", "mstsc", "putty", "plink", "pscp", "psftp", "winscp");
        Add(Scp, "scp");
        Add(_ => Leaf(ActionClass.Outward, "sends email"), "send-mailmessage", "mail", "mailx", "sendmail", "mutt", "msmtp", "blat");
        Add(Web, "curl", "wget", "invoke-webrequest", "iwr", "invoke-restmethod", "irm", "start-bitstransfer");
        Add(InvokeCommand, "invoke-command", "icm");

        Add(Git, "git");
        Add(GitHub, "gh");
        Add(NodePackages, "npm", "pnpm", "yarn", "bun");
        Add(_ => Unknown("runs a package from the registry"), "npx", "pnpx", "bunx");
        Add(DotNet, "dotnet");
        Add(Sub(("push", ActionClass.Outward, "publishes a package"), ("delete", ActionClass.Destructive, "deletes a published package")), "nuget");
        Add(Sub(("publish", ActionClass.Outward, "publishes a crate"), ("owner", ActionClass.Outward, "changes a crate's owners"),
            ("yank", ActionClass.Destructive, "withdraws a published crate"), ("install", ActionClass.Install, "installs a program"),
            ("uninstall", ActionClass.Destructive, "uninstalls a program"), ("login", ActionClass.Login, "signs in to crates.io")), "cargo");
        Add(Sub(("install", ActionClass.Install, "installs a gem"), ("push", ActionClass.Outward, "publishes a gem"),
            ("uninstall", ActionClass.Destructive, "uninstalls a gem"), ("yank", ActionClass.Destructive, "withdraws a published gem")), "gem");
        Add(Sub(("install", ActionClass.Install, "installs a program")), "go");
        Add(Docker, "docker", "podman");
        Add(Sub(("upload", ActionClass.Outward, "publishes a package")), "twine");
        Add(Sub(("publish", ActionClass.Outward, "publishes an extension")), "vsce", "ovsx");
        Add(Sub(("publish", ActionClass.Outward, "publishes a package")), "poetry", "flit", "hatch");
        Add(Sub(("deploy", ActionClass.Outward, "publishes to a repository")), "mvn");
        Add(Sub(("publish", ActionClass.Outward, "publishes to a repository")), "gradle", "gradlew");
        Add(Pip, "pip", "pip3", "pipx", "uv", "conda", "mamba");
        Add(WindowsPackages, "winget", "choco", "scoop");
        Add(PosixPackages, "apt", "apt-get", "yum", "dnf", "zypper", "apk", "pacman", "brew", "port", "snap", "flatpak", "emerge");
        Add(Msiexec, "msiexec");
        Add(Dism, "dism");
        Add(_ => Leaf(ActionClass.Install, "installs software"),
            "install-module", "install-package", "install-script", "install-psresource", "add-appxpackage", "add-appxprovisionedpackage",
            "enable-windowsoptionalfeature", "add-windowscapability", "install-windowsfeature");
        Add(_ => Leaf(ActionClass.Destructive, "uninstalls software"),
            "uninstall-module", "uninstall-package", "uninstall-script", "uninstall-psresource", "remove-appxpackage",
            "remove-appxprovisionedpackage", "disable-windowsoptionalfeature", "remove-windowscapability", "uninstall-windowsfeature");
        Add(Editor, "code", "code-insiders", "cursor", "codium");
        Add(Cloud, "az", "aws", "gcloud", "firebase", "vercel", "netlify", "heroku", "fly", "flyctl", "wrangler", "supabase",
            "railway", "doctl", "kubectl", "helm", "terraform", "pulumi");

        Add(Registry, "reg");
        Add(ServiceControl, "sc");
        Add(Schtasks, "schtasks");
        Add(Wevtutil, "wevtutil");
        Add(Net, "net", "net1");
        Add(Route, "route", "arp");
        Add(Certutil, "certutil");
        Add(_ => Leaf(ActionClass.HardDeny, "changes security settings"),
            "regedit", "regedt32", "regini", "netsh", "bcdedit", "secedit", "auditpol", "gpedit", "set-executionpolicy",
            "set-mppreference", "add-mppreference", "remove-mppreference", "new-netfirewallrule", "set-netfirewallrule",
            "remove-netfirewallrule", "enable-netfirewallrule", "disable-netfirewallrule", "set-netfirewallprofile", "icacls",
            "cacls", "xcacls", "takeown", "set-acl", "clear-eventlog", "remove-eventlog", "limit-eventlog",
            "disable-computerrestore", "enable-computerrestore", "set-localuser", "new-localuser", "remove-localuser",
            "add-localgroupmember", "remove-localgroupmember", "enable-localuser", "disable-localuser", "certmgr",
            "import-certificate", "import-pfxcertificate", "export-pfxcertificate", "unblock-file", "vssadmin", "wbadmin",
            "new-service", "set-service", "remove-service", "stop-service", "start-service", "restart-service",
            "suspend-service", "resume-service", "register-scheduledtask", "unregister-scheduledtask", "set-scheduledtask",
            "new-scheduledtask", "new-scheduledtaskaction", "new-scheduledtasktrigger", "start-scheduledtask",
            "enable-scheduledtask", "disable-scheduledtask", "at", "mountvol");
        Add(_ => Leaf(ActionClass.HardDeny, "handles stored credentials"),
            "cmdkey", "vaultcmd", "get-credential", "get-storedcredential", "new-storedcredential", "remove-storedcredential",
            "op", "bw", "keepassxc-cli", "kpcli", "lpass", "dcli", "rundll32");
        Add(_ => Leaf(ActionClass.HardDeny, "asks for administrator rights"), "sudo", "doas", "gsudo", "runas", "su", "pkexec");
        Add(SetItemProperty, "set-itemproperty", "sp", "new-itemproperty", "remove-itemproperty", "rp", "rename-itemproperty",
            "rnp", "copy-itemproperty", "cpp", "move-itemproperty", "mp", "clear-itemproperty", "clp", "set-item", "si", "clear-item", "cli");
        Add(Setx, "setx");
        Add(SetCommand, "set", "set-variable", "sv", "new-variable", "nv");
        Add(PosixExport, "export", "declare", "typeset", "local", "readonly", "unset");
        Add(c => c.Args.Count == 0 ? Work("lists aliases") : Unknown("defines an alias, which can stand in for a command"), "alias");
        Add(_ => Unknown("redefines or loads commands"), "unalias", "set-alias", "new-alias", "sal", "nal", "remove-alias",
            "import-module", "ipmo", "add-type", "trap", "new-object", "invoke-item", "ii", "explorer", "open", "xdg-open",
            "start-transcript", "register-objectevent", "register-engineevent");

        Add(PowerShellHost, "powershell", "pwsh", "powershell_ise");
        Add(CmdHost, "cmd");
        Add(PosixHost, "bash", "sh", "zsh", "dash", "ksh", "ash", "mksh");
        Add(Wsl, "wsl");
        Add(EnvWrapper, "env");
        Add(c => c.Inner(c.Args), "nohup", "time", "exec", "builtin", "setsid", "call");
        Add(c => c.Inner(SkipOptions(c.Args, ["-n", "-c", "-p", "-t"])), "nice", "ionice", "stdbuf", "chrt", "watch");
        Add(Timeout, "timeout");
        Add(CommandBuiltin, "command");
        Add(Xargs, "xargs");
        Add(StartProgram, "start", "start-process", "saps");
        Add(InvokeExpression, "invoke-expression", "iex");
        Add(Eval, "eval");
        Add(c => c.Args.Count == 0 ? Unknown("sources nothing") : Script(c.Args, c.Part, c.State, c.Context, c.Words), "source");
        Add(Interpreter, "python", "python3", "py", "pythonw", "node", "nodejs", "deno", "tsx", "ts-node", "ruby", "perl",
            "php", "lua", "rscript", "cscript", "wscript");
        return table;
    }

    private static List<CommandWord> SkipOptions(List<CommandWord> args, string[] valued)
    {
        var k = 0;
        while (k < args.Count && args[k].Text.StartsWith('-'))
        {
            k += valued.Contains(args[k].Text, StringComparer.Ordinal) ? 2 : 1;
        }
        return args.Skip(k).ToList();
    }

    private static Handler Sub(params (string Sub, ActionClass Class, string Reason)[] rules) => call =>
    {
        var sub = call.Sub();
        foreach (var (name, actionClass, reason) in rules)
            if (sub == name) return Leaf(actionClass, reason);
        return Unknown($"{call.Name} {sub} is not a command the classifier knows".Replace("  ", " ", StringComparison.Ordinal));
    };

    // ---- files ----

    private static Handler ReadFiles(Slash slash) => call =>
    {
        // Every word that is not a flag is read as a path, values included: a pattern read as a path inside the
        // folder costs nothing, while a skipped value could hide a read outside the access folders.
        var parsed = call.Parse([], psPrefix: true, slash);
        var leaves = new List<Classification>();
        foreach (var w in parsed.Positional.Concat(parsed.InlineValues))
            foreach (var item in call.Items(w)) leaves.Add(call.Read(item));
        if (parsed.Splatted) leaves.Add(Unknown("its parameters are hidden in a splat"));
        if (leaves.Count == 0) leaves.Add(call.Read(call.CwdWord));
        return Classification.Of(leaves);
    };

    private static Classification ChangeFolder(Call call)
    {
        var parsed = call.Parse(["path", "literalpath", "stackname"], psPrefix: true, Slash.FlagUnderCmd);
        var target = parsed.Named.GetValueOrDefault("path") ?? parsed.Named.GetValueOrDefault("literalpath")
                     ?? parsed.Positional.FirstOrDefault();
        if (call.Name is "pop-location" or "popd" || target is null || target.Text == "-")
        {
            call.State.Cwd = null;
            return Work("changes folder");
        }
        call.State.Cwd = Paths.Full(target, call.Cwd, call.Context);
        return Work("changes folder");
    }

    private static Classification Network(Call call) =>
        new(ActionClass.ReadWeb, "checks a network address") { Target = call.Sub(), CarriesData = true, Field = "read" };

    private static Classification NewItem(Call call)
    {
        var parsed = call.Parse(["path", "literalpath", "name", "itemtype", "type", "value", "target", "credential"]);
        var type = (parsed.Named.GetValueOrDefault("itemtype") ?? parsed.Named.GetValueOrDefault("type"))?.Text ?? "";
        if (type.Contains("link", StringComparison.OrdinalIgnoreCase) || type.Contains("junction", StringComparison.OrdinalIgnoreCase))
            return Unknown("makes a link, which can make a later delete reach outside the worktree");
        var paths = parsed.Values("path", "literalpath").Concat(parsed.Positional.Take(1)).SelectMany(call.Items).ToList();
        var name = parsed.Named.GetValueOrDefault("name");
        if (name is not null)
        {
            paths = paths.Count == 0
                ? [name]
                : paths.Select(p => new CommandWord(p.Text.TrimEnd('\\', '/') + "\\" + name.Text, p.Dynamic || name.Dynamic)).ToList();
        }
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        if (paths.Count == 0) return Unknown("creates an item the classifier cannot place");
        return Classification.Of(paths.Select(call.Write));
    }

    private static Handler WriteEachPositional(Slash slash) => call =>
    {
        var parsed = call.Parse(["path", "mode", "m", "reference", "r", "d", "date", "t"], psPrefix: false, slash);
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        var targets = parsed.Positional.Concat(parsed.Values("path")).SelectMany(call.Items).ToList();
        if (targets.Count == 0) return Unknown("creates something the classifier cannot place");
        return Classification.Of(targets.Select(call.Write));
    };

    private static Classification SetContent(Call call)
    {
        var parsed = call.Parse(["path", "literalpath", "filepath", "pspath", "value", "encoding", "inputobject", "width",
            "delimiter", "stream", "credential", "filter", "include", "exclude", "variable", "useculture"]);
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        var targets = parsed.Values("path", "literalpath", "filepath", "pspath").Concat(parsed.Positional.Take(1)).SelectMany(call.Items).ToList();
        if (targets.Count == 0)
            return call.Name is "tee-object" ? Work("passes its input on") : Unknown("writes to a file the classifier cannot place");
        return Classification.Of(targets.Select(call.Write));
    }

    private static Classification Tee(Call call)
    {
        var parsed = call.Parse([], psPrefix: false);
        return parsed.Positional.Count == 0 ? Work("passes its input on") : Classification.Of(parsed.Positional.Select(call.Write));
    }

    private static Classification Copy(Call call)
    {
        var parsed = call.Parse(["path", "literalpath", "destination", "filter", "include", "exclude", "credential", "tosession",
            "fromsession", "t", "target-directory"], psPrefix: call.Name.Contains('-', StringComparison.Ordinal), call.Name is "xcopy" ? Slash.Flag : Slash.FlagUnderCmd);
        if (parsed.Named.ContainsKey("tosession") || parsed.Named.ContainsKey("fromsession"))
            return Leaf(ActionClass.Outward, "copies to or from another computer");
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        var sources = parsed.Values("path", "literalpath").SelectMany(call.Items).ToList();
        var positional = parsed.Positional.SelectMany(call.Items).ToList();
        CommandWord? destination = parsed.Named.GetValueOrDefault("destination") ?? parsed.Named.GetValueOrDefault("t")
                                   ?? parsed.Named.GetValueOrDefault("target-directory");
        if (destination is null && positional.Count > 0 && (positional.Count > 1 || sources.Count > 0))
        {
            destination = positional[^1];
            positional.RemoveAt(positional.Count - 1);
        }
        sources.AddRange(positional);
        if (sources.Count == 0) return Unknown("copies something the classifier cannot place");
        var leaves = sources.Select(call.Read).ToList();
        leaves.Add(call.Write(destination ?? call.CwdWord));
        return Classification.Of(leaves);
    }

    private static Classification Robocopy(Call call)
    {
        var parsed = call.Parse([], psPrefix: false, Slash.Flag);
        if (parsed.Positional.Count < 2) return Unknown("a robocopy the classifier cannot read");
        var source = parsed.Positional[0];
        var destination = parsed.Positional[1];
        var leaves = new List<Classification> { call.Read(source), call.Write(destination) };
        if (parsed.Flag("/mir", "/purge")) leaves.Add(call.Remove(destination));
        if (parsed.Flag("/mov", "/move")) leaves.Add(call.Remove(source));
        return Classification.Of(leaves);
    }

    private static Classification MoveItem(Call call)
    {
        var parsed = call.Parse(["path", "literalpath", "destination", "filter", "include", "exclude", "credential", "t", "target-directory"],
            psPrefix: call.Name.Contains('-', StringComparison.Ordinal), Slash.FlagUnderCmd);
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        var sources = parsed.Values("path", "literalpath").SelectMany(call.Items).ToList();
        var positional = parsed.Positional.SelectMany(call.Items).ToList();
        CommandWord? destination = parsed.Named.GetValueOrDefault("destination") ?? parsed.Named.GetValueOrDefault("t")
                                   ?? parsed.Named.GetValueOrDefault("target-directory");
        if (destination is null && positional.Count > 0 && (positional.Count > 1 || sources.Count > 0))
        {
            destination = positional[^1];
            positional.RemoveAt(positional.Count - 1);
        }
        sources.AddRange(positional);
        if (sources.Count == 0) return Unknown("moves something the classifier cannot place");
        var leaves = sources.Select(call.Remove).ToList();
        leaves.Add(call.Write(destination ?? call.CwdWord));
        return Classification.Of(leaves);
    }

    private static Classification RenameItem(Call call)
    {
        var parsed = call.Parse(["path", "literalpath", "newname", "credential"], psPrefix: true, Slash.FlagUnderCmd);
        var source = parsed.Named.GetValueOrDefault("path") ?? parsed.Named.GetValueOrDefault("literalpath") ?? parsed.Positional.FirstOrDefault();
        if (source is null || parsed.Splatted) return Unknown("renames something the classifier cannot place");
        return call.Remove(source);
    }

    private static Classification RemoveItem(Call call)
    {
        var powershell = call.Name.Contains('-', StringComparison.Ordinal) || call.Shell == ShellKind.PowerShell;
        var parsed = call.Parse(powershell ? ["path", "literalpath", "lp", "pspath", "include", "exclude", "filter", "credential", "stream"] : ["size", "s"],
            psPrefix: powershell, call.Name is "del" or "erase" or "rd" or "rmdir" ? Slash.FlagUnderCmd : Slash.Path);
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        var targets = parsed.Values("path", "literalpath", "lp", "pspath").Concat(parsed.Positional).SelectMany(call.Items).ToList();
        if (targets.Count == 0)
            return Leaf(ActionClass.Destructive, "removes what it is handed, which the classifier cannot see");
        return Classification.Of(targets.Select(call.Remove));
    }

    private static Classification Find(Call call)
    {
        // cmd's find: find [/v /c /n /i] "text" [files].
        if (call.Args.Count > 0 && (call.Args[0].Quoted || (call.Args[0].Text.StartsWith('/') && call.Args[0].Text.Length <= 3)))
        {
            var parsed = call.Parse([], psPrefix: false, Slash.Flag);
            var files = parsed.Positional.Skip(1).ToList();
            return files.Count == 0 ? Work("searches its input") : Classification.Of(files.Select(call.Read));
        }
        var leaves = new List<Classification>();
        var roots = new List<CommandWord>();
        var k = 0;
        while (k < call.Args.Count && !call.Args[k].Text.StartsWith('-') && call.Args[k].Text is not ("(" or "!" or "\\(")) roots.Add(call.Args[k++]);
        if (roots.Count == 0) roots.Add(new CommandWord("."));
        leaves.AddRange(roots.Select(call.Read));
        for (; k < call.Args.Count; k++)
        {
            var t = call.Args[k].Text;
            if (t == "-delete") leaves.AddRange(roots.Select(call.Remove));
            else if (t is "-exec" or "-execdir" or "-ok" or "-okdir")
            {
                var inner = new List<CommandWord>();
                for (k++; k < call.Args.Count && call.Args[k].Text is not (";" or "+" or "\\;"); k++)
                    inner.Add(call.Args[k].Text.Contains("{}", StringComparison.Ordinal) ? call.Args[k] with { Dynamic = true } : call.Args[k]);
                leaves.Add(call.Inner(inner));
            }
            else if (t is "-fprint" or "-fprint0" or "-fprintf" or "-fls" && k + 1 < call.Args.Count) leaves.Add(call.Write(call.Args[++k]));
        }
        return Classification.Of(leaves);
    }

    private static Classification Sed(Call call)
    {
        var args = call.Args;
        var inPlace = args.Any(a => a.Text == "-i" || a.Text.StartsWith("-i", StringComparison.Ordinal) && !a.Text.StartsWith("-i-", StringComparison.Ordinal)
                                    || a.Text.StartsWith("--in-place", StringComparison.Ordinal) || (a.Text.StartsWith('-') && !a.Text.StartsWith("--", StringComparison.Ordinal) && a.Text.Contains('i', StringComparison.Ordinal)));
        var scripts = new List<CommandWord>();
        var files = new List<CommandWord>();
        for (var k = 0; k < args.Count; k++)
        {
            var t = args[k].Text;
            if (t is "-e" or "--expression" && k + 1 < args.Count) { scripts.Add(args[++k]); continue; }
            if (t is "-f" or "--file") return Unknown("runs a sed script file");
            if (t.StartsWith('-')) continue;
            if (scripts.Count == 0) scripts.Add(args[k]);
            else files.Add(args[k]);
        }
        if (scripts.Count == 0 || scripts.Any(s => s.Dynamic || !SimpleSedScript(s.Text))) return Unknown("a sed script that can run commands or write files");
        if (inPlace) return files.Count == 0 ? Unknown("edits files the classifier cannot place") : Classification.Of(files.Select(call.Write));
        return files.Count == 0 ? Work("edits its input") : Classification.Of(files.Select(call.Read));
    }

    private static bool SimpleSedScript(string script)
    {
        // Line or pattern addresses with p, d, q or =, or one s/// with g, i, p or a number as flags.
        foreach (var command in script.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var c = command.Trim(' ', '\t');
            if (c.Length == 0) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"\A(?:\d+|\$|/[^/]*/)?(?:,(?:\d+|\$|/[^/]*/))?\s*[pdq=]\z")) continue;
            var s = System.Text.RegularExpressions.Regex.Match(c, @"\A(?:\d+|\$|/[^/]*/)?(?:,(?:\d+|\$|/[^/]*/))?\s*s(.)");
            if (s.Success)
            {
                var delimiter = s.Groups[1].Value[0];
                var body = c[s.Length..];
                var parts = 0;
                var k = 0;
                for (; k < body.Length && parts < 2; k++)
                {
                    if (body[k] == '\\') { k++; continue; }
                    if (body[k] == delimiter) parts++;
                }
                if (parts == 2 && System.Text.RegularExpressions.Regex.IsMatch(body[k..], @"\A[gip0-9]*\z")) continue;
            }
            return false;
        }
        return true;
    }

    private static Classification Sort(Call call)
    {
        var parsed = call.Parse(["o", "output", "k", "key", "t", "field-separator", "s", "buffer-size", "t", "temporary-directory"], psPrefix: false, Slash.FlagUnderCmd);
        var leaves = parsed.Positional.Select(call.Read).ToList();
        if ((parsed.Named.GetValueOrDefault("o") ?? parsed.Named.GetValueOrDefault("output")) is { } output) leaves.Add(call.Write(output));
        for (var k = 0; k + 1 < call.Args.Count; k++)
            if (call.Shell == ShellKind.Cmd && call.Args[k].Text.Equals("/o", StringComparison.OrdinalIgnoreCase)) leaves.Add(call.Write(call.Args[k + 1]));
        return leaves.Count == 0 ? Work("sorts its input") : Classification.Of(leaves);
    }

    private static Classification DiskDump(Call call)
    {
        var leaves = new List<Classification>();
        foreach (var a in call.Args)
        {
            if (a.Text.StartsWith("if=", StringComparison.Ordinal)) leaves.Add(call.Read(a with { Text = a.Text[3..] }));
            if (a.Text.StartsWith("of=", StringComparison.Ordinal)) leaves.Add(call.Remove(a with { Text = a.Text[3..] }));
        }
        return leaves.Count == 0 ? Work("copies its input") : Classification.Of(leaves);
    }

    private static bool IsRemote(string text) =>
        text.StartsWith("rsync://", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(text, @"\A(?:[^/\\:@\s]+@)?[^/\\:\s]{2,}:");

    private static Classification Rsync(Call call)
    {
        var parsed = call.Parse(["e", "rsh", "exclude", "include", "filter", "f", "exclude-from", "include-from"], psPrefix: false);
        if (parsed.Named.ContainsKey("e") || parsed.Named.ContainsKey("rsh")) return Unknown("runs a remote shell it names");
        if (parsed.Positional.Any(p => IsRemote(p.Text))) return Leaf(ActionClass.Outward, "copies to or from another computer");
        if (parsed.Positional.Count < 2) return Unknown("an rsync the classifier cannot read");
        var destination = parsed.Positional[^1];
        var sources = parsed.Positional.Take(parsed.Positional.Count - 1).ToList();
        var leaves = sources.Select(call.Read).ToList();
        leaves.Add(call.Write(destination));
        if (parsed.Flags.Any(f => f.StartsWith("delete", StringComparison.Ordinal))) leaves.Add(call.Remove(destination));
        if (parsed.Flag("remove-source-files")) leaves.AddRange(sources.Select(call.Remove));
        return Classification.Of(leaves);
    }

    private static Classification Scp(Call call)
    {
        var parsed = call.Parse(["p", "i", "o", "c", "f", "l", "s", "j", "f"], psPrefix: false);
        return parsed.Positional.Any(p => IsRemote(p.Text))
            ? Leaf(ActionClass.Outward, "copies to or from another computer")
            : Unknown("an scp the classifier cannot read");
    }

    private static Classification Archive(Call call)
    {
        switch (call.Name)
        {
            case "compress-archive":
            case "expand-archive":
            {
                var parsed = call.Parse(["path", "literalpath", "destinationpath", "compressionlevel"]);
                if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
                var sources = parsed.Values("path", "literalpath").Concat(parsed.Positional.Take(1)).SelectMany(call.Items).ToList();
                var destination = parsed.Named.GetValueOrDefault("destinationpath") ?? parsed.Positional.Skip(1).FirstOrDefault();
                var leaves = sources.Select(call.Read).ToList();
                leaves.Add(call.Write(destination ?? call.CwdWord));
                return Classification.Of(leaves);
            }
            case "tar":
            {
                var parsed = call.Parse(["f", "file", "c", "directory"], psPrefix: false);
                var create = call.Args.Any(a => a.Text is "--create" || (a.Text.StartsWith('-') && !a.Text.StartsWith("--", StringComparison.Ordinal) && a.Text.Contains('c', StringComparison.Ordinal))
                                               || (call.Args.IndexOf(a) == 0 && !a.Text.StartsWith('-') && a.Text.Contains('c', StringComparison.Ordinal)));
                var extract = call.Args.Any(a => a.Text is "--extract" or "--get" || (a.Text.StartsWith('-') && !a.Text.StartsWith("--", StringComparison.Ordinal) && a.Text.Contains('x', StringComparison.Ordinal))
                                                || (call.Args.IndexOf(a) == 0 && !a.Text.StartsWith('-') && a.Text.Contains('x', StringComparison.Ordinal)));
                var file = parsed.Named.GetValueOrDefault("f") ?? parsed.Named.GetValueOrDefault("file");
                var directory = parsed.Named.GetValueOrDefault("c") ?? parsed.Named.GetValueOrDefault("directory");
                var leaves = new List<Classification>();
                if (file is not null) leaves.Add(create ? call.Write(file) : call.Read(file));
                if (extract) leaves.Add(call.Write(directory ?? call.CwdWord));
                if (create) leaves.AddRange(parsed.Positional.Select(call.Read));
                return leaves.Count == 0 ? Unknown("a tar the classifier cannot read") : Classification.Of(leaves);
            }
            case "unzip":
            {
                var parsed = call.Parse(["d", "x"], psPrefix: false);
                var leaves = parsed.Positional.Take(1).Select(call.Read).ToList();
                leaves.Add(call.Write(parsed.Named.GetValueOrDefault("d") ?? call.CwdWord));
                return Classification.Of(leaves);
            }
            case "zip":
            {
                var parsed = call.Parse([], psPrefix: false);
                if (parsed.Positional.Count == 0) return Unknown("a zip the classifier cannot read");
                var leaves = parsed.Positional.Skip(1).Select(call.Read).ToList();
                leaves.Add(call.Write(parsed.Positional[0]));
                return Classification.Of(leaves);
            }
            default:
            {
                // 7z: a (add) archive files...; x or e (extract) archive [-o dir].
                var sub = call.Sub();
                var output = call.Args.FirstOrDefault(a => a.Text.StartsWith("-o", StringComparison.Ordinal));
                var positional = call.Args.Where(a => !a.Text.StartsWith('-')).Skip(1).ToList();
                if (sub is "x" or "e")
                {
                    var leaves = positional.Take(1).Select(call.Read).ToList();
                    leaves.Add(call.Write(output is null ? call.CwdWord : output with { Text = output.Text[2..] }));
                    return Classification.Of(leaves);
                }
                if (sub is "a" or "u" && positional.Count > 0)
                {
                    var leaves = positional.Skip(1).Select(call.Read).ToList();
                    leaves.Add(call.Write(positional[0]));
                    return Classification.Of(leaves);
                }
                return sub is "l" or "t" ? Classification.Of(positional.Take(1).Select(call.Read)) : Unknown("a 7z the classifier cannot read");
            }
        }
    }

    private static Classification ChangeMode(Call call)
    {
        if (call.Name == "attrib")
        {
            var paths = call.Args.Where(a => !a.Text.StartsWith('+') && !a.Text.StartsWith('-') && !(a.Text.StartsWith('/') && a.Text.Length <= 3)).ToList();
            var changes = call.Args.Any(a => a.Text.StartsWith('+') || (a.Text.StartsWith('-') && a.Text.Length == 2));
            if (paths.Count == 0) paths.Add(call.CwdWord);
            return Classification.Of(paths.Select(p => changes ? call.Write(p) : call.Read(p)));
        }
        var parsed = call.Parse(["reference"], psPrefix: false);
        var targets = parsed.Positional.Skip(parsed.Named.ContainsKey("reference") ? 0 : 1).ToList();
        if (targets.Count == 0) return Unknown($"a {call.Name} the classifier cannot read");
        return Classification.Of(targets.Select(call.Write));
    }

    // ---- network ----

    private static readonly string[] CurlDataFlags =
    [
        "-d", "--data", "--data-raw", "--data-binary", "--data-urlencode", "--data-ascii", "--json", "-F", "--form",
        "--form-string", "-T", "--upload-file", "--post-data", "--post-file", "--body-data", "--body-file", "--post301",
    ];

    private static readonly string[] CredentialFlags = ["-H", "--header", "-u", "--user", "-b", "--cookie", "--oauth2-bearer", "-E", "--cert"];

    private static Classification Web(Call call)
    {
        if (call.Name == "start-bitstransfer")
        {
            var transfer = call.Args.SkipWhile(a => !a.Text.StartsWith("-TransferType", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            if (transfer is null || transfer.Dynamic || transfer.Text.StartsWith("Upload", StringComparison.OrdinalIgnoreCase))
                return transfer is null ? new Classification(ActionClass.ReadWeb, "downloads a file") { CarriesData = true, ReadsUntrusted = true, Field = "read" }
                                        : Leaf(ActionClass.Outward, "uploads a file");
            return new Classification(ActionClass.ReadWeb, "downloads a file") { CarriesData = true, ReadsUntrusted = true, Field = "read" };
        }
        var args = call.Args;
        var outward = false;
        var carries = false;
        string? why = null;
        CommandWord? output = null;
        var urls = new List<CommandWord>();
        for (var k = 0; k < args.Count; k++)
        {
            var w = args[k];
            var t = w.Text;
            if (w.Dynamic && t.StartsWith('@')) { outward = true; why = "its parameters are hidden in a splat"; continue; }
            if (t is "-X" or "--request" && k + 1 < args.Count)
            {
                var method = args[++k];
                if (method.Dynamic || !IsReadMethod(method.Text)) { outward = true; why = $"sends a {method.Text} request"; }
                continue;
            }
            if (t.StartsWith("-X", StringComparison.Ordinal) && t.Length > 2 && !t.StartsWith("--", StringComparison.Ordinal))
            {
                if (!IsReadMethod(t[2..])) { outward = true; why = $"sends a {t[2..]} request"; }
                continue;
            }
            if (t.StartsWith("--request=", StringComparison.Ordinal) || t.StartsWith("--method=", StringComparison.Ordinal))
            {
                var method = t[(t.IndexOf('=', StringComparison.Ordinal) + 1)..];
                if (!IsReadMethod(method)) { outward = true; why = $"sends a {method} request"; }
                continue;
            }
            if (t == "-K" || t.StartsWith("--config", StringComparison.Ordinal) || t == "-i" && call.Name == "wget" || t.StartsWith("--input-file", StringComparison.Ordinal))
                return Unknown("reads its requests from a file");
            if (CurlDataFlags.Contains(t, StringComparer.Ordinal))
            {
                outward = true; why = "sends data in the request body"; k++; continue;
            }
            // -d@notes.txt, -Fname=x, -Tfile and --data=x carry their value in the same word. (In Windows PowerShell 5.1
            // curl is Invoke-WebRequest, so a -TimeoutSec there reads as -T: a stricter reading, never a looser one.)
            var attached = call.Name is "curl" or "wget" && t.Length > 2 && !t.StartsWith("--", StringComparison.Ordinal)
                           && (t.StartsWith("-d", StringComparison.Ordinal) || t.StartsWith("-F", StringComparison.Ordinal) || t.StartsWith("-T", StringComparison.Ordinal));
            if (attached || CurlDataFlags.Any(f => f.StartsWith("--", StringComparison.Ordinal) && t.StartsWith(f + "=", StringComparison.Ordinal)))
            {
                outward = true; why = "sends data in the request body"; continue;
            }
            if (CredentialFlags.Contains(t, StringComparer.Ordinal)) { carries = true; k++; continue; }
            if ((t is "-o" or "--output" or "--output-document" or "--output-file" || (t == "-O" && call.Name == "wget")) && k + 1 < args.Count)
            {
                output = args[++k];
                continue;
            }
            if (t.StartsWith('-') && t.Length > 2 && t[1] != '-' && call.Name is "invoke-webrequest" or "iwr" or "invoke-restmethod" or "irm" or "curl" or "wget" && Ps(t) is { } param)
            {
                var name = param.Name;
                var value = param.Inline is not null ? new CommandWord(param.Inline, w.Dynamic) : k + 1 < args.Count ? args[k + 1] : null;
                if (Prefix(name, "method") || Prefix(name, "custommethod"))
                {
                    if (param.Inline is null) k++;
                    if (value is null || value.Dynamic || !IsReadMethod(value.Text)) { outward = true; why = $"sends a {value?.Text ?? "non-GET"} request"; }
                    continue;
                }
                if (Prefix(name, "body") || Prefix(name, "infile") || Prefix(name, "form"))
                {
                    outward = true; why = "sends data in the request body"; if (param.Inline is null) k++; continue;
                }
                if (Prefix(name, "headers") || Prefix(name, "credential") || Prefix(name, "token") || Prefix(name, "authentication")
                    || Prefix(name, "websession") || Prefix(name, "certificate") || Prefix(name, "usedefaultcredentials"))
                {
                    carries = true; if (param.Inline is null && !Prefix(name, "usedefaultcredentials")) k++; continue;
                }
                if (Prefix(name, "outfile")) { output = value; if (param.Inline is null) k++; continue; }
                if (Prefix(name, "uri")) { if (value is not null) urls.Add(value); if (param.Inline is null) k++; continue; }
                continue;
            }
            if (!t.StartsWith('-')) urls.Add(w);
        }
        var leaves = new List<Classification>();
        var origins = urls.Select(u => u.Dynamic ? null : GrantTarget.OriginOf(u.Text)).ToList();
        var target = urls.Count > 0 ? urls[0].Text : null;
        if (outward)
            leaves.Add(new Classification(ActionClass.Outward, why ?? "sends data") { Target = target, CarriesData = true });
        else
        {
            var known = origins.Count > 0 && origins.All(o => o is not null);
            leaves.Add(new Classification(ActionClass.ReadWeb, known ? $"reads {origins[0]}" : "reads the web")
            {
                Target = target,
                GrantTarget = known ? GrantTarget.Site(origins[0]!) : null,
                CarriesData = carries || !known || urls.Any(u => u.Text.Contains('?', StringComparison.Ordinal) || u.Text.Contains('@', StringComparison.Ordinal)),
                ReadsUntrusted = true,
                Field = "read",
            });
        }
        if (output is not null) leaves.Add(call.Write(output));
        return Classification.Of(leaves);
    }

    private static bool IsReadMethod(string method) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
        || method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase) || method.Equals("Default", StringComparison.OrdinalIgnoreCase);

    private static bool Prefix(string given, string full) => given.Length >= 2 && full.StartsWith(given, StringComparison.Ordinal);

    private static (string Name, string? Inline)? Ps(string text)
    {
        var body = text.TrimStart('-');
        var colon = body.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 ? (body[..colon].ToLowerInvariant(), body[(colon + 1)..]) : (body.ToLowerInvariant(), null);
    }

    private static Classification InvokeCommand(Call call)
    {
        var parsed = call.Parse(["computername", "cn", "session", "hostname", "vmname", "vmid", "containerid", "connectionuri", "uri",
            "filepath", "scriptblock", "argumentlist", "args", "credential", "port", "jobname", "throttlelimit", "inputobject"]);
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        if (new[] { "computername", "cn", "session", "hostname", "vmname", "vmid", "containerid", "connectionuri", "uri" }.Any(parsed.Named.ContainsKey))
            return Leaf(ActionClass.Outward, "runs commands on another computer");
        if (parsed.Named.GetValueOrDefault("filepath") is { } file) return Script([file], call.Part, call.State, call.Context, call.Words);
        return Work("runs a script block");
    }
}
