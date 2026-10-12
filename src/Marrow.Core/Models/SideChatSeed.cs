using System.Globalization;
using System.Text;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W6, the side chat's seed (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.9 "Context: at each side-chat turn,
// nth seeds a fresh read-only snapshot of the main thread: the last 6 turns verbatim (tool outputs trimmed to 2k characters each), an
// earlier-turns summary made once and cached, the current plan and todos, the changed-files list and what the main run is doing now
// (Phase, current tool). Each turn sees the latest state"; 13.2 "Side chat seeding: snapshot content and limits; never writes to the
// parent"; ED-NEW-web-w6-1). No Swift counterpart. A pure function of the main thread's recorded items: it reads them and builds text,
// it holds nothing of the parent and writes nothing to it. The earlier-turns summary is a mechanical digest, never a model call (the
// same rule as the account handoff, HandoffSummary): one short line per turn, made the first time the turn is outside the verbatim
// window and kept in a SideChatDigest from then on.

namespace Marrow.Core.Models;

/// <summary>What the main run is doing (the runtime's phase).</summary>
public enum SideChatMainPhase
{
    Idle,
    Starting,
    Running,
}

/// <summary>The numbers of the seed (DESIGN 3.9) and the bounds that keep it small.</summary>
public sealed record SideChatLimits
{
    /// <summary>The recent turns given verbatim.</summary>
    public int RecentTurns { get; init; } = 6;

    /// <summary>Each tool output, in characters.</summary>
    public int ToolOutputCharacters { get; init; } = 2_000;

    /// <summary>Each user or assistant message, in characters.</summary>
    public int MessageCharacters { get; init; } = 12_000;

    /// <summary>The plan's text, in characters.</summary>
    public int PlanCharacters { get; init; } = 6_000;

    /// <summary>A digest line's two clauses (what was asked, what came of it), each in characters.</summary>
    public int DigestClauseCharacters { get; init; } = 180;

    /// <summary>The digest as a whole; the oldest lines go first.</summary>
    public int DigestCharacters { get; init; } = 8_000;

    /// <summary>The changed-files list; the rest is counted.</summary>
    public int ChangedFiles { get; init; } = 120;

    /// <summary>The whole snapshot. Past it the seed gives up tool output, then turns, then message length, in that order.</summary>
    public int SnapshotCharacters { get; init; } = 60_000;

    /// <summary>The side chat's own earlier exchanges that travel with a turn.</summary>
    public int SideTurns { get; init; } = 6;

    /// <summary>Each message of those exchanges, in characters.</summary>
    public int SideMessageCharacters { get; init; } = 3_000;

    public static SideChatLimits Default { get; } = new();
}

/// <summary>
/// The earlier-turns summary, kept by the side chat. A turn's line is made once, the first time it is needed, and read back after
/// that: a later change to the turn's items (a rewind removes turns, it does not edit them) is not seen by a turn already summarised.
/// </summary>
public sealed class SideChatDigest
{
    private readonly Dictionary<Guid, string> lines = [];

    /// <summary>How many lines are kept.</summary>
    public int Count => lines.Count;

    /// <summary>How many times a line was made (a test reads it to see "once").</summary>
    public int Made { get; private set; }

    /// <summary>The line of a turn: kept, else made now by <paramref name="make"/> and kept.</summary>
    public string Line(Guid turn, Func<string> make)
    {
        ArgumentNullException.ThrowIfNull(make);
        if (lines.TryGetValue(turn, out var kept)) return kept;
        Made++;
        return lines[turn] = make();
    }

    /// <summary>Forgets the lines of turns that are not in <paramref name="live"/> (a thread that lost turns).</summary>
    public void Keep(IReadOnlySet<Guid> live)
    {
        foreach (var turn in lines.Keys.Where(turn => !live.Contains(turn)).ToArray()) lines.Remove(turn);
    }

    /// <summary>Forgets everything.</summary>
    public void Clear() => lines.Clear();
}

/// <summary>The main thread as the seed reads it. Nothing here is changed by the seed.</summary>
/// <param name="Title">The thread's title.</param>
/// <param name="Phase">What the run is doing.</param>
/// <param name="Items">The recorded items in order.</param>
/// <param name="Turns">The turns in order.</param>
/// <param name="PendingApprovals">Requests waiting for the person.</param>
/// <param name="WaitingForAnswer">A question card waits for the person.</param>
/// <param name="ProjectName">The project, if it has a name.</param>
/// <param name="Branch">The branch the thread works on, if any.</param>
/// <param name="Root">The folder the thread works in; a changed file under it is listed relative to it.</param>
public sealed record SideChatMain(
    string Title,
    SideChatMainPhase Phase,
    IReadOnlyList<TimelineItem> Items,
    IReadOnlyList<TurnRecord> Turns,
    int PendingApprovals = 0,
    bool WaitingForAnswer = false,
    string? ProjectName = null,
    string? Branch = null,
    string? Root = null);

/// <summary>The snapshot and what it holds.</summary>
/// <param name="Text">The snapshot text.</param>
/// <param name="VerbatimTurns">How many recent turns are given in full.</param>
/// <param name="SummarisedTurns">How many earlier turns are given as digest lines.</param>
/// <param name="ChangedFiles">The changed files, in the order they first changed.</param>
/// <param name="WasShortened">The snapshot gave something up to stay inside its bound.</param>
public sealed record SideChatSnapshot(string Text, int VerbatimTurns, int SummarisedTurns, IReadOnlyList<string> ChangedFiles, bool WasShortened);

/// <summary>Builds the snapshot of a main thread.</summary>
public static class SideChatSeed
{
    private const string Omitted = "[… {0} characters omitted …]";

    /// <summary>The snapshot of <paramref name="main"/> as it is now, inside <paramref name="limits"/>.</summary>
    public static SideChatSnapshot Build(SideChatMain main, SideChatDigest digest, SideChatLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(digest);
        limits ??= SideChatLimits.Default;

        var turns = main.Turns.ToList();
        var byTurn = Group(main.Items, turns);
        digest.Keep(turns.Select(turn => turn.Id).ToHashSet());
        var changed = ChangedFiles(main, turns, byTurn);

        // The window shrinks from the verbatim side: tool output first, then whole turns, then the length of every message. A turn that
        // leaves the window is not lost: it is in the digest from then on.
        var steps = Steps(limits);
        for (var attempt = 0; ; attempt++)
        {
            var step = steps[attempt];
            var text = Render(main, turns, byTurn, changed, digest, limits, step);
            if (text.Length <= limits.SnapshotCharacters || attempt == steps.Count - 1)
            {
                var shortened = attempt > 0;
                if (text.Length > limits.SnapshotCharacters)
                {
                    text = Clip(text, limits.SnapshotCharacters, 0.5);
                    shortened = true;
                }
                var verbatim = Math.Min(step.Turns, turns.Count);
                return new SideChatSnapshot(text, verbatim, turns.Count - verbatim, changed, shortened);
            }
        }
    }

    private readonly record struct Step(int Turns, int ToolOutput, int Message);

    private static List<Step> Steps(SideChatLimits limits)
    {
        var steps = new List<Step> { new(limits.RecentTurns, limits.ToolOutputCharacters, limits.MessageCharacters) };
        void Add(Step step)
        {
            if (step != steps[^1]) steps.Add(step);
        }
        Add(new(limits.RecentTurns, Math.Min(limits.ToolOutputCharacters, 600), Math.Min(limits.MessageCharacters, 6_000)));
        Add(new(limits.RecentTurns, 0, Math.Min(limits.MessageCharacters, 3_000)));
        foreach (var turns in new[] { 4, 3, 2, 1 })
            if (turns < limits.RecentTurns) Add(new(turns, 0, Math.Min(limits.MessageCharacters, turns >= 3 ? 3_000 : 2_000)));
        Add(new(Math.Min(1, limits.RecentTurns), 0, Math.Min(limits.MessageCharacters, 1_000)));
        return steps;
    }

    // Items by their turn, in order. An item with no turn belongs to the turn before it (a notice between two turns).
    private static Dictionary<Guid, List<TimelineItem>> Group(IReadOnlyList<TimelineItem> items, List<TurnRecord> turns)
    {
        var known = turns.Select(turn => turn.Id).ToHashSet();
        var groups = new Dictionary<Guid, List<TimelineItem>>();
        foreach (var turn in turns) groups.TryAdd(turn.Id, []); // a repeated turn id (a damaged file) shares one group
        Guid? current = null;
        foreach (var item in items)
        {
            if (item.TurnID is { } id)
            {
                if (!known.Contains(id)) continue; // a turn this thread does not record: its items are not the previous turn's
                current = id;
            }
            if (current is { } at) groups[at].Add(item);
        }
        return groups;
    }

    private static string Render(SideChatMain main, List<TurnRecord> turns, Dictionary<Guid, List<TimelineItem>> byTurn,
        IReadOnlyList<string> changed, SideChatDigest digest, SideChatLimits limits, Step step)
    {
        var text = new StringBuilder();
        text.Append("Main thread: \"").Append(OneLine(main.Title, 120)).Append('"');
        if (!string.IsNullOrWhiteSpace(main.ProjectName)) text.Append(", project \"").Append(OneLine(main.ProjectName!, 80)).Append('"');
        if (!string.IsNullOrWhiteSpace(main.Branch)) text.Append(", branch ").Append(OneLine(main.Branch!, 80));
        text.Append('\n');
        text.Append("Main run: ").Append(Activity(main, turns, byTurn)).Append('\n');

        if (LatestPlan(main.Items) is { } plan)
        {
            text.Append("\nPlan (").Append(PlanState(plan.ProposedPlanState)).Append("):\n")
                .Append(Clip(Normalize(plan.Markdown), limits.PlanCharacters, 0.7)).Append('\n');
        }
        if (LatestTodos(main.Items) is { Count: > 0 } todos)
        {
            text.Append("\nTodos:\n");
            foreach (var todo in todos) text.Append("- [").Append(TodoState(todo.TodoStepStatus)).Append("] ").Append(OneLine(todo.Text, 300)).Append('\n');
        }

        text.Append("\nChanged files");
        if (changed.Count == 0) text.Append(": none yet\n");
        else
        {
            text.Append(" (").Append(changed.Count.ToString(CultureInfo.InvariantCulture)).Append("):\n");
            foreach (var file in changed.Take(limits.ChangedFiles)) text.Append("- ").Append(OneLine(file, 260)).Append('\n');
            if (changed.Count > limits.ChangedFiles)
                text.Append("- and ").Append((changed.Count - limits.ChangedFiles).ToString(CultureInfo.InvariantCulture)).Append(" more\n");
        }

        var recent = Math.Min(step.Turns, turns.Count);
        var earlier = turns.Take(turns.Count - recent).ToList();
        if (earlier.Count > 0)
        {
            text.Append("\nEarlier turns, summarised (").Append(earlier.Count.ToString(CultureInfo.InvariantCulture)).Append("):\n");
            var lines = earlier.Select(turn => digest.Line(turn.Id, () => DigestLine(turn, byTurn[turn.Id], limits))).ToList();
            var kept = new List<string>();
            var length = 0;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                length += lines[i].Length + 1;
                if (length > limits.DigestCharacters && kept.Count > 0) break;
                kept.Insert(0, lines[i]);
            }
            if (kept.Count < lines.Count)
                text.Append("(").Append((lines.Count - kept.Count).ToString(CultureInfo.InvariantCulture)).Append(" older turns left out)\n");
            foreach (var line in kept) text.Append(line).Append('\n');
        }

        if (recent == 0)
        {
            text.Append("\nNo turns yet.\n");
            return text.ToString().TrimEnd();
        }
        text.Append("\nThe last ").Append(recent == 1 ? "turn" : recent.ToString(CultureInfo.InvariantCulture) + " turns").Append(", in full:\n");
        foreach (var turn in turns.Skip(turns.Count - recent)) RenderTurn(text, turn, byTurn[turn.Id], step, main.Root);
        return text.ToString().TrimEnd();
    }

    private static void RenderTurn(StringBuilder text, TurnRecord turn, List<TimelineItem> items, Step step, string? root)
    {
        text.Append("\n--- Turn ").Append((turn.Index + 1).ToString(CultureInfo.InvariantCulture)).Append(" (").Append(StatusWord(turn.Status)).Append(") ---\n");
        foreach (var item in items)
        {
            switch (item.TimelineItemContent)
            {
                case TimelineItem.Content.User user:
                    var who = user.Value.IsFromHydra ? "A helper's report" : user.Value.IsHydraBrief ? "Brief to a helper" : "User";
                    text.Append(who).Append(":\n").Append(Clip(Normalize(user.Value.Text), step.Message, 0.6)).Append('\n');
                    foreach (var attachment in user.Value.Attachments) text.Append("(attached: ").Append(OneLine(attachment.Name, 120)).Append(")\n");
                    break;
                case TimelineItem.Content.Assistant assistant:
                    if (assistant.Value.Text.Trim().Length == 0) break;
                    text.Append("Assistant").Append(assistant.Value.IsStreaming ? " (still writing)" : "").Append(":\n")
                        .Append(Clip(Normalize(assistant.Value.Text), step.Message, 0.6)).Append('\n');
                    break;
                case TimelineItem.Content.Tool tool:
                    RenderTool(text, tool.Value, step, root);
                    break;
                case TimelineItem.Content.Plan plan:
                    text.Append("Plan (").Append(PlanState(plan.Value.ProposedPlanState)).Append(") written in this turn.\n");
                    break;
                case TimelineItem.Content.Notice notice:
                    text.Append("Notice (").Append(notice.Value.NoticeLevel.ToString().ToLowerInvariant()).Append("): ").Append(OneLine(notice.Value.Message, 400)).Append('\n');
                    break;
                case TimelineItem.Content.Ask ask:
                    text.Append(AskUserWords.Describe(ask.Value));
                    break;
                // Reasoning is the model's private working, not part of the conversation; todos and the turn summary are given once above.
            }
        }
    }

    private static void RenderTool(StringBuilder text, ToolCall call, Step step, string? root)
    {
        text.Append("Tool (").Append(KindWord(call.ToolCallKind)).Append("): ").Append(OneLine(call.Title, 300));
        text.Append(" - ").Append(ToolStatus(call));
        text.Append('\n');
        if (!string.IsNullOrWhiteSpace(call.Detail) && !string.Equals(call.Detail!.Trim(), call.Title.Trim(), StringComparison.Ordinal))
            text.Append("  ").Append(OneLine(call.Detail!, 400)).Append('\n');
        foreach (var edit in call.Edits)
            text.Append("  edited ").Append(OneLine(Relative(edit.Path, root), 260)).Append(" (+").Append(edit.Additions.ToString(CultureInfo.InvariantCulture))
                .Append(" -").Append(edit.Deletions.ToString(CultureInfo.InvariantCulture)).Append(")\n");
        if (step.ToolOutput > 0 && !string.IsNullOrWhiteSpace(call.Output))
        {
            // A command's end is where its verdict is, so the tail keeps more than the head.
            text.Append("  output:\n").Append(Indent(Clip(Normalize(call.Output), step.ToolOutput, 0.34))).Append('\n');
        }
        else if (!string.IsNullOrWhiteSpace(call.Output))
            text.Append("  (output left out)\n");
    }

    private static string DigestLine(TurnRecord turn, List<TimelineItem> items, SideChatLimits limits)
    {
        var asked = items.Select(item => item.TimelineItemContent).OfType<TimelineItem.Content.User>()
            .Select(user => user.Value).FirstOrDefault(user => !user.IsHydraBrief)?.Text ?? "";
        var answer = items.Select(item => item.TimelineItemContent).OfType<TimelineItem.Content.Assistant>()
            .Select(assistant => assistant.Value.Text).LastOrDefault(value => value.Trim().Length != 0) ?? "";
        var tools = items.Count(item => item.TimelineItemContent is TimelineItem.Content.Tool);
        var files = turn.TouchedPaths?.Take(6).ToList() ?? [];
        var line = new StringBuilder();
        line.Append("Turn ").Append((turn.Index + 1).ToString(CultureInfo.InvariantCulture)).Append(" (").Append(StatusWord(turn.Status)).Append("): ");
        line.Append("asked \"").Append(OneLine(asked, limits.DigestClauseCharacters)).Append('"');
        if (answer.Length != 0) line.Append("; answered \"").Append(OneLine(answer, limits.DigestClauseCharacters)).Append('"');
        if (tools != 0) line.Append("; ").Append(tools.ToString(CultureInfo.InvariantCulture)).Append(tools == 1 ? " tool call" : " tool calls");
        if (files.Count != 0)
        {
            line.Append("; changed ").Append(string.Join(", ", files.Select(file => OneLine(file, 120))));
            if (turn.TouchedPaths!.Count > files.Count) line.Append(" and ").Append((turn.TouchedPaths.Count - files.Count).ToString(CultureInfo.InvariantCulture)).Append(" more");
        }
        return line.ToString();
    }

    // What the run is doing, as a fact the side chat can state: the phase, then the step it is in.
    private static string Activity(SideChatMain main, List<TurnRecord> turns, Dictionary<Guid, List<TimelineItem>> byTurn)
    {
        if (main.PendingApprovals > 0)
            return "waiting for the person to answer " + (main.PendingApprovals == 1 ? "a request" : main.PendingApprovals.ToString(CultureInfo.InvariantCulture) + " requests") + ".";
        if (main.WaitingForAnswer) return "waiting for the person to answer its question.";
        if (main.Phase == SideChatMainPhase.Idle)
            return turns.Count == 0 ? "idle; no turn has run yet." : "idle, waiting for the person's next message.";
        var items = turns.Count == 0 ? [] : byTurn[turns[^1].Id];
        var step = "working";
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var content = items[i].TimelineItemContent;
            if (content is TimelineItem.Content.Tool { Value.ToolCallStatus: ToolCall.Status.Running } running)
            {
                step = "working, " + ActivityWord(running.Value.ToolCallKind) + ": " + OneLine(running.Value.Title, 200);
                break;
            }
            if (content is TimelineItem.Content.Assistant { Value.IsStreaming: true }) { step = "working, writing its reply"; break; }
            if (content is TimelineItem.Content.Reasoning { Value.IsStreaming: true }) { step = "working, thinking"; break; }
            if (content is TimelineItem.Content.Tool or TimelineItem.Content.Assistant) break;
        }
        return (main.Phase == SideChatMainPhase.Starting ? "starting a turn" : step) + ".";
    }

    private static ProposedPlan? LatestPlan(IReadOnlyList<TimelineItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
            if (items[i].TimelineItemContent is TimelineItem.Content.Plan plan && plan.Value.ProposedPlanState != ProposedPlan.State.Dismissed) return plan.Value;
        return null;
    }

    private static ValueList<TodoStep>? LatestTodos(IReadOnlyList<TimelineItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
            if (items[i].TimelineItemContent is TimelineItem.Content.Todos todos) return todos.Value;
        return null;
    }

    // The files the run changed: each turn's touched paths, then the paths of the edits its tools reported, once each, in the order they
    // first changed.
    private static List<string> ChangedFiles(SideChatMain main, List<TurnRecord> turns, Dictionary<Guid, List<TimelineItem>> byTurn)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path)
        {
            var shown = Relative(path, main.Root);
            if (shown.Length != 0 && seen.Add(shown)) files.Add(shown);
        }
        foreach (var turn in turns)
        {
            foreach (var path in turn.TouchedPaths ?? ValueList<string>.Empty) Add(path);
            foreach (var item in byTurn[turn.Id])
                if (item.TimelineItemContent is TimelineItem.Content.Tool tool)
                    foreach (var edit in tool.Value.Edits) Add(edit.Path);
        }
        return files;
    }

    /// <summary>A path under <paramref name="root"/> relative to it, with forward slashes; any other path as it is, slashes unified.</summary>
    public static string Relative(string path, string? root)
    {
        var shown = path.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(root)) return shown;
        var prefix = root.Trim().Replace('\\', '/').TrimEnd('/') + "/";
        return shown.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? shown[prefix.Length..] : shown;
    }

    private static string ActivityWord(ToolCall.Kind kind) => kind switch
    {
        ToolCall.Kind.Command => "running a command",
        ToolCall.Kind.Read => "reading",
        ToolCall.Kind.Edit => "editing",
        ToolCall.Kind.Search => "searching",
        ToolCall.Kind.Web => "reading the web",
        ToolCall.Kind.Mcp => "calling an MCP tool",
        ToolCall.Kind.Agent => "running a helper",
        _ => "using a tool",
    };

    private static string KindWord(ToolCall.Kind kind) => kind switch
    {
        ToolCall.Kind.Command => "command",
        ToolCall.Kind.Read => "read",
        ToolCall.Kind.Edit => "edit",
        ToolCall.Kind.Search => "search",
        ToolCall.Kind.Web => "web",
        ToolCall.Kind.Mcp => "mcp",
        ToolCall.Kind.Agent => "helper",
        _ => "other",
    };

    private static string ToolStatus(ToolCall call) => call.ToolCallStatus switch
    {
        ToolCall.Status.Running => "running",
        ToolCall.Status.Completed => call.ExitCode is { } code && code != 0 ? "finished, exit " + code.ToString(CultureInfo.InvariantCulture) : "finished",
        ToolCall.Status.Failed => call.ExitCode is { } failed ? "failed, exit " + failed.ToString(CultureInfo.InvariantCulture) : "failed",
        ToolCall.Status.Declined => "declined by the person",
        _ => "",
    };

    private static string StatusWord(TurnStatus status) => status switch
    {
        TurnStatus.Running => "running",
        TurnStatus.Completed => "completed",
        TurnStatus.Failed => "failed",
        TurnStatus.Interrupted => "stopped",
        _ => "",
    };

    private static string PlanState(ProposedPlan.State state) => state switch
    {
        ProposedPlan.State.Drafting => "being drafted",
        ProposedPlan.State.Proposed => "proposed, not yet approved",
        ProposedPlan.State.Accepted => "approved",
        ProposedPlan.State.Dismissed => "dismissed",
        _ => "",
    };

    private static string TodoState(TodoStep.Status status) => status switch
    {
        TodoStep.Status.Done => "done",
        TodoStep.Status.Active => "doing",
        _ => "to do",
    };

    // ---------------------------------------------------------------------------- text helpers

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd();

    private static string Indent(string text) => "    " + text.Replace("\n", "\n    ", StringComparison.Ordinal);

    /// <summary>The text on one line, whitespace collapsed, cut at <paramref name="limit"/> characters with an ellipsis.</summary>
    internal static string OneLine(string text, int limit)
    {
        var builder = new StringBuilder(Math.Min(text.Length, limit + 1));
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length != 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(character);
            if (builder.Length > limit) break;
        }
        if (builder.Length <= limit) return builder.ToString();
        return builder.ToString(0, EndBefore(builder.ToString(), limit)).TrimEnd() + "…";
    }

    /// <summary>
    /// <paramref name="text"/> cut to about <paramref name="limit"/> characters: its head (the <paramref name="headShare"/> part) and its
    /// tail, with a note of how much went between. A cut never splits a surrogate pair.
    /// </summary>
    internal static string Clip(string text, int limit, double headShare)
    {
        if (limit <= 0) return "";
        if (text.Length <= limit) return text;
        var head = EndBefore(text, (int)Math.Round(limit * headShare));
        var tail = StartAfter(text, text.Length - (limit - (int)Math.Round(limit * headShare)));
        if (tail < head) tail = head;
        return text[..head].TrimEnd() + "\n" + string.Format(CultureInfo.InvariantCulture, Omitted, tail - head) + "\n" + text[tail..].TrimStart();
    }

    private static int EndBefore(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        return index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) ? index - 1 : index;
    }

    private static int StartAfter(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        return index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) ? index + 1 : index;
    }
}
