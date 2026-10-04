using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Marrow.Core.Tests.Fixtures;

// VT-4 test support: reads the node fixtures under tests\fixtures\voice\ (made by make-vt4-fixtures.js from the real design sources).
// Numbers a JSON file cannot carry travel as the words "NaN", "Infinity" and "-Infinity".
internal static class VoiceFixtureFile
{
    internal static string Path(string name) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(HerePath())!, "..", "..", "fixtures", "voice", name));

    internal static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));

    internal static JsonDocument Json(string name) => JsonDocument.Parse(Bytes(name));

    /// <summary>A fixture number: a JSON number, or one of the words for the values JSON cannot write.</summary>
    internal static double Number(JsonElement element) => element.ValueKind == JsonValueKind.String
        ? element.GetString() switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            var other => double.Parse(other!, CultureInfo.InvariantCulture),
        }
        : element.GetDouble();

    /// <summary>The floats a fixture array holds: each text is the shortest decimal of a float32, so the cast gives that float.</summary>
    internal static float[] Floats(JsonElement array) => array.EnumerateArray().Select(e => (float)Number(e)).ToArray();

    internal static double[] Doubles(JsonElement array) => array.EnumerateArray().Select(Number).ToArray();

    /// <summary>
    /// Asserts two doubles agree to 1e-9 absolute plus 1e-10 relative: the fixtures keep 13 significant digits, and V8's and .NET's
    /// <c>exp</c>, <c>pow</c> and <c>sin</c> may differ in the last bit.
    /// </summary>
    internal static void AssertClose(double expected, double actual, string what)
    {
        if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), what + ": expected NaN, got " + actual.ToString("R", CultureInfo.InvariantCulture)); return; }
        var tolerance = 1e-9 + 1e-10 * Math.Abs(expected);
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            what + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) + ", got " + actual.ToString("R", CultureInfo.InvariantCulture));
    }

    private static string HerePath([CallerFilePath] string path = "") => path;
}
