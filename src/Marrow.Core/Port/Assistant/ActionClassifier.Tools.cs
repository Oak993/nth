// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a, 15.5; unit AS-1; ED-NEW-dextracore-2). No Swift twin.
// MCP tools are placed by their name, then made stricter (never looser) by their description and annotations, because a
// server can lie about both. nth-assistant's own tools (15.5) map by name, each with the extra rule the engine applies.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public static partial class ActionClassifier
{
    private static readonly HashSet<string> SubscriptionWords = new(StringComparer.Ordinal)
        { "subscribe", "subscription", "recurring", "renew", "renewal", "autopay" };

    private static readonly HashSet<string> TransferWords = new(StringComparer.Ordinal)
        { "transfer", "wire", "payout", "withdraw", "withdrawal", "deposit", "remit", "remittance", "swap", "trade", "sell", "stake", "charge" };

    private static readonly HashSet<string> ToPersonWords = new(StringComparer.Ordinal)
        { "pay", "payment", "tip", "donate", "donation", "refund", "venmo", "zelle", "paypal", "money", "funds", "cash", "reimburse" };

    private static readonly HashSet<string> PurchaseWords = new(StringComparer.Ordinal)
        { "buy", "purchase", "checkout", "order", "cart", "invoice", "billing", "bill" };

    /// <summary>Money verbs: a money movement whatever else the name says.</summary>
    private static readonly HashSet<string> MoneyVerbs = new(StringComparer.Ordinal)
    {
        "pay", "buy", "purchase", "checkout", "subscribe", "renew", "transfer", "wire", "withdraw", "deposit", "remit", "swap",
        "trade", "sell", "stake", "charge", "tip", "donate", "refund", "reimburse", "autopay",
    };

    /// <summary>Verbs that only ever mean sending: they count wherever they appear.</summary>
    private static readonly HashSet<string> OutwardVerbs = new(StringComparer.Ordinal)
    {
        "send", "publish", "forward", "invite", "upload", "notify", "broadcast", "submit", "announce", "retweet", "respond",
        "mention", "react", "unshare",
    };

    /// <summary>Words that are a send as a verb and a thing as a noun ("post", "list_posts"): they count with no read verb.</summary>
    private static readonly HashSet<string> OutwardNouns = new(StringComparer.Ordinal)
    {
        "post", "reply", "share", "email", "message", "mail", "sms", "dm", "chat", "comment", "tweet", "push", "invitation",
        "notification", "text",
    };

    private static readonly HashSet<string> DestructiveVerbs = new(StringComparer.Ordinal)
    {
        "delete", "remove", "destroy", "erase", "purge", "wipe", "truncate", "uninstall", "overwrite", "unpublish",
        "terminate", "kill",
    };

    /// <summary>Destructive as a verb, a state as an adjective ("get_closed_issues"): they count with no read verb.</summary>
    private static readonly HashSet<string> DestructiveNouns = new(StringComparer.Ordinal)
        { "close", "clear", "cancel", "reset", "revoke", "deactivate", "ban", "kick", "drop", "trash" };

    private static readonly HashSet<string> CommitmentVerbs = new(StringComparer.Ordinal)
        { "book", "accept", "agree", "rsvp", "confirm", "reserve", "register", "enroll", "approve", "decline", "sign", "signup", "consent", "authorize" };

    private static readonly HashSet<string> InstallVerbs = new(StringComparer.Ordinal) { "install", "provision" };

    private static readonly HashSet<string> LoginWords = new(StringComparer.Ordinal)
        { "login", "logon", "signin", "authenticate", "oauth", "reauth", "reauthenticate" };

    /// <summary>Words that mean the tool runs code or commands it is handed: never placed by name.</summary>
    private static readonly HashSet<string> ExecutionWords = new(StringComparer.Ordinal)
    {
        "run", "execute", "exec", "eval", "evaluate", "invoke", "shell", "command", "script", "terminal", "spawn",
        "powershell", "bash", "cmd", "repl", "interpret",
    };

    private static readonly HashSet<string> ReadVerbs = new(StringComparer.Ordinal)
    {
        "get", "list", "read", "search", "find", "fetch", "query", "lookup", "look", "view", "show", "describe", "retrieve",
        "browse", "check", "count", "inspect", "preview", "status", "summarize", "summary", "info", "poll", "scan", "load",
        "select", "recall", "peek", "explain", "analyze", "compare", "diff", "history", "download", "export", "whoami",
    };

    private static readonly HashSet<string> WriteVerbs = new(StringComparer.Ordinal)
    {
        "create", "update", "edit", "set", "add", "put", "patch", "modify", "change", "write", "save", "insert", "upsert",
        "rename", "move", "copy", "assign", "tag", "label", "mark", "star", "unstar", "pin", "unpin", "archive", "unarchive",
        "enable", "disable", "configure", "draft", "append", "attach", "schedule", "start", "stop", "restart", "merge", "open",
        "new", "make", "import", "sync", "store", "complete", "toggle", "replace", "fill",
    };

    /// <summary>The words of a tool name or description, lower case and without a plural or tense ending.</summary>
    internal static List<string> ToolWords(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0) words.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }
        for (var k = 0; k < text.Length; k++)
        {
            var ch = text[k];
            if (!char.IsLetterOrDigit(ch)) { Flush(); continue; }
            // camelCase: sendMessage -> send, message; HTTPRequest -> http, request.
            if (char.IsUpper(ch) && current.Length > 0
                && (char.IsLower(current[^1]) || char.IsDigit(current[^1]) || (k + 1 < text.Length && char.IsLower(text[k + 1]) && char.IsUpper(current[^1]))))
                Flush();
            current.Append(ch);
        }
        Flush();
        // "log in", "sign in", "sign up", "sign on" read as one word.
        for (var k = 0; k + 1 < words.Count; k++)
        {
            if (words[k] is "log" or "sign" && words[k + 1] is "in" or "on")
            {
                words[k] = words[k] == "log" ? "login" : "signin";
                words.RemoveAt(k + 1);
            }
            else if (words[k] == "sign" && words[k + 1] == "up")
            {
                words[k] = "signup";
                words.RemoveAt(k + 1);
            }
        }
        return words;
    }

    /// <summary>
    /// Whether a word is in a set, as written or without a plural ending; with <paramref name="tenses"/> also without
    /// -ed or -ing ("sending", "deleted").
    /// </summary>
    private static bool InSet(string word, HashSet<string> set, bool tenses = false) => Stems(word, tenses).Any(set.Contains);

    private static IEnumerable<string> Stems(string word, bool tenses)
    {
        yield return word;
        if (word.Length > 3 && word.EndsWith("es", StringComparison.Ordinal)) yield return word[..^2];
        if (word.Length > 2 && word.EndsWith('s')) yield return word[..^1];
        if (!tenses) yield break;
        if (word.Length > 4 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            yield return word[..^3];
            yield return word[..^3] + "e";
            if (word.Length > 5 && word[^4] == word[^5]) yield return word[..^4];
        }
        if (word.Length > 3 && word.EndsWith("ed", StringComparison.Ordinal))
        {
            yield return word[..^2];
            yield return word[..^1];
            if (word.Length > 4 && word[^3] == word[^4]) yield return word[..^3];
        }
    }

    private static MoneyKind MoneyKindOf(IEnumerable<string> words)
    {
        var kinds = new List<MoneyKind>();
        foreach (var w in words)
        {
            if (InSet(w, SubscriptionWords)) kinds.Add(MoneyKind.Subscription);
            if (InSet(w, TransferWords)) kinds.Add(MoneyKind.Transfer);
            if (InSet(w, ToPersonWords)) kinds.Add(MoneyKind.ToPerson);
            if (InSet(w, PurchaseWords)) kinds.Add(MoneyKind.Purchase);
        }
        // Anything that is not a plain purchase is never allowed, so it wins over a purchase.
        if (kinds.Contains(MoneyKind.Transfer)) return MoneyKind.Transfer;
        if (kinds.Contains(MoneyKind.ToPerson)) return MoneyKind.ToPerson;
        if (kinds.Contains(MoneyKind.Subscription)) return MoneyKind.Subscription;
        return kinds.Count > 0 ? MoneyKind.Purchase : MoneyKind.None;
    }

    /// <summary>The class of an MCP tool call (12.1a).</summary>
    internal static Classification ClassifyMcp(ActionRequest.McpTool tool)
    {
        var name = ToolWords(tool.Tool);
        List<string> described = tool.Description is null ? [] : ToolWords(tool.Description);
        var target = GrantTarget.Connector(tool.Server);
        var label = $"{tool.Server}.{tool.Tool}";
        Classification Placed(ActionClass actionClass, string reason, string field) =>
            new(actionClass, reason) { Target = label, GrantTarget = target, Field = field };

        var leaves = new List<Classification>();
        var hasRead = name.Any(w => InSet(w, ReadVerbs));
        var hasWrite = name.Any(w => InSet(w, WriteVerbs));
        // A name that starts with a read verb uses later words as things, not actions: "list_deleted_items" lists.
        // Otherwise the name's words are read in every tense. A description is prose, so only its present-tense
        // verbs count ("Sends a message", not "Lists sent messages").
        var nameTenses = !(name.Count > 0 && InSet(name[0], ReadVerbs));
        bool Strict(HashSet<string> set) => name.Any(w => InSet(w, set, nameTenses)) || described.Any(w => InSet(w, set));
        bool NameOnly(HashSet<string> set) => !hasRead && name.Any(w => InSet(w, set, tenses: true));

        var moneyVerb = Strict(MoneyVerbs);
        var moneyNoun = name.Any(w => InSet(w, PurchaseWords) || InSet(w, SubscriptionWords) || InSet(w, TransferWords) || InSet(w, ToPersonWords));
        if (moneyVerb || (moneyNoun && (hasWrite || !hasRead)))
        {
            var kind = MoneyKindOf(moneyVerb ? name.Concat(described) : name);
            leaves.Add(Placed(ActionClass.Money, "moves money", "pay") with { Money = kind == MoneyKind.None ? MoneyKind.ToPerson : kind });
        }
        if (Strict(OutwardVerbs) || (NameOnly(OutwardNouns) && !name.Contains("draft")))
            leaves.Add(Placed(ActionClass.Outward, "sends something out", "send") with { CarriesData = true });
        if (Strict(DestructiveVerbs) || NameOnly(DestructiveNouns)) leaves.Add(Placed(ActionClass.Destructive, "deletes or cancels something", "delete"));
        if (Strict(CommitmentVerbs)) leaves.Add(Placed(ActionClass.Commitment, "books or agrees to something", "commit"));
        if (Strict(InstallVerbs)) leaves.Add(Placed(ActionClass.Install, "installs something", "install"));
        if (name.Any(LoginWords.Contains)) leaves.Add(Placed(ActionClass.Login, "signs in", "sign in"));
        // An execution word counts even beside a read verb: "execute_query", "run_command_get_output" still run what
        // they are handed, and a read query can DROP or DELETE.
        if (name.Any(w => InSet(w, ExecutionWords, tenses: true))) leaves.Add(Unknown("runs code or commands it is handed") with { Target = label });

        // Annotations only ever add a stricter reading.
        if (tool.Annotations?.DestructiveHint == true) leaves.Add(Placed(ActionClass.Destructive, "the server says it is destructive", "delete"));

        if (leaves.Count == 0)
        {
            if (hasWrite || tool.Annotations?.ReadOnlyHint == false)
                leaves.Add(Placed(ActionClass.ConnectorWrite, $"writes through {tool.Server}", "write"));
            else if (hasRead)
                // A read still sends its arguments (a query, an id) to the server, so once tainted it asks (12.4).
                leaves.Add(Placed(ActionClass.ReadWeb, $"reads through {tool.Server}", "read") with { ReadsUntrusted = true, CarriesData = true });
            else
                leaves.Add(Unknown($"the tool {label} is not one the classifier can place") with { Target = label });
        }
        else if (hasWrite && leaves.All(l => l.Class is ActionClass.Login))
            leaves.Add(Placed(ActionClass.ConnectorWrite, $"writes through {tool.Server}", "write"));
        return Classification.Of(leaves);
    }

    /// <summary>The class of one of nth-assistant's own tools (15.5), with the guard the engine applies.</summary>
    internal static Classification ClassifyAssistantTool(string name)
    {
        Classification Tool(ActionClass actionClass, string reason, AssistantToolGuard guard = AssistantToolGuard.None) =>
            new(actionClass, reason) { Target = name, Guard = guard, Field = name };
        return name switch
        {
            // Tools that start a new context or hand instructions to another agent carry the Delegation guard, so a
            // tainted brain cannot launder its taint into a clean mission, chat, team or agent (12.4): the instructions
            // go as data only and the receiver starts tainted.
            "start_mission" or "spawn_chat" or "launch_hydra_team" or "message_agent" =>
                Tool(ActionClass.Agents, "directs agents", AssistantToolGuard.Delegation),
            "stop_agent" or "merge_results" or "request_helpers" or "report" or "ask_owner" or "deliver" => Tool(ActionClass.Agents, "directs agents"),
            "list_sessions" or "read_session" or "recall" or "usage_status" or "plan_budget" => Tool(ActionClass.ReadLocal, "reads nth's own state"),
            "forget" => Tool(ActionClass.Work, "deletes the assistant's own memory notes", AssistantToolGuard.MemoryDelete),
            "remember" => Tool(ActionClass.Work, "writes a memory note", AssistantToolGuard.MemoryWrite),
            "suggest" => Tool(ActionClass.Work, "adds a line to the Suggestions lane", AssistantToolGuard.Suggestion),
            "delegate_to" => Tool(ActionClass.Agents, "hands a piece to an Astur", AssistantToolGuard.Delegation),
            "schedule_duty" or "watch_folder" or "watch_page" or "create_assistant" or "request_grant" or "edit_grant" or "set_reserve_rule" =>
                Tool(ActionClass.WriteElsewhere, "changes the assistant's standing configuration", AssistantToolGuard.PermanentConfiguration),
            "propose_capability" => Tool(ActionClass.Install, "proposes a new connector, skill, server or workflow", AssistantToolGuard.PermanentConfiguration),
            _ => Unknown($"{name} is not an nth-assistant tool") with { Target = name },
        };
    }
}
