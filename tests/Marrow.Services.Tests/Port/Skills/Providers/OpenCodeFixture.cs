using System.Text.Json;
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11d. A throwaway home/project/data tree under MARROW_PORT_DATA_DIR (R-TEST-9); nothing here touches a real user folder.
public sealed class OpenCodeFixture : IDisposable
{
    public OpenCodeFixture()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? throw new InvalidOperationException("MARROW_PORT_DATA_DIR is required.");
        Root = Path.Combine(parent, "oc sk11d " + Guid.NewGuid().ToString("N"));
        Home = Path.Combine(Root, "home dir");
        Project = Path.Combine(Home, "proj");
        Session = Path.Combine(Project, "pkg", "app");
        Data = Path.Combine(Root, "Marrow Code");
        Generation = Path.Combine(Data, "skills", "views", "opencode", "chat-c1");
        Directory.CreateDirectory(Session);
        Directory.CreateDirectory(Path.Combine(Project, ".git"));
        Directory.CreateDirectory(Generation);
    }

    public string Root { get; }
    public string Home { get; }
    public string Project { get; }
    public string Session { get; }
    public string Data { get; }
    public string Generation { get; }
    public string Config => Path.Combine(Home, ".config", "opencode");
    public string AuthFile => Path.Combine(Home, ".local", "share", "opencode", "auth.json");
    public Dictionary<string, string> Env { get; } = new(StringComparer.OrdinalIgnoreCase) { ["PATH"] = "user-one" };
    public OpenCodeSkillLaunch Launch => new(Generation, Session, Data, Home);

    public string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public string Paths(params string[] paths) => "{\"skills\":{\"paths\":" + JsonSerializer.Serialize(paths) + "}}";

    public static string[] ReadPaths(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("skills").GetProperty("paths").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }
}
