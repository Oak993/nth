using System.Text.Json;
using Marrow.Core.Models.ComposerLights;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The Customization page's rows as the locked cz.js says them (tests/fixtures/customization/page.json, made by page.generate.cjs), and the settings a fixture case describes.</summary>
internal static class PageFixture
{
    internal static JsonDocument Open()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/page.json")));
    }

    /// <summary>The composer light a fixture case describes: <paramref name="style"/> with the case's beam or aura keys (the playground's own names, which the stored JSON uses).</summary>
    internal static ComposerLightSettings Settings(string style, JsonElement? beam, JsonElement? aura)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("style", style);
            if (beam is { } b) { w.WritePropertyName("beam"); b.WriteTo(w); }
            if (aura is { } a) { w.WritePropertyName("aura"); a.WriteTo(w); }
            w.WriteEndObject();
        }
        return ComposerLightSettings.FromJson(stream.ToArray());
    }
}
