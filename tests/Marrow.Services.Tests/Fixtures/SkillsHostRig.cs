// PORT-ONLY: SK-13. Fresh data roots under MARROW_PORT_DATA_DIR and fake-protection host options for the composition tests. Nothing reaches a real user folder, a network or a CLI.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Terms;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Fixtures;

internal static class SkillsHostRig
{
    public static string FreshRoot()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent)) throw new InvalidOperationException("SK-13 tests require MARROW_PORT_DATA_DIR naming a disposable absolute folder.");
        var root = Path.Combine(parent, "sk13 host " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    public static SkillsCatalogue Catalogue(params CatalogueItem[] items) => new() { Schema = 1, ReadOn = "2026-10-05", Items = items };

    /// <summary>Host options with the pipeline rig's fake protection (the double that is both the store protection and the install lock), no download or binding seam and a scripted licence text.</summary>
    public static SkillsHostOptions Options(PipelineProtection protection, SkillsCatalogue catalogue, ILicenceText? licence = null, TimeProvider? clock = null) => new()
    {
        Catalogue = catalogue, Protection = protection, InstallLock = protection, LicenceText = licence, Clock = clock,
        ClashSource = _ => [],
    };

    public static SkillsHostOptions Options(PipelineRig rig, params CatalogueItem[] items) => Options((PipelineProtection)rig.Protection, Catalogue(items));

    public static GenerationItem Item(PipelineRig rig, string pin) =>
        new(rig.Trusted(pin), [new GenerationSkill("fixture", "skill", "skill", "Fixture description", SkillGroup.Documents, 0, false)]);

    public static SkillSwitches Switches() => new("fixture", new Dictionary<string, bool>(), new Dictionary<string, bool>());

    /// <summary>Unlock everything the fake locked and delete a fresh root (a read-only file would stop a plain delete).</summary>
    public static void Delete(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }
}
