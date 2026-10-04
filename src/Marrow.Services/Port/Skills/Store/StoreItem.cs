// PORT-ONLY: SK-5. Immutable installed-pin descriptor. No disk manifest is a trust root (ED-NEW-f-sk-5).
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Store;

public sealed class StoreItem
{
    public StoreItem(CatalogueItem catalogue, string folder, StoreManifest manifest, string? installedPin = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("An absolute installed folder is required.", nameof(folder));
        if (catalogue.PinState != PinState.Pinned || CatalogueGate.Missing(catalogue).Count != 0)
            throw new ArgumentException("The installed catalogue descriptor must be fully pinned.", nameof(catalogue));
        Pin = installedPin ?? catalogue.Pin ?? throw new ArgumentException("Multi-pin sets require an explicit installed pin key.", nameof(installedPin));
        if (string.IsNullOrWhiteSpace(Pin) || (catalogue.Pin is not null && Pin != catalogue.Pin))
            throw new ArgumentException("The installed pin must match the frozen catalogue descriptor.", nameof(installedPin));
        if (catalogue.Kind is ItemKind.Skillset or ItemKind.Plugin)
        {
            var expected = new StoreManifest(catalogue.Files);
            if (!expected.Files.OrderBy(f => f.Path, StringComparer.Ordinal).SequenceEqual(manifest.Files.OrderBy(f => f.Path, StringComparer.Ordinal)))
                throw new ArgumentException("A vendor set's inventory must match every embedded catalogue file.", nameof(manifest));
        }
        Catalogue = catalogue with
        {
            Needs = Array.AsReadOnly(catalogue.Needs.ToArray()), Provides = Array.AsReadOnly(catalogue.Provides.ToArray()),
            Artifacts = Array.AsReadOnly(catalogue.Artifacts.ToArray()), Files = Array.AsReadOnly(catalogue.Files.ToArray()),
            Folders = Array.AsReadOnly(catalogue.Folders.ToArray()), Packages = Array.AsReadOnly(catalogue.Packages.ToArray()),
            Skills = Array.AsReadOnly(catalogue.Skills.Select(s => s with { Needs = Array.AsReadOnly(s.Needs.ToArray()) }).ToArray()),
        };
        Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        Manifest = manifest;
    }

    public CatalogueItem Catalogue { get; }
    public string Pin { get; }
    public string Folder { get; }
    public StoreManifest Manifest { get; }
}
