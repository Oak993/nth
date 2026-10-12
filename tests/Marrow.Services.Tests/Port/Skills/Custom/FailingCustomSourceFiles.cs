using Marrow.Services.Port.Skills.Custom;

namespace Marrow.Services.Tests.Port.Skills.Custom;

// ED-NEW-mcpimport-6: the in-memory files of CustomSourceFake, with the failures a real disk can have.
internal sealed class FailingCustomSourceFiles(CustomSourceFake inner) : ICustomSourceFiles, ICustomGitHubSource
{
    public bool FailPublish { get; set; }
    public string? LockedId { get; set; }
    public int Publishes { get; private set; }
    public Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel) => inner.ReadFolder(path, cancel);
    public Task<byte[]> ReadZip(string path, CancellationToken cancel) => inner.ReadZip(path, cancel);
    public Task<CustomGitHubArchive> Fetch(string repository, CancellationToken cancel) => inner.Fetch(repository, cancel);
    public byte[]? ReadRegistry(string root) => inner.ReadRegistry(root);
    public IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id) => inner.ReadInstalled(root, id);
    public void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files)
    {
        Publishes++;
        if (FailPublish) throw new IOException("Fixture disk is full");
        inner.Publish(root, id, files);
    }
    public void Delete(string root, string id)
    {
        if (id == LockedId) throw new IOException("Fixture file is locked");
        inner.Delete(root, id);
    }
    public void SaveRegistry(string root, byte[] bytes) => inner.SaveRegistry(root, bytes);
}
