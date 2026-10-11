using System.IO.Compression;
using Marrow.Services.Port.Skills.Custom;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

internal sealed class CustomOwnFake : ICustomSourceFiles, ICustomGitHubSource
{
    internal Dictionary<string, byte[]> Source { get; } = [];
    internal Dictionary<string, Dictionary<string, byte[]>> Installed { get; } = [];
    internal byte[]? Registry { get; private set; }
    internal bool FailCommit { get; set; }
    internal bool FailPublish { get; set; }
    internal int Fetches { get; private set; }
    internal CustomSources Open() => new("/fake/own", this, this, new ManualTimeProvider());
    public Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel) => Task.FromResult<IReadOnlyDictionary<string, byte[]>>(Source.ToDictionary(p => p.Key, p => p.Value.ToArray()));
    public Task<byte[]> ReadZip(string path, CancellationToken cancel) => Task.FromResult(Archive(Source));
    public Task<CustomGitHubArchive> Fetch(string repository, CancellationToken cancel)
    {
        Fetches++;
        return Task.FromResult(new CustomGitHubArchive(new string('a', 40), "https://codeload.github.com/" + repository + "/zip/" + new string('a', 40), Archive(Source.ToDictionary(p => "fixture-commit/" + p.Key, p => p.Value))));
    }
    public byte[]? ReadRegistry(string root) => Registry?.ToArray();
    public IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id) => Installed[id];
    public void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files)
    {
        if (FailPublish) throw new IOException("Fixture disk is full");
        Installed.Add(id, files.ToDictionary(p => p.Key, p => p.Value.ToArray()));
    }
    public void Delete(string root, string id) => Installed.Remove(id);
    public void SaveRegistry(string root, byte[] bytes)
    {
        if (FailCommit) throw new IOException("Fixture publication failed");
        Registry = bytes.ToArray();
    }
    private static byte[] Archive(Dictionary<string, byte[]> files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var (path, bytes) in files) { using var target = zip.CreateEntry(path).Open(); target.Write(bytes); }
        return memory.ToArray();
    }
}
