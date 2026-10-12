using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W11 (ED-NEW-web-w11-1). A scripted page behind FakeWebEngine's CDP channel, fake Visus tabs and
// cards, and a clock whose short delays pass at once while the 25 s waits hold until a test moves it.

/// <summary>Short delays (under a second) pass at once and move the clock; longer ones wait for <see cref="Advance"/>.</summary>
internal sealed class WebTestClock : IPcClock
{
    private readonly Lock gate = new();
    private readonly List<(long Due, TaskCompletionSource Done)> waiting = [];
    private long now = 1_000_000;

    public long NowMs
    {
        get
        {
            lock (gate) return now;
        }
    }

    public int Pending
    {
        get
        {
            lock (gate) return waiting.Count(w => !w.Done.Task.IsCompleted);
        }
    }

    public Task Delay(int milliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (milliseconds < 1000)
        {
            lock (gate) now += milliseconds;
            return Task.CompletedTask;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        lock (gate) waiting.Add((now + milliseconds, done));
        return done.Task;
    }

    public void Advance(int milliseconds)
    {
        List<TaskCompletionSource> due;
        lock (gate)
        {
            now += milliseconds;
            due = [.. waiting.Where(w => w.Due <= now).Select(w => w.Done)];
            waiting.RemoveAll(w => w.Due <= now);
        }
        foreach (var done in due) done.TrySetResult();
    }
}

/// <summary>One element of the scripted page: its accessibility node, its facts (what the facts script returns) and its box.</summary>
internal sealed class AgentNode
{
    public required long Backend { get; init; }
    public required string Role { get; init; }
    public required string Name { get; init; }
    public string? Value { get; set; }
    public JsonObject Facts { get; init; } = [];
    public (double X, double Y, double W, double H) Box { get; init; }
    public string? Url { get; init; }
    public List<string> Options { get; init; } = [];
    public string? Chosen { get; set; }
}

/// <summary>A page that answers the engine's CDP calls from its nodes, and records every call.</summary>
internal sealed class AgentPage
{
    public List<AgentNode> Nodes { get; } = [];
    public long Focused { get; set; }
    public long? HitOverride { get; set; }
    public List<long> Secrets { get; } = [];
    public string Text { get; set; } = "";
    public List<string> Links { get; } = [];
    public string EvalValue { get; set; } = "42";
    public List<(string Method, JsonObject Params, string? Session)> Calls { get; } = [];

    /// <summary>Where the page moves focus when a field is focused (a page steering typing elsewhere); null keeps it.</summary>
    public long? FocusTo { get; set; }

    /// <summary>Runs on every call before it is answered (a link's click navigating, say).</summary>
    public Action<string, JsonObject>? OnCall { get; set; }

    /// <summary>When set, a call to this method waits on the task (a slow step).</summary>
    public (string Method, Task Until)? Hold { get; set; }

    public IEnumerable<string> Methods(string method) => Calls.Where(c => c.Method == method).Select(c => c.Method);

    public int Count(string method) => Calls.Count(c => c.Method == method);

    public IEnumerable<JsonObject> Mouse(string type) => Calls.Where(c => c.Method == "Input.dispatchMouseEvent" && (string?)c.Params["type"] == type).Select(c => c.Params);

    public async Task<string> Answer(string method, string paramsJson, string? session)
    {
        var parameters = JsonNode.Parse(string.IsNullOrEmpty(paramsJson) ? "{}" : paramsJson) as JsonObject ?? [];
        Calls.Add((method, parameters, session));
        OnCall?.Invoke(method, parameters);
        if (Hold is { } hold && hold.Method == method) await hold.Until;
        return Handle(method, parameters);
    }

    private AgentNode? Node(JsonObject parameters)
    {
        if (parameters["backendNodeId"] is JsonValue b && b.TryGetValue<long>(out var backend)) return Nodes.FirstOrDefault(n => n.Backend == backend);
        if (parameters["objectId"] is JsonValue o && o.GetValue<string>() is { } id && id.StartsWith('n') && long.TryParse(id[1..], out var fromObject))
            return Nodes.FirstOrDefault(n => n.Backend == fromObject);
        return null;
    }

    private string Handle(string method, JsonObject p)
    {
        switch (method)
        {
            case "Accessibility.getFullAXTree":
            {
                var nodes = new JsonArray
                {
                    new JsonObject
                    {
                        ["nodeId"] = "root",
                        ["role"] = new JsonObject { ["value"] = "RootWebArea" },
                        ["name"] = new JsonObject { ["value"] = "Page" },
                        ["childIds"] = new JsonArray([.. Nodes.Select(n => (JsonNode)JsonValue.Create("n" + n.Backend.ToString(System.Globalization.CultureInfo.InvariantCulture))!)]),
                    },
                };
                foreach (var node in Nodes) nodes.Add(Ax(node));
                return new JsonObject { ["nodes"] = nodes }.ToJsonString();
            }
            case "Accessibility.getPartialAXTree":
                return Node(p) is { } partial ? new JsonObject { ["nodes"] = new JsonArray { Ax(partial) } }.ToJsonString() : "{\"nodes\":[]}";
            case "DOM.resolveNode":
                if (Node(p) is not { } resolved) throw new InvalidOperationException("No node with given id found");
                return "{\"object\":{\"type\":\"object\",\"objectId\":\"n" + resolved.Backend.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}";
            case "Runtime.callFunctionOn":
            {
                var node = Node(p) ?? throw new InvalidOperationException("Cannot find context with specified id");
                var function = (string?)p["functionDeclaration"] ?? "";
                if (function.Contains("const ga =", StringComparison.Ordinal)) return Value(node.Facts.DeepClone());
                if (function.Contains("this.focus();", StringComparison.Ordinal))
                {
                    Focused = FocusTo ?? node.Backend;
                    return Value(JsonValue.Create("field"));
                }
                if (function.Contains("function (want)", StringComparison.Ordinal))
                {
                    var want = p["arguments"]?[0]?["value"]?.GetValue<string>() ?? "";
                    var option = node.Options.FirstOrDefault(o => string.Equals(o, want, StringComparison.OrdinalIgnoreCase));
                    node.Chosen = option ?? node.Chosen;
                    return option is null ? "{\"result\":{\"type\":\"object\",\"subtype\":\"null\",\"value\":null}}" : Value(JsonValue.Create(option));
                }
                if (function.Contains("links:", StringComparison.Ordinal)) return Value(new JsonObject { ["text"] = node.Name, ["links"] = new JsonArray() });
                return "{}";
            }
            case "Runtime.evaluate":
            {
                var expression = (string?)p["expression"] ?? "";
                if (expression == "document.readyState") return Value(JsonValue.Create("complete"));
                if (expression.Contains("document.activeElement", StringComparison.Ordinal))
                    return Focused == 0 ? "{\"result\":{\"type\":\"object\",\"subtype\":\"null\"}}" : "{\"result\":{\"type\":\"object\",\"objectId\":\"n" + Focused.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}}";
                if (expression.EndsWith(".call(document)", StringComparison.Ordinal))
                    return Value(new JsonObject { ["text"] = Text, ["links"] = new JsonArray([.. Links.Select(l => (JsonNode)JsonValue.Create(l)!)]) });
                if (p["contextId"] is not null) return "{}";
                return "{\"result\":{\"type\":\"number\",\"value\":" + EvalValue + "}}";
            }
            case "DOM.getContentQuads":
            {
                if (Node(p) is not { } boxed || boxed.Box.W <= 0) return "{\"quads\":[]}";
                var (x, y, w, h) = boxed.Box;
                return new JsonObject { ["quads"] = new JsonArray { new JsonArray(x, y, x + w, y, x + w, y + h, x, y + h) } }.ToJsonString();
            }
            case "DOM.getNodeForLocation":
            {
                var px = p["x"]!.GetValue<int>();
                var py = p["y"]!.GetValue<int>();
                var hit = HitOverride is { } forced ? Nodes.FirstOrDefault(n => n.Backend == forced)
                    : Nodes.LastOrDefault(n => n.Box.W > 0 && px >= n.Box.X && px <= n.Box.X + n.Box.W && py >= n.Box.Y && py <= n.Box.Y + n.Box.H);
                if (hit is null) throw new InvalidOperationException("No node found at given location");
                return "{\"backendNodeId\":" + hit.Backend.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"frameId\":\"main\"}";
            }
            case "DOM.getDocument":
                return "{\"root\":{\"nodeId\":1,\"backendNodeId\":1}}";
            case "DOM.querySelectorAll":
                return new JsonObject { ["nodeIds"] = new JsonArray([.. Secrets.Select(s => (JsonNode)JsonValue.Create(s)!)]) }.ToJsonString();
            case "DOM.describeNode":
                return "{\"node\":{\"backendNodeId\":" + p["nodeId"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";
            case "Page.getLayoutMetrics":
                return """{"cssVisualViewport":{"clientWidth":1280,"clientHeight":800,"pageX":0,"pageY":0},"cssLayoutViewport":{"clientWidth":1280,"clientHeight":800},"cssContentSize":{"width":1280,"height":3000}}""";
            case "Page.captureScreenshot":
                return "{\"data\":\"" + Convert.ToBase64String(Jpeg(1280, 800)) + "\"}";
            default:
                return "{}";
        }
    }

    private static JsonObject Ax(AgentNode node)
    {
        var ax = new JsonObject
        {
            ["nodeId"] = "n" + node.Backend.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["role"] = new JsonObject { ["value"] = node.Role },
            ["name"] = new JsonObject { ["value"] = node.Name },
            ["backendDOMNodeId"] = node.Backend,
            ["childIds"] = new JsonArray(),
        };
        if (node.Value is not null) ax["value"] = new JsonObject { ["value"] = node.Value };
        if (node.Url is not null) ax["properties"] = new JsonArray { new JsonObject { ["name"] = "url", ["value"] = new JsonObject { ["type"] = "string", ["value"] = node.Url } } };
        return ax;
    }

    private static string Value(JsonNode? value) => new JsonObject { ["result"] = new JsonObject { ["type"] = "object", ["value"] = value } }.ToJsonString();

    /// <summary>The start of a JPEG with its frame size (enough for the size reader).</summary>
    public static byte[] Jpeg(int width, int height) =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)(height & 0xFF), (byte)(width >> 8), (byte)(width & 0xFF), 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xD9];

    public static JsonObject FactsOf(string tag, string name, string? type = null, string role = "", bool inForm = false, bool submits = false,
        string? action = null, string? method = null, string? autocomplete = null, bool editable = false, bool isSelect = false, bool? isChecked = null,
        bool contentEditable = false, string? frameUrl = "https://a.test/invite", string? href = null)
    {
        var facts = new JsonObject
        {
            ["tag"] = tag,
            ["type"] = type,
            ["role"] = role,
            ["name"] = name,
            ["inForm"] = inForm,
            ["submits"] = submits,
            ["formAction"] = action,
            ["formMethod"] = method,
            ["autocomplete"] = autocomplete,
            ["editable"] = editable,
            ["contentEditable"] = contentEditable,
            ["isSelect"] = isSelect,
            ["checked"] = isChecked,
            ["frameUrl"] = frameUrl,
            ["href"] = href,
            ["rows"] = new JsonArray { new JsonArray("To", "lee@example.com"), new JsonArray("Role", "Member") },
        };
        return facts;
    }

    /// <summary>The invite page of the canvas: an email field, a role list, Send invite, and the traps around them.</summary>
    public static AgentPage Invite()
    {
        var page = new AgentPage { Text = "Invite people to the team. Quarterly numbers: 42 apples." };
        page.Links.Add("https://b.test/docs");
        const string action = "https://a.test/invite";
        page.Nodes.AddRange(
        [
            new AgentNode { Backend = 2, Role = "textbox", Name = "Email", Box = (100, 100, 200, 30), Facts = FactsOf("input", "", "email", inForm: true, action: action, method: "post", autocomplete: "email", editable: true) },
            new AgentNode { Backend = 3, Role = "combobox", Name = "Role", Box = (100, 150, 200, 30), Options = ["Member", "Admin"], Facts = FactsOf("select", "", isSelect: true, inForm: true, action: action, method: "post") },
            new AgentNode { Backend = 4, Role = "button", Name = "Send invite", Box = (100, 200, 120, 40), Facts = FactsOf("button", "Send invite", inForm: true, submits: true, action: action, method: "post") },
            new AgentNode { Backend = 5, Role = "textbox", Name = "Password", Value = "hunter2", Box = (100, 250, 200, 30), Facts = FactsOf("input", "", "password", editable: true) },
            new AgentNode { Backend = 6, Role = "link", Name = "Docs", Url = "https://b.test/docs", Box = (100, 300, 80, 20), Facts = FactsOf("a", "Docs", href: "https://b.test/docs") },
            new AgentNode { Backend = 7, Role = "button", Name = "Buy now", Box = (100, 350, 120, 40), Facts = FactsOf("button", "Buy now") },
            new AgentNode { Backend = 8, Role = "button", Name = "भेजें", Box = (100, 400, 120, 40), Facts = FactsOf("button", "भेजें") },
            new AgentNode { Backend = 9, Role = "checkbox", Name = "Remember me", Box = (100, 450, 20, 20), Facts = FactsOf("input", "Remember me", "checkbox", isChecked: false) },
            new AgentNode { Backend = 10, Role = "button", Name = "Next", Box = (100, 500, 120, 40), Facts = FactsOf("button", "Next") },
        ]);
        page.Secrets.Add(5);
        return page;
    }
}

internal sealed class FakeAgentTab : IWebAgentTab
{
    public FakeAgentTab(string id, VisusTabKind kind, bool shared, string url, AgentPage page)
    {
        Id = id;
        Kind = kind;
        Shared = shared;
        Page = page;
        Fake = (FakeWebView)new FakeWebEngine().CreateView(new WebCreationOptions(WebProfileKind.Agent, "agent", false, "C:\\nth-test", "en-US", null, false));
        Fake.Navigate(url);
        Fake.CdpHandler = (method, parameters, session) =>
        {
            if (method.StartsWith("Input.", StringComparison.Ordinal)) InputSteps.Add(OpenSteps);
            return page.Answer(method, parameters, session);
        };
        Cdp = new CdpBroker(Fake);
    }

    public string Id { get; }
    public VisusTabKind Kind { get; }
    public bool Shared { get; set; }
    public AgentPage Page { get; set; }
    public FakeWebView Fake { get; }
    public string Url => Fake.Url ?? "";
    public string Title { get; set; } = "Invite people";
    public IWebEngineView? View => Fake;
    public CdpBroker? Cdp { get; }
    public bool IsClosed { get; set; }
    public bool Loading { get; set; }
    public int Steps { get; private set; }

    /// <summary>How many model steps were open on the tab when each input event reached the page.</summary>
    public List<int> InputSteps { get; } = [];
    public int OpenSteps { get; private set; }
    public List<string> Navigations { get; } = [];
    public string? BackUrl { get; set; }

    public event Action? UserInput;

    public Task<bool> MakeLiveAsync(CancellationToken cancellationToken) => Task.FromResult(!IsClosed);

    /// <summary>A navigation as WebView2 runs one: it starts (its handlers may cancel it), then completes.</summary>
    public void Navigate(string url)
    {
        Navigations.Add(url);
        var starting = Fake.RaiseNavigationStarting(url, userInitiated: false);
        if (!starting.Cancel) Fake.RaiseNavigationCompleted(url);
    }

    public void GoBack()
    {
        if (BackUrl is { } back) Navigate(back);
    }

    public void GoForward()
    {
    }

    public void Reload() => Navigate(Url);

    public IDisposable BeginModelStep()
    {
        Steps++;
        OpenSteps++;
        return new Ending(() => OpenSteps--);
    }

    public void RaiseUserInput() => UserInput?.Invoke();

    public event Action<string>? StepFailed;

    public void RaiseStepFailed(string reason) => StepFailed?.Invoke(reason);

    private sealed class Ending(Action end) : IDisposable
    {
        private Action? end = end;

        public void Dispose() => Interlocked.Exchange(ref end, null)?.Invoke();
    }
}

internal sealed class FakeAgentHost : IWebAgentHost
{
    public List<FakeAgentTab> All { get; } = [];
    public Func<string, AgentPage> PageFor { get; set; } = _ => AgentPage.Invite();
    public List<string> Conversation { get; } = [];
    public List<string> Opened { get; } = [];

    public IReadOnlyList<IWebAgentTab> Tabs(string thread) => [.. All.Where(t => !t.IsClosed && t.Kind != VisusTabKind.Private && (t.Kind == VisusTabKind.Agent || t.Shared))];

    public Task<IWebAgentTab?> OpenAgentTab(string thread, string url, CancellationToken cancellationToken)
    {
        Opened.Add(url);
        var tab = new FakeAgentTab("visus-" + (All.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), VisusTabKind.Agent, false, url, PageFor(url));
        All.Add(tab);
        return Task.FromResult<IWebAgentTab?>(tab);
    }

    public Task<T> OnUi<T>(Func<Task<T>> work) => work();

    public bool InConversation(string thread, string url) => Conversation.Any(c => url.StartsWith(c, StringComparison.OrdinalIgnoreCase));

    public string ModelName(string? model) => "Claude Sonnet";

    public string? ChatTitle(string thread) => "Team invites";

    public FakeAgentTab Add(string url, VisusTabKind kind = VisusTabKind.Agent, bool shared = false, AgentPage? page = null)
    {
        var tab = new FakeAgentTab("visus-" + (All.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), kind, shared, url, page ?? PageFor(url));
        All.Add(tab);
        return tab;
    }
}

internal sealed class FakeAgentAsker : IWebAgentAsker
{
    public List<object> Asked { get; } = [];
    public List<CancellationToken> Tokens { get; } = [];
    public Func<WebSiteAsk, Task<WebSiteAnswer>> Site { get; set; } = _ => Task.FromResult(WebSiteAnswer.AllowOnce);
    public Func<WebNavigateAsk, Task<WebNavigateAnswer>> Navigate { get; set; } = _ => Task.FromResult(WebNavigateAnswer.AllowOnce);
    public Func<WebGuardAsk, Task<bool>> Guard { get; set; } = _ => Task.FromResult(true);
    public Func<WebEvalAsk, Task<bool>> Script { get; set; } = _ => Task.FromResult(true);

    public IEnumerable<T> Of<T>() => Asked.OfType<T>();

    public Task<WebSiteAnswer> AskSite(WebSiteAsk ask, CancellationToken cancellationToken) => Record(ask, cancellationToken, Site);

    public Task<WebNavigateAnswer> AskNavigate(WebNavigateAsk ask, CancellationToken cancellationToken) => Record(ask, cancellationToken, Navigate);

    public Task<bool> Confirm(WebGuardAsk ask, CancellationToken cancellationToken) => Record(ask, cancellationToken, Guard);

    public Task<bool> AskEval(WebEvalAsk ask, CancellationToken cancellationToken) => Record(ask, cancellationToken, Script);

    private Task<TAnswer> Record<TAsk, TAnswer>(TAsk ask, CancellationToken cancellationToken, Func<TAsk, Task<TAnswer>> answer) where TAsk : notnull
    {
        Asked.Add(ask);
        Tokens.Add(cancellationToken);
        return answer(ask);
    }
}

/// <summary>One chat's web tools over the fakes.</summary>
internal sealed class WebAgentRig : IDisposable
{
    public const string Thread = "chat-1";

    /// <param name="tactusPause">Bind a Tactus pause control (as when Tactus is on), so the pill's pause and rows cover Visus.</param>
    public WebAgentRig(bool tactusPause = false)
    {
        if (tactusPause) Pause = new PcPauseControl(Clock, Run);
        Hub = new WebAgentHub(() => Run, () => Pause, Clock, () => "Ctrl+Alt+Esc");
        Hub.Bind(Host, Asker, Grants, () => Settings);
        Hub.Activity += a => Activity.Add(a);
    }

    public WebTestClock Clock { get; } = new();
    public PcRunControl Run { get; } = new();
    public PcPauseControl? Pause { get; }
    public FakeAgentHost Host { get; } = new();
    public FakeAgentAsker Asker { get; } = new();
    public WebAgentGrants Grants { get; } = new();
    public WebAgentSettings Settings { get; set; } = WebAgentSettings.Default;
    public WebAgentHub Hub { get; }
    public List<WebAgentActivity> Activity { get; } = [];
    public bool Vision { get; set; } = true;

    public WebAgentEngine Engine => Hub.Engine(Thread)!;

    public Task<WebToolResult> Start(string tool, string json = "{}", CancellationToken cancellationToken = default) =>
        Engine.Call(tool, JsonNode.Parse(json) as JsonObject, new WebAgentCaller(Thread, "claude-sonnet", Vision), cancellationToken);

    public async Task<WebToolResult> Call(string tool, string json = "{}")
    {
        var call = Start(tool, json);
        var finished = await Task.WhenAny(call, Task.Delay(5000));
        Assert.True(ReferenceEquals(finished, call), tool + " did not finish");
        return await call;
    }

    /// <summary>An agent tab on the invite page, known to the engine as t1 (listed once).</summary>
    public async Task<FakeAgentTab> InviteTab(string url = "https://a.test/invite", bool shared = false, VisusTabKind kind = VisusTabKind.Agent)
    {
        var tab = Host.Add(url, kind, shared);
        await Call(WebToolSchemas.Tabs);
        return tab;
    }

    public void Dispose()
    {
        Hub.Forget(Thread);
        Pause?.Dispose();
    }
}
