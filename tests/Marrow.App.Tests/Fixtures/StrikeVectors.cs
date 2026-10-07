using System.Text.Json;

namespace Marrow.App.Tests.Fixtures;

public static class StrikeVectors
{
    public static string Folder => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../fixtures/strike-port"));
    public static JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder, name)));
}
