using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

/// <summary>
/// W16c test rig: the hidden diagram renderer over the fake web engine. It plays the UI host (attaches the platform view when the
/// engine opens one and reports the page loaded) and the page (answers the readiness probe and each drawing from a script), so a
/// test reads exactly what nth asked of the page and in what order. Time is the web rig's manual clock.
/// </summary>
internal sealed class DiagramRig : IDisposable
{
    public const string Drawing = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"5\" height=\"5\"/></svg>";

    public DiagramRig(string? installed = "141.0.3537.71", ArtifactLibraryFiles? libraries = null)
    {
        Web = new WebRig(installed);
        Engine = new MermaidRenderEngine(Web.Surface, libraries ?? ArtifactRuntimeRig.Libraries, Web.Time, static work => work());
        Engine.HostedChanged += OnHosted;
    }

    public WebRig Web { get; }

    public MermaidRenderEngine Engine { get; }

    /// <summary>False leaves the engine's view without a platform view, as when no window hosts it.</summary>
    public bool Host { get; set; } = true;

    /// <summary>False leaves the page unloaded.</summary>
    public bool Load { get; set; } = true;

    /// <summary>Whether the page reports Mermaid loaded.</summary>
    public bool MermaidLoaded { get; set; } = true;

    /// <summary>The expressions of the drawings the page was asked for, oldest first.</summary>
    public List<string> Drawings { get; } = [];

    /// <summary>Every Runtime.evaluate expression, probe included.</summary>
    public List<string> Expressions { get; } = [];

    /// <summary>The page's answer to a drawing (its JSON); null answers a good drawing.</summary>
    public Func<string, Task<string>>? Page { get; set; }

    public int Hosted { get; private set; }

    public int Unhosted { get; private set; }

    public FakeWebView? Fake => Engine.View is { } view ? WebRig.Fake(view) : null;

    private void OnHosted()
    {
        if (Engine.View is not { } view)
        {
            Unhosted++;
            return;
        }
        Hosted++;
        var fake = WebRig.Fake(view);
        fake.CdpHandler = Handle;
        if (!Host || view.Liveness != WebLiveness.Pending) return;
        view.AttachNative(1, 2);
        if (Load) fake.RaiseNavigationCompleted(MermaidRenderSite.Address);
    }

    private Task<string> Handle(string method, string parameters, string? session)
    {
        if (method != "Runtime.evaluate") return Task.FromResult("{}");
        var expression = JsonNode.Parse(parameters)!["expression"]!.GetValue<string>();
        Expressions.Add(expression);
        if (expression.StartsWith("window.nthDiagramReady", StringComparison.Ordinal))
            return Task.FromResult($"{{\"result\":{{\"type\":\"boolean\",\"value\":{(MermaidLoaded ? "true" : "false")}}}}}");
        Drawings.Add(expression);
        return Page?.Invoke(expression) ?? Task.FromResult(Good());
    }

    public static string Good(string svg = Drawing) =>
        new JsonObject { ["result"] = new JsonObject { ["type"] = "object", ["value"] = new JsonObject { ["ok"] = true, ["svg"] = svg } } }.ToJsonString();

    public static string Failed(string kind, string error, int? line = null)
    {
        var value = new JsonObject { ["ok"] = false, ["kind"] = kind, ["error"] = error };
        if (line is { } n) value["line"] = n;
        return new JsonObject { ["result"] = new JsonObject { ["type"] = "object", ["value"] = value } }.ToJsonString();
    }

    public static DiagramTheme Theme(bool dark = false) => new(dark, "Segoe UI", 14, new Dictionary<string, string> { ["primaryColor"] = dark ? "#2a2a2f" : "#ffffff", ["lineColor"] = "#6f6f72" });

    public static DiagramRenderRequest Request(string source = "graph LR\n  A --> B", bool dark = false) => new(source, Theme(dark));

    /// <summary>The expression's source argument, read back from the JSON in it.</summary>
    public static string SourceOf(string expression)
    {
        var begin = expression.IndexOf('(', StringComparison.Ordinal) + 1;
        using var reader = JsonDocument.Parse(ExtractFirstJson(expression[begin..]));
        return reader.RootElement.GetString()!;
    }

    private static string ExtractFirstJson(string rest)
    {
        // The source is the first JSON string of the call: from the opening quote to its closing, honouring escapes.
        var end = 1;
        while (end < rest.Length)
        {
            if (rest[end] == '\\') end += 2;
            else if (rest[end] == '"') break;
            else end++;
        }
        return rest[..(end + 1)];
    }

    /// <summary>The options object of a drawing's expression.</summary>
    public static JsonNode Options(string expression)
    {
        var begin = expression.IndexOf('(', StringComparison.Ordinal) + 1;
        var first = ExtractFirstJson(expression[begin..]);
        var after = expression[(begin + first.Length + 1)..^1];
        return JsonNode.Parse(after)!;
    }

    public void Dispose()
    {
        Engine.Dispose();
        Web.Dispose();
    }
}
