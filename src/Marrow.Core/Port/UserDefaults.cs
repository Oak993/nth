using System.Globalization;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. The <c>UserDefaults</c> seam (ED-079). It stores property-list values (bool, long, double, string,
/// byte[] for Data, DateTimeOffset for Date, and IReadOnlyList&lt;object&gt; / IReadOnlyDictionary&lt;string, object&gt;
/// of those) under the exact upstream key names, and keeps ABSENT distinct from false/0, because the source
/// reads <c>defaults.object(forKey:) as? Bool ?? true</c> (absent is true) next to <c>defaults.bool(forKey:)</c>
/// (absent is false). Source: draft C (proto-C Port/SettingsStore.cs).
/// </summary>
public interface ISettingsStore
{
    /// <summary><c>object(forKey:)</c>: null when absent.</summary>
    object? Object(string key);

    /// <summary><c>set(_:forKey:)</c>: null removes, as <c>set(nil, forKey:)</c> does.</summary>
    void Set(string key, object? value);

    /// <summary><c>removeObject(forKey:)</c>.</summary>
    void RemoveObject(string key);
}

/// <summary>
/// <c>UserDefaults.standard</c>. Marrow.App sets the process default to the production store at start-up
/// (ED-079: its file format is the orchestrator's open decision); tests override it per async flow. Until then
/// every access traps, so a missing binding can never silently lose settings.
/// </summary>
public static class UserDefaults
{
    public static Ambient<ISettingsStore> Standard { get; } = new(new UnboundStore());

    /// <summary>PORT-ONLY (ED-NEW-sidebarmodes-11): whether a store is bound for this flow, for a reader that has a truthful answer
    /// without one (no store, no remapped chord) and must not trip the trap on a process-wide lazy default.</summary>
    public static bool IsBound => Standard.Current is not UnboundStore;

    private sealed class UnboundStore : ISettingsStore
    {
        private const string Message = "UserDefaults.Standard is not bound: Marrow.App sets it at start-up; tests override it.";
        public object? Object(string key) => throw new SwiftTrapException(Message);
        public void Set(string key, object? value) => throw new SwiftTrapException(Message);
        public void RemoveObject(string key) => throw new SwiftTrapException(Message);
    }
}

/// <summary>Swift's bridging of a stored property-list value (an NSNumber underneath) to <c>as? T</c> and the typed getters.</summary>
public static class Plist
{
    /// <summary><c>object(forKey:) as? Bool</c>: a Bool, or a number that is exactly 0 or 1.</summary>
    public static bool? AsBool(object? value) => value switch
    {
        bool b => b,
        long l when l is 0 or 1 => l == 1,
        int i when i is 0 or 1 => i == 1,
        double d when d is 0 or 1 => d == 1,
        _ => null,
    };

    /// <summary><c>as? Int</c>: an integer, a Bool (0/1), or a Double with no fractional part.</summary>
    public static long? AsInt(object? value) => value switch
    {
        long l => l,
        int i => i,
        bool b => b ? 1 : 0,
        double d when double.IsFinite(d) && Math.Truncate(d) == d && Math.Abs(d) < 9.2e18 => (long)d,
        _ => null,
    };

    /// <summary><c>as? Double</c>: any number (a Bool as 1.0/0.0).</summary>
    public static double? AsDouble(object? value) => value switch
    {
        double d => d,
        long l => l,
        int i => i,
        bool b => b ? 1 : 0,
        _ => null,
    };

    /// <summary><c>bool(forKey:)</c>: absent is false.</summary>
    public static bool Bool(this ISettingsStore store, string key) => AsBool(store.Object(key)) ?? false;

    /// <summary>
    /// <c>string(forKey:)</c>: a string, or an integer rendered as a string. A stored Double goes through
    /// NSNumber.stringValue, whose text is UNVERIFIED (O-10), so it is deliberately not guessed: it traps.
    /// </summary>
    public static string? String(this ISettingsStore store, string key) => store.Object(key) switch
    {
        string s => s,
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        double => throw new SwiftTrapException("string(forKey:) of a stored Double is UNVERIFIED (O-10); stop and report."),
        _ => null,
    };

    /// <summary><c>data(forKey:)</c>: stored Data, else nil.</summary>
    public static byte[]? Data(this ISettingsStore store, string key) => store.Object(key) as byte[];

    /// <summary><c>array(forKey:)</c>: a stored array, else nil.</summary>
    public static IReadOnlyList<object>? Array(this ISettingsStore store, string key) => store.Object(key) as IReadOnlyList<object>;

    /// <summary><c>dictionary(forKey:)</c>: a stored dictionary, else nil.</summary>
    public static IReadOnlyDictionary<string, object>? Dictionary(this ISettingsStore store, string key) =>
        store.Object(key) as IReadOnlyDictionary<string, object>;

    /// <summary><c>stringArray(forKey:)</c>: a stored array whose every element is a string, else nil.</summary>
    public static IReadOnlyList<string>? StringArray(this ISettingsStore store, string key) =>
        store.Object(key) is IReadOnlyList<object> list && list.All(item => item is string)
            ? list.Cast<string>().ToArray() : null;
}
