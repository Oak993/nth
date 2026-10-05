// PORT-ONLY: SK-13 / DL-16 (PLAN WU-18 r3 (a)). A fake data root as an installed Marrow leaves it: locked tool folders, locked skill sets, spares and chat generations, the shim folder, a partial
// download, state, routes, switches, the condemned list and a provider home beside it that the hook must not touch. Marrow-made bytes only.
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Fixtures;

internal sealed class UninstallTree
{
    public UninstallTree(StoreFixture fixture, int filesPerFolder = 4)
    {
        var paths = fixture.Paths;
        Locked = [
            Path.Combine(paths.Tools, "T1-3.14.8-aaaa1111"), Path.Combine(paths.Tools, "T3-26.2.6-bbbb2222"),
            Path.Combine(paths.Store, "anthropics-skills@8a1541c4"), Path.Combine(paths.Store, "openai-skills@49f948fa"),
            Path.Combine(paths.Views, "claude", "spare-aaaa"), Path.Combine(paths.Views, "claude", "chat-one"), Path.Combine(paths.Views, "codex", "chat-two"),
        ];
        foreach (var folder in Locked)
        {
            for (var i = 0; i < filesPerFolder; i++)
            {
                var path = Path.Combine(folder, "sub" + i % 3, "file" + i + ".txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "Marrow-made fixture " + i);
            }
            fixture.Trees.Lock(folder);
        }
        Directory.CreateDirectory(Path.Combine(paths.Addons, "bin"));
        File.WriteAllText(Path.Combine(paths.Addons, "bin", "python.exe"), "shim fixture");
        Directory.CreateDirectory(Path.Combine(paths.Addons, "downloads"));
        File.WriteAllText(Path.Combine(paths.Addons, "downloads", "a1b2c3d4.part"), "partial fixture");
        Directory.CreateDirectory(Path.Combine(paths.Addons, "routes"));
        File.WriteAllText(Path.Combine(paths.Addons, "routes", "0.json"), "{}");
        File.WriteAllText(paths.StateFile, "{}");
        Directory.CreateDirectory(Path.Combine(paths.Addons, "work", "python-agent", "3.14"));
        File.WriteAllText(Path.Combine(paths.Addons, "work", "python-agent", "3.14", "agent-wrote.txt"), "an agent's own pip install");
        File.WriteAllText(paths.EnabledFile, "enabled sentinel");
        File.WriteAllText(paths.CondemnedFile, "[]");
        File.WriteAllText(Path.Combine(paths.Skills, "chat-paths.json"), "{}");
        File.WriteAllText(Path.Combine(paths.Skills, "generation-launches.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(paths.Skills, "reset-pending"), "");
        ProviderHome = Path.Combine(fixture.Root, "provider-home");
        Directory.CreateDirectory(Path.Combine(ProviderHome, "transcripts"));
        File.WriteAllText(Path.Combine(ProviderHome, "transcripts", "chat.jsonl"), "a provider transcript");
        Other = Path.Combine(fixture.Root, "UserDefaults.json");
        File.WriteAllText(Other, "{}");
    }

    /// <summary>The locked folders (each carries the deny entry and read-only files).</summary>
    public IReadOnlyList<string> Locked { get; }

    /// <summary>A sibling the hook must leave alone: a provider's home with a transcript in it.</summary>
    public string ProviderHome { get; }

    /// <summary>Another Marrow data file outside the skills folders.</summary>
    public string Other { get; }

    public string FirstFile(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).First();
}
