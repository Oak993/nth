// PORT-ONLY: SK-17 review finding 7. Real host/composer/wrapping boundary with an offline inconsistent pinned disclosure.
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.App.Tests.Port.Skills;

[Trait("Decision", "ED-326"), Trait("Rule", "R-TEST-4")]
public sealed class SkillsPreparationErrorTests
{
    [Theory]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Cursor)]
    public async Task A_corrupt_pinned_disclosure_becomes_a_localized_provider_failure_through_real_preparation(ProviderKind provider)
    {
        using var files = new GenerationFixture();
        var original = files.Add("O3", "pdf").Store;
        var skillFile = Path.Combine(original.Folder, "pdf", "SKILL.md");
        files.Trees.Unlock(original.Folder);
        var bytes = Encoding.UTF8.GetBytes("---\nname: corrupt-pdf\ndescription: Offline corrupt pin fixture\n---\nBODY\n");
        File.WriteAllBytes(skillFile, bytes);
        files.Trees.Lock(original.Folder);
        // The inventory verifies byte-for-byte; the corrupt pin disagrees semantically with its frozen skill name.
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var catalogue = original.Catalogue with
        {
            Files = original.Catalogue.Files.Select(f => f.Path == "pdf/SKILL.md" ? f with { Size = bytes.Length, Sha256 = hash } : f).ToArray(),
        };
        var pin = new StoreItem(catalogue, original.Folder, new StoreManifest(catalogue.Files));
        PipelineRoutes.Prepare(files.Paths.StateFile, new(pin.Catalogue.Id, pin.Pin, pin.Folder, [], []));
        AddonsStateStore.Save(files.Paths.StateFile, AddonsState.Restore([new ItemRecord(pin.Catalogue.Id, ItemState.Installed, pin.Pin, null, null)], [], [], []));
        using var host = SkillsHost.Open(files.Paths.Root, new SkillsHostOptions
        {
            Catalogue = new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-06", Items = [catalogue] },
            Protection = files.Protection, InstallLock = new WindowsInstallLock(),
            Trust = (_, _, _) => new([pin], new Dictionary<(string, string), IReadOnlyList<string>>(), []),
        });
        using var storage = Storage.Locations.Override(StorageLocations.Create(files.Paths.Root, Path.Combine(files.Paths.Root, "home")));
        var project = Path.Combine(files.Paths.Root, "project");
        Directory.CreateDirectory(project);
        Assert.Same(host, SkillsHost.Find(Storage.Root));
        using (var lease = host.Pipeline.AcquireForViewBuild(pin.Catalogue.Id)) Assert.NotNull(lease);
        var raw = Assert.Throws<InvalidDataException>(() =>
        {
            using var generation = provider == ProviderKind.Cursor
                ? CursorSkillsComposer.Prepare(host, "raw", project, files.Paths.Root, files.Budget)
                : AntigravitySkillsComposer.Prepare(host, "raw", project, files.Paths.Root, files.Budget);
        });
        Assert.Equal("Pinned skill name differs from the catalogue.", raw.Message);
        var type = typeof(ThreadRuntime).Assembly.GetType(provider == ProviderKind.Cursor
            ? "Marrow.App.Port.Skills.CursorSkills" : "Marrow.App.Port.Skills.AntigravitySkills")!;
        var boundary = (Ambient<Func<string, string, Task<Generation>>>)type.GetProperty("Prepare", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal("PrepareOwned", boundary.Current.Method.Name); // No replacement of the wrapping boundary.
        var error = await Assert.ThrowsAsync<ProviderError>(() => boundary.Current("wrapped", project));
        Assert.Equal(raw.Message, Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase).Message);
        Assert.Equal(raw.Message, SwiftErrors.LocalizedDescription(error));
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(skillFile))));
        Assert.False(Directory.Exists(host.Paths.Views));
    }
}
