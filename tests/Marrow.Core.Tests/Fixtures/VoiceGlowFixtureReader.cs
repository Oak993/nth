using System.Reflection;
using System.Text.Json;
using Marrow.Core.Port.Voice.Glow;

namespace Marrow.Core.Tests.Fixtures;

// VT-4 test support: turns a fixture's `props` object into VoiceGlowProps and compares a C# record to a fixture's JSON object property by
// property (JSON camelCase names, C# PascalCase), so a field the port forgot or invented fails the test instead of passing unseen.
internal static class VoiceGlowFixtureReader
{
    internal static VoiceGlowProps Props(JsonElement json)
    {
        object boxed = new VoiceGlowProps();
        foreach (var entry in json.EnumerateObject())
        {
            if (entry.Name is "active" or "level") continue; // host-side props
            var property = typeof(VoiceGlowProps).GetProperty(Pascal(entry.Name), BindingFlags.Public | BindingFlags.Instance);
            Assert.True(property is not null, "VoiceGlowProps has no " + entry.Name);
            property!.SetValue(boxed, Convert(entry.Value, property.PropertyType));
        }

        return (VoiceGlowProps)boxed;
    }

    private static object? Convert(JsonElement value, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (target == typeof(double)) return value.GetDouble();
        if (target == typeof(bool)) return value.GetBoolean();
        if (target == typeof(string)) return value.GetString();
        if (target.IsEnum) return Enum.Parse(target, value.GetString()!, ignoreCase: true);
        if (target == typeof(VoiceGlowBandColorProps))
            return new VoiceGlowBandColorProps(
                value.TryGetProperty("core", out var c) ? c.GetString() : null, value.TryGetProperty("above", out var a) ? a.GetString() : null,
                value.TryGetProperty("mid", out var m) ? m.GetString() : null, value.TryGetProperty("below", out var b) ? b.GetString() : null);
        if (target == typeof(IReadOnlyList<string?>)) return value.EnumerateArray().Select(e => e.GetString()).ToList();
        throw new InvalidOperationException("no conversion for " + target);
    }

    /// <summary>Compares every public property of <paramref name="actual"/> with the fixture object; keys in <paramref name="ignore"/> are not in the C# type.</summary>
    internal static void AssertRecord(object actual, JsonElement expected, string what, params string[] ignore)
    {
        var properties = actual.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name);
        var seen = new HashSet<string>();
        foreach (var entry in expected.EnumerateObject())
        {
            if (ignore.Contains(entry.Name)) continue;
            var name = Pascal(entry.Name);
            Assert.True(properties.TryGetValue(name, out var property), $"{what}: the port has no {name} for the design's {entry.Name}");
            seen.Add(name);
            AssertValue(property!.GetValue(actual), entry.Value, $"{what}.{entry.Name}");
        }

        foreach (var name in properties.Keys)
        {
            if (seen.Contains(name)) continue;
            // a property the design leaves undefined (JSON drops it) is null in the port
            Assert.True(properties[name].GetValue(actual) is null, $"{what}: the port has {name} but the design's object has no {Camel(name)}");
        }
    }

    private static void AssertValue(object? actual, JsonElement expected, string what)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Number:
                VoiceFixtureFile.AssertClose(expected.GetDouble(), System.Convert.ToDouble(actual, System.Globalization.CultureInfo.InvariantCulture), what);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.True(actual is bool, what + " is not a bool");
                Assert.Equal(expected.GetBoolean(), (bool)actual!);
                break;
            case JsonValueKind.String:
                if (actual is Enum e) Assert.Equal(expected.GetString(), e.ToString().ToLowerInvariant());
                else Assert.Equal(expected.GetString(), actual as string);
                break;
            case JsonValueKind.Object:
                Assert.NotNull(actual);
                AssertRecord(actual!, expected, what);
                break;
            case JsonValueKind.Array:
                var items = ((IEnumerable<string?>)actual!).ToList();
                var wanted = expected.EnumerateArray().Select(x => x.GetString()).ToList();
                Assert.Equal(wanted, items);
                break;
            default:
                throw new InvalidOperationException(what + ": " + expected.ValueKind);
        }
    }

    private static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
