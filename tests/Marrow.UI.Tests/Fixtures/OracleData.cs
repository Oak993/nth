using System.Text.Json;
using Avalonia;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>One accepted element of the build spec's oracle (SPEC.md 2): its measured boundary points in the top-left frame, the Swift radius and size, and the rms of each candidate curve the spec's fitter found.</summary>
internal sealed record OracleElement(string Name, double W, double H, double SourceRadius, string Corner, bool Capsule,
    IReadOnlyDictionary<string, double> Rms, Point[] Points)
{
    /// <summary>A capsule's radius is half its height; every other element's is the Swift source radius.</summary>
    public double Radius => Capsule ? H / 2 : SourceRadius;

    /// <summary>The left or right end of a wide shape, not a corner.</summary>
    public bool IsEnd => Corner is "left" or "right";
}

/// <summary>The oracle elements the build spec's tools/measure_corners.py accepted (tests/Marrow.UI.Tests/Port/ContinuousCornerOracle.json).</summary>
internal static class OracleData
{
    public static IReadOnlyList<OracleElement> Elements { get; } = Load();

    private static List<OracleElement> Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "Marrow.UI.Tests", "Port", "ContinuousCornerOracle.json")));
        return json.RootElement.EnumerateArray().Select(e => new OracleElement(
            e.GetProperty("name").GetString()!, e.GetProperty("w").GetDouble(), e.GetProperty("h").GetDouble(),
            e.GetProperty("sourceRadius").GetDouble(), e.GetProperty("corner").GetString()!, e.GetProperty("capsule").GetBoolean(),
            e.GetProperty("rms").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()),
            e.GetProperty("points").EnumerateArray().Select(p => new Point(p[0].GetDouble(), p[1].GetDouble())).ToArray())).ToList();
    }
}
