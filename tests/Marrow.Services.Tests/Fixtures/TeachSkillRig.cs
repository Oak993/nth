using Marrow.Services.Port.Skills.Custom;
using Marrow.Services.Tests.Port.Skills.Custom;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>Dextra AS-19: the own-skill service over an in-memory store that reads the real staging folder the writer made, as the disk service does.</summary>
internal sealed class TeachSkillRig : ICustomSourceFiles
{
    private readonly CustomSourceFake inner = new();
    private readonly DiskCustomSourceFiles disk = new();

    public TeachSkillRig() => Sources = new CustomSources("/fake/own", this, inner, TimeProvider.System);

    public CustomSources Sources { get; }

    public Dictionary<string, Dictionary<string, byte[]>> Installed => inner.Installed;

    public bool FailCommit
    {
        get => inner.FailCommit;
        set => inner.FailCommit = value;
    }

    public Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel) => disk.ReadFolder(path, cancel);

    public Task<byte[]> ReadZip(string path, CancellationToken cancel) => inner.ReadZip(path, cancel);

    public byte[]? ReadRegistry(string root) => inner.ReadRegistry(root);

    public IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id) => inner.ReadInstalled(root, id);

    public void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files) => inner.Publish(root, id, files);

    public void Delete(string root, string id) => inner.Delete(root, id);

    public void SaveRegistry(string root, byte[] bytes) => inner.SaveRegistry(root, bytes);
}
