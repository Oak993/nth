using Marrow.Services.Port.Skills.Custom;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-19: the own-skill service over memory. It reads the staging folder the writer made from the real disk, as the disk service does,
/// and keeps the installed files and the registry in memory. Helper code only (R-TEST-3).
/// </summary>
internal sealed class TeachSkillStore : ICustomSourceFiles, ICustomGitHubSource
{
    private readonly DiskCustomSourceFiles disk = new();

    public TeachSkillStore() => Sources = new CustomSources("/fake/own", this, this, TimeProvider.System);

    public CustomSources Sources { get; }

    public Dictionary<string, Dictionary<string, byte[]>> Installed { get; } = [];

    public byte[]? Registry { get; private set; }

    public bool FailCommit { get; set; }

    public Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel) => disk.ReadFolder(path, cancel);

    public Task<byte[]> ReadZip(string path, CancellationToken cancel) => throw new NotSupportedException();

    public Task<CustomGitHubArchive> Fetch(string repository, CancellationToken cancel) => throw new NotSupportedException();

    public byte[]? ReadRegistry(string root) => Registry?.ToArray();

    public IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id) => Installed[id];

    public void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files) => Installed.Add(id, files.ToDictionary(p => p.Key, p => p.Value.ToArray()));

    public void Delete(string root, string id) => Installed.Remove(id);

    public void SaveRegistry(string root, byte[] bytes)
    {
        if (FailCommit) throw new IOException("Fixture commit failure");
        Registry = bytes.ToArray();
    }
}
