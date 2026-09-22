using System.Globalization;
using System.Reflection;

namespace Marrow.Core.Port;

/// <summary>
/// The raw value of one case of a Swift <c>enum X: String</c>, written on the member (ED-075). Where Swift spells no
/// raw value, it is the case name verbatim (<c>catppuccinMocha</c> is "catppuccinMocha").
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RawValueAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

/// <summary>
/// PORT-ONLY. <c>rawValue</c>, <c>X(rawValue:)</c> and <c>X.allCases</c> for a ported String enum (ED-075). The type
/// initializer rejects a member without [RawValue], a duplicate raw value, or an explicit numeric value, so a
/// C# enum keeps Swift declaration order (allCases order) and the exact, case-sensitive raw spelling.
/// Enum.TryParse is wrong three ways (numeric strings, case-insensitive names, comma-joined flags) and
/// Enum.GetValues sorts by value. Source: draft C (proto-C SwiftValues.cs).
/// </summary>
public static class SwiftEnum<TEnum> where TEnum : struct, Enum
{
    private static readonly TEnum[] cases;
    private static readonly Dictionary<TEnum, string> raw = [];
    private static readonly Dictionary<string, TEnum> byRaw = new(SwiftStringComparer.Instance);

    static SwiftEnum()
    {
        var fields = typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static);
        cases = new TEnum[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            var value = (TEnum)fields[i].GetValue(null)!;
            if (Convert.ToInt64(value, CultureInfo.InvariantCulture) != i)
                throw new SwiftTrapException($"{typeof(TEnum).Name}.{fields[i].Name} must have the implicit value {i} (Swift declaration order).");
            var attribute = fields[i].GetCustomAttribute<RawValueAttribute>()
                ?? throw new SwiftTrapException($"{typeof(TEnum).Name}.{fields[i].Name} has no [RawValue].");
            cases[i] = value;
            raw[value] = attribute.Value;
            if (!byRaw.TryAdd(attribute.Value, value))
                throw new SwiftTrapException($"{typeof(TEnum).Name} repeats the raw value \"{attribute.Value}\".");
        }
    }

    /// <summary><c>X.allCases</c>, in declaration order.</summary>
    public static IReadOnlyList<TEnum> AllCases => cases;

    /// <summary><c>x.rawValue</c>.</summary>
    public static string RawValue(TEnum value) => raw[value];

    /// <summary>
    /// <c>X(rawValue:)</c>: exact and case-sensitive, never numeric, never flags. The synthesized initializer switches
    /// over the raw literals, so it compares as Swift <c>==</c> does: canonical equivalence (R-STR-2).
    /// </summary>
    public static TEnum? FromRawValue(string? rawValue) =>
        rawValue is not null && byRaw.TryGetValue(rawValue, out var value) ? value : null;
}
