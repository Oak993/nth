using System.Globalization;
using System.Text.Json;

namespace Marrow.Core.Port;

/// <summary>Swift <c>DecodingError</c>. The case name is kept for diagnostics and tests; no user sees it.</summary>
public sealed class DecodingError(string kind, string message) : JsonException(message)
{
    /// <summary>keyNotFound, valueNotFound, typeMismatch or dataCorrupted.</summary>
    public string Kind { get; } = kind;
}

/// <summary>
/// PORT-ONLY. Keyed decoding with JSONDecoder's exact strictness (ED-072). Every Swift <c>init(from:)</c>,
/// synthesized or custom, becomes ONE reader function built from these, field by field, with the same
/// strict/lenient split as the Swift initializer. System.Text.Json's default binding is never used for a model:
/// it keeps a C# default where Swift throws. Source: draft A (proto-A SwiftDecoding.cs), with draft C's
/// Swift-shaped names.
/// <code>
///   Swift                                            C#
///   try c.decode(T.self, forKey: .k)                 Decode(e, "k", ReadT)
///   synthesized non-optional field (even with = x)   Decode(e, "k", ReadT)
///   try c.decodeIfPresent(T.self, forKey: .k)        DecodeIfPresent(e, "k", ReadT) / DecodeIfPresentValue(...)
///   synthesized Optional field                       DecodeIfPresent(...) / DecodeIfPresentValue(...)
///   c.value(.k, default: x)                          Value(e, "k", ReadT, () =&gt; x)   (via Coding.Value, the twin)
///   c.value(.k, default: [Lenient&lt;T&gt;]()).compactMap(\.value)   LenientList(e, "k", ReadT, () =&gt; ValueList&lt;T&gt;.Empty)
/// </code>
/// </summary>
public static class SwiftDecoding
{
    public delegate T Reader<out T>(JsonElement element);

    /// <summary>Strict: missing throws keyNotFound, null throws valueNotFound, a wrong type throws typeMismatch.</summary>
    public static T Decode<T>(JsonElement container, string key, Reader<T> read)
    {
        RequireObject(container);
        if (!container.TryGetProperty(key, out var field)) throw new DecodingError("keyNotFound", $"No value associated with key \"{key}\".");
        if (field.ValueKind == JsonValueKind.Null) throw new DecodingError("valueNotFound", $"Expected a value for key \"{key}\" but found null.");
        return read(field);
    }

    /// <summary><c>decodeIfPresent</c>: missing or null gives null, but a WRONG TYPE still throws.</summary>
    public static T? DecodeIfPresent<T>(JsonElement container, string key, Reader<T> read) where T : class
    {
        RequireObject(container);
        return container.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null ? read(field) : null;
    }

    /// <summary><c>decodeIfPresent</c> for a value type.</summary>
    public static T? DecodeIfPresentValue<T>(JsonElement container, string key, Reader<T> read) where T : struct
    {
        RequireObject(container);
        return container.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null ? read(field) : null;
    }

    /// <summary>
    /// The machinery behind <c>container.value(key, default:)</c> (Coding.swift:4-7): missing, null, a wrong type
    /// or every thrown error (any exception SwiftTry.IsThrownError accepts, CancellationError included) gives
    /// the fallback, evaluated lazily and afresh on each call; a trap propagates.
    /// </summary>
    public static T Value<T>(JsonElement container, string key, Reader<T> read, Func<T> fallback)
    {
        if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null)
        {
            try { return read(field); }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) { }
        }
        return fallback();
    }

    /// <summary>
    /// <c>container.value(key, default: [Lenient&lt;T&gt;]()).compactMap(\.value)</c>: a non-array gives the fallback;
    /// inside an array every thrown error (any exception SwiftTry.IsThrownError accepts, CancellationError
    /// included) drops that element and keeps the rest; a trap propagates. A null element is dropped too.
    /// </summary>
    public static ValueList<T> LenientList<T>(JsonElement container, string key, Reader<T> read, Func<ValueList<T>> fallback) =>
        Value(container, key, field =>
        {
            if (field.ValueKind != JsonValueKind.Array) throw Mismatch("Array", field);
            var items = new List<T>();
            foreach (var element in field.EnumerateArray())
            {
                try { items.Add(read(element)); }
                catch (Exception error) when (SwiftTry.IsThrownError(error)) { }
            }
            return new ValueList<T>(items);
        }, fallback);

    private static void RequireObject(JsonElement container)
    {
        if (container.ValueKind != JsonValueKind.Object) throw new DecodingError("typeMismatch", "Expected to decode Dictionary<String, Any>.");
    }

    // ---- Primitive readers with JSONDecoder's type rules ----

    /// <summary>A JSON string only; a number is a typeMismatch.</summary>
    public static string ReadString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw Mismatch("String", element);

    /// <summary>Only true/false. JSONDecoder rejects 0/1 and "true" for Bool.</summary>
    public static bool ReadBool(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Mismatch("Bool", element),
    };

    /// <summary>Swift <c>Int</c> is 64-bit: an integer literal, or an exactly integral number such as 1.0 or 1e2.</summary>
    public static long ReadInt(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number) throw Mismatch("Int", element);
        if (element.TryGetInt64(out var integer)) return integer;
        if (element.TryGetDouble(out var number) && double.IsFinite(number) && Math.Truncate(number) == number &&
            Math.Abs(number) < 9007199254740992d) return (long)number;
        if (element.TryGetDecimal(out var precise) && decimal.Truncate(precise) == precise && precise >= long.MinValue && precise <= long.MaxValue)
            return (long)precise;
        throw new DecodingError("dataCorrupted", $"Parsed JSON number <{element.GetRawText()}> does not fit in Int.");
    }

    /// <summary>
    /// Swift <c>UInt</c> is 64-bit and unsigned (R-NUM-4): the same rule as <see cref="ReadInt"/> over 0 to
    /// UInt.max, so a negative number or one past 2^64 - 1 does not fit and throws, as JSONDecoder's does.
    /// First needed by Core/Models/Shortcuts.swift:9 (<c>KeyChord.modifiers</c>).
    /// </summary>
    public static ulong ReadUInt(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number) throw Mismatch("UInt", element);
        if (element.TryGetUInt64(out var integer)) return integer;
        if (element.TryGetDouble(out var number) && double.IsFinite(number) && Math.Truncate(number) == number &&
            number >= 0 && number < 9007199254740992d) return (ulong)number;
        if (element.TryGetDecimal(out var precise) && decimal.Truncate(precise) == precise && precise >= ulong.MinValue && precise <= ulong.MaxValue)
            return (ulong)precise;
        throw new DecodingError("dataCorrupted", $"Parsed JSON number <{element.GetRawText()}> does not fit in UInt.");
    }

    /// <summary>Any JSON number.</summary>
    public static double ReadDouble(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value) && double.IsFinite(value)
            ? value : throw Mismatch("Double", element);

    /// <summary>Codable <c>UUID</c>: a string in <c>UUID(uuidString:)</c> form, either case.</summary>
    public static Guid ReadUUID(JsonElement element) =>
        SwiftUUID.Parse(ReadString(element)) ?? throw new DecodingError("dataCorrupted", "Attempted to decode UUID from invalid UUID string.");

    /// <summary>Codable <c>Date</c> under <c>.iso8601</c> (the storage decoder).</summary>
    public static DateTimeOffset ReadStorageDate(JsonElement element) =>
        FoundationISO8601.StorageDate(ReadString(element)) ?? throw new DecodingError("dataCorrupted", "Expected date string to be ISO8601-formatted.");

    /// <summary>Codable <c>Date</c> under the default <c>.deferredToDate</c> (plain JSONDecoder()): reference-date seconds.</summary>
    public static DateTimeOffset ReadDeferredDate(JsonElement element) => SwiftDate.FromReferenceSeconds(ReadDouble(element));

    /// <summary>A raw-value String enum: an exact, case-sensitive lookup; an unknown value is dataCorrupted.</summary>
    public static T ReadRawValue<T>(JsonElement element) where T : struct, Enum =>
        SwiftEnum<T>.FromRawValue(ReadString(element))
        ?? throw new DecodingError("dataCorrupted", $"Cannot initialize {typeof(T).Name} from invalid String value {element.GetRawText()}");

    /// <summary><c>[T]</c>: an array whose every element decodes (a null element is valueNotFound).</summary>
    public static ValueList<T> ReadArray<T>(JsonElement element, Reader<T> read)
    {
        if (element.ValueKind != JsonValueKind.Array) throw Mismatch("Array", element);
        var items = new List<T>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null) throw new DecodingError("valueNotFound", "Expected a value but found null.");
            items.Add(read(item));
        }
        return new ValueList<T>(items);
    }

    /// <summary><c>[String: T]</c>. A duplicate key keeps the last value (UNVERIFIED O-09 for JSONDecoder).</summary>
    public static ValueMap<string, T> ReadMap<T>(JsonElement element, Reader<T> read)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Mismatch("Dictionary", element);
        var pairs = new List<KeyValuePair<string, T>>();
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null) throw new DecodingError("valueNotFound", $"Expected a value for key \"{property.Name}\" but found null.");
            pairs.Add(KeyValuePair.Create(property.Name, read(property.Value)));
        }
        return new ValueMap<string, T>(pairs, StringComparer.Ordinal);
    }

    private static DecodingError Mismatch(string type, JsonElement element) =>
        new("typeMismatch", $"Expected to decode {type} but found {element.ValueKind.ToString().ToLowerInvariant()} instead.");
}

/// <summary>
/// PORT-ONLY. Synthesized Codable for an enum with associated values (SE-0295): <c>{"case":{"_0":payload}}</c>,
/// labelled payloads under their labels, a case without payload as <c>{"case":{}}</c>. Decoding requires exactly
/// one key. Never a <c>$type</c> discriminator ([JsonPolymorphic]); Swift never reads one. Source: draft A.
/// </summary>
public static class SwiftEnumCoding
{
    public static (string Case, JsonElement Payload) ReadCase(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new DecodingError("typeMismatch", "Expected to decode Dictionary<String, Any>.");
        var members = element.EnumerateObject().ToList();
        if (members.Count != 1) throw new DecodingError("typeMismatch", "Invalid number of keys found, expected one.");
        if (members[0].Value.ValueKind != JsonValueKind.Object) throw new DecodingError("typeMismatch", "Expected to decode Dictionary<String, Any>.");
        return (members[0].Name, members[0].Value);
    }

    public static void WriteCase(Utf8JsonWriter writer, string caseName, Action<Utf8JsonWriter>? members = null)
    {
        writer.WriteStartObject();
        writer.WriteStartObject(caseName);
        members?.Invoke(writer);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}

/// <summary>PORT-ONLY. <c>JSONSerialization</c> number classification: the LEXICAL form decides int versus double.</summary>
public static class SwiftJSONNumber
{
    /// <summary>"1" is an Int; "1.0", "1e2" and integers beyond Int64 are Doubles (the last UNVERIFIED O-09).</summary>
    public static bool IsInt(JsonElement number, out long value)
    {
        value = 0;
        var raw = number.GetRawText();
        return raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0 && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
