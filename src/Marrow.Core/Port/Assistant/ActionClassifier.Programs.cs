// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a; unit AS-1; ED-NEW-dextracore-2). No Swift twin.
// Programs with subcommands (git, gh, npm, dotnet, package managers, Windows settings tools) and the wrappers that run
// another command (powershell -Command and -EncodedCommand, cmd /c, bash -c, wsl, env, xargs, start, iex).
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public static partial class ActionClassifier
{
    // ---- git ----

    private static readonly HashSet<string> GitRead = new(StringComparer.Ordinal)
    {
        "status", "diff", "log", "show", "blame", "annotate", "grep", "ls-files", "ls-tree", "rev-parse", "rev-list", "cat-file",
        "describe", "shortlog", "whatchanged", "version", "help", "name-rev", "merge-base", "for-each-ref", "show-ref",
        "count-objects", "fsck", "check-ignore", "check-attr", "check-ref-format", "var", "verify-commit", "verify-tag", "cherry",
        "range-diff", "request-pull", "show-branch", "diff-tree", "diff-files", "diff-index", "get-tar-commit-id",
    };

    private static readonly HashSet<string> GitLocal = new(StringComparer.Ordinal)
    {
        "add", "commit", "mv", "merge", "rebase", "cherry-pick", "revert", "apply", "am", "switch", "notes", "bisect",
        "sparse-checkout", "init", "repack", "maintenance", "pack-refs", "update-index", "read-tree", "commit-tree", "write-tree",
        "mktree", "hash-object", "symbolic-ref", "rm", "stage", "rerere",
    };

    private static Classification Git(Call call)
    {
        var args = call.Args;
        var cwd = call.Cwd;
        var k = 0;
        while (k < args.Count && args[k].Text.StartsWith('-'))
        {
            var t = args[k].Text;
            if (t == "-C")
            {
                cwd = k + 1 < args.Count && cwd is not null ? Paths.Full(args[k + 1], cwd, call.Context) : null;
                k += 2;
                continue;
            }
            if (t is "--no-pager" or "-P" or "--paginate" or "-p" or "--no-replace-objects" or "--literal-pathspecs"
                or "--no-optional-locks" or "--glob-pathspecs" or "--noglob-pathspecs" or "--icase-pathspecs")
            {
                k++;
                continue;
            }
            if (t is "--version" or "--help" or "-h") return Work("prints git's version or help");
            return Unknown($"git {t} changes how git runs");
        }
        if (k >= args.Count) return Work("prints git's help");
        var sub = args[k].Text.ToLowerInvariant();
        var rest = args.Skip(k + 1).ToList();
        bool Has(params string[] flags) => rest.Any(a => flags.Contains(a.Text, StringComparer.Ordinal));
        bool ShortHas(char letter) => rest.Any(a => a.Text.Length > 1 && a.Text[0] == '-' && a.Text[1] != '-' && a.Text.Contains(letter, StringComparison.Ordinal));
        var first = rest.FirstOrDefault(a => !a.Text.StartsWith('-'))?.Text.ToLowerInvariant() ?? "";
        var inside = cwd is not null && call.Context.MissionWorktree is { } worktree && PathRules.IsInside(cwd, worktree, strict: false);
        Classification Local(string reason) => inside
            ? Work(reason)
            : new Classification(ActionClass.WriteElsewhere, reason + " outside the mission worktree")
                { Target = cwd, GrantTarget = cwd is null ? null : GrantTarget.Folder(cwd), Field = "write" };
        Classification Discard(string reason) => inside ? Work(reason) : Leaf(ActionClass.Destructive, reason + " outside the mission worktree") with { Target = cwd };
        var remote = new Classification(ActionClass.ReadWeb, "reads a remote repository") { CarriesData = true, ReadsUntrusted = true, Field = "read" };

        if (sub is "bisect" && first == "run" || sub is "rebase" && (Has("-x", "--exec") || rest.Any(a => a.Text.StartsWith("--exec=", StringComparison.Ordinal))))
            return Unknown("runs a command it names");
        if (GitRead.Contains(sub)) return Work("reads the repository");
        if (GitLocal.Contains(sub)) return Local("changes the repository");
        switch (sub)
        {
            case "branch":
                if (Has("-D", "-d", "--delete") || ShortHas('D')) return Leaf(ActionClass.Destructive, "deletes a branch");
                return rest.Count == 0 ? Work("lists branches") : Local("changes branches");
            case "tag":
                if (Has("-d", "--delete")) return Leaf(ActionClass.Destructive, "deletes a tag");
                return rest.Count == 0 || Has("-l", "--list", "-n") ? Work("lists tags") : Local("creates a tag");
            case "stash":
                if (first is "list" or "show") return Work("reads the stash");
                if (first is "drop" or "clear") return Leaf(ActionClass.Destructive, "throws away stashed changes");
                return Local("stashes or restores changes");
            case "checkout":
                if (Has("--", ".", "-f", "--force", "-p", "--patch", "--ours", "--theirs") || rest.Any(a => a.Text == ".")) return Discard("throws away uncommitted changes");
                return Local("switches branch");
            case "restore":
                if ((Has("--staged", "-S") || ShortHas('S')) && !Has("--worktree", "-W")) return Local("unstages changes");
                return Discard("throws away uncommitted changes");
            case "reset":
                return Has("--hard", "--merge", "--keep") ? Discard("throws away commits and changes") : Local("moves the branch");
            case "clean":
                if (Has("-n", "--dry-run") || ShortHas('n')) return Work("lists files it would remove");
                return Has("-f", "--force") || ShortHas('f') ? Discard("removes untracked files") : Work("refuses to clean without --force");
            case "gc":
                return rest.Any(a => a.Text.StartsWith("--prune=", StringComparison.Ordinal) && !a.Text.EndsWith("=2.weeks.ago", StringComparison.Ordinal))
                    ? Leaf(ActionClass.Destructive, "prunes objects the activity log could restore") : Local("tidies the repository");
            case "prune":
            case "filter-branch":
            case "filter-repo":
                return Leaf(ActionClass.Destructive, "rewrites or prunes history");
            case "update-ref":
                return Has("-d", "--delete") ? Leaf(ActionClass.Destructive, "deletes a ref") : Local("moves a ref");
            case "reflog":
                return first is "expire" or "delete" ? Leaf(ActionClass.Destructive, "throws away the reflog") : Work("reads the reflog");
            case "worktree":
                return first switch
                {
                    "list" => Work("lists worktrees"),
                    "remove" or "prune" => Leaf(ActionClass.Destructive, "removes a worktree"),
                    _ => new Classification(ActionClass.WriteElsewhere, "changes the repository's worktrees") { Field = "write" },
                };
            case "fetch":
            case "ls-remote":
                return remote;
            case "pull":
                return Classification.Of([remote, Local("merges what it pulls")]);
            case "submodule":
                return first is "status" or "foreach" or "summary" or "" ? (first == "foreach" ? Unknown("runs a command in each submodule") : Work("reads submodules"))
                    : Classification.Of([remote, Local("updates submodules")]);
            case "clone":
            {
                var positional = rest.Where(a => !a.Text.StartsWith('-')).ToList();
                var destination = positional.Count > 1 ? positional[1] : new CommandWord(cwd ?? "", cwd is null);
                return Classification.Of([remote, Paths.Write(destination, cwd, call.Context)]);
            }
            case "remote":
                if (first is "" or "show" or "get-url") return Work("reads the remotes");
                if (first is "update" or "prune") return remote;
                return new Classification(ActionClass.WriteElsewhere, "changes the repository's remotes") { Field = "write" };
            case "push":
                return new Classification(ActionClass.Outward, "sends commits to a remote") { Target = string.Join(' ', rest.Select(a => a.Text)), CarriesData = true };
            case "send-email":
                return Leaf(ActionClass.Outward, "sends email");
            case "svn":
                return first == "dcommit" ? Leaf(ActionClass.Outward, "sends commits to a remote") : Unknown("a git svn command");
            case "lfs":
                return first switch
                {
                    "push" => Leaf(ActionClass.Outward, "sends files to a remote"),
                    "pull" or "fetch" => remote,
                    "ls-files" or "status" or "env" or "version" => Work("reads git lfs"),
                    _ => Unknown("a git lfs command"),
                };
            case "config":
            {
                var reads = Has("--get", "--get-all", "--get-regexp", "--get-urlmatch", "-l", "--list", "--show-origin", "--show-scope", "--name-only", "--get-color", "--get-colorbool");
                var writes = Has("--unset", "--unset-all", "--add", "--replace-all", "--rename-section", "--remove-section", "-e", "--edit");
                var positional = rest.Count(a => !a.Text.StartsWith('-'));
                if (!writes && (reads || positional <= 1)) return Work("reads git settings");
                return new Classification(ActionClass.WriteElsewhere, "changes git settings") { Field = "write" };
            }
            case "credential":
            case "credential-store":
            case "credential-cache":
            case "credential-manager":
            case "credential-manager-core":
                return Leaf(ActionClass.HardDeny, "handles stored credentials");
            case "archive":
            {
                var output = rest.FirstOrDefault(a => a.Text.StartsWith("--output=", StringComparison.Ordinal));
                var o = rest.FindIndex(a => a.Text == "-o");
                if (output is not null) return Paths.Write(output with { Text = output.Text[9..] }, cwd, call.Context);
                if (o >= 0 && o + 1 < rest.Count) return Paths.Write(rest[o + 1], cwd, call.Context);
                return Work("writes an archive to its output");
            }
            case "format-patch":
            {
                var o = rest.FindIndex(a => a.Text is "-o" or "--output-directory");
                return o >= 0 && o + 1 < rest.Count ? Paths.Write(rest[o + 1], cwd, call.Context) : Local("writes patch files");
            }
            case "bundle":
                return first == "create" && rest.Count(a => !a.Text.StartsWith('-')) > 1
                    ? Paths.Write(rest.Where(a => !a.Text.StartsWith('-')).ElementAt(1), cwd, call.Context)
                    : Work("reads a bundle");
            default:
                return Unknown($"git {sub} is not a git command the classifier knows (an alias can run anything)");
        }
    }

    // ---- GitHub CLI ----

    private static Classification GitHub(Call call)
    {
        var sub = call.Sub();
        var sub2 = call.Sub(1);
        var read = new Classification(ActionClass.ReadWeb, "reads GitHub") { CarriesData = true, ReadsUntrusted = true, Field = "read" };
        Classification Out(string reason) => Leaf(ActionClass.Outward, reason);
        switch (sub)
        {
            case "auth":
                return sub2 switch
                {
                    "login" or "refresh" or "setup-git" or "switch" => Leaf(ActionClass.Login, "signs in to GitHub"),
                    "token" => Leaf(ActionClass.HardDeny, "prints a sign-in token"),
                    "status" or "logout" => Work("reads or ends the GitHub sign-in"),
                    _ => Unknown("a gh auth command"),
                };
            case "secret":
            case "ssh-key":
            case "gpg-key":
                return sub2 is "list" ? read : Leaf(ActionClass.HardDeny, "handles stored credentials");
            case "api":
            {
                var method = call.Args.SkipWhile(a => a.Text is not ("-X" or "--method")).Skip(1).FirstOrDefault();
                var inlineMethod = call.Args.FirstOrDefault(a => a.Text.StartsWith("--method=", StringComparison.Ordinal) || (a.Text.StartsWith("-X", StringComparison.Ordinal) && a.Text.Length > 2));
                var fields = call.HasIgnoreCase("-f", "-F", "--field", "--raw-field", "--input")
                             || call.Args.Any(a => a.Text.StartsWith("--field=", StringComparison.Ordinal) || a.Text.StartsWith("--raw-field=", StringComparison.Ordinal) || a.Text.StartsWith("--input=", StringComparison.Ordinal));
                var methodText = method?.Text ?? (inlineMethod is null ? null : inlineMethod.Text.StartsWith("--", StringComparison.Ordinal) ? inlineMethod.Text[9..] : inlineMethod.Text[2..]);
                if (fields || (methodText is not null && !IsReadMethod(methodText)) || method?.Dynamic == true) return Out("sends a request to GitHub");
                return read;
            }
            case "pr":
                return sub2 switch
                {
                    "view" or "list" or "status" or "diff" or "checks" => read,
                    "checkout" => Classification.Of([read, Git(new Call("git", [new CommandWord("git"), new CommandWord("checkout"), new CommandWord("pr")], call.Part, call.State, call.Context, call.Depth))]),
                    _ => Out("posts to a pull request"),
                };
            case "issue":
                return sub2 switch
                {
                    "view" or "list" or "status" => read,
                    "delete" => Leaf(ActionClass.Destructive, "deletes an issue"),
                    _ => Out("posts to an issue"),
                };
            case "release":
                return sub2 switch
                {
                    "view" or "list" => read,
                    "download" => Classification.Of([read, call.Write(call.CwdWord)]),
                    "delete" or "delete-asset" => Leaf(ActionClass.Destructive, "deletes a release"),
                    _ => Out("publishes a release"),
                };
            case "repo":
                return sub2 switch
                {
                    "view" or "list" => read,
                    "clone" => Classification.Of([read, call.Write(call.CwdWord)]),
                    "delete" or "archive" => Leaf(ActionClass.Destructive, "deletes or archives a repository"),
                    _ => Out("changes a repository on GitHub"),
                };
            case "gist":
                return sub2 switch
                {
                    "view" or "list" => read,
                    "clone" => Classification.Of([read, call.Write(call.CwdWord)]),
                    "delete" => Leaf(ActionClass.Destructive, "deletes a gist"),
                    _ => Out("publishes a gist"),
                };
            case "workflow":
            case "run":
                return sub2 switch
                {
                    "view" or "list" or "watch" => read,
                    "download" => Classification.Of([read, call.Write(call.CwdWord)]),
                    "delete" => Leaf(ActionClass.Destructive, "deletes a run"),
                    _ => Out("starts or changes a workflow on GitHub"),
                };
            case "label":
                return sub2 is "list" ? read : Out("changes labels on GitHub");
            case "cache":
                return sub2 is "list" ? read : Leaf(ActionClass.Destructive, "deletes a cache");
            case "search":
            case "status":
            case "browse":
                return read;
            case "extension":
                return sub2 switch
                {
                    "install" or "upgrade" => Leaf(ActionClass.Install, "installs a gh extension"),
                    "remove" => Leaf(ActionClass.Destructive, "removes a gh extension"),
                    "list" => Work("lists gh extensions"),
                    _ => Unknown("runs a gh extension"),
                };
            case "config":
                return sub2 is "get" or "list" ? Work("reads gh settings") : new Classification(ActionClass.WriteElsewhere, "changes gh settings") { Field = "write" };
            case "alias":
                return sub2 is "list" ? Work("lists gh aliases") : Unknown("defines a gh alias, which can run anything");
            case "--version":
            case "version":
            case "help":
                return Work("prints gh's version or help");
            default:
                return Unknown($"gh {sub} is not a command the classifier knows");
        }
    }

    // ---- packages ----

    private static Classification NodePackages(Call call)
    {
        var parsed = call.Parse([], psPrefix: false);
        var positional = parsed.Positional.Select(p => p.Text.ToLowerInvariant()).ToList();
        var sub = positional.FirstOrDefault() ?? "";
        var global = parsed.Flag("g", "global") || parsed.Flags.Contains("location=global") || (call.Name == "yarn" && sub == "global");
        if (call.Name == "yarn" && sub == "global") sub = positional.Count > 1 ? positional[1] : "";
        switch (sub)
        {
            case "publish":
                return Leaf(ActionClass.Outward, "publishes a package");
            case "unpublish":
            case "deprecate":
                return Leaf(ActionClass.Destructive, "withdraws a published package");
            case "dist-tag":
            case "owner":
            case "access":
            case "team":
            case "org":
            case "hook":
            case "star":
            case "unstar":
                return Leaf(ActionClass.Outward, "changes a package on the registry");
            case "login":
            case "adduser":
            case "add-user":
                return Leaf(ActionClass.Login, "signs in to the registry");
            case "logout":
                return Work("signs out of the registry");
            case "token":
                return Leaf(ActionClass.HardDeny, "handles registry tokens");
            case "install" or "i" or "add" or "in" or "ins" or "inst" or "insta" or "instal" or "isnt" or "isnta" or "isntal" or "isntall" or "ci"
                or "update" or "up" or "upgrade" or "udpate":
                return global ? Leaf(ActionClass.Install, "installs a package for every project")
                              : Unknown("installs project packages, which runs their install scripts");
            case "uninstall" or "un" or "remove" or "rm" or "r" or "unlink":
                return global ? Leaf(ActionClass.Destructive, "uninstalls a package for every project") : Unknown("removes project packages");
            case "config":
            case "c":
            {
                var verb = positional.Count > 1 ? positional[1] : "";
                return verb is "get" or "list" or "ls" ? Work("reads npm settings")
                    : new Classification(ActionClass.WriteElsewhere, "changes npm settings") { Field = "write" };
            }
            case "view" or "v" or "info" or "show" or "search" or "s" or "se" or "outdated" or "repo" or "docs" or "bugs" or "ping":
                return new Classification(ActionClass.ReadWeb, "reads the registry") { CarriesData = true, ReadsUntrusted = true, Field = "read" };
            case "ls" or "list" or "la" or "ll" or "explain" or "why" or "root" or "prefix" or "bin" or "help":
                return Work("reads the project's packages");
            case "audit":
                return positional.Contains("fix") ? Unknown("changes project packages")
                    : new Classification(ActionClass.ReadWeb, "checks packages against the registry") { CarriesData = true, Field = "read" };
            case "exec" or "x" or "dlx" or "create" or "init":
                return Unknown("runs a package from the registry");
            default:
                return Unknown($"{call.Name} {sub} runs the project's own scripts".TrimEnd());
        }
    }

    private static Classification DotNet(Call call)
    {
        var sub = call.Sub();
        var sub2 = call.Sub(1);
        var global = call.Has("-g", "--global");
        switch (sub)
        {
            case "nuget":
                return sub2 switch
                {
                    "push" => Leaf(ActionClass.Outward, "publishes a package"),
                    "delete" => Leaf(ActionClass.Destructive, "deletes a published package"),
                    "locals" => call.Has("--clear", "-c") ? Work("clears the local package caches") : Work("lists the local package caches"),
                    _ => Unknown("a dotnet nuget command"),
                };
            case "tool":
                return sub2 switch
                {
                    "install" or "update" => global ? Leaf(ActionClass.Install, "installs a tool for every project") : Unknown("installs a project tool"),
                    "uninstall" => global ? Leaf(ActionClass.Destructive, "uninstalls a tool") : Unknown("removes a project tool"),
                    "list" or "search" => Work("lists tools"),
                    "run" => Unknown("runs a project tool"),
                    _ => Unknown("a dotnet tool command"),
                };
            case "workload":
                return sub2 switch
                {
                    "install" or "update" or "repair" or "restore" => Leaf(ActionClass.Install, "installs a workload"),
                    "uninstall" or "clean" => Leaf(ActionClass.Destructive, "uninstalls a workload"),
                    "list" or "search" => Work("lists workloads"),
                    _ => Unknown("a dotnet workload command"),
                };
            case "new":
                return sub2 is "install" || call.Has("-i", "--install") ? Leaf(ActionClass.Install, "installs a template")
                    : sub2 is "uninstall" || call.Has("-u", "--uninstall") ? Leaf(ActionClass.Destructive, "uninstalls a template")
                    : Unknown("creates a project from a template");
            case "dev-certs":
                return call.Has("--trust", "-t", "--clean") ? Leaf(ActionClass.HardDeny, "changes the trusted certificates") : Unknown("a dotnet dev-certs command");
            case "user-secrets":
                return Leaf(ActionClass.HardDeny, "handles stored secrets");
            case "--version" or "--info" or "--list-sdks" or "--list-runtimes" or "help" or "--help":
                return Work("prints dotnet's version or help");
            default:
                return Unknown($"dotnet {sub} runs the project's own build".TrimEnd());
        }
    }

    private static Classification Docker(Call call)
    {
        var sub = call.Sub();
        var sub2 = call.Sub(1);
        return sub switch
        {
            "push" => Leaf(ActionClass.Outward, "publishes an image"),
            "login" => Leaf(ActionClass.Login, "signs in to a registry"),
            "logout" => Work("signs out of a registry"),
            "rm" or "rmi" => Leaf(ActionClass.Destructive, "removes containers or images"),
            "system" or "image" or "container" or "volume" or "network" or "builder" when sub2 is "prune" or "rm" or "remove" =>
                Leaf(ActionClass.Destructive, "removes containers, images or volumes"),
            "ps" or "images" or "version" or "info" or "inspect" or "logs" => Work("reads Docker"),
            "pull" => new Classification(ActionClass.ReadWeb, "downloads an image") { CarriesData = true, Field = "read" },
            _ => Unknown($"docker {sub} runs or changes containers"),
        };
    }

    private static Classification Pip(Call call)
    {
        var args = call.Args.Select(a => a.Text.ToLowerInvariant()).Where(t => !t.StartsWith('-')).ToList();
        if (call.Name == "uv" && args.FirstOrDefault() is "pip" or "tool") args = args.Skip(1).ToList();
        var sub = args.FirstOrDefault() ?? "";
        return sub switch
        {
            "install" or "create" or "update" or "upgrade" or "inject" => Leaf(ActionClass.Install, "installs packages"),
            "uninstall" or "remove" => Leaf(ActionClass.Destructive, "uninstalls packages"),
            "list" or "show" or "freeze" or "check" or "inspect" or "tree" => Work("reads installed packages"),
            "download" => Classification.Of([new Classification(ActionClass.ReadWeb, "downloads packages") { CarriesData = true, Field = "read" }, call.Write(call.CwdWord)]),
            "search" or "index" => new Classification(ActionClass.ReadWeb, "searches packages") { CarriesData = true, Field = "read" },
            "config" => args.Skip(1).FirstOrDefault() is "get" or "list" or "debug" ? Work("reads pip settings")
                : new Classification(ActionClass.WriteElsewhere, "changes pip settings") { Field = "write" },
            _ => Unknown($"{call.Name} {sub} changes the project's packages".TrimEnd()),
        };
    }

    private static Classification WindowsPackages(Call call)
    {
        var sub = call.Sub();
        return sub switch
        {
            "install" or "add" or "upgrade" or "update" or "import" or "configure" => Leaf(ActionClass.Install, "installs software"),
            "uninstall" or "remove" or "rm" => Leaf(ActionClass.Destructive, "uninstalls software"),
            "source" or "bucket" => call.Sub(1) is "list" or "export" ? Work("lists software sources") : Leaf(ActionClass.Install, "adds or removes a software source"),
            "search" or "show" or "list" or "info" or "export" or "status" or "outdated" or "find" or "which" or "cat" or "home" =>
                new Classification(ActionClass.ReadWeb, "reads the package catalogue") { CarriesData = true, Field = "read" },
            _ => Unknown($"{call.Name} {sub} is not a command the classifier knows"),
        };
    }

    private static Classification PosixPackages(Call call)
    {
        var sub = call.Args.FirstOrDefault()?.Text ?? "";
        if (call.Name == "pacman")
            return sub.StartsWith("-S", StringComparison.Ordinal) || sub.StartsWith("-U", StringComparison.Ordinal) ? Leaf(ActionClass.Install, "installs software")
                : sub.StartsWith("-R", StringComparison.Ordinal) ? Leaf(ActionClass.Destructive, "uninstalls software")
                : sub.StartsWith("-Q", StringComparison.Ordinal) ? Work("reads installed software") : Unknown("a pacman command");
        sub = call.Sub();
        return sub switch
        {
            "install" or "add" or "reinstall" or "upgrade" or "update" or "dist-upgrade" or "full-upgrade" or "tap" => Leaf(ActionClass.Install, "installs software"),
            "remove" or "purge" or "uninstall" or "autoremove" or "erase" or "del" or "untap" => Leaf(ActionClass.Destructive, "uninstalls software"),
            "search" or "show" or "info" or "list" or "policy" => Work("reads the package catalogue"),
            _ => Unknown($"{call.Name} {sub} is not a command the classifier knows"),
        };
    }

    private static Classification Msiexec(Call call) =>
        call.HasIgnoreCase("/x", "/uninstall", "-x", "-uninstall")
            ? Leaf(ActionClass.Destructive, "uninstalls software")
            : Leaf(ActionClass.Install, "installs software");

    private static Classification Dism(Call call)
    {
        var flags = call.Args.Select(a => a.Text.ToLowerInvariant()).ToList();
        if (flags.Any(f => f.StartsWith("/enable-", StringComparison.Ordinal) || f.StartsWith("/add-", StringComparison.Ordinal))) return Leaf(ActionClass.Install, "installs a Windows feature");
        if (flags.Any(f => f.StartsWith("/disable-", StringComparison.Ordinal) || f.StartsWith("/remove-", StringComparison.Ordinal))) return Leaf(ActionClass.Destructive, "removes a Windows feature");
        if (flags.Any(f => f.StartsWith("/get-", StringComparison.Ordinal) || f is "/checkhealth" or "/scanhealth")) return Work("reads Windows features");
        return Leaf(ActionClass.HardDeny, "changes the Windows image");
    }

    private static Classification Editor(Call call)
    {
        if (call.Args.Any(a => a.Text.StartsWith("--install-extension", StringComparison.Ordinal))) return Leaf(ActionClass.Install, "installs an editor extension");
        if (call.Args.Any(a => a.Text.StartsWith("--uninstall-extension", StringComparison.Ordinal))) return Leaf(ActionClass.Destructive, "uninstalls an editor extension");
        if (call.Has("--list-extensions", "--version", "-v", "--help", "-h")) return Work("reads the editor");
        return Unknown("opens an editor");
    }

    private static Classification Cloud(Call call)
    {
        var sub = call.Sub();
        var sub2 = call.Sub(1);
        if (sub == "login" || sub2 == "login" || (call.Name == "aws" && sub == "configure"))
            return Leaf(ActionClass.Login, $"signs in to {call.Name}");
        return Unknown($"{call.Name} changes cloud resources");
    }

    // ---- Windows settings ----

    private static Classification Registry(Call call)
    {
        var sub = call.Sub();
        var key = call.Args.Skip(1).FirstOrDefault()?.Text ?? "";
        var secret = key.Contains(@"\SAM", StringComparison.OrdinalIgnoreCase) || key.Contains(@"\SECURITY", StringComparison.OrdinalIgnoreCase)
                     || key.EndsWith("SAM", StringComparison.OrdinalIgnoreCase) || key.EndsWith("SECURITY", StringComparison.OrdinalIgnoreCase);
        switch (sub)
        {
            case "query":
            case "compare":
                return secret ? Leaf(ActionClass.HardDeny, "reads the registry's account secrets") : new Classification(ActionClass.ReadLocal, "reads the registry") { Target = key };
            case "export":
                return secret ? Leaf(ActionClass.HardDeny, "reads the registry's account secrets")
                    : Classification.Of([new Classification(ActionClass.ReadLocal, "reads the registry") { Target = key },
                        call.Args.Count > 2 ? call.Write(call.Args[2]) : Unknown("exports the registry somewhere it cannot place")]);
            default:
                return Leaf(ActionClass.HardDeny, "changes the registry (security settings)") with { Target = key };
        }
    }

    private static Classification ServiceControl(Call call)
    {
        var sub = call.Args.FirstOrDefault(a => !a.Text.StartsWith(@"\\", StringComparison.Ordinal))?.Text.ToLowerInvariant() ?? "";
        return sub is "query" or "queryex" or "qc" or "qdescription" or "qfailure" or "qfailureflags" or "qsidtype" or "qprivs"
            or "qtriggerinfo" or "qpreferrednode" or "qmanagedaccount" or "qprotection" or "enumdepend" or "getdisplayname" or "getkeyname" or "showsid"
            ? new Classification(ActionClass.ReadLocal, "reads Windows services")
            : Leaf(ActionClass.HardDeny, "changes a Windows service");
    }

    private static Classification Schtasks(Call call) =>
        call.HasIgnoreCase("/query") || call.Args.Count == 0
            ? new Classification(ActionClass.ReadLocal, "reads Task Scheduler")
            : Leaf(ActionClass.HardDeny, "changes Task Scheduler");

    private static Classification Wevtutil(Call call) =>
        call.Sub() is "qe" or "query-events" or "gl" or "get-log" or "el" or "enum-logs" or "gp" or "get-publisher" or "ep" or "enum-publishers" or "gli" or "get-loginfo"
            ? Work("reads event logs")
            : Leaf(ActionClass.HardDeny, "changes or clears event logs");

    private static Classification Net(Call call) => call.Sub() switch
    {
        "view" or "statistics" or "stats" or "config" or "time" or "help" or "helpmsg" => Work("reads network settings"),
        "use" => Unknown("connects a network drive"),
        _ => Leaf(ActionClass.HardDeny, "changes accounts, shares or services"),
    };

    private static Classification Route(Call call) =>
        call.Sub() is "print" || (call.Name == "arp" && call.Has("-a", "-g")) || call.Args.Count == 0
            ? Work("reads network routes")
            : Leaf(ActionClass.HardDeny, "changes network routes");

    private static Classification Certutil(Call call)
    {
        var flags = call.Args.Select(a => a.Text.ToLowerInvariant()).ToList();
        if (flags.Contains("-hashfile")) return Classification.Of(call.Args.Where(a => !a.Text.StartsWith('-')).Take(1).Select(call.Read));
        if (flags.Any(f => f is "-addstore" or "-delstore" or "-importpfx" or "-repairstore" or "-setreg" or "-delkey")) return Leaf(ActionClass.HardDeny, "changes the certificate store");
        return Unknown("certutil can download and decode files");
    }

    private static Classification SetItemProperty(Call call)
    {
        var parsed = call.Parse(["path", "literalpath", "name", "value", "type", "propertytype", "credential", "filter", "include", "exclude", "newname", "destination"]);
        if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
        var paths = parsed.Values("path", "literalpath").Concat(parsed.Positional.Take(1)).SelectMany(call.Items).ToList();
        if (paths.Count == 0) return Unknown("changes an item the classifier cannot place");
        return Classification.Of(paths.Select(call.Write));
    }

    private static Classification Setx(Call call) =>
        call.HasIgnoreCase("/m")
            ? Leaf(ActionClass.HardDeny, "changes a system-wide setting")
            : new Classification(ActionClass.WriteElsewhere, "saves an environment setting for later programs") { Field = "write" };

    private static Classification SetCommand(Call call)
    {
        if (call.Name == "set" && call.Shell == ShellKind.Posix) return Work("sets shell options");
        if (call.Name == "set" && call.Shell == ShellKind.Cmd)
        {
            var assignment = call.Args.FirstOrDefault(a => !a.Text.StartsWith('/'));
            if (assignment is null) return Work("prints the environment");
            var name = assignment.Text.Split('=')[0].Trim('"', ' ');
            return SensitiveVariables.Contains(name) ? Unknown($"changes {name}, which decides what later commands run") : Work("sets a variable");
        }
        var parsed = call.Parse(["name", "value", "scope", "option", "visibility", "description"]);
        var variable = parsed.Named.GetValueOrDefault("name") ?? parsed.Positional.FirstOrDefault();
        if (variable is null) return Work("prints variables");
        return SensitiveVariables.Contains(VariableName(variable.Text)) || variable.Dynamic
            ? Unknown($"changes {variable.Text}, which can decide what later commands run")
            : Work("sets a variable");
    }

    private static Classification PosixExport(Call call)
    {
        foreach (var a in call.Args.Where(a => !a.Text.StartsWith('-')))
        {
            var name = a.Text.Split('=')[0];
            if (SensitiveVariables.Contains(name)) return Unknown($"changes {name}, which decides what later commands run");
        }
        return Work("sets variables");
    }

    // ---- wrappers ----

    private static string CodeOf(List<CommandWord> rest, ShellKind shell) =>
        rest.Count == 1 ? rest[0].Text : Join(rest, shell);

    private static Classification PowerShellHost(Call call)
    {
        var args = call.Args;
        var cwd = call.Cwd;
        for (var k = 0; k < args.Count; k++)
        {
            var w = args[k];
            var t = w.Text;
            if (t.Length > 1 && (t[0] == '-' || t[0] == '/') && !w.Quoted)
            {
                var name = t.TrimStart('-', '/').ToLowerInvariant();
                var colon = name.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0) name = name[..colon];
                if (name == "ea" || name.StartsWith("encodeda", StringComparison.Ordinal)) return Unknown("passes encoded arguments");
                if (name is "e" or "ec" || (name.Length >= 2 && "encodedcommand".StartsWith(name, StringComparison.Ordinal)))
                {
                    if (k + 1 >= args.Count || args[k + 1].Dynamic) return Unknown("runs an encoded command the classifier cannot read");
                    var decoded = DecodePowerShell(args[k + 1].Text);
                    return decoded is null ? Unknown("runs an encoded command the classifier cannot read") : call.InnerShell(decoded, ShellKind.PowerShell, cwd);
                }
                if (name is "c" || (name.Length >= 2 && "command".StartsWith(name, StringComparison.Ordinal)))
                {
                    var rest = args.Skip(k + 1).ToList();
                    if (rest.Count == 0) return Unknown("runs a command it does not name");
                    if (rest.Count == 1 && rest[0].Text == "-")
                        return call.Part.Stdin is { } input ? call.InnerShell(input, ShellKind.PowerShell, cwd) : Unknown("runs commands from its input");
                    if (rest.Any(r => r.Dynamic)) return Unknown("runs a command that is only known when it runs");
                    return call.InnerShell(string.Join(' ', rest.Select(r => r.Text)), ShellKind.PowerShell, cwd);
                }
                if (name is "f" || (name.Length >= 2 && "file".StartsWith(name, StringComparison.Ordinal)))
                {
                    if (k + 1 >= args.Count) return Unknown("runs a script it does not name");
                    return Script(args.Skip(k + 1).ToList(), call.Part, new RunState(cwd), call.Context, call.Words);
                }
                if (name == "wd" || (name.Length >= 2 && "workingdirectory".StartsWith(name, StringComparison.Ordinal) && name != "w"))
                {
                    cwd = k + 1 < args.Count ? Paths.Full(args[k + 1], cwd, call.Context) : null;
                    k++;
                    continue;
                }
                if (name is "ep" or "ex" or "exec" || name.StartsWith("executionp", StringComparison.Ordinal) || name.StartsWith("execu", StringComparison.Ordinal)
                    || name is "w" or "win" || name.StartsWith("windows", StringComparison.Ordinal) || name.StartsWith("inputf", StringComparison.Ordinal)
                    || name is "if" or "inp" or "of" or "o" || name.StartsWith("outputf", StringComparison.Ordinal) || name is "v" || name.StartsWith("vers", StringComparison.Ordinal)
                    || name.StartsWith("config", StringComparison.Ordinal) || name.StartsWith("psconsole", StringComparison.Ordinal)
                    || name.StartsWith("custompipe", StringComparison.Ordinal) || name.StartsWith("settings", StringComparison.Ordinal))
                {
                    if (colon < 0) k++;
                    continue;
                }
                continue; // -NoProfile, -NonInteractive, -NoLogo, -NoExit, -Mta, -Sta, -Login...
            }
            var remaining = args.Skip(k).ToList();
            if (Paths.Extension(t) == ".ps1") return Script(remaining, call.Part, new RunState(cwd), call.Context, call.Words);
            if (remaining.Any(r => r.Dynamic)) return Unknown("runs a command that is only known when it runs");
            return call.InnerShell(string.Join(' ', remaining.Select(r => r.Text)), ShellKind.PowerShell, cwd);
        }
        return call.Part.Stdin is { } stdin ? call.InnerShell(stdin, ShellKind.PowerShell, cwd) : Unknown("starts an interactive shell");
    }

    /// <summary>The text of a PowerShell -EncodedCommand (Base64 of UTF-16LE), or null.</summary>
    internal static string? DecodePowerShell(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64.Trim());
            if (bytes.Length % 2 != 0 || bytes.Length > CommandParser.MaxLength * 2) return null;
            return Encoding.Unicode.GetString(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Classification CmdHost(Call call)
    {
        var args = call.Args;
        for (var k = 0; k < args.Count; k++)
        {
            var t = args[k].Text;
            if (t.StartsWith('/'))
            {
                var flag = t.ToLowerInvariant();
                if (flag is "/c" or "/k" or "/r")
                {
                    var rest = args.Skip(k + 1).ToList();
                    if (rest.Count == 0) return Unknown("runs a command it does not name");
                    return call.InnerShell(CodeOf(rest, ShellKind.Cmd), ShellKind.Cmd);
                }
                if (flag.StartsWith("/c", StringComparison.Ordinal) && flag.Length > 2)
                    return call.InnerShell(t[2..] + (k + 1 < args.Count ? " " + Join(args.Skip(k + 1), ShellKind.Cmd) : ""), ShellKind.Cmd);
                continue;
            }
            return Unknown("a cmd call the classifier cannot read");
        }
        return call.Part.Stdin is { } stdin ? call.InnerShell(stdin, ShellKind.Cmd) : Unknown("starts an interactive shell");
    }

    private static Classification PosixHost(Call call)
    {
        var args = call.Args;
        for (var k = 0; k < args.Count; k++)
        {
            var t = args[k].Text;
            if (t == "--") { k++; return k < args.Count ? Script(args.Skip(k).ToList(), call.Part, call.State, call.Context, call.Words) : Unknown("runs nothing"); }
            if (t.StartsWith("--", StringComparison.Ordinal))
            {
                if (t is "--rcfile" or "--init-file") return Unknown("loads a startup file");
                continue;
            }
            if (t.StartsWith('-') && t.Length > 1)
            {
                if (t.Contains('c', StringComparison.Ordinal))
                {
                    if (k + 1 >= args.Count) return Unknown("runs a command it does not name");
                    if (args[k + 1].Dynamic) return Unknown("runs a command that is only known when it runs");
                    return call.InnerShell(args[k + 1].Text, ShellKind.Posix);
                }
                if (t.Contains('o', StringComparison.Ordinal) && k + 1 < args.Count) k++;
                continue;
            }
            return Script(args.Skip(k).ToList(), call.Part, call.State, call.Context, call.Words);
        }
        return call.Part.Stdin is { } stdin ? call.InnerShell(stdin, ShellKind.Posix) : Unknown("starts an interactive shell");
    }

    private static Classification Wsl(Call call)
    {
        var args = call.Args;
        var posixPart = call.Part with { Shell = ShellKind.Posix };
        for (var k = 0; k < args.Count; k++)
        {
            var t = args[k].Text.ToLowerInvariant();
            switch (t)
            {
                case "-e" or "--exec":
                    return ClassifyWords(args.Skip(k + 1).ToList(), posixPart, call.State, call.Context, call.Depth + 1);
                case "--":
                    return call.InnerShell(Join(args.Skip(k + 1), ShellKind.Posix), ShellKind.Posix);
                case "-u" or "--user":
                    if (k + 1 < args.Count && args[k + 1].Text == "root") return Leaf(ActionClass.HardDeny, "runs as root");
                    k++;
                    continue;
                case "-d" or "--distribution" or "--cd":
                    k++;
                    continue;
                case "--install":
                    return Leaf(ActionClass.Install, "installs a Linux distribution");
                case "--unregister" or "--shutdown" or "--terminate" or "-t":
                    return Leaf(ActionClass.Destructive, "removes or stops a Linux distribution");
                case "-l" or "--list" or "--status" or "--version" or "-v" or "--help":
                    return Work("reads WSL");
            }
            if (t.StartsWith('-')) return Unknown($"wsl {t} is not an option the classifier knows");
            return call.InnerShell(Join(args.Skip(k), ShellKind.Posix), ShellKind.Posix);
        }
        return Unknown("starts an interactive shell");
    }

    private static Classification EnvWrapper(Call call)
    {
        var args = call.Args;
        var cwd = call.Cwd;
        for (var k = 0; k < args.Count; k++)
        {
            var t = args[k].Text;
            if (t is "-i" or "--ignore-environment" or "-0" or "--null" or "-v" or "--debug") continue;
            if (t is "-u" or "--unset") { k++; continue; }
            if (t is "-C" or "--chdir")
            {
                cwd = k + 1 < args.Count ? Paths.Full(args[k + 1], cwd, call.Context) : null;
                if (cwd is null) return Unknown("runs in a folder the classifier cannot place");
                k++;
                continue;
            }
            if (t is "-S" or "--split-string" || t.StartsWith("--split-string=", StringComparison.Ordinal)) return Unknown("splits a command string");
            if (IsAssignmentWord(t, out var name))
            {
                if (SensitiveVariables.Contains(name)) return Unknown($"changes {name}, which decides what later commands run");
                continue;
            }
            return call.Inner(args.Skip(k).ToList(), cwd);
        }
        return Work("prints the environment");
    }

    private static Classification Timeout(Call call)
    {
        if (call.Args.Count == 0 || call.Args[0].Text.StartsWith('/')) return Work("waits");
        var args = SkipOptions(call.Args, ["-s", "--signal", "-k", "--kill-after"]);
        return args.Count <= 1 ? Work("waits") : call.Inner(args.Skip(1).ToList());
    }

    private static Classification CommandBuiltin(Call call)
    {
        if (call.Has("-v", "-V")) return Work("describes a command");
        return call.Inner(call.Args.SkipWhile(a => a.Text == "-p").ToList());
    }

    private static Classification Xargs(Call call)
    {
        string[] valued = ["-n", "-L", "-P", "-I", "-d", "-E", "-s", "-a", "--max-args", "--max-lines", "--max-procs", "--delimiter", "--arg-file", "--eof"];
        var args = call.Args;
        var k = 0;
        var leaves = new List<Classification>();
        while (k < args.Count && args[k].Text.StartsWith('-'))
        {
            if (args[k].Text is "-a" or "--arg-file" && k + 1 < args.Count) leaves.Add(call.Read(args[k + 1]));
            k += valued.Contains(args[k].Text, StringComparer.Ordinal) ? 2 : 1;
        }
        var rest = args.Skip(k).ToList();
        // The words xargs adds come from its input: only known when it runs.
        if (rest.Count == 0) rest = [new CommandWord("echo")];
        rest.Add(new CommandWord("xargs-input", Dynamic: true));
        leaves.Add(call.Inner(rest));
        return Classification.Of(leaves);
    }

    private static Classification StartProgram(Call call)
    {
        var powershellStyle = call.Name != "start" || call.Shell == ShellKind.PowerShell || call.Args.Any(a => a.Text.StartsWith('-'));
        List<CommandWord> command;
        string? cwd = call.Cwd;
        if (powershellStyle)
        {
            var parsed = call.Parse(["filepath", "argumentlist", "args", "verb", "workingdirectory", "credential", "windowstyle",
                "redirectstandardoutput", "redirectstandarderror", "redirectstandardinput"]);
            if (parsed.Splatted) return Unknown("its parameters are hidden in a splat");
            if (parsed.Named.GetValueOrDefault("verb") is { } verb && (verb.Dynamic || verb.Text.Equals("RunAs", StringComparison.OrdinalIgnoreCase) || verb.Text.Equals("RunAsUser", StringComparison.OrdinalIgnoreCase)))
                return Leaf(ActionClass.HardDeny, "asks for administrator rights");
            if (parsed.Named.ContainsKey("credential")) return Leaf(ActionClass.HardDeny, "runs as another user");
            var file = parsed.Named.GetValueOrDefault("filepath") ?? parsed.Positional.FirstOrDefault();
            var list = parsed.Named.GetValueOrDefault("argumentlist") ?? parsed.Named.GetValueOrDefault("args")
                       ?? (parsed.Named.ContainsKey("filepath") ? parsed.Positional.FirstOrDefault() : parsed.Positional.Skip(1).FirstOrDefault());
            if (parsed.Named.GetValueOrDefault("workingdirectory") is { } wd) cwd = Paths.Full(wd, cwd, call.Context);
            if (file is null) return Unknown("starts a program it does not name");
            command = [file];
            if (list is not null)
            {
                if (list.Dynamic) return Unknown("starts a program with arguments only known when it runs");
                var parsedList = CommandParser.Parse(list.Text.Replace(',', ' '), ShellKind.PowerShell);
                if (parsedList.HasProblems || parsedList.Parts.Count > 1) return Unknown("starts a program with arguments the classifier cannot read");
                command.AddRange(parsedList.Parts.SelectMany(p => p.Words));
            }
            var leaves = new List<Classification>();
            foreach (var name in new[] { "redirectstandardoutput", "redirectstandarderror" })
                if (parsed.Named.GetValueOrDefault(name) is { } output) leaves.Add(Paths.Write(output, cwd, call.Context));
            leaves.Add(StartTarget(call, command, cwd));
            return Classification.Of(leaves);
        }
        // cmd: start ["title"] [/d dir] [/b /min /wait ...] command args
        var args = call.Args.ToList();
        var k = 0;
        if (k < args.Count && args[k].Quoted && args.Count > 1) k++;
        while (k < args.Count && args[k].Text.StartsWith('/'))
        {
            if (args[k].Text.Equals("/d", StringComparison.OrdinalIgnoreCase) && k + 1 < args.Count) { cwd = Paths.Full(args[k + 1], cwd, call.Context); k++; }
            else if (args[k].Text.Equals("/node", StringComparison.OrdinalIgnoreCase) || args[k].Text.Equals("/affinity", StringComparison.OrdinalIgnoreCase)) k++;
            k++;
        }
        command = args.Skip(k).ToList();
        if (command.Count == 0) return Unknown("opens a new window");
        return StartTarget(call, command, cwd);
    }

    private static Classification StartTarget(Call call, List<CommandWord> command, string? cwd)
    {
        var target = command[0];
        if (!target.Dynamic && GrantTarget.OriginOf(target.Text) is not null)
            return Fetch(target.Text) with { Reason = "opens a page in the browser" };
        return call.Inner(command, cwd);
    }

    private static Classification InvokeExpression(Call call)
    {
        var parsed = call.Parse(["command"]);
        List<CommandWord> code = parsed.Named.GetValueOrDefault("command") is { } c ? [c] : parsed.Positional;
        if (code.Count == 0 || code.Any(w => w.Dynamic)) return Unknown("runs code that is only known when it runs");
        return call.InnerShell(string.Join(' ', code.Select(w => w.Text)), ShellKind.PowerShell);
    }

    private static Classification Eval(Call call)
    {
        if (call.Args.Count == 0) return Work("runs nothing");
        if (call.Args.Any(w => w.Dynamic)) return Unknown("runs code that is only known when it runs");
        return call.InnerShell(string.Join(' ', call.Args.Select(w => w.Text)), ShellKind.Posix);
    }

    private static Classification Interpreter(Call call)
    {
        var args = call.Args;
        var python = call.Name is "python" or "python3" or "py" or "pythonw";
        var node = call.Name is "node" or "nodejs" or "tsx" or "ts-node";
        var k = 0;
        if (call.Name is "deno" or "bun" && k < args.Count)
        {
            var sub = args[k].Text;
            if (sub is "eval" or "x" or "repl" or "install" or "add" or "i" or "upgrade" or "compile") return Unknown($"{call.Name} {sub} runs code or packages");
            if (sub is "run" or "test" or "task") k++;
        }
        for (; k < args.Count; k++)
        {
            var t = args[k].Text;
            if (t.StartsWith('-'))
            {
                if (python && t == "-c") return Unknown("runs inline code");
                if (python && t == "-m")
                {
                    var module = k + 1 < args.Count ? args[k + 1].Text : "";
                    if (module == "pip")
                    {
                        var pipWords = new List<CommandWord> { new("pip") };
                        pipWords.AddRange(args.Skip(k + 2));
                        return Pip(new Call("pip", pipWords, call.Part, call.State, call.Context, call.Depth));
                    }
                    return Unknown($"runs the {module} module");
                }
                if (python && t is "-X" or "-W" or "--check-hash-based-pycs") { k++; continue; }
                if (node && t is "-e" or "--eval" or "-p" or "--print" or "-r" or "--require" or "--import" or "--loader" or "--experimental-loader")
                    return Unknown("runs inline or preloaded code");
                if (call.Name is "ruby" or "perl" or "php" or "lua" && t is "-e" or "-r" or "-E" or "-n" or "-p" or "-i")
                    return Unknown("runs inline code");
                if (call.Name is "cscript" or "wscript" && t.StartsWith("//", StringComparison.Ordinal)) continue;
                continue;
            }
            if (call.Name is "cscript" or "wscript" && t.StartsWith("//", StringComparison.Ordinal)) continue;
            return Script(args.Skip(k).ToList(), call.Part, call.State, call.Context, call.Words);
        }
        return call.Part.Stdin is not null ? Unknown("runs code from its input") : Unknown("starts an interactive interpreter");
    }
}
