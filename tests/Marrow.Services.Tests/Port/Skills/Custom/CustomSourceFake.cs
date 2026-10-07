using System.IO.Compression;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

internal sealed class CustomSourceFake : ICustomSourceFiles, ICustomGitHubSource
{
    private readonly object gate = new();
    public Dictionary<string, byte[]> Files { get; set; } = [];
    public Dictionary<string, Dictionary<string, byte[]>> Installed { get; } = [];
    public byte[]? Registry { get; private set; }
    public bool FailCommit { get; set; }
    public int GitHubCalls { get; private set; }
    public CustomSources Open() => new("/fake/own", this, this, TimeProvider.System);
    public Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel) => Task.FromResult<IReadOnlyDictionary<string, byte[]>>(Files.ToDictionary(p => p.Key, p => p.Value.ToArray()));
    public Task<byte[]> ReadZip(string path, CancellationToken cancel) => Task.FromResult(Zip(Files));
    public Task<CustomGitHubArchive> Fetch(string repository, CancellationToken cancel)
    {
        GitHubCalls++;
        return Task.FromResult(new CustomGitHubArchive(new string('a', 40), "https://codeload.github.com/" + repository + "/zip/" + new string('a', 40), Zip(Files.ToDictionary(p => "repository-commit/" + p.Key, p => p.Value))));
    }
    public byte[]? ReadRegistry(string root) => Registry?.ToArray();
    public IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id) => Installed[id];
    public void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files) { lock (gate) Installed.Add(id, files.ToDictionary(p => p.Key, p => p.Value.ToArray())); }
    public void Delete(string root, string id) { lock (gate) Installed.Remove(id); }
    public void SaveRegistry(string root, byte[] bytes)
    {
        if (FailCommit) throw new IOException("Fixture commit failure");
        Registry = bytes.ToArray();
    }
    private static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var (path, bytes) in files) { using var target = zip.CreateEntry(path).Open(); target.Write(bytes); }
        return memory.ToArray();
    }
}
