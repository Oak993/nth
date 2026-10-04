// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

internal static class SpareGenerationBuilder
{
    public static IReadOnlyList<InstallTransaction> Prepare(InstallPlan plan, string store, IInstallLock seal, Action<InstallStep, string>? checkpoint, List<InstallTransaction> transactions, Func<string, IDisposable>? reserve = null)
    {
        var result = new List<InstallTransaction>();
        foreach (var projection in plan.Spares ?? throw new InvalidDataException("Spare projections are required for a skill set."))
        {
            var provider = InstallPaths.Relative(projection.Provider);
            if (provider.Contains('/', StringComparison.Ordinal)) throw new InvalidDataException("Provider must be a path component.");
            var key = InstallPaths.Relative(projection.Key);
            if (key.Contains('/', StringComparison.Ordinal)) throw new InvalidDataException("Spare key must be a path component.");
            var parent = InstallPaths.Under(plan.DataRoot, "skills/views/" + provider);
            var spare = new InstallTransaction(parent, "spare-" + key + "-" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)), seal, reserve);
            transactions.Add(spare);
            checkpoint?.Invoke(InstallStep.Created, spare.Path);
            if (Directory.EnumerateFileSystemEntries(spare.Path).Any()) throw new InvalidDataException("New spare folder was modified.");
            var expected = new List<Marrow.Core.Port.Skills.Catalogue.CatalogueFile>();
            foreach (var relative in projection.Files)
            {
                var pin = plan.Item.Files.SingleOrDefault(f => f.Path == relative) ?? throw new InvalidDataException("Spare file is not in the catalogue.");
                var destination = projection.Prefix is null ? relative : projection.Prefix + "/" + relative;
                InstallHash.Verify(InstallPaths.Under(store, relative), pin.Size, pin.Sha256);
                InstallTree.Copy(InstallPaths.Under(store, relative), InstallPaths.Under(spare.Path, destination));
                expected.Add(pin with { Path = destination });
            }
            foreach (var generated in projection.GeneratedFiles)
            {
                var target = InstallPaths.Under(spare.Path, generated.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target)) throw new InvalidDataException("Generated spare file collides with a copied file.");
                File.WriteAllText(target, generated.Value, new System.Text.UTF8Encoding(false));
                var bytes = System.Text.Encoding.UTF8.GetBytes(generated.Value);
                expected.Add(new Marrow.Core.Port.Skills.Catalogue.CatalogueFile { Path = generated.Key, Size = bytes.Length, Sha256 = InstallHash.Sha256(bytes) });
            }
            spare.Prepare(checkpoint, expected);
            result.Add(spare);
        }
        return result;
    }
}
