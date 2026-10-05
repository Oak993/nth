using System.Text.Json;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The working dots as the locked cz.js says them (tests/fixtures/customization/dots.json, made by dots.generate.cjs from the real wdColour() and dotsSec()).</summary>
internal static class DotsFixture
{
    internal static JsonDocument Open()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/dots.json")));
    }

    /// <summary>The colour the design gives the model mode for <paramref name="maker"/> in the appearance, lower-case "#rrggbb".</summary>
    internal static string Model(JsonDocument fixture, string maker, bool dark) =>
        fixture.RootElement.GetProperty("colours").EnumerateArray()
            .Single(c => c.GetProperty("mode").GetString() == "model" && c.GetProperty("maker").GetString() == maker && c.GetProperty("dark").GetBoolean() == dark)
            .GetProperty("hex").GetString()!;

    /// <summary>The colour the design gives the custom mode for the accent id in the appearance.</summary>
    internal static string Custom(JsonDocument fixture, string id, bool dark) =>
        fixture.RootElement.GetProperty("colours").EnumerateArray()
            .Single(c => c.GetProperty("mode").GetString() == "custom" && c.GetProperty("custom").GetString() == id && c.GetProperty("dark").GetBoolean() == dark)
            .GetProperty("hex").GetString()!;
}
