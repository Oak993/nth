// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a, 12.1b; unit AS-1; ED-NEW-dextracore-2). No Swift twin.
// Maps every provider approval payload and tool call to an action class. Pure: no I/O, no model, no clock. The rule
// that holds everything up: anything it does not recognise is Unknown, and Unknown asks. A command of several parts
// is classified part by part (the engine keeps the strictest verdict), a shell wrapper is opened and its inner
// command classified, and a command read with Auto is read with all three grammars and every reading is kept.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>The classifier of 12.1a.</summary>
public static partial class ActionClassifier
{
    /// <summary>The class of one action.</summary>
    public static Classification Classify(ActionRequest request, ClassifierContext context)
    {
        var cwd = context.WorkingDirectory ?? context.MissionWorktree;
        return request switch
        {
            ActionRequest.ShellCommand shell => ClassifyShell(shell.Command, shell.Shell, shell.WorkingDirectory ?? cwd, context, 0),
            ActionRequest.McpTool tool => ClassifyMcp(tool),
            ActionRequest.FileRead read => Paths.Read(new CommandWord(read.Path), cwd, context),
            ActionRequest.FileWrite write => Paths.Write(new CommandWord(write.Path), cwd, context),
            ActionRequest.WebFetch fetch => Fetch(fetch.Url),
            ActionRequest.WebSearch search => new Classification(ActionClass.ReadWeb, "searches the web")
            {
                Target = search.Query, CarriesData = true, ReadsUntrusted = true, Field = "read",
            },
            ActionRequest.AssistantTool tool => ClassifyAssistantTool(tool.Name),
            _ => Unknown("an action the classifier does not know"),
        };
    }

    /// <summary>
    /// The class of a provider approval (4.1). The approval carries less than the tool call: a command's text is its
    /// title, a file change names its file relative to the chat's folder, a tool its name. Prefer
    /// <see cref="Classify"/> with the call's own input when it is at hand.
    /// </summary>
    public static Classification ClassifyApproval(ApprovalRequest approval, ClassifierContext context, ShellKind shell = ShellKind.Auto)
    {
        switch (approval.RequestKind)
        {
            case ApprovalRequest.Kind.Command:
                return Classify(new ActionRequest.ShellCommand(approval.Title, shell), context);
            case ApprovalRequest.Kind.FileChange:
                return approval.Title is "Write file" or "Edit file" or "Edit notebook" || string.IsNullOrWhiteSpace(approval.Title)
                    ? Unknown("a file change that names no file")
                    : Classify(new ActionRequest.FileWrite(approval.Title), context);
            case ApprovalRequest.Kind.Tool:
            {
                var title = approval.Title;
                if (title.StartsWith("mcp__", StringComparison.Ordinal))
                {
                    var rest = title[5..];
                    var split = rest.IndexOf("__", StringComparison.Ordinal);
                    if (split > 0) return ClassifyMcp(new ActionRequest.McpTool(rest[..split], rest[(split + 2)..], approval.Detail));
                }
                return title switch
                {
                    "WebFetch" => Fetch(approval.Detail ?? ""),
                    "WebSearch" => Classify(new ActionRequest.WebSearch(approval.Detail ?? ""), context),
                    _ => Unknown("a tool the classifier does not know"),
                };
            }
            default:
                // Permissions ("Grant extra access?") and Plan approvals are not actions the table can place.
                return Unknown("a request for more access");
        }
    }

    internal static Classification Unknown(string reason) => new(ActionClass.Unknown, reason);

    // ---- shells ----

    internal static Classification ClassifyShell(string command, ShellKind shell, string? cwd, ClassifierContext context, int depth)
    {
        ShellKind[] grammars = shell == ShellKind.Auto ? [ShellKind.PowerShell, ShellKind.Cmd, ShellKind.Posix] : [shell];
        var leaves = new List<Classification>();
        foreach (var grammar in grammars)
        {
            var parsed = CommandParser.Parse(command, grammar, depth, nested: depth > 0);
            leaves.AddRange(ClassifyParsed(parsed, cwd, context, depth));
        }
        return Classification.Of(leaves);
    }

    private static List<Classification> ClassifyParsed(ParsedCommand parsed, string? cwd, ClassifierContext context, int depth)
    {
        var leaves = new List<Classification>();
        if (parsed.HasProblems) leaves.Add(Unknown("could not be read: " + string.Join(", ", parsed.Problems.Distinct())));
        var state = new RunState(cwd);
        foreach (var part in parsed.Parts) leaves.Add(ClassifyPart(part, state, context, depth));
        if (leaves.Count == 0) leaves.Add(new Classification(ActionClass.Work, "runs nothing"));
        return leaves;
    }

    /// <summary>The working folder as the parts of one command line change it (cd); null once it is not known.</summary>
    internal sealed class RunState(string? cwd)
    {
        public string? Cwd { get; set; } = cwd;
    }

    private static Classification ClassifyPart(CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        var leaves = new List<Classification>();
        foreach (var redirect in part.Redirects)
        {
            var target = new CommandWord(redirect.Target, redirect.Dynamic);
            leaves.Add(redirect.Input ? Paths.Read(target, state.Cwd, context) : Paths.Write(target, state.Cwd, context));
        }
        // A cd inside a substitution, block or subshell changes the folder in some shells and not in others.
        var before = state.Cwd;
        leaves.Add(ClassifyWords(part.Words.ToList(), part, state, context, depth));
        if (part.Nested && !string.Equals(before, state.Cwd, StringComparison.OrdinalIgnoreCase)) state.Cwd = null;
        return Classification.Of(leaves);
    }

    private static readonly HashSet<string> PosixKeywords = new(StringComparer.Ordinal)
        { "if", "then", "else", "elif", "fi", "do", "done", "while", "until", "!", "{", "}", "time", "coproc" };

    private static readonly HashSet<string> PosixHeaders = new(StringComparer.Ordinal) { "for", "select", "case", "esac", "in", ";;" };

    private static readonly HashSet<string> PowerShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "elseif", "else", "foreach", "for", "while", "do", "until", "switch", "try", "catch", "finally", "return",
        "exit", "break", "continue", "throw", "trap", "param", "begin", "process", "end", "data",
    };

    private static readonly HashSet<string> SensitiveVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "PATHEXT", "PSModulePath", "BASH_ENV", "ENV", "LD_PRELOAD", "LD_LIBRARY_PATH", "GIT_SSH", "GIT_SSH_COMMAND",
        "GIT_EXEC_PATH", "GIT_CONFIG", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_DIR", "GIT_WORK_TREE", "PROMPT_COMMAND",
        "NODE_OPTIONS", "PYTHONSTARTUP", "PYTHONPATH", "COMSPEC", "PSDefaultParameterValues", "ExecutionContext",
    };

    private static bool IsAssignmentWord(string text, out string name)
    {
        name = "";
        var eq = text.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0) return false;
        var head = text[..eq].TrimEnd('+');
        if (head.Length == 0 || !(char.IsLetter(head[0]) || head[0] == '_')) return false;
        if (!head.All(ch => char.IsLetterOrDigit(ch) || ch == '_')) return false;
        name = head;
        return true;
    }

    private static string VariableName(string text)
    {
        // "$env:PATH" -> "PATH"; "${env:Path}" -> "Path"; "$x" -> "x".
        var t = text.TrimStart('$').Trim('{', '}');
        var colon = t.LastIndexOf(':');
        return colon >= 0 ? t[(colon + 1)..] : t;
    }

    /// <summary>Classifies one simple command given as words (after any wrapper has been taken off).</summary>
    internal static Classification ClassifyWords(List<CommandWord> words, CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        if (depth > CommandParser.MaxDepth) return Unknown("nested too deep");
        var shell = part.Shell;

        // Control-flow words: their commands were split into parts of their own.
        if (words.Count > 0 && !words[0].Quoted)
        {
            if (shell == ShellKind.Posix)
            {
                if (PosixHeaders.Contains(words[0].Text)) return new Classification(ActionClass.Work, "a loop or case header");
                while (words.Count > 0 && !words[0].Quoted && PosixKeywords.Contains(words[0].Text)) words.RemoveAt(0);
                if (words.Count > 0 && (words[0].Text == "function" || (words.Count > 1 && words[1].Text.StartsWith("()", StringComparison.Ordinal))))
                    return Unknown("defines a function, which can stand in for a command");
            }
            if (shell == ShellKind.PowerShell && words.Count > 0 && !words[0].Quoted)
            {
                if (words[0].Text.Equals("function", StringComparison.OrdinalIgnoreCase) || words[0].Text.Equals("filter", StringComparison.OrdinalIgnoreCase))
                    return Unknown("defines a function, which can stand in for a command");
                if (PowerShellKeywords.Contains(words[0].Text)) return new Classification(ActionClass.Work, "control flow");
            }
            if (shell == ShellKind.Cmd && words.Count > 0) return CmdControl(words, part, state, context, depth);
        }
        if (words.Count == 0) return new Classification(ActionClass.Work, "runs nothing");

        // NAME=value prefixes (POSIX and cmd's set are handled as commands).
        while (words.Count > 0 && shell == ShellKind.Posix && !words[0].Quoted && IsAssignmentWord(words[0].Text, out var assigned))
        {
            if (SensitiveVariables.Contains(assigned)) return Unknown($"changes {assigned}, which decides what later commands run");
            words.RemoveAt(0);
        }
        if (words.Count == 0) return new Classification(ActionClass.Work, "sets a variable");

        var first = words[0];

        // PowerShell assignments and expressions.
        if (shell == ShellKind.PowerShell && first.Text.StartsWith('$') && !first.Quoted)
            return PowerShellExpression(words, part, state, context, depth);

        // The call operator and dot-sourcing.
        if (!first.Quoted && (first.Text == "&" || (first.Text == "." && words.Count > 1 && shell is ShellKind.PowerShell or ShellKind.Posix)))
        {
            if (words.Count < 2) return Unknown("a call with nothing to call");
            var target = words[1];
            if (target.Dynamic)
                return target.Text.StartsWith('{') ? new Classification(ActionClass.Work, "runs a script block")
                                                   : Unknown("calls a command that is only known when it runs");
            if (first.Text == ".") return Script(words.Skip(1).ToList(), part, state, context);
            words = words.Skip(1).ToList();
            first = words[0];
        }

        if (first.Dynamic)
        {
            var text = first.Text;
            var expression = text.Length > 1 && (text[0] is '(' or '{' || text.StartsWith("$(", StringComparison.Ordinal) || text.StartsWith("@(", StringComparison.Ordinal))
                             && text[^1] is ')' or '}';
            if (words.Count == 1 && expression) return new Classification(ActionClass.Work, "evaluates an expression");
            return Unknown("the command is only known when it runs");
        }
        if (first.Text.StartsWith('[') && shell != ShellKind.Posix) return Unknown("calls .NET directly");
        if (first.Text.Length == 0) return Unknown("an empty command name");

        var raw = first.Text;
        var extension = Paths.Extension(raw);
        if (Paths.IsScriptExtension(extension)) return Script(words, part, state, context);
        string name;
        if (Paths.HasSeparator(raw))
        {
            var full = Paths.Full(first, state.Cwd, context);
            if (full is null || Paths.IsAgentWritable(full, context)) return Unknown("runs a program from a folder agents can write");
            name = Paths.CommandName(raw);
        }
        else
        {
            name = Paths.CommandName(raw);
            if (shell == ShellKind.Cmd && Shadowed(name, state.Cwd, context))
                return Unknown($"cmd would run a file named {name} from the working folder first");
        }
        return Command(name, words, part, state, context, depth);
    }

    private static bool Shadowed(string name, string? cwd, ClassifierContext context)
    {
        if (cwd is null) return true;
        if (context.CommandShadowed is { } probe) return probe(cwd, name);
        return Paths.IsAgentWritable(cwd, context);
    }

    private static Classification PowerShellExpression(List<CommandWord> words, CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        var first = words[0].Text;
        string[] operators = ["=", "+=", "-=", "*=", "/=", "%=", "??="];
        if (words.Count >= 2 && !words[1].Quoted && operators.Contains(words[1].Text))
        {
            if (SensitiveVariables.Contains(VariableName(first))) return Unknown($"changes {VariableName(first)}, which decides what later commands run");
            return AssignedValue(words.Skip(2).ToList(), part, state, context, depth);
        }
        var eq = first.IndexOf('=', StringComparison.Ordinal);
        if (eq > 1 && System.Text.RegularExpressions.Regex.IsMatch(first[..eq], @"\A\$\{?[A-Za-z_][\w:]*\}?[+\-*/%?]{0,2}\z"))
        {
            var variable = first[..eq].TrimEnd('+', '-', '*', '/', '%', '?');
            if (SensitiveVariables.Contains(VariableName(variable))) return Unknown($"changes {VariableName(variable)}, which decides what later commands run");
            var rest = new List<CommandWord>();
            if (first.Length > eq + 1) rest.Add(new CommandWord(first[(eq + 1)..], words[0].Dynamic));
            rest.AddRange(words.Skip(1));
            return AssignedValue(rest, part, state, context, depth);
        }
        if (words.Count == 1 && System.Text.RegularExpressions.Regex.IsMatch(first, @"\A\$\{?[A-Za-z_?][\w:]*\}?\z"))
            return new Classification(ActionClass.Work, "prints a variable");
        return Unknown("runs a PowerShell expression");
    }

    private static Classification AssignedValue(List<CommandWord> rest, CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        if (rest.Count == 0) return new Classification(ActionClass.Work, "assigns a value");
        var head = rest[0];
        if (head.Quoted || head.Dynamic || head.Text.Length == 0 || !char.IsLetter(head.Text[0]))
            return rest.Count == 1 ? new Classification(ActionClass.Work, "assigns a value") : Unknown("assigns the result of an expression");
        // $x = Get-Content f runs Get-Content.
        return ClassifyWords(rest, part, state, context, depth);
    }

    private static Classification CmdControl(List<CommandWord> words, CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        var head = words[0].Text;
        if (head.Equals("rem", StringComparison.OrdinalIgnoreCase) || head.StartsWith("::", StringComparison.Ordinal))
            return new Classification(ActionClass.Work, "a comment");
        if (head.Equals("if", StringComparison.OrdinalIgnoreCase))
        {
            var k = 1;
            if (k < words.Count && words[k].Text.Equals("/i", StringComparison.OrdinalIgnoreCase)) k++;
            if (k < words.Count && words[k].Text.Equals("not", StringComparison.OrdinalIgnoreCase)) k++;
            if (k < words.Count && (words[k].Text.Equals("exist", StringComparison.OrdinalIgnoreCase)
                                    || words[k].Text.Equals("defined", StringComparison.OrdinalIgnoreCase)
                                    || words[k].Text.Equals("errorlevel", StringComparison.OrdinalIgnoreCase)
                                    || words[k].Text.Equals("cmdextversion", StringComparison.OrdinalIgnoreCase))) k += 2;
            else if (k < words.Count && words[k].Text.Contains("==", StringComparison.Ordinal)) k += 1;
            else if (k + 2 < words.Count) k += 3; // a op b
            else return Unknown("an if the classifier cannot read");
            var rest = words.Skip(k).ToList();
            var elseAt = rest.FindIndex(w => w.Text.Equals("else", StringComparison.OrdinalIgnoreCase));
            var leaves = new List<Classification>();
            if (elseAt >= 0)
            {
                leaves.Add(Sub(rest.Take(elseAt).ToList()));
                leaves.Add(Sub(rest.Skip(elseAt + 1).ToList()));
            }
            else leaves.Add(Sub(rest));
            return Classification.Of(leaves);
        }
        if (head.Equals("for", StringComparison.OrdinalIgnoreCase))
        {
            var doAt = words.FindIndex(w => w.Text.Equals("do", StringComparison.OrdinalIgnoreCase));
            if (doAt < 0) return Unknown("a for loop the classifier cannot read");
            // The loop variable is only known when it runs.
            return Sub(words.Skip(doAt + 1).Select(w => w.Text.Contains('%', StringComparison.Ordinal) ? w with { Dynamic = true } : w).ToList());
        }
        if (head.Equals("else", StringComparison.OrdinalIgnoreCase)) return Sub(words.Skip(1).ToList());
        return ClassifyWordsAfterControl(words, part, state, context, depth);

        Classification Sub(List<CommandWord> sub) => sub.Count == 0
            ? new Classification(ActionClass.Work, "runs nothing")
            : ClassifyWordsAfterControl(sub, part, state, context, depth);
    }

    private static Classification ClassifyWordsAfterControl(List<CommandWord> words, CommandPart part, RunState state, ClassifierContext context, int depth)
    {
        var first = words[0];
        if (first.Dynamic) return Unknown("the command is only known when it runs");
        var raw = first.Text;
        if (Paths.IsScriptExtension(Paths.Extension(raw))) return Script(words, part, state, context);
        string name;
        if (Paths.HasSeparator(raw))
        {
            var full = Paths.Full(first, state.Cwd, context);
            if (full is null || Paths.IsAgentWritable(full, context)) return Unknown("runs a program from a folder agents can write");
            name = Paths.CommandName(raw);
        }
        else
        {
            name = Paths.CommandName(raw);
            if (Shadowed(name, state.Cwd, context)) return Unknown($"cmd would run a file named {name} from the working folder first");
        }
        return Command(name, words, part, state, context, depth);
    }

    // ---- scripts and the allowlist ----

    /// <summary>
    /// A script file run directly or through its interpreter (12.1a): Unknown unless it is inside the mission
    /// worktree, unchanged since the mission started, and on the allowlist; then Run.
    /// </summary>
    internal static Classification Script(List<CommandWord> words, CommandPart part, RunState state, ClassifierContext context, List<CommandWord>? fullCommand = null)
    {
        var script = words[0];
        if (script.Dynamic) return Unknown("runs a script that is only known when it runs");
        var full = Paths.Full(script, state.Cwd, context);
        var command = fullCommand ?? words;
        if (full is null) return Unknown($"runs a script the classifier cannot place: {script.Text}");
        if (context.MissionWorktree is null || !PathRules.IsInside(full, context.MissionWorktree, strict: true))
            return Unknown($"runs a script outside the mission worktree: {script.Text}") with { Target = full };
        if (!context.UnchangedScripts.Any(s => string.Equals(PathRules.Normalize(s), full, StringComparison.OrdinalIgnoreCase)))
            return Unknown($"runs a script that changed since the mission started, or is new: {script.Text}") with { Target = full };
        if (!IsAllowlisted(command, state.Cwd, context))
            return Unknown($"runs a script that is not on the allowlist: {script.Text}") with { Target = full };
        return new Classification(ActionClass.Run, $"runs an allowlisted script: {script.Text}") { Target = full };
    }

    /// <summary>
    /// Whether a command's words start with one allowlist entry's words. Command names compare without case or
    /// extension; paths compare as full paths; a word only known when it runs never matches.
    /// </summary>
    internal static bool IsAllowlisted(List<CommandWord> words, string? cwd, ClassifierContext context)
    {
        if (words.Count == 0 || words.Any(w => w.Dynamic)) return false;
        foreach (var entry in context.Allowlist)
        {
            var parsed = CommandParser.Parse(entry, ShellKind.PowerShell);
            if (parsed.HasProblems || parsed.Parts.Count != 1) continue;
            var want = parsed.Parts[0].Words;
            if (want.Count == 0 || want.Count > words.Count) continue;
            var matches = true;
            for (var k = 0; k < want.Count && matches; k++)
                matches = SameWord(want[k].Text, words[k].Text, k == 0, cwd, context);
            if (matches) return true;
        }
        return false;
    }

    private static bool SameWord(string want, string have, bool commandName, string? cwd, ClassifierContext context)
    {
        if (commandName && !Paths.HasSeparator(want) && !Paths.HasSeparator(have))
            return string.Equals(Paths.CommandName(want), Paths.CommandName(have), StringComparison.OrdinalIgnoreCase);
        if (Paths.HasSeparator(want) || Paths.HasSeparator(have))
        {
            var root = context.MissionWorktree ?? cwd;
            var a = Paths.Full(new CommandWord(want), root, context);
            var b = Paths.Full(new CommandWord(have), cwd, context);
            return a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(want, have, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Joins words back into one command line, quoting any word that holds a space.</summary>
    internal static string Join(IEnumerable<CommandWord> words, ShellKind shell)
    {
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (sb.Length > 0) sb.Append(' ');
            var needsQuotes = w.Text.Length == 0 || w.Text.Any(char.IsWhiteSpace);
            if (!needsQuotes) { sb.Append(w.Text); continue; }
            sb.Append(shell == ShellKind.PowerShell ? '\'' : '"');
            sb.Append(shell == ShellKind.PowerShell ? w.Text.Replace("'", "''", StringComparison.Ordinal) : w.Text.Replace("\"", "\\\"", StringComparison.Ordinal));
            sb.Append(shell == ShellKind.PowerShell ? '\'' : '"');
        }
        return sb.ToString();
    }

    // ---- web ----

    internal static Classification Fetch(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return new Classification(ActionClass.ReadLocal, "reads a local file through a URL") { Target = trimmed, OutsideAccess = true, ReadsUntrusted = true };
        var origin = GrantTarget.OriginOf(trimmed);
        if (origin is null) return Unknown($"fetches something that is not a web address: {trimmed}");
        return new Classification(ActionClass.ReadWeb, $"reads {origin}")
        {
            Target = trimmed,
            GrantTarget = GrantTarget.Site(origin),
            CarriesData = trimmed.Contains('?', StringComparison.Ordinal) || trimmed.Contains('@', StringComparison.Ordinal),
            ReadsUntrusted = true,
            Field = "read",
        };
    }
}
