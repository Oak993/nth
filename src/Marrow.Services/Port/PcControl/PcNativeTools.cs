// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-42; DESIGN.md 2.4, 3.4, 4.8). The nth-pc tools for the API providers
// (DeepSeek, Meta, Z.ai, OpenRouter, Local), which run nth's own tool loop instead of an MCP client. Their tool messages
// are text only, so a call answers its text for the tool message and its screenshots separately, for the session to put
// in a following user message when the model takes images; MCPHub would have turned the images into "[image]". The
// same engine, approvals, Stop and log as the MCP server; no MCP in between. Also the screenshot history rule: the last
// two screenshots stay, older ones become "[screenshot o:17 removed]", and none outlive the turn unless the setting
// "Keep screenshots in chat history" is on.
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.PcControl;

/// <summary>One call's answer: the tool message's text, the images for a model that takes them, and whether it failed.</summary>
public sealed record PcNativeResult(string Text, IReadOnlyList<PcImage> Images, bool Failed);

public sealed class PcNativeTools(PcRegistration registration, Func<PcRegistration, PcControlEngine?> engines, Func<PcSettings> settings)
{
    /// <summary>How many screenshots the history keeps (DESIGN.md 2.4).</summary>
    public const int KeptScreenshots = 2;

    /// <summary>The first words of a screenshot part's label, which marks the user message that carries screenshots.</summary>
    public const string Label = "nth-pc screenshot ";

    /// <summary>
    /// The tools for an API session, or null while Tactus is off or unbound (the request is then exactly as before).
    /// <paramref name="vision"/> is whether the session's model takes images.
    /// </summary>
    public static PcNativeTools? For(SessionConfiguration configuration, bool vision) =>
        PcControlBridge.Registration(configuration) is { } registration
            ? new PcNativeTools(registration with { Vision = vision }, PcControlBridge.Engine, () => PcControlBridge.Parts.Current()?.Settings() ?? PcSettings.Default)
            : null;

    public PcRegistration Registration => registration;

    public PcSettings Settings => Current();

    /// <summary>The function-tool definitions to add to the request; none while Tactus is off.</summary>
    public IReadOnlyList<JSONValue> Definitions
    {
        get
        {
            var now = Current();
            if (!now.Enabled) return [];
            return [.. PcToolSchemas.Listed(now).Select(spec => JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(spec.FunctionDefinition().ToJsonString()))!)];
        }
    }

    /// <summary>True for a tool name these tools offer now; anything else stays what it was.</summary>
    public bool Owns(string toolName)
    {
        var now = Current();
        return now.Enabled && PcToolSchemas.Listed(now).Any(spec => spec.Name == toolName);
    }

    /// <summary>The system prompt block (DESIGN.md 4.8, as initialize.instructions for the CLI providers); null while off.</summary>
    public string? PromptBlock() => Current().Enabled ? PcInstructions.Text : null;

    /// <summary>The timeline row's title for a call ("Tactus · Look at a window").</summary>
    public static string Title(string toolName) =>
        "Tactus · " + (PcToolSchemas.Find(toolName)?.Title ?? toolName);

    /// <summary>
    /// Runs one call on the chat's engine. A refusal or an error is a failed result with the engine's recovery hint, never
    /// an exception; the turn's cancellation cancels the call's waiting steps and then throws.
    /// </summary>
    public async Task<PcNativeResult> Execute(string toolName, JSONValue arguments, CancellationToken cancellationToken)
    {
        if (!Owns(toolName)) return new PcNativeResult("Error: " + toolName + " is not offered now (Tactus is off).", [], true);
        if (engines(registration) is not { } engine) return new PcNativeResult("Error: Tactus is not available now.", [], true);
        var args = JsonNode.Parse(arguments.CompactString) as JsonObject ?? [];
        var result = await engine.Call(toolName, args, cancellationToken).ConfigureAwait(true);
        var images = new List<PcImage>();
        if (registration.Vision)
        {
            if (result.Image is { } shot) images.Add(shot);
            images.AddRange(result.Details);
        }
        return new PcNativeResult(result.Render(), images, result.Error is not null);
    }

    /// <summary>
    /// The user message that carries a round's screenshots after its tool messages: each image follows a label that
    /// names its observation, as user attachments go (an image_url part with a data URL).
    /// </summary>
    public static JSONValue ScreenshotMessage(IReadOnlyList<(string Tool, PcImage Image)> shots)
    {
        var parts = new List<JSONValue>();
        foreach (var (tool, image) in shots)
        {
            parts.Add(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", Label + image.ObsId + " (from " + tool + ")")));
            parts.Add(JSONValue.FromDictionaryLiteral(("type", "image_url"),
                ("image_url", JSONValue.FromDictionaryLiteral(("url", "data:image/jpeg;base64," + Convert.ToBase64String(image.Jpeg))))));
        }
        return JSONValue.FromDictionaryLiteral(("role", "user"), ("content", JSONValue.FromArrayLiteral([.. parts])));
    }

    /// <summary>True for a user message <see cref="ScreenshotMessage"/> made (it is never the start of a turn).</summary>
    public static bool IsScreenshotMessage(JSONValue message) =>
        message["role"]?.JSONValueString == "user"
        && message["content"]?.JSONValueArray is { Count: > 0 } parts
        && parts.Any(part => ObservationOf(part) is not null);

    /// <summary>
    /// The history with only the newest <paramref name="keep"/> screenshots (counted by observation; a screenshot's crops go
    /// with it). Each older image becomes the text "[screenshot o:17 removed]"; a message left with no image becomes plain
    /// text. Every other message is the same value.
    /// </summary>
    public static List<JSONValue> Pruned(IReadOnlyList<JSONValue> messages, int keep)
    {
        var kept = new HashSet<string>(StringComparer.Ordinal);
        var result = new JSONValue[messages.Count];
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            result[index] = message;
            if (!IsScreenshotMessage(message)) continue;
            var parts = message["content"]!.JSONValueArray!;
            var observations = parts.Select(ObservationOf).OfType<string>().Distinct(StringComparer.Ordinal).Reverse().ToList();
            foreach (var observation in observations)
                if (kept.Count < keep) kept.Add(observation);
            if (observations.All(kept.Contains)) continue;
            var rewritten = new List<JSONValue>();
            var removed = new List<string>();
            for (var at = 0; at < parts.Count; at++)
            {
                if (ObservationOf(parts[at]) is not { } observation)
                {
                    rewritten.Add(parts[at]);
                    continue;
                }
                var image = at + 1 < parts.Count && parts[at + 1]["type"]?.JSONValueString == "image_url";
                if (kept.Contains(observation))
                {
                    rewritten.Add(parts[at]);
                    if (image) rewritten.Add(parts[at + 1]);
                }
                else if (!removed.Contains(observation))
                {
                    removed.Add(observation);
                }
                if (image) at++;
            }
            rewritten.Add(JSONValue.FromDictionaryLiteral(("type", "text"), ("text", string.Join(" ", removed.Select(o => "[screenshot " + o + " removed]")))));
            JSONValue content = rewritten.Any(part => ObservationOf(part) is not null)
                ? JSONValue.FromArrayLiteral([.. rewritten])
                : string.Join(" ", rewritten.Select(part => part["text"]?.JSONValueString).OfType<string>());
            result[index] = JSONValue.FromDictionaryLiteral(("role", "user"), ("content", content));
        }
        return [.. result];
    }

    private static string? ObservationOf(JSONValue part)
    {
        if (part["type"]?.JSONValueString != "text" || part["text"]?.JSONValueString is not { } text || !text.StartsWith(Label, StringComparison.Ordinal)) return null;
        var rest = text[Label.Length..];
        var end = rest.IndexOf(' ', StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    private PcSettings Current()
    {
        try { return settings(); }
        catch (Exception error) when (error is not OutOfMemoryException) { return PcSettings.Default; }
    }
}
