using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// PORT-ONLY (web surfaces W24, DESIGN.md 7.1, 7.3; ED-NEW-web-w24-1). A live answer on a timeline row: the view the model showed
// (nth-ui text, or one HTML page for an inline widget), kept on the row's ToolPayload so it is saved with the thread, comes back
// after a restart, and is changed in place by a later ui_update. The row that holds one is drawn as the view, not as a tool step.
// Everything is plain data (strings, numbers, flags) so it round-trips through the storage JSON without a converter registry.

/// <summary>What a live answer is made of.</summary>
public enum LiveViewKind
{
    /// <summary>nth-ui text, drawn natively.</summary>
    Ui,

    /// <summary>One HTML page, for a sandboxed inline widget.</summary>
    Html,
}

/// <summary>A live answer as the row holds it.</summary>
[JsonConverter(typeof(ToolLiveViewConverter))]
public sealed record ToolLiveView
{
    /// <summary>The longest text kept (the nth-ui parser's own body cap).</summary>
    public const int MaxTextLength = 262_144;

    /// <summary>The longest saved state kept.</summary>
    public const int MaxStateLength = 16_000;

    /// <summary>The name the model gave the view (or nth gave it); ui_update and a later ui_show find the view by it. Empty while the call is still arriving and has not named it.</summary>
    public string Id { get; init; } = "";

    public LiveViewKind Kind { get; init; }

    /// <summary>What the row is called: the view's first card or heading, or the widget's title.</summary>
    public string? Title { get; init; }

    /// <summary>The view's lines (Ui) or the page (Html), as far as they have arrived.</summary>
    public string Text { get; init; } = "";

    /// <summary>1 for the view as first shown; one more for every replacement or update.</summary>
    public int Version { get; init; } = 1;

    /// <summary>False while the text is still arriving.</summary>
    public bool Complete { get; init; }

    /// <summary>True for the lite subset (a small local model's view).</summary>
    public bool Lite { get; init; }

    /// <summary>An HTML widget's height in DIP, when the model asked for one.</summary>
    public int? Height { get; init; }

    /// <summary>The reader's own states (what they set the controls to), as a JSON object, so the view reopens as they left it.</summary>
    public string? State { get; init; }

    /// <summary>Why the call that made the view failed; the row then says so in place of the view.</summary>
    public string? Problem { get; init; }

    /// <summary>True once a later view with the same id replaced this one; the row then shrinks to a note.</summary>
    public bool Superseded { get; init; }

    public bool IsHtml => Kind == LiveViewKind.Html;
}

internal sealed class ToolLiveViewConverter : JsonConverter<ToolLiveView>
{
    public override ToolLiveView Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    private static string? OptionalString(JsonElement element, string key) =>
        SwiftDecoding.Value<string?>(element, key, field => SwiftDecoding.ReadString(field), () => null);

    // Every member is optional and a malformed member is dropped, so one bad field never costs a whole thread.
    internal static ToolLiveView Decode(JsonElement element) => new()
    {
        Id = OptionalString(element, "id") ?? "",
        Kind = OptionalString(element, "kind") == "html" ? LiveViewKind.Html : LiveViewKind.Ui,
        Title = OptionalString(element, "title"),
        Text = OptionalString(element, "text") ?? "",
        Version = (int)Math.Clamp(SwiftDecoding.Value(element, "version", SwiftDecoding.ReadInt, () => 1L), 1, int.MaxValue),
        Complete = SwiftDecoding.Value(element, "complete", SwiftDecoding.ReadBool, () => false),
        Lite = SwiftDecoding.Value(element, "lite", SwiftDecoding.ReadBool, () => false),
        Height = SwiftDecoding.Value<int?>(element, "height", field => (int)Math.Clamp(SwiftDecoding.ReadInt(field), 0, int.MaxValue), () => null),
        State = OptionalString(element, "state"),
        Problem = OptionalString(element, "problem"),
        Superseded = SwiftDecoding.Value(element, "superseded", SwiftDecoding.ReadBool, () => false),
    };

    public override void Write(Utf8JsonWriter writer, ToolLiveView value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ToolLiveView value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("kind", value.Kind == LiveViewKind.Html ? "html" : "ui");
        if (value.Title is { } title) writer.WriteString("title", title);
        writer.WriteString("text", value.Text);
        writer.WriteNumber("version", value.Version);
        if (value.Complete) writer.WriteBoolean("complete", true);
        if (value.Lite) writer.WriteBoolean("lite", true);
        if (value.Height is { } height) writer.WriteNumber("height", height);
        if (value.State is { } state) writer.WriteString("state", state);
        if (value.Problem is { } problem) writer.WriteString("problem", problem);
        if (value.Superseded) writer.WriteBoolean("superseded", true);
        writer.WriteEndObject();
    }
}
