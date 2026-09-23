// PORT-ONLY: ED-079 and IMPLEMENTING.md 4.3 "Production settings store". Foundation's UserDefaults has no .NET twin; this is
// the store Marrow.App binds as UserDefaults.Standard, a per-user JSON file under the "Marrow Code" data folder (OD-0008 C7).
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Services.Port;

/// <summary>
/// The production <see cref="ISettingsStore"/> behind <c>UserDefaults.standard</c> (ED-079, R-SET-1).
/// <para>
/// It keeps the UserDefaults contract the Swift call sites rely on: <c>object(forKey:)</c> of an absent key is nil, so
/// <c>as? Bool ?? true</c> gives true (App/AppSettings.swift:708) while <c>bool(forKey:)</c> gives false (:719);
/// <c>set(nil, forKey:)</c> removes the key (:516) as <c>removeObject(forKey:)</c> does (:265); a stored value keeps its
/// property-list type (Bool, Int, Double, String, Data, Date, array, dictionary; :263, UpdateChecker.swift:238 read back
/// <c>as? Date</c> at :118); the store keeps a copy of what it is given; and a value that is not a property list is refused
/// ("Attempt to insert non-property list object"), which the port models as a trap.
/// </para>
/// <para>
/// The file is compact UTF-8 JSON: one object whose keys are the defaults keys in ordinal order, each value a one-member
/// object tagged with its XML property-list element name (<c>bool</c>, <c>integer</c>, <c>real</c>, <c>string</c>,
/// <c>data</c> as base64, <c>date</c> as round-trip UTC text, <c>array</c>, <c>dict</c>); a non-finite real is the
/// string <c>+infinity</c>, <c>-infinity</c> or <c>nan</c>. Every change rewrites the whole file with one atomic replace
/// through <see cref="FileOps.WriteAtomic"/> (PA-0015, R-FS-1) before the call returns, in call order. A write that fails
/// keeps the value in memory, as UserDefaults does, and is kept in <see cref="LastWriteError"/>; <c>set</c> never throws
/// at a call site (App/AppSettings.swift:288).
/// </para>
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    private const string TagBool = "bool";
    private const string TagInteger = "integer";
    private const string TagReal = "real";
    private const string TagString = "string";
    private const string TagData = "data";
    private const string TagDate = "date";
    private const string TagArray = "array";
    private const string TagDict = "dict";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly string _path;
    private Exception? _lastWriteError;

    /// <summary>
    /// Opens the store kept in <paramref name="path"/>. A missing file is an empty store and nothing is created until the
    /// first write. A file that is not a settings document reads as empty; an entry the store cannot read is skipped. A
    /// file that exists but cannot be read throws, so a held or unreadable file is never overwritten with defaults.
    /// </summary>
    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        if (!File.Exists(path)) return;
        var bytes = File.ReadAllBytes(path); // R-FS-1: read bytes, then decode.
        Load(bytes, _values);
    }

    /// <summary>The file of the production store: <c>%LOCALAPPDATA%\Marrow Code\UserDefaults.json</c> (OD-0008 C7).</summary>
    public static string ProductionPath(string localAppData)
    {
        ArgumentException.ThrowIfNullOrEmpty(localAppData);
        return Path.Join(localAppData, "Marrow Code", "UserDefaults.json");
    }

    /// <summary>The error of the most recent write that failed, or null once a write succeeds.</summary>
    public Exception? LastWriteError
    {
        get { lock (_gate) return _lastWriteError; }
    }

    /// <summary><c>object(forKey:)</c>: null when absent; a copy the caller may change freely.</summary>
    public object? Object(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
            return _values.TryGetValue(key, out var value) ? CopyOut(value) : null;
    }

    /// <summary>
    /// <c>set(_:forKey:)</c>: stores a copy of a property-list value; null removes the key (App/AppSettings.swift:516).
    /// A value that is not a property list traps and stores nothing.
    /// </summary>
    public void Set(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (value is null)
        {
            RemoveObject(key);
            return;
        }
        var frozen = Freeze(value, key); // traps before anything changes
        lock (_gate)
        {
            _values[key] = frozen;
            Persist();
        }
    }

    /// <summary><c>removeObject(forKey:)</c> (App/AppSettings.swift:265): removing an absent key is not an error.</summary>
    public void RemoveObject(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            _values.Remove(key);
            Persist();
        }
    }

    // Called under _gate: one atomic replace of the whole file, in call order.
    private void Persist()
    {
        try
        {
            var bytes = Serialize(_values);
            var folder = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder); // a first launch has no data folder yet
            FileOps.WriteAtomic(_path, bytes);
            _lastWriteError = null;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            _lastWriteError = error;
        }
    }

    // Converting what the caller passes into the store's own immutable property-list values.

    private static object Freeze(object value, string key) => value switch
    {
        bool b => b,
        long l => l,
        int i => (long)i,
        short s => (long)s,
        sbyte sb => (long)sb,
        byte by => (long)by,
        ushort us => (long)us,
        uint ui => (long)ui,
        double d => d,
        float f => (double)f,
        string s => s,
        byte[] data => data.ToArray(),
        DateTimeOffset date => date.ToUniversalTime(),
        IReadOnlyDictionary<string, object> map => FreezeMap(map.Select(pair => new KeyValuePair<object, object?>(pair.Key, pair.Value)), key),
        IDictionary map => FreezeMap(Entries(map), key),
        IEnumerable list => FreezeList(list, key),
        _ => throw NotAPropertyList(value, key),
    };

    private static ReadOnlyCollection<object> FreezeList(IEnumerable list, string key)
    {
        var items = new List<object>();
        foreach (var item in list)
            items.Add(item is null ? throw NotAPropertyList(list, key) : Freeze(item, key));
        return items.AsReadOnly();
    }

    private static ReadOnlyDictionary<string, object> FreezeMap(IEnumerable<KeyValuePair<object, object?>> entries, string key)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (entryKey, entryValue) in entries)
        {
            if (entryKey is not string name || entryValue is null) throw NotAPropertyList(entries, key);
            map[name] = Freeze(entryValue, key);
        }
        return map.AsReadOnly();
    }

    // IDictionary's own enumerator yields DictionaryEntry for any generic dictionary (its IEnumerable one does not).
    private static IEnumerable<KeyValuePair<object, object?>> Entries(IDictionary map)
    {
        var entries = map.GetEnumerator();
        while (entries.MoveNext()) yield return new KeyValuePair<object, object?>(entries.Key, entries.Value);
    }

    private static SwiftTrapException NotAPropertyList(object value, string key) =>
        new($"Attempt to insert non-property list object {value} for key {key}");

    // Data is the only mutable leaf; collections are read-only wrappers, copied so no caller reaches a stored byte[].
    private static object CopyOut(object value) => value switch
    {
        byte[] data => data.ToArray(),
        ReadOnlyCollection<object> list => list.Select(CopyOut).ToList().AsReadOnly(),
        ReadOnlyDictionary<string, object> map => map.ToDictionary(pair => pair.Key, pair => CopyOut(pair.Value), StringComparer.Ordinal).AsReadOnly(),
        _ => value,
    };

    // The file format.

    private static byte[] Serialize(Dictionary<string, object> values)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var key in values.Keys.Order(StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                WriteValue(writer, values[key]);
            }
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, object value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case bool b:
                writer.WriteBoolean(TagBool, b);
                break;
            case long l:
                writer.WriteNumber(TagInteger, l);
                break;
            case double d:
                writer.WritePropertyName(TagReal);
                if (double.IsPositiveInfinity(d)) writer.WriteStringValue("+infinity");
                else if (double.IsNegativeInfinity(d)) writer.WriteStringValue("-infinity");
                else if (double.IsNaN(d)) writer.WriteStringValue("nan");
                else writer.WriteRawValue(d.ToString("R", CultureInfo.InvariantCulture)); // shortest round trip, "-0" kept
                break;
            case string s:
                writer.WriteString(TagString, s);
                break;
            case byte[] data:
                writer.WriteBase64String(TagData, data);
                break;
            case DateTimeOffset date:
                writer.WriteString(TagDate, date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case ReadOnlyCollection<object> list:
                writer.WriteStartArray(TagArray);
                foreach (var item in list) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            case ReadOnlyDictionary<string, object> map:
                writer.WriteStartObject(TagDict);
                foreach (var name in map.Keys.Order(StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    WriteValue(writer, map[name]);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new SwiftTrapException($"SettingsStore holds a value of type {value.GetType()}"); // Freeze admits none
        }
        writer.WriteEndObject();
    }

    private static void Load(byte[] bytes, Dictionary<string, object> into)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, SwiftJson.DocumentOptions);
        }
        catch (JsonException)
        {
            return; // not a settings document: an empty store
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (TryRead(property.Value, out var value)) into[property.Name] = value; // an unreadable entry is skipped
            }
        }
    }

    private static bool TryRead(JsonElement element, out object value)
    {
        value = null!;
        if (element.ValueKind != JsonValueKind.Object) return false;
        using var members = element.EnumerateObject();
        if (!members.MoveNext()) return false;
        var tagged = members.Current;
        if (members.MoveNext()) return false;
        var body = tagged.Value;
        switch (tagged.Name)
        {
            case TagBool when body.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = body.GetBoolean();
                return true;
            case TagInteger when body.ValueKind == JsonValueKind.Number && body.TryGetInt64(out var integer):
                value = integer;
                return true;
            case TagReal:
                return TryReadReal(body, out value);
            case TagString when body.ValueKind == JsonValueKind.String:
                value = body.GetString()!;
                return true;
            case TagData when body.ValueKind == JsonValueKind.String && body.TryGetBytesFromBase64(out var data):
                value = data;
                return true;
            case TagDate when body.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParseExact(body.GetString(), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date):
                value = date.ToUniversalTime();
                return true;
            case TagArray when body.ValueKind == JsonValueKind.Array:
            {
                var items = new List<object>();
                foreach (var item in body.EnumerateArray())
                {
                    if (!TryRead(item, out var read)) return false;
                    items.Add(read);
                }
                value = items.AsReadOnly();
                return true;
            }
            case TagDict when body.ValueKind == JsonValueKind.Object:
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var entry in body.EnumerateObject())
                {
                    if (!TryRead(entry.Value, out var read)) return false;
                    map[entry.Name] = read;
                }
                value = map.AsReadOnly();
                return true;
            }
            default:
                return false;
        }
    }

    private static bool TryReadReal(JsonElement body, out object value)
    {
        value = null!;
        if (body.ValueKind == JsonValueKind.String)
        {
            switch (body.GetString())
            {
                case "+infinity": value = double.PositiveInfinity; return true;
                case "-infinity": value = double.NegativeInfinity; return true;
                case "nan": value = double.NaN; return true;
                default: return false;
            }
        }
        if (body.ValueKind != JsonValueKind.Number) return false;
        // The raw text, so "-0" reads back as negative zero.
        if (!double.TryParse(body.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var real) || !double.IsFinite(real))
            return false;
        value = real;
        return true;
    }
}
