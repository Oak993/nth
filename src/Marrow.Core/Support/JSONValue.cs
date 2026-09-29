using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Core.Support;

// Source: DroppyCode/Core/Support/JSONValue.swift (B2 1fbf81e), :1-165.
// :3-12. A Sendable JSON tree. Every provider protocol is decoded into this shape off the main actor.
// R-ENUM-2: associated-value enum -> abstract record with private constructor and nested sealed records.
// init(foundation:) and foundationObject (:26-57) have no caller outside this file at B2 and are not ported
// literally: PR-STORE-117's parked choice (ED-146) instead builds the tree through a Utf8JsonWriter, with every
// Double routed through SwiftJson.WriteDouble (R-CODE-5), and renders it with SwiftJson.Write under the
// SerializationCompact/SerializationPretty styles (R-CODE-4), which is Parse's and Data's only path.
public abstract record JSONValue
{
    private JSONValue() { }

    // :5
    public sealed record Null : JSONValue;

    // :6
    public sealed record Bool(bool Value) : JSONValue;

    // :7. Swift Int -> long (R-NUM-4).
    public sealed record Int(long Value) : JSONValue;

    // :8. R-VAL-4: a Double can be NaN, so equality uses == explicitly (NaN != NaN, unlike double.Equals).
    public sealed record Double(double Value) : JSONValue
    {
        public bool Equals(Double? other) => other is not null && Value == other.Value;

        public override int GetHashCode() => Value.GetHashCode();
    }

    // :9. R-VAL-4/R-STR-2: Swift String == is canonical equivalence, not ordinal.
    public sealed record String(string Value) : JSONValue
    {
        public bool Equals(String? other) => other is not null && SwiftString.Equal(Value, other.Value);

        public override int GetHashCode() => SwiftStringComparer.Instance.GetHashCode(Value);
    }

    // :10. R-VAL-2: a Swift [JSONValue] inside a model is a ValueList.
    public sealed record Array(ValueList<JSONValue> Value) : JSONValue;

    // :11. R-VAL-2: a Swift [String: JSONValue] inside a model is a ValueMap; keys are user/external text
    // (provider payload member names), so they take SwiftStringComparer.Instance (R-STR-2).
    public sealed record Object(ValueMap<string, JSONValue> Value) : JSONValue;

    // :15-20. JSONSerialization.jsonObject(with:options:[.fragmentsAllowed]), nil on failure.
    public static JSONValue? Parse(byte[] data) => SwiftTry.Optional(() =>
    {
        using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
        return FromElement(document.RootElement);
    });

    // :22-24. parse(_ string:) is parse(Data(string.utf8)).
    public static JSONValue? Parse(string text) => Parse(Encoding.UTF8.GetBytes(text));

    // :26-45, folded into Parse via System.Text.Json rather than a literal Foundation bridge (see the type doc).
    // R-CODE-8: an integral literal (no '.', 'e' or 'E') that fits a long is .Int; every other number is .Double
    // (SwiftJSONNumber.IsInt, O-09).
    private static JSONValue FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => new Bool(true),
        JsonValueKind.False => new Bool(false),
        JsonValueKind.Number => SwiftJSONNumber.IsInt(element, out var value) ? new Int(value) : new Double(element.GetDouble()),
        JsonValueKind.String => new String(element.GetString()!),
        JsonValueKind.Array => new Array(new ValueList<JSONValue>(ElementsOf(element))),
        JsonValueKind.Object => new Object(new ValueMap<string, JSONValue>(MembersOf(element), SwiftStringComparer.Instance)),
        _ => new Null(),
    };

    private static IEnumerable<JSONValue> ElementsOf(JsonElement array)
    {
        foreach (var item in array.EnumerateArray()) yield return FromElement(item);
    }

    // Last occurrence wins, as the Swift dictionary JSONSerialization builds from repeated JSON object keys would.
    private static IEnumerable<KeyValuePair<string, JSONValue>> MembersOf(JsonElement obj)
    {
        foreach (var property in obj.EnumerateObject()) yield return new(property.Name, FromElement(property.Value));
    }

    // :59-63 (ED-146). Both the tree build and the render sit inside one SwiftTry.Optional: a failure (a
    // non-finite Double, caught as an EncodingError by SwiftJson.WriteDouble/SwiftNumber.JsonText, R-CODE-5)
    // falls back to the four bytes "null" for the whole output, compact and pretty (PR-STORE-117, O-47).
    public byte[] Data(bool pretty = false) => SwiftTry.Optional(() =>
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = SwiftJson.DocumentOptions.MaxDepth }))
        {
            WriteTo(writer, this);
        }
        using var document = JsonDocument.Parse(stream.ToArray(), SwiftJson.DocumentOptions); // JSONSerialization has no 64-level writer limit
        return SwiftJson.Write(document.RootElement, pretty ? SwiftJsonStyle.SerializationPretty : SwiftJsonStyle.SerializationCompact);
    }) ?? Encoding.UTF8.GetBytes("null");

    private static void WriteTo(Utf8JsonWriter writer, JSONValue value)
    {
        switch (value)
        {
            case Null: writer.WriteNullValue(); break;
            case Bool b: writer.WriteBooleanValue(b.Value); break;
            case Int i: writer.WriteNumberValue(i.Value); break;
            case Double d: SwiftJson.WriteDouble(writer, d.Value); break;
            case String s: writer.WriteStringValue(s.Value); break;
            case Array a:
                writer.WriteStartArray();
                foreach (var item in a.Value) WriteTo(writer, item);
                writer.WriteEndArray();
                break;
            case Object o:
                writer.WriteStartObject();
                foreach (var (key, member) in o.Value)
                {
                    writer.WritePropertyName(key);
                    WriteTo(writer, member);
                }
                writer.WriteEndObject();
                break;
            default: throw new UnreachableException();
        }
    }

    // :65
    public string CompactString => Encoding.UTF8.GetString(Data());

    // :66
    public string PrettyString => Encoding.UTF8.GetString(Data(pretty: true));

    // :68-71
    public JSONValue? this[string key] => this is Object o && o.Value.TryGetValue(key, out var value) ? value : null;

    // :73-76. Swift Int -> long (R-NUM-4).
    public JSONValue? this[long index] => this is Array a && index >= 0 && index < a.Value.Count ? a.Value[(int)index] : null;

    // :78-81, strict.
    public string? JSONValueString => this is String s ? s.Value : null; // Swift: string

    // :83-90. .int as is; a finite .double truncated toward zero (traps outside the Int range, R-NUM-4); a
    // .string parsed with the standard integer parser; everything else nil.
    public long? JSONValueInt => this switch // Swift: int
    {
        Int i => i.Value,
        Double d => double.IsFinite(d.Value) ? SwiftNumber.TruncatingToInt(d.Value) : null,
        String s => SwiftNumber.ParseInt(s.Value),
        _ => null,
    };

    // :92-98. .int widened; .double as is; everything else, a string included, nil.
    public double? JSONValueDouble => this switch // Swift: double
    {
        Int i => i.Value,
        Double d => d.Value,
        _ => null,
    };

    // :100-103, strict.
    public bool? JSONValueBool => this is Bool b ? b.Value : null; // Swift: bool

    // :105-108, strict.
    public IReadOnlyList<JSONValue>? JSONValueArray => this is Array a ? a.Value : null; // Swift: array

    // :110-113, strict.
    public IReadOnlyDictionary<string, JSONValue>? JSONValueObject => this is Object o ? o.Value : null; // Swift: object

    // :115-118
    public bool IsNull => this is Null;

    // :120-130. Text rendering for display, unwrapping plain strings.
    public string DisplayText => this switch
    {
        Null => "",
        String s => s.Value,
        Bool b => b.Value ? "true" : "false",
        Int i => i.Value.ToString(CultureInfo.InvariantCulture), // R-NUM-2
        Double d => SwiftNumber.Description(d.Value), // R-NUM-2
        Array or Object => PrettyString,
        _ => throw new UnreachableException(),
    };

    // :132-134
    public static JSONValue Optional(string? value) => value is null ? new Null() : new String(value);

    // :137-165. R-ENUM-3/R-NAME-5: the literal conformances become implicit conversions and static factories.
    public static JSONValue FromNilLiteral() => new Null(); // :137-139

    public static implicit operator JSONValue(bool value) => new Bool(value); // :141-143

    public static implicit operator JSONValue(long value) => new Int(value); // :145-147

    public static implicit operator JSONValue(double value) => new Double(value); // :149-151

    public static implicit operator JSONValue(string value) => new String(value); // :153-155

    public static JSONValue FromArrayLiteral(params JSONValue[] elements) => new Array(new ValueList<JSONValue>(elements)); // :157-159

    // :161-165. The last occurrence of a repeated key wins; the FIRST spelling of a set of canonically
    // equivalent keys is the one Dictionary(_:uniquingKeysWith:) keeps (it replaces only the value in the
    // bucket it found), so ValueMap's own last-write-wins construction over these pairs reproduces both rules.
    public static JSONValue FromDictionaryLiteral(params (string Key, JSONValue Value)[] elements) => new Object(new ValueMap<string, JSONValue>(
        elements.Select(e => new KeyValuePair<string, JSONValue>(e.Key, e.Value)), SwiftStringComparer.Instance));
}
