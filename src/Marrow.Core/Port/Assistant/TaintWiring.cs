// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4 "Taint" and "Taint follows text"; unit AS-7; ED-NEW-dextra-as7-1). No Swift
// twin. The wiring of Taint.cs to what agents really do: every tool call a Dextra chat makes (the provider's own tools,
// shown as ToolStarted) is placed by the classifier, and a call that reads untrusted content taints the agent that made
// it. It fails closed: a tool nth cannot place taints. Pure: the controller feeds it the calls and keeps the taint.
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public static class TaintWiring
{
    /// <summary>Files whose text is untrusted wherever they are (12.4: PDFs, email).</summary>
    private static readonly Dictionary<string, ContentOrigin> UntrustedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ContentOrigin.Pdf,
        [".eml"] = ContentOrigin.Email,
        [".msg"] = ContentOrigin.Email,
        [".mbox"] = ContentOrigin.Email,
        [".emlx"] = ContentOrigin.Email,
    };

    /// <summary>
    /// Provider tools that bring no outside text into the context: the plan and to-do tools, a background shell's own
    /// output (its command was placed when it started), stopping one, and a skill the owner installed. Any other tool of
    /// a kind nth does not know taints (fail closed).
    /// </summary>
    private static readonly HashSet<string> CleanOthers = new(StringComparer.OrdinalIgnoreCase)
    {
        "TodoWrite", "ExitPlanMode", "EnterPlanMode", "BashOutput", "KillShell", "KillBash", "TaskOutput", "TaskStop", "Monitor", "Review",
    };

    /// <summary>
    /// The taint one tool call brings into the agent that ran it, read in the agent's own folder. Clean for a read inside
    /// the access folders, an edit, a project command and nth-dextra's own tools (their results carry their own taint).
    /// </summary>
    /// <param name="provider">The provider that ran it: only Claude shows a sub-agent's own tool calls to nth.</param>
    public static Taint Of(ToolCall call, ClassifierContext context, ProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(context);
        var title = call.Title ?? "";
        switch (call.ToolCallKind)
        {
            case ToolCall.Kind.Edit:
                return Taint.Clean;
            case ToolCall.Kind.Read:
                return ReadOf(title, context);
            case ToolCall.Kind.Search:
            {
                // Claude's Glob and Grep: the pattern, then the folder; LS: the folder; Codex: the command it ran.
                if (provider == ProviderKind.Claude)
                {
                    var folder = call.Detail ?? (LooksLikePath(title) ? title : null);
                    return folder is null ? Taint.Clean : ReadOf(folder, context);
                }
                return CommandOf(title, context);
            }
            case ToolCall.Kind.Web:
                return Taint.From(ContentOrigin.Web, Clip(title.Length > 0 ? title : "the web"));
            case ToolCall.Kind.Mcp:
            {
                var (server, tool) = SplitMcp(title);
                if (string.Equals(server, AssistantNames.Server, StringComparison.OrdinalIgnoreCase)) return Taint.Clean;
                return Taint.From(ContentOrigin.Connector, Clip(server.Length > 0 ? server + " " + tool : title));
            }
            case ToolCall.Kind.Command:
                return CommandOf(title, context);
            case ToolCall.Kind.Agent:
                // Claude streams a sub-agent's own tool calls (AgentEvent), which taint the chat as they happen; another
                // provider's sub-agent works where nth cannot see, so what it hands back is untrusted.
                return provider == ProviderKind.Claude ? Taint.Clean : Taint.From(ContentOrigin.ToolOutput, Clip("a sub-agent: " + title));
            default:
                var name = title.Split(' ', 2)[0];
                if (CleanOthers.Contains(name) || title.StartsWith("Skill ", StringComparison.Ordinal)) return Taint.Clean;
                return Taint.From(ContentOrigin.ToolOutput, Clip(title.Length > 0 ? title : "a tool"));
        }
    }

    /// <summary>
    /// Whether a tainted agent's call can leave text in the mission's files, which every agent of the mission then reads
    /// and runs (ED-NEW-dextra-as7-1): an edit, a command, a connector call, a sub-agent or a tool nth does not know.
    /// </summary>
    public static bool MayWriteFiles(ToolCall call) =>
        call.ToolCallKind is not (ToolCall.Kind.Read or ToolCall.Kind.Search or ToolCall.Kind.Web);

    /// <summary>The taint of a file read: untrusted outside the access folders, and for a PDF or a mail file anywhere.</summary>
    public static Taint ReadOf(string path, ClassifierContext context)
    {
        var trimmed = path.Trim().Trim('"');
        var extension = Extension(trimmed);
        if (extension is not null && UntrustedExtensions.TryGetValue(extension, out var origin)) return Taint.From(origin, Clip(trimmed));
        var read = ActionClassifier.Classify(new ActionRequest.FileRead(trimmed), context);
        return Untrusted(read, trimmed);
    }

    /// <summary>The taint of a command's output: what it reads that is untrusted, or anything nth cannot place.</summary>
    public static Taint CommandOf(string command, ClassifierContext context)
    {
        var placed = ActionClassifier.Classify(new ActionRequest.ShellCommand(command), context);
        return Untrusted(placed, command);
    }

    /// <summary>
    /// The taint a classified action brings in: each part that reads untrusted content, and each part whose output nth
    /// cannot vouch for (an unknown program, an install, anything that talks to the network or a connector).
    /// </summary>
    public static Taint Untrusted(Classification classification, string what)
    {
        var sources = new List<Taint>();
        foreach (var leaf in classification.Leaves)
        {
            if (TaintRules.OriginOf(leaf) is { } origin)
            {
                var detail = leaf.Target ?? what;
                if (origin == ContentOrigin.OutsideFile && Extension(detail) is { } ext && UntrustedExtensions.TryGetValue(ext, out var kind)) origin = kind;
                sources.Add(Taint.From(origin, Clip(detail)));
            }
            else if (leaf.Class is ActionClass.Unknown or ActionClass.ReadWeb or ActionClass.Install or ActionClass.Outward or ActionClass.Login
                     or ActionClass.ConnectorWrite or ActionClass.Commitment or ActionClass.Money or ActionClass.PersonalDataOut)
                sources.Add(Taint.From(leaf.GrantTarget?.Kind == GrantTargetKind.Connector ? ContentOrigin.Connector : ContentOrigin.ToolOutput, Clip(what)));
            else if (leaf.Class == ActionClass.ReadLocal && leaf.OutsideAccess)
                sources.Add(Taint.From(ContentOrigin.OutsideFile, Clip(leaf.Target ?? what)));
        }
        return Taint.UnionOf(sources);
    }

    /// <summary>"server · tool" (Claude) or "server · tool" with spaces (Codex): the server and the tool.</summary>
    public static (string Server, string Tool) SplitMcp(string title)
    {
        var dot = title.IndexOf('·', StringComparison.Ordinal);
        if (dot < 0) return ("", title.Trim());
        return (title[..dot].Trim(), title[(dot + 1)..].Trim());
    }

    private static bool LooksLikePath(string text) =>
        text.Contains('\\', StringComparison.Ordinal) || text.Contains('/', StringComparison.Ordinal) || (text.Length >= 2 && text[1] == ':');

    private static string? Extension(string path)
    {
        var name = path.Replace('/', '\\');
        var slash = name.LastIndexOf('\\');
        var dot = name.LastIndexOf('.');
        return dot > slash && dot < name.Length - 1 ? name[dot..].TrimEnd(' ', '"', '\'') : null;
    }

    /// <summary>A source's detail as the card and the log keep it.</summary>
    public static string Clip(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= DetailChars ? line : line[..DetailChars] + "…";
    }

    public const int DetailChars = 160;

    /// <summary>The most sources an agent's taint keeps (it is journaled with the mission at every change).</summary>
    public const int MaxSources = 16;

    /// <summary>The source that stands for every source past <see cref="MaxSources"/>.</summary>
    public static readonly TaintSource More = new(ContentOrigin.ToolOutput, "and more untrusted sources");

    /// <summary>
    /// A taint cut to <see cref="MaxSources"/>: the first sources as they came, then <see cref="More"/>. It stays tainted,
    /// and capping it again, or after a union with anything, gives the same taint, so a long run adds nothing more.
    /// </summary>
    public static Taint Capped(Taint taint)
    {
        ArgumentNullException.ThrowIfNull(taint);
        if (taint.Sources.Count <= MaxSources) return taint;
        var kept = taint.Sources.Where(s => s != More).Take(MaxSources - 1).Append(More);
        return new Taint(new ValueList<TaintSource>(kept));
    }
}
