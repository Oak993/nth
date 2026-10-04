// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.
using System.Security.Cryptography;
using System.Text;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>A single live chat's immutable disclosure snapshot and SK-5 lease. Caller disposes at session/probe end.</summary>
public sealed class Generation : IDisposable
{
    private readonly StoreLease lease;
    private readonly StoreManifest manifest;
    private readonly StoreVerifier verifier;
    private readonly IGenerationStore store;
    private int disposed;
    internal Generation(StoreLease lease, StoreManifest manifest, IStoreProtection protection, IGenerationStore store,
        string key, IEnumerable<PublishedSkill> skills, IEnumerable<string> leftOut, IEnumerable<string> plugins)
    {
        this.lease = lease; this.manifest = manifest; this.store = store; verifier = new(protection);
        Key = key; Skills = Array.AsReadOnly(skills.ToArray()); LeftOut = Array.AsReadOnly(leftOut.ToArray()); PluginFolders = Array.AsReadOnly(plugins.ToArray());
    }
    public string Folder => lease.Folder;
    public string Key { get; }
    public string? PreviousFolder { get; internal set; }
    internal Action? OnDisposed { get; set; }
    public IReadOnlyList<PublishedSkill> Skills { get; }
    public IReadOnlyList<string> LeftOut { get; }
    // SK-11 passes these additional folders only to plugin-loading providers, each as a separate --plugin-dir argument.
    public IReadOnlyList<string> PluginFolders { get; }
    public string Catalogue => string.Join("\n", Skills.Where(s => !s.ExplicitOnly).Select(s => s.Listing));
    public bool Verify()
    {
        if (Volatile.Read(ref disposed) != 0) return false;
        if (verifier.Verify(Folder, manifest).IsValid) return true;
        store.Retire(Folder);
        return false;
    }
    public string Invoke(string name) => ReadReference(name, "SKILL.md");
    public string ReadReference(string name, string relative) => new UTF8Encoding(false, true).GetString(ReadReferenceBytes(name, relative));
    public byte[] ReadReferenceBytes(string name, string relative)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        StoreManifest.RequireRelative(relative);
        var skill = Skills.SingleOrDefault(s => s.Name == name || s.Id == name) ?? throw new InvalidOperationException("The skill is not in this generation.");
        if (!Verify()) throw new IOException("The generation failed whole-tree verification.");
        var path = Path.Combine(skill.Folder, relative.Replace('/', Path.DirectorySeparatorChar));
        var rootRelative = Path.GetRelativePath(Folder, path).Replace(Path.DirectorySeparatorChar, '/');
        var pinned = manifest.Files.SingleOrDefault(f => f.Path == rootRelative) ?? throw new InvalidOperationException("The file is not in the pinned generation.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.LongLength != pinned.Size || !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(pinned.Sha256, StringComparison.OrdinalIgnoreCase))
        { store.Retire(Folder); throw new IOException("The invoked file changed while it was being read."); }
        return bytes;
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; lease.Dispose(); OnDisposed?.Invoke(); }
}
