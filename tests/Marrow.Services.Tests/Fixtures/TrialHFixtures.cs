using System.Text.Json;
using Marrow.Services.Port.Updates;

namespace Marrow.Services.Tests.Fixtures;

public static class TrialHFixtures
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("The trial fixture checkout is missing."), "tests", "fixtures", "dev-tools", "trial-h");
        }
    }

    public static JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, name + ".json")));

    public static DevToolCommandResult Result(string name)
    {
        using var doc = Read(name);
        var root = doc.RootElement;
        return new(root.GetProperty("exitCode").ValueKind == JsonValueKind.Null ? null : root.GetProperty("exitCode").GetInt32(), root.GetProperty("stdout").GetString()!, root.GetProperty("stderr").GetString()!);
    }

    public static DevToolCommand Command(string name)
    {
        using var doc = Read(name);
        return new(doc.RootElement.GetProperty("executable").GetString()!, doc.RootElement.GetProperty("arguments").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    public static OfficeInstallation Office(string name)
    {
        using var doc = JsonDocument.Parse(Result(name).Stdout);
        var c = doc.RootElement;
        return new(c.GetProperty("VersionToReport").GetString()!, c.GetProperty("UpdateChannel").GetString(), c.GetProperty("ClientPath").GetString()!, c.GetProperty("ClientExists").GetBoolean());
    }

    public static IReadOnlyList<string> Names => Directory.EnumerateFiles(Root, "*.json").Select(Path.GetFileNameWithoutExtension).Select(n => n!).ToArray();
}
