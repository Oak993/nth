using System.Text.Json;

namespace Marrow.App.Tests.Fixtures;

internal static class VoicePreviewFixture
{
    internal static JsonDocument Read()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/voice.json")));
    }

    internal static double ProcessingSeconds
    {
        get { using var fixture = Read(); return fixture.RootElement.GetProperty("processing").GetDouble(); }
    }
}
