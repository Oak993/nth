// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2; ED-NEW-web-w12-1). The four artifact tools every model gets, in one
// place for the nth-ui MCP server (CLI providers) and the native tool loop (API providers): their names, descriptions
// and input schemas, how their arguments are read (leniently: a small model's files or edits sent as a JSON string, a
// version as "v3"), and what the model reads back. Our own words; nothing here is copied from another product. No Swift
// counterpart.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Services.Web;

/// <summary>What one artifact tool call hands back: the text the model reads, whether it failed, and its details as structured content.</summary>
public sealed record ArtifactToolResult(string Text, bool Failed, JsonObject? Structured);

public static class ArtifactTools
{
    public const string CreateName = "artifact_create";
    public const string UpdateName = "artifact_update";
    public const string ReadName = "artifact_read";
    public const string ListName = "artifact_list";

    /// <summary>The timeline row's title (the panel's name, DESIGN.md 15).</summary>
    public const string Title = "Tabula";

    /// <summary>artifact_read returns at most this many characters of content.</summary>
    public const int MaxReadCharacters = 400_000;

    public static IReadOnlyList<string> Names { get; } = [CreateName, UpdateName, ReadName, ListName];

    public static bool IsName(string? name) => name is CreateName or UpdateName or ReadName or ListName;

    public static string Description(string name) => name switch
    {
        CreateName =>
            "Create an artifact: something the user keeps and reuses beside the chat, shown in nth's Tabula pane, with every change kept as a version. "
            + "Use it for a page or small app (kind html), a React component (react), an SVG image (svg), a Mermaid diagram (mermaid), a document (markdown), "
            + "a program or a long code file (code, with language), a live view (nth-ui), or several files that work together (project: files and an entry, index.html when left out). "
            + "Keep short answers and small snippets in the chat. Returns the artifact's id; change it later with artifact_update.",
        UpdateName =>
            "Change an artifact; every call makes a new version. Pass exactly one of: content (the whole new content); "
            + "edits (find and replace pairs applied in order, each find copied exactly from the current version and matching exactly once; add path for a project's file); "
            + "or files (a project's changed files: a path to its new content, or to null to delete the file). Prefer edits for small changes.",
        ReadName => "Read an artifact's content and details: the latest version, or the version you name. For a project, path reads one of its files.",
        ListName => "List this chat's artifacts: their ids, titles, kinds and versions.",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>The tool's input schema (a fresh node each call).</summary>
    public static JsonObject InputSchema(string name) => (JsonObject)JsonNode.Parse(SchemaText(name))!;

    /// <summary>The OpenAI-style function definition the API providers' tool loop sends.</summary>
    public static JSONValue FunctionDefinition(string name) => JSONValue.FromDictionaryLiteral(
        ("type", "function"),
        ("function", JSONValue.FromDictionaryLiteral(("name", name), ("description", Description(name)), ("parameters", JSONValue.Parse(SchemaText(name))!))));

    private static string SchemaText(string name) => name switch
    {
        CreateName => """
            {"type":"object","properties":{
            "kind":{"type":"string","enum":["html","react","svg","mermaid","markdown","code","nth-ui","project"],"description":"What the artifact is."},
            "title":{"type":"string","description":"A short title the user sees, like Pricing calculator."},
            "content":{"type":"string","description":"The whole content, for every kind but project."},
            "id":{"type":"string","description":"Optional short id of letters, digits and hyphens; made from the title when left out."},
            "language":{"type":"string","description":"For code: the language, like python. For react: tsx when the component is TypeScript."},
            "files":{"type":"object","additionalProperties":{"type":"string"},"description":"For a project: every file, its path relative to the project mapped to its content."},
            "entry":{"type":"string","description":"For a project: the file that opens first; index.html when left out."}},
            "required":["kind","title"]}
            """,
        UpdateName => """
            {"type":"object","properties":{
            "id":{"type":"string","description":"The artifact's id."},
            "content":{"type":"string","description":"The whole new content (not for a project)."},
            "edits":{"type":"array","description":"Find and replace pairs, applied in order.","items":{"type":"object","properties":{
            "find":{"type":"string","description":"Text copied exactly from the current version; it must match exactly once."},
            "replace":{"type":"string","description":"The text to put in its place."},
            "path":{"type":"string","description":"For a project: the file to edit."}},"required":["find","replace"]}},
            "files":{"type":"object","additionalProperties":{"type":["string","null"]},"description":"For a project: changed files, a path mapped to its new content or to null to delete it."},
            "entry":{"type":"string","description":"For a project: a new entry file."},
            "title":{"type":"string","description":"A new title, when it should change."}},
            "required":["id"]}
            """,
        ReadName => """
            {"type":"object","properties":{
            "id":{"type":"string","description":"The artifact's id."},
            "version":{"type":"integer","description":"The version to read; the latest when left out."},
            "path":{"type":"string","description":"For a project: read only this file."}},
            "required":["id"]}
            """,
        ListName => """{"type":"object","properties":{}}""",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>The timeline row's title for a call, known before it runs: "Tabula · Pricing calculator" when the call names a title or an id.</summary>
    public static string RowTitle(string name, JsonNode? arguments)
    {
        var subject = name is CreateName or UpdateName ? Text(arguments, "title") ?? Text(arguments, "id") : Text(arguments, "id");
        return subject is { } named && named.Trim().Length > 0 ? Title + " · " + ArtifactStore.CleanTitle(named) : Title;
    }

    /// <summary>Runs one tool against the chat's artifacts. A refusal is a failed result the model can act on, never a throw.</summary>
    public static ArtifactToolResult Run(ArtifactStore store, Guid thread, string name, JsonNode? arguments, ArtifactAuthor author, string? prompt)
    {
        try
        {
            return name switch
            {
                CreateName => Create(store, thread, arguments, author, prompt),
                UpdateName => Update(store, thread, arguments, author, prompt),
                ReadName => Read(store, thread, arguments),
                ListName => List(store, thread),
                _ => Failure($"{name} is not an artifact tool."),
            };
        }
        catch (ArtifactException error) { return Failure(error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return Failure("The artifact could not be saved: " + error.Message); }
    }

    private static ArtifactToolResult Create(ArtifactStore store, Guid thread, JsonNode? arguments, ArtifactAuthor author, string? prompt)
    {
        var files = Files(arguments, allowNull: false);
        var kindText = Text(arguments, "kind");
        var kind = ArtifactKinds.Parse(kindText) ?? (kindText is null && files is not null ? ArtifactKind.Project : (ArtifactKind?)null)
            ?? throw new ArtifactException($"kind must be one of: {string.Join(", ", ArtifactKinds.WireNames)}.");
        var title = Text(arguments, "title") is { } given && given.Trim().Length > 0 ? given : throw new ArtifactException("A title is required.");
        var snapshot = store.Create(thread, new ArtifactDraft
        {
            Kind = kind,
            Title = title,
            Id = Text(arguments, "id"),
            Language = Text(arguments, "language"),
            Content = Text(arguments, "content"),
            Files = files?.ToDictionary(file => file.Key, file => file.Value!, StringComparer.Ordinal),
            Entry = Text(arguments, "entry"),
        }, author, prompt);
        var artifact = snapshot.Artifact;
        return new ArtifactToolResult(
            $"Created \"{artifact.Id}\" ({ArtifactKinds.DisplayName(artifact.Kind, artifact.Language)}), v1. It is open in Tabula. To change it, call {UpdateName} with id \"{artifact.Id}\".",
            false, Details(snapshot));
    }

    private static ArtifactToolResult Update(ArtifactStore store, Guid thread, JsonNode? arguments, ArtifactAuthor author, string? prompt)
    {
        var id = Text(arguments, "id") is { } given && given.Trim().Length > 0 ? given : throw new ArtifactException("The artifact's id is required.");
        var before = store.Find(thread, id);
        var snapshot = store.Update(thread, id, new ArtifactRevision
        {
            Content = Text(arguments, "content"),
            Edits = Edits(arguments),
            Files = Files(arguments, allowNull: true),
            Entry = Text(arguments, "entry"),
            Title = Text(arguments, "title"),
        }, author, prompt);
        var artifact = snapshot.Artifact;
        var renamed = before is not null && !string.Equals(before.Title, artifact.Title, StringComparison.Ordinal) ? $" Its title is now \"{artifact.Title}\"." : "";
        return new ArtifactToolResult($"Updated \"{artifact.Id}\" to v{snapshot.Version.Number}.{renamed}", false, Details(snapshot));
    }

    private static ArtifactToolResult Read(ArtifactStore store, Guid thread, JsonNode? arguments)
    {
        var id = Text(arguments, "id") is { } given && given.Trim().Length > 0 ? given : throw new ArtifactException("The artifact's id is required.");
        var snapshot = store.Read(thread, id, Version(arguments));
        var (artifact, version) = (snapshot.Artifact, snapshot.Version);
        var text = new StringBuilder();
        text.Append("id: ").Append(artifact.Id).Append('\n');
        text.Append("title: ").Append(artifact.Title).Append('\n');
        text.Append("kind: ").Append(artifact.Kind.WireName()).Append(artifact.Language is { } language ? " (" + language + ")" : "").Append('\n');
        text.Append("version: v").Append(Number(version.Number)).Append(version.Number == artifact.Current ? " (latest)" : " (the latest is v" + Number(artifact.Current) + ")").Append('\n');
        text.Append("by: ").Append(Author(version.Author)).Append('\n');
        if (version.Prompt is { } asked) text.Append("prompt: ").Append(asked.Replace('\n', ' ')).Append('\n');
        if (snapshot.Files is { } files)
        {
            text.Append("entry: ").Append(version.Entry).Append('\n');
            text.Append("files: ").Append(string.Join(", ", files.Keys)).Append('\n');
            if (Text(arguments, "path") is { Length: > 0 } wanted)
            {
                var path = ArtifactStore.ProjectPath(wanted);
                var match = files.Keys.FirstOrDefault(key => string.Equals(key, path, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArtifactException($"\"{path}\" is not a file of v{version.Number}; its files are {string.Join(", ", files.Keys)}.");
                text.Append("\n--- ").Append(match).Append(" ---\n").Append(files[match]);
            }
            else
                foreach (var (path, content) in files) text.Append("\n--- ").Append(path).Append(" ---\n").Append(content).Append(content.EndsWith('\n') ? "" : "\n");
        }
        else text.Append('\n').Append(snapshot.Text);
        var output = text.ToString();
        if (output.Length > MaxReadCharacters)
            output = output[..MaxReadCharacters] + "\n[cut here: the content is longer than " + MaxReadCharacters.ToString("N0", CultureInfo.InvariantCulture) + " characters"
                + (snapshot.Files is null ? "]" : "; read one file with path]");
        return new ArtifactToolResult(output, false, Details(snapshot));
    }

    private static ArtifactToolResult List(ArtifactStore store, Guid thread)
    {
        var artifacts = store.List(thread);
        var rows = new JsonArray();
        foreach (var artifact in artifacts) rows.Add(Summary(artifact));
        if (artifacts.Count == 0) return new ArtifactToolResult("This chat has no artifacts yet.", false, new JsonObject { ["artifacts"] = rows });
        var text = new StringBuilder(artifacts.Count == 1 ? "1 artifact in this chat, newest change first:" : $"{Number(artifacts.Count)} artifacts in this chat, newest change first:");
        foreach (var artifact in artifacts)
            text.Append("\n- ").Append(artifact.Id).Append(" · ").Append(artifact.Kind.WireName()).Append(" · \"").Append(artifact.Title).Append("\" · v").Append(Number(artifact.Current));
        return new ArtifactToolResult(text.ToString(), false, new JsonObject { ["artifacts"] = rows });
    }

    private static ArtifactToolResult Failure(string message) => new("Error: " + message, true, null);

    private static string Author(ArtifactAuthor author) => author.IsYou ? "you" : string.Join(" · ", new[] { author.Provider, author.Model }.OfType<string>());

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static JsonObject Summary(Artifact artifact) => new()
    {
        ["id"] = artifact.Id,
        ["title"] = artifact.Title,
        ["kind"] = artifact.Kind.WireName(),
        ["version"] = artifact.Current,
        ["updated"] = artifact.Updated.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
    };

    private static JsonObject Details(ArtifactSnapshot snapshot)
    {
        var details = Summary(snapshot.Artifact);
        details["version"] = snapshot.Version.Number;
        details["latest"] = snapshot.Artifact.Current;
        if (snapshot.Artifact.Language is { } language) details["language"] = language;
        if (snapshot.Files is { } files)
        {
            details["entry"] = snapshot.Version.Entry;
            var paths = new JsonArray();
            foreach (var path in files.Keys) paths.Add(path);
            details["files"] = paths;
        }
        return details;
    }

    // ---- lenient argument reading ----

    private static string? Text(JsonNode? arguments, string key) => arguments is JsonObject map && map.TryGetPropertyValue(key, out var value) ? Scalar(value) : null;

    private static string? Scalar(JsonNode? value) => value is JsonValue scalar
        ? scalar.GetValueKind() switch
        {
            JsonValueKind.String => scalar.GetValue<string>(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => scalar.ToJsonString(),
            _ => null,
        }
        : null;

    /// <summary>A JSON object, or a string holding one (some models send nested objects as text).</summary>
    private static JsonNode? Structured(JsonNode? arguments, string key)
    {
        if (arguments is not JsonObject map || !map.TryGetPropertyValue(key, out var value) || value is null) return null;
        if (value is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String)
        {
            var text = scalar.GetValue<string>().Trim();
            if (text.Length == 0) return null;
            try { return JsonNode.Parse(text); }
            catch (JsonException) { throw new ArtifactException($"{key} is not valid JSON."); }
        }
        return value;
    }

    private static Dictionary<string, string?>? Files(JsonNode? arguments, bool allowNull)
    {
        if (Structured(arguments, "files") is not { } node) return null;
        if (node is not JsonObject map) throw new ArtifactException("files must be an object that maps each path to its content.");
        var files = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (path, value) in map)
        {
            if (value is null)
            {
                if (!allowNull) throw new ArtifactException($"\"{path}\" has no content.");
                files[path] = null;
            }
            else files[path] = Scalar(value) ?? throw new ArtifactException($"The content of \"{path}\" must be text.");
        }
        return files;
    }

    private static List<ArtifactTextEdit>? Edits(JsonNode? arguments)
    {
        if (Structured(arguments, "edits") is not { } node) return null;
        var items = node switch
        {
            JsonArray array => array.ToList(),
            JsonObject single => new List<JsonNode?> { single },
            _ => throw new ArtifactException("edits must be a list of {find, replace} pairs."),
        };
        var edits = new List<ArtifactTextEdit>();
        foreach (var item in items)
        {
            if (item is not JsonObject edit || Text(edit, "find") is not { } find || Text(edit, "replace") is not { } replace)
                throw new ArtifactException("Each edit needs find and replace.");
            edits.Add(new ArtifactTextEdit(find, replace, Text(edit, "path")));
        }
        return edits;
    }

    private static int? Version(JsonNode? arguments)
    {
        if (Text(arguments, "version") is not { } text || text.Trim().Length == 0) return null;
        var digits = text.Trim().TrimStart('v', 'V');
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number : throw new ArtifactException($"\"{text}\" is not a version number.");
    }
}
