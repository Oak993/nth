using System.Globalization;
using System.Text;
using System.Text.Json;

// PORT-ONLY: web surfaces W15, the nth-ui language (E:/Droppy/work/design/web-surfaces/DESIGN.md 7.2; ED-NEW-web-w15-1;
// owner 2026-10-11 "Web Surfaces Approved"). No Swift counterpart. The values a live view's expressions compute: a
// number, a text, a bool, a list, an object (only from an object literal), null, or an error that carries why. Values
// are immutable and bounded (lists to MaxListLength items, texts to MaxTextLength characters), so no expression can
// build something huge.

namespace Marrow.Core.NthUi;

public enum NthUiValueKind
{
    Null,
    Number,
    Text,
    Bool,
    List,
    Object,
    Error,
}

/// <summary>One value of the nth-ui expression language (DESIGN 7.2). Never throws; bad input becomes an error value.</summary>
public readonly struct NthUiValue : IEquatable<NthUiValue>
{
    public const int MaxListLength = 10_000;
    public const int MaxTextLength = 20_000;
    private const int MaxJsonDepth = 16;

    private readonly object? payload;
    private readonly double number;

    private NthUiValue(NthUiValueKind kind, double number, object? payload)
    {
        Kind = kind;
        this.number = number;
        this.payload = payload;
    }

    public NthUiValueKind Kind { get; }

    public static NthUiValue Null => default;

    public static NthUiValue True { get; } = new(NthUiValueKind.Bool, 1, null);

    public static NthUiValue False { get; } = new(NthUiValueKind.Bool, 0, null);

    public static NthUiValue Of(double value) =>
        double.IsFinite(value) ? new(NthUiValueKind.Number, value == 0 ? 0 : value, null) : Error("the result is not a finite number");

    public static NthUiValue Of(bool value) => value ? True : False;

    public static NthUiValue Of(string value) =>
        value.Length > MaxTextLength ? Error("the text is too long") : new(NthUiValueKind.Text, 0, value);

    public static NthUiValue List(IEnumerable<NthUiValue> items)
    {
        var array = items as NthUiValue[] ?? items.Take(MaxListLength + 1).ToArray();
        return array.Length > MaxListLength ? Error("the list is too long") : new(NthUiValueKind.List, 0, array);
    }

    public static NthUiValue Object(IEnumerable<KeyValuePair<string, NthUiValue>> fields)
    {
        var list = new List<KeyValuePair<string, NthUiValue>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (index.TryGetValue(field.Key, out var at)) list[at] = field;
            else
            {
                index[field.Key] = list.Count;
                list.Add(field);
            }
            if (list.Count > MaxListLength) return Error("the object is too large");
        }
        return new(NthUiValueKind.Object, 0, list.ToArray());
    }

    public static NthUiValue Error(string message) => new(NthUiValueKind.Error, 0, message);

    public bool IsNull => Kind == NthUiValueKind.Null;

    public bool IsNumber => Kind == NthUiValueKind.Number;

    public bool IsText => Kind == NthUiValueKind.Text;

    public bool IsBool => Kind == NthUiValueKind.Bool;

    public bool IsList => Kind == NthUiValueKind.List;

    public bool IsObject => Kind == NthUiValueKind.Object;

    public bool IsError => Kind == NthUiValueKind.Error;

    /// <summary>The number of a number or a bool (1 or 0); 0 for anything else.</summary>
    public double AsNumber => Kind is NthUiValueKind.Number or NthUiValueKind.Bool ? number : 0;

    public bool AsBool => Kind == NthUiValueKind.Bool && number != 0;

    /// <summary>The text of a text value, or an error's message; empty for anything else.</summary>
    public string AsText => payload as string ?? "";

    public IReadOnlyList<NthUiValue> Items => payload as NthUiValue[] ?? [];

    public IReadOnlyList<KeyValuePair<string, NthUiValue>> Fields => payload as KeyValuePair<string, NthUiValue>[] ?? [];

    public bool TryGetField(string name, out NthUiValue value)
    {
        foreach (var field in Fields)
        {
            if (!string.Equals(field.Key, name, StringComparison.Ordinal)) continue;
            value = field.Value;
            return true;
        }
        value = Null;
        return false;
    }

    /// <summary>false, null, 0, "", an empty list or object, and errors are false; everything else is true.</summary>
    public bool IsTruthy => Kind switch
    {
        NthUiValueKind.Null or NthUiValueKind.Error => false,
        NthUiValueKind.Number or NthUiValueKind.Bool => number != 0,
        NthUiValueKind.Text => AsText.Length > 0,
        NthUiValueKind.List => Items.Count > 0,
        NthUiValueKind.Object => Fields.Count > 0,
    };

    /// <summary>The name of the kind as the language documents it ("number", "text", ...).</summary>
    public string TypeName => Kind switch
    {
        NthUiValueKind.Null => "null",
        NthUiValueKind.Number => "number",
        NthUiValueKind.Text => "text",
        NthUiValueKind.Bool => "bool",
        NthUiValueKind.List => "list",
        NthUiValueKind.Object => "object",
        NthUiValueKind.Error => "error",
    };

    /// <summary>How the value reads in a view: numbers without noise digits, lists joined by ", ", errors as an em dash.</summary>
    public string Display(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        return Kind switch
        {
            NthUiValueKind.Null => "",
            NthUiValueKind.Number => FormatNumber(number, culture),
            NthUiValueKind.Text => AsText,
            NthUiValueKind.Bool => number != 0 ? "true" : "false",
            NthUiValueKind.List => Clip(string.Join(", ", Items.Select(i => i.Display(culture)))),
            NthUiValueKind.Object => Clip(ToJson()),
            NthUiValueKind.Error => "\u2014",
        };
    }

    /// <summary>A plain number for display: integers without decimals, others to at most ten decimals, trailing zeros cut.</summary>
    public static string FormatNumber(double value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        if (!double.IsFinite(value)) return "\u2014";
        var magnitude = Math.Abs(value);
        if (magnitude >= 1e15 || (magnitude > 0 && magnitude < 1e-10)) return value.ToString("0.#####E+0", culture);
        var rounded = Math.Round(value, 10, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "0" : rounded.ToString("0.##########", culture);
    }

    public override string ToString() => Display();

    public bool Equals(NthUiValue other)
    {
        if (Kind != other.Kind) return false;
        switch (Kind)
        {
            case NthUiValueKind.Null:
                return true;
            case NthUiValueKind.Number:
            case NthUiValueKind.Bool:
                return number == other.number;
            case NthUiValueKind.Text:
            case NthUiValueKind.Error:
                return string.Equals(AsText, other.AsText, StringComparison.Ordinal);
            case NthUiValueKind.List:
                return Items.SequenceEqual(other.Items);
            case NthUiValueKind.Object:
                return Fields.Count == other.Fields.Count && Fields.All(f => other.TryGetField(f.Key, out var v) && v.Equals(f.Value));
        }
        return false;
    }

    public override bool Equals(object? obj) => obj is NthUiValue other && Equals(other);

    public override int GetHashCode() => Kind switch
    {
        NthUiValueKind.Number or NthUiValueKind.Bool => HashCode.Combine(Kind, number),
        NthUiValueKind.Text or NthUiValueKind.Error => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(AsText)),
        NthUiValueKind.List => HashCode.Combine(Kind, Items.Count),
        NthUiValueKind.Object => HashCode.Combine(Kind, Fields.Count),
        NthUiValueKind.Null => 0,
    };

    public static bool operator ==(NthUiValue left, NthUiValue right) => left.Equals(right);

    public static bool operator !=(NthUiValue left, NthUiValue right) => !left.Equals(right);

    /// <summary>The value as JSON (errors as null), for saved state, context and tool arguments.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteJson(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public void WriteJson(Utf8JsonWriter writer)
    {
        switch (Kind)
        {
            case NthUiValueKind.Number:
                writer.WriteNumberValue(number);
                break;
            case NthUiValueKind.Text:
                writer.WriteStringValue(AsText);
                break;
            case NthUiValueKind.Bool:
                writer.WriteBooleanValue(number != 0);
                break;
            case NthUiValueKind.List:
                writer.WriteStartArray();
                foreach (var item in Items) item.WriteJson(writer);
                writer.WriteEndArray();
                break;
            case NthUiValueKind.Object:
                writer.WriteStartObject();
                foreach (var field in Fields)
                {
                    writer.WritePropertyName(field.Key);
                    field.Value.WriteJson(writer);
                }
                writer.WriteEndObject();
                break;
            case NthUiValueKind.Null:
            case NthUiValueKind.Error:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>Reads JSON back into a value; anything unreadable or too deep becomes an error value.</summary>
    public static NthUiValue FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxJsonDepth + 2 });
            return FromJson(document.RootElement);
        }
        catch (JsonException)
        {
            return Error("the JSON could not be read");
        }
    }

    public static NthUiValue FromJson(JsonElement element, int depth = 0)
    {
        if (depth > MaxJsonDepth) return Error("the JSON is nested too deeply");
        return element.ValueKind switch
        {
            JsonValueKind.Number => Of(element.GetDouble()),
            JsonValueKind.String => Of(element.GetString() ?? ""),
            JsonValueKind.True => True,
            JsonValueKind.False => False,
            JsonValueKind.Array => List(element.EnumerateArray().Take(MaxListLength + 1).Select(e => FromJson(e, depth + 1)).ToArray()),
            JsonValueKind.Object => Object(element.EnumerateObject().Take(MaxListLength + 1)
                .Select(p => new KeyValuePair<string, NthUiValue>(p.Name, FromJson(p.Value, depth + 1)))),
            _ => Null,
        };
    }

    private static string Clip(string text) => text.Length > MaxTextLength ? text[..MaxTextLength] : text;
}
