// PORT-ONLY: RR-18 / OD-0099. Custom additions are owned copies, distinct from provider imports (OD-0118).
namespace Marrow.Services.Port.Skills.Custom;

public enum CustomAddKind { Plugins, Skills, Tools }
public enum CustomAddSource { Folder, Zip, GitHub }
public sealed record CustomAddRequest(CustomAddKind Kind, CustomAddSource Source, string Location, string Name, string Command);
public sealed record CustomAddCandidate(string Key, string Name, string Description, string? Problem);
public sealed record CustomProvenance(CustomAddSource Source, string Location, string? Commit, string? ArtifactUrl, string SourceSha256, DateTimeOffset CheckedAt);
public sealed record CustomFilePin(string Path, long Size, string Sha256);
public sealed record OwnItem(string Id, CustomAddKind Kind, string Name, string Description, string? Command, string? Executable, bool Enabled, CustomProvenance Provenance, CustomFilePin[] Files);
public sealed record CustomCommand(string Name, string Entry, string ItemId);
public sealed record CustomGitHubArchive(string Commit, string Url, byte[] Bytes);

public interface ICustomAddBackend
{
    Task<IReadOnlyList<CustomAddCandidate>> Check(CustomAddRequest request, CancellationToken cancel);
    Task<OwnItem> Add(CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel);
}

/// <summary>All filesystem effects; tests use an in-memory implementation. Publish must create a new immutable item directory, SaveRegistry must atomically replace the registry.</summary>
public interface ICustomSourceFiles
{
    Task<IReadOnlyDictionary<string, byte[]>> ReadFolder(string path, CancellationToken cancel);
    Task<byte[]> ReadZip(string path, CancellationToken cancel);
    byte[]? ReadRegistry(string root);
    IReadOnlyDictionary<string, byte[]> ReadInstalled(string root, string id);
    void Publish(string root, string id, IReadOnlyDictionary<string, byte[]> files);
    void Delete(string root, string id);
    void SaveRegistry(string root, byte[] bytes);
}

/// <summary>Resolve a public repository to a full commit, then fetch its archive. No clone, credentials, shell or install scripts.</summary>
public interface ICustomGitHubSource
{
    Task<CustomGitHubArchive> Fetch(string repository, CancellationToken cancel);
}
