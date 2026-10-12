// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4, 15.5; unit AS-4; ED-NEW-dextra-as4-2). No Swift twin.
// The nth-dextra tools of 15.5, one place for the MCP server, the API providers' native tools and the fenced-text
// fallback: each tool's name, title, description, arguments and who may call it. The arguments are strict: an unknown
// name, a missing required one, a wrong type, a value out of range or past its length refuses the call before the
// policy engine or any action is reached. Pure: no I/O.
using System.Globalization;
using Marrow.Core.Support;

namespace Marrow.Core.Port.Assistant;

/// <summary>Who a chat is to Dextra: its brain, or an agent of one of its missions.</summary>
public enum AssistantChatRole
{
    /// <summary>Dextra's own home thread, or an Astur's: every tool.</summary>
    Home,
    /// <summary>A mission's lead, head or helper chat: only the tools an agent needs (<see cref="AssistantToolDefinition.ForAgents"/>).</summary>
    Mission,
}

public enum AssistantParameterType { String, Integer, Number, Boolean, StringList }

/// <summary>One argument of a tool.</summary>
public sealed record AssistantToolParameter(string Name, AssistantParameterType Type, string Description)
{
    public bool Required { get; init; }

    /// <summary>The longest a string (or each string of a list) may be, in UTF-16 units.</summary>
    public int MaxLength { get; init; } = 200;

    /// <summary>The values a string (or each string of a list) may take; empty: any.</summary>
    public ValueList<string> Choices { get; init; } = ValueList<string>.Empty;

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    /// <summary>The most strings a list may hold.</summary>
    public int MaxItems { get; init; } = 32;

    /// <summary>Text the agent wrote (a brief, a message, a note): the audit log keeps only its length (12.5).</summary>
    public bool FreeText { get; init; }
}

/// <summary>One nth-dextra tool.</summary>
public sealed record AssistantToolDefinition(string Name, string Title, string Description, ValueList<AssistantToolParameter> Parameters)
{
    /// <summary>A mission's agents may call it too (every tool is offered to a home thread).</summary>
    public bool ForAgents { get; init; }

    /// <summary>It only reads nth's own state (MCP readOnlyHint).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>It may delete something for real (MCP destructiveHint).</summary>
    public bool Destructive { get; init; }

    /// <summary>It reaches outside nth (MCP openWorldHint).</summary>
    public bool OpenWorld { get; init; }

    /// <summary>
    /// Runs on the policy engine's Allow before AS-7 turns auto-answering on: it only reads nth's own state, stops work, or
    /// puts something in front of the owner or the parent. Every other tool's Allow becomes a card until then (16).
    /// </summary>
    public bool RunsBeforeAutoAnswer { get; init; }

    /// <summary>Whether a chat of this role is offered the tool.</summary>
    public bool OfferedTo(AssistantChatRole role) => role == AssistantChatRole.Home || ForAgents;

    /// <summary>The JSON schema of the arguments, with no other property allowed.</summary>
    public JSONValue InputSchema()
    {
        var properties = Parameters.Select(p => (p.Name, PropertySchema(p))).ToArray();
        var required = Parameters.Where(p => p.Required).Select(p => (JSONValue)p.Name).ToArray();
        return JSONValue.FromDictionaryLiteral(
            ("type", "object"),
            ("properties", JSONValue.FromDictionaryLiteral(properties)),
            ("required", JSONValue.FromArrayLiteral(required)),
            ("additionalProperties", false));
    }

    private static JSONValue PropertySchema(AssistantToolParameter p)
    {
        var fields = new List<(string, JSONValue)>();
        void Text(List<(string, JSONValue)> into)
        {
            into.Add(("type", "string"));
            into.Add(("maxLength", (long)p.MaxLength));
            if (p.Choices.Count > 0) into.Add(("enum", JSONValue.FromArrayLiteral([.. p.Choices.Select(c => (JSONValue)c)])));
        }
        switch (p.Type)
        {
            case AssistantParameterType.String: Text(fields); break;
            case AssistantParameterType.Integer: fields.Add(("type", "integer")); break;
            case AssistantParameterType.Number: fields.Add(("type", "number")); break;
            case AssistantParameterType.Boolean: fields.Add(("type", "boolean")); break;
            case AssistantParameterType.StringList:
                var item = new List<(string, JSONValue)>();
                Text(item);
                fields.Add(("type", "array"));
                fields.Add(("items", JSONValue.FromDictionaryLiteral([.. item])));
                fields.Add(("maxItems", (long)p.MaxItems));
                break;
        }
        if (p.Type is AssistantParameterType.Integer or AssistantParameterType.Number)
        {
            if (p.Minimum is { } min) fields.Add(("minimum", Number(p.Type, min)));
            if (p.Maximum is { } max) fields.Add(("maximum", Number(p.Type, max)));
        }
        fields.Add(("description", p.Description));
        return JSONValue.FromDictionaryLiteral([.. fields]);
    }

    private static JSONValue Number(AssistantParameterType type, double value) =>
        type == AssistantParameterType.Integer ? (JSONValue)(long)value : value;

    /// <summary>
    /// Null when the arguments fit the schema, else why not. Absent arguments (null or JSON null) are an empty object.
    /// </summary>
    public string? Validate(JSONValue? arguments)
    {
        if (arguments is null or JSONValue.Null) arguments = JSONValue.FromDictionaryLiteral();
        if (arguments.JSONValueObject is not { } given) return "the arguments must be one JSON object";
        foreach (var name in given.Keys)
            if (!Parameters.Any(p => p.Name == name)) return $"{Name} takes no argument \"{name}\"";
        foreach (var p in Parameters)
        {
            if (!given.TryGetValue(p.Name, out var value) || value is JSONValue.Null)
            {
                if (p.Required) return $"{Name} needs \"{p.Name}\"";
                continue;
            }
            if (Problem(p, value) is { } problem) return $"\"{p.Name}\" {problem}";
        }
        return null;
    }

    private static string? Problem(AssistantToolParameter p, JSONValue value)
    {
        switch (p.Type)
        {
            case AssistantParameterType.String:
                return value is JSONValue.String s ? TextProblem(p, s.Value) : "must be a string";
            case AssistantParameterType.Boolean:
                return value is JSONValue.Bool ? null : "must be true or false";
            case AssistantParameterType.Integer:
            {
                double? number = value switch
                {
                    JSONValue.Int i => i.Value,
                    JSONValue.Double d when double.IsFinite(d.Value) && Math.Floor(d.Value) == d.Value && Math.Abs(d.Value) < 9e15 => d.Value,
                    _ => null,
                };
                return number is { } n ? RangeProblem(p, n) : "must be a whole number";
            }
            case AssistantParameterType.Number:
            {
                double? number = value switch
                {
                    JSONValue.Int i => i.Value,
                    JSONValue.Double d when double.IsFinite(d.Value) => d.Value,
                    _ => null,
                };
                return number is { } n ? RangeProblem(p, n) : "must be a number";
            }
            case AssistantParameterType.StringList:
            {
                if (value.JSONValueArray is not { } items) return "must be a list of strings";
                if (items.Count > p.MaxItems) return $"holds at most {p.MaxItems} items";
                foreach (var item in items)
                {
                    if (item is not JSONValue.String text) return "must be a list of strings";
                    if (TextProblem(p, text.Value) is { } problem) return problem;
                }
                return null;
            }
            default:
                return "has a type nth does not know";
        }
    }

    private static string? TextProblem(AssistantToolParameter p, string text)
    {
        if (p.Required && p.Type == AssistantParameterType.String && string.IsNullOrWhiteSpace(text)) return "must not be empty";
        if (text.Length > p.MaxLength) return $"is longer than {p.MaxLength} characters";
        if (p.Choices.Count > 0 && !p.Choices.Contains(text)) return "must be one of " + string.Join(", ", p.Choices);
        return null;
    }

    private static string? RangeProblem(AssistantToolParameter p, double n)
    {
        if (p.Minimum is { } min && n < min) return "must be at least " + min.ToString(CultureInfo.InvariantCulture);
        if (p.Maximum is { } max && n > max) return "must be at most " + max.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>One line for the fenced fallback's tool list: "recall(query, limit?): Reads memory notes...".</summary>
    public string Signature() =>
        Name + "(" + string.Join(", ", Parameters.Select(p => p.Required ? p.Name : p.Name + "?")) + "): " + Description;
}

/// <summary>The 24 tools of 15.5.</summary>
public static class AssistantToolCatalog
{
    private const int Brief = 20_000;

    private static AssistantToolParameter Text(string name, string description, int max = Brief) =>
        new(name, AssistantParameterType.String, description) { Required = true, MaxLength = max, FreeText = true };

    private static AssistantToolParameter Id(string name, string description, int max = 200) =>
        new(name, AssistantParameterType.String, description) { Required = true, MaxLength = max };

    private static AssistantToolParameter Choice(string name, string description, params string[] choices) =>
        new(name, AssistantParameterType.String, description) { Required = true, Choices = new ValueList<string>(choices) };

    private static AssistantToolParameter Whole(string name, string description, int min, int max) =>
        new(name, AssistantParameterType.Integer, description) { Minimum = min, Maximum = max };

    private static AssistantToolParameter Optional(this AssistantToolParameter p) => p with { Required = false };

    private static AssistantToolDefinition Tool(string name, string title, string description, params AssistantToolParameter[] parameters) =>
        new(name, title, description, new ValueList<AssistantToolParameter>(parameters));

    public static ValueList<AssistantToolDefinition> All { get; } = new(
    [
        Tool("list_sessions", "Sessions", "Lists nth's chats with their state, project, provider and model. Chats hidden from Dextra are never listed.",
            Choice("state", "Only chats in this state.", "all", "working", "needs_you", "idle").Optional(),
            Id("project", "Only chats of this project (its id or name).").Optional(),
            Whole("limit", "The most chats to list.", 1, 200)) with { ReadOnly = true, RunsBeforeAutoAnswer = true },
        Tool("read_session", "Read a chat", "Reads one chat as a short summary, or a snapshot of its latest messages. Text that came from the web, email or files is marked as data.",
            Id("session", "The chat's id, from list_sessions."),
            Choice("mode", "summary (the default) or snapshot.", "summary", "snapshot").Optional(),
            Whole("limit", "For a snapshot: the most messages.", 1, 200)) with { ReadOnly = true, RunsBeforeAutoAnswer = true },
        Tool("start_mission", "Start a mission", "Starts a mission from a brief. A mission above the owner's threshold shows a plan card first.",
            Text("title", "A short title for the board.", 200),
            Text("brief", "What the mission must deliver, and anything the agents need to know."),
            Id("project", "The project it works in (its id or name).").Optional(),
            Choice("priority", "How urgent it is.", "low", "normal", "high").Optional(),
            new AssistantToolParameter("budget_usd", AssistantParameterType.Number, "The most it may spend, in US dollars.") { Minimum = 0, Maximum = 10_000 }),
        Tool("spawn_chat", "Start a chat", "Starts one chat for a step of a mission.",
            Id("mission", "The mission's id."),
            Text("step", "The step's name.", 200),
            Text("brief", "What the chat must do."),
            Id("provider", "The provider to use; empty: the router picks.").Optional(),
            Id("model", "The model to use; empty: the router picks.").Optional()),
        Tool("launch_hydra_team", "Start a team", "Starts a lead chat with a Hydra team for a step of a mission.",
            Id("mission", "The mission's id."),
            Text("brief", "What the team must deliver."),
            Whole("heads", "How many heads.", 1, 16),
            new AssistantToolParameter("profiles", AssistantParameterType.StringList, "Hydra head profiles to use, by id.") { MaxItems = 16 }),
        Tool("message_agent", "Message an agent", "Sends a message to a running agent, to redirect it.",
            Id("agent", "The agent's id (a chat or a head)."),
            Text("message", "What to tell it.", 8_000)),
        Tool("stop_agent", "Stop an agent", "Stops an agent, head or chat this assistant started. Its finished work is kept.",
            Id("agent", "The agent's id (a chat or a head)."),
            Text("reason", "Why, in one line.", 500).Optional()) with { RunsBeforeAutoAnswer = true },
        Tool("merge_results", "Merge results", "Merges what a mission's agents delivered into one result that names each part's source.",
            Id("mission", "The mission's id."),
            new AssistantToolParameter("parts", AssistantParameterType.StringList, "The agents or results to merge, by id; empty: all of them.") { MaxItems = 32 },
            Text("instructions", "How to merge them.", 4_000).Optional()),
        Tool("request_helpers", "Ask for helpers", "Asks for helper agents as a child mission, within the depth and budget limits. Over them, it raises a card for the owner.",
            Text("task", "What the helpers must do."),
            Whole("helpers", "How many helpers.", 1, 8),
            Text("reason", "Why you need them.", 1_000)) with { ForAgents = true },
        Tool("ask_owner", "Ask the owner", "Puts a question on a decision card for the owner. The answer comes back when he gives it; carry on with other work meanwhile.",
            Text("question", "The question, in one or two sentences.", 1_000),
            new AssistantToolParameter("options", AssistantParameterType.StringList, "Answers to choose from, if any.") { MaxItems = 6, FreeText = true },
            Text("detail", "What he needs to know to answer.", 4_000).Optional()) with { ForAgents = true, RunsBeforeAutoAnswer = true },
        Tool("schedule_duty", "Schedule a duty", "Creates a standing duty: when it runs and what it does. Always a card for the owner.",
            Text("title", "A short title.", 200),
            Text("instructions", "What the duty does each time it runs."),
            Text("schedule", "When it runs, for example \"weekdays 08:00\".", 200),
            new AssistantToolParameter("silent", AssistantParameterType.Boolean, "Leave it out of the morning digest.")),
        Tool("watch_folder", "Watch a folder", "Creates a folder trigger: what to do when files change there. Always a card for the owner.",
            Id("path", "The folder's full path.", 1_000),
            Text("instructions", "What to do when it changes."),
            Id("pattern", "Only files that match, for example \"*.pdf\".").Optional()),
        Tool("watch_page", "Watch a page", "Creates a web page watch: what to do when the page changes. Always a card for the owner.",
            Id("url", "The page's address.", 2_000),
            Text("instructions", "What to do when it changes."),
            Text("every", "How often to look, for example \"1h\".", 100).Optional()) with { OpenWorld = true },
        Tool("remember", "Remember", "Writes a memory note with its source. Secrets are never stored, and nothing is kept about third parties.",
            Text("note", "The note.", 4_000),
            Text("about", "What it is about, in a few words.", 200).Optional(),
            Id("source", "Where it came from (a chat, a file, the owner).", 1_000).Optional()) with { ForAgents = true },
        Tool("recall", "Recall", "Reads the memory notes that match.",
            Text("query", "What to look for.", 1_000),
            Whole("limit", "The most notes.", 1, 50)) with { ForAgents = true, ReadOnly = true, RunsBeforeAutoAnswer = true },
        Tool("forget", "Forget", "Deletes memory notes for real.",
            new AssistantToolParameter("notes", AssistantParameterType.StringList, "The notes' ids, from recall.") { Required = true, MaxItems = 50 }) with { Destructive = true },
        Tool("usage_status", "Usage", "Shows the accounts, their windows, and what is left above each " + AssistantNames.Reserve + ".",
            Id("account", "Only this account.").Optional()) with { ForAgents = true, ReadOnly = true, RunsBeforeAutoAnswer = true },
        Tool("plan_budget", "Plan a budget", "Builds a plan that stays within the " + AssistantNames.Reserve + "s: the accounts and models, and what each step may spend.",
            Text("brief", "The work to plan."),
            Whole("agents", "How many agents at most.", 1, 64)) with { ReadOnly = true, RunsBeforeAutoAnswer = true },
        Tool("delegate_to", "Hand to an " + AssistantNames.Astur, "Hands a piece of work to one of the " + AssistantNames.Asturs + ", which reports back.",
            Id("astur", "The " + AssistantNames.Astur + "'s id or name."),
            Text("brief", "What it must do."),
            Text("material", "Material to pass along as data.").Optional()),
        Tool("report", "Report", "Reports results up to the parent: the mission's lead, or Dextra.",
            Text("summary", "What was done and what was found."),
            Choice("status", "How it ended.", "done", "partial", "failed", "blocked"),
            new AssistantToolParameter("artifacts", AssistantParameterType.StringList, "Files or results it made, by path or id.") { MaxItems = 32, MaxLength = 1_000 }) with { ForAgents = true, RunsBeforeAutoAnswer = true },
        Tool("create_assistant", "Draft an " + AssistantNames.Astur, "Drafts an " + AssistantNames.Astur + "'s profile on a card for the owner. Always a card.",
            Text("name", "Its name.", 100),
            Text("area", "Its area, for example \"Finance\".", 100),
            Text("purpose", "What it is for.", 4_000),
            Choice("shape", "Its node shape.", "squircle", "hexagon", "diamond", "rounded_triangle", "clover", "star").Optional()),
        Tool("propose_capability", "Propose a capability", "Proposes a connector, skill, MCP server or workflow the request needs, with what it would allow and why. Installing is always confirmed by the owner.",
            Choice("kind", "What it is.", "connector", "skill", "mcp_server", "workflow"),
            Text("name", "Its name.", 200),
            Text("why", "Why the request needs it.", 2_000),
            Text("allows", "What it would allow.", 2_000)),
        Tool("suggest", "Suggest", "Adds one line to the Suggestions lane of the morning digest (at most 3 a day).",
            Text("line", "The suggestion, in one line.", 300),
            Text("action", "What accepting it does.", 300).Optional()),
        Tool("deliver", "Deliver", "Hands a result to the owner in the form for his device.",
            Text("title", "A short title.", 200),
            Text("content", "The result.", 100_000),
            Choice("format", "Its form.", "text", "markdown", "file", "link").Optional(),
            Choice("device", "Where he will read it.", "pc", "phone").Optional()),
    ]);

    private static readonly Dictionary<string, AssistantToolDefinition> byName = All.ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>The tool of that exact name, or null.</summary>
    public static AssistantToolDefinition? Find(string? name) => name is not null && byName.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>The tools a chat of this role is offered, in catalogue order.</summary>
    public static IEnumerable<AssistantToolDefinition> For(AssistantChatRole role) => All.Where(t => t.OfferedTo(role));
}
