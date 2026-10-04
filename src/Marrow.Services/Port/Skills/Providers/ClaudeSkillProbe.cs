// PORT-ONLY: SK-11a owns an SK-9 listing lease until Claude's command probe exits.
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Port.Skills.Providers;

/// <summary>Created immediately after SkillGenerations.Listing verifies its snapshot. Plugin roots come from the same installed descriptors and switches.</summary>
public sealed class ClaudeSkillProbe : IDisposable
{
    private readonly GenerationListing listing;
    private readonly Func<GenerationListing> acquire;
    public ClaudeSkillProbe(Func<GenerationListing> acquire, IEnumerable<GenerationItem> items, SkillSwitches switches)
    {
        this.acquire = acquire;
        listing = acquire();
        try
        {
            PluginFolders = Array.AsReadOnly(items.SelectMany(item => item.PluginRoots
                .Where(root => switches.PluginEnabled(item.Store.Catalogue.Id, root))
                .Select(root => "plugins/" + item.Store.Catalogue.Id + (root.Length == 0 ? "" : "/" + root)))
                .Select(root => { StoreManifest.RequireRelative(root); return Path.Combine(Folder, root.Replace('/', Path.DirectorySeparatorChar)); })
                .Where(Directory.Exists).ToArray());
            HasContent = listing.Skills.Count != 0 || PluginFolders.Count != 0;
        }
        catch { listing.Dispose(); throw; }
    }
    public string Folder => listing.Folder;
    public bool Verify()
    {
        // Reacquiring the same live listing asks SK-9 to verify against its trusted manifest.
        // Do not reconstruct a trusted manifest from bytes found on disk.
        try { using var check = acquire(); return StorePaths.Comparer.Equals(check.Folder, Folder); }
        catch (IOException) { return false; }
    }
    public IReadOnlyList<string> PluginFolders { get; }
    public bool HasContent { get; }
    public void Dispose() => listing.Dispose();
}
