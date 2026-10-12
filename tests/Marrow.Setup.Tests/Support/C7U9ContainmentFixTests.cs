using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// C7b expressly authorizes junctions inside disposable temp folders. No child process or settings change.
public sealed class C7U9ContainmentFixTests : IDisposable
{
    private readonly SweepArea area = new();
    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Ensure_does_not_delete_a_foreign_claim_file_through_a_refused_root_junction()
    {
        var foreign = Path.Combine(area.Base, "foreign");
        var marker = area.Put(Path.Combine(foreign, "1.0.0", SetupFolders.ClaimFileName), "another app's marker");
        C7DisposableJunction.Create(area.Base, area.SetupRoot, foreign);
        try
        {
            using var scope = StagingScope.Create(area.LocalAppData, area.Temp, "1.0.0", lastChance: false);
            var error = Record.Exception(() => { _ = scope.Ensure(); });
            scope.Dispose();
            Assert.True(File.Exists(marker), "the refused root junction must not permit a deleting claim in its foreign target");
            Assert.Equal("another app's marker", File.ReadAllText(marker));
            Assert.IsAssignableFrom<IOException>(error);
        }
        finally { Directory.Delete(area.SetupRoot, recursive: false); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Taking_a_claim_never_opens_a_foreign_existing_file_for_delete()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        var marker = area.Put(Path.Combine(folder, SetupFolders.ClaimFileName), "foreign marker");
        var error = Record.Exception(() => { using var claim = FolderClaim.Take(folder); });
        Assert.True(File.Exists(marker), "nth must not delete an unmarked preexisting claim file");
        Assert.Equal("foreign marker", File.ReadAllText(marker));
        Assert.IsAssignableFrom<IOException>(error);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_sweep_preserves_a_foreign_claim_even_inside_an_owned_unit()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        area.Put(Path.Combine(folder, ".nth-setup-owner"), "nth Setup owned unit v1\n");
        var foreign = area.Put(Path.Combine(folder, SetupFolders.ClaimFileName), "foreign marker");
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);
        Assert.True(File.Exists(foreign), "a parent marker never authorizes removal of a foreign claim");
        Assert.Equal("foreign marker", File.ReadAllText(foreign));
        Assert.False(result.Clean);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Unmarked_legacy_units_are_preserved_and_reported_instead_of_adopted()
    {
        var foreign = area.Put(Path.Combine(area.SetupRoot, "1.0.0", SetupFolders.EngineFileName), "not proven nth-created");
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);
        Assert.True(File.Exists(foreign), "the reserved folder and filename cannot prove ownership");
        Assert.False(result.Clean);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Unknown_payload_inside_an_owned_unit_and_unknown_loose_files_are_preserved()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        area.Put(Path.Combine(folder, ".nth-setup-owner"), "nth Setup owned unit v1\n");
        var foreign = area.Put(Path.Combine(folder, "family-photo.jpg"), "foreign photo");
        var loose = area.Put(Path.Combine(area.SetupRoot, "personal.txt"), "foreign note");
        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);
        Assert.True(File.Exists(foreign), "the unit marker cannot authorize arbitrary unknown payloads");
        Assert.True(File.Exists(loose));
        Assert.False(result.Clean);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_new_claim_writes_persistent_unit_provenance_before_payload_is_allowed()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        using var claim = FolderClaim.Take(folder);
        var marker = Path.Combine(folder, ".nth-setup-owner");
        Assert.True(File.Exists(marker), "the actual writer gate must record provenance before returning to its payload writer");
        Assert.Equal("nth Setup owned unit v1\n", File.ReadAllText(marker));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_existing_owned_claim_is_validated_without_opening_it_for_delete()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        area.Put(Path.Combine(folder, ".nth-setup-owner"), "nth Setup owned unit v1\n");
        var marker = area.Put(Path.Combine(folder, SetupFolders.ClaimFileName), "nth Setup claim v1\n");
        using (var claim = FolderClaim.Take(folder)) Assert.True(File.Exists(marker));
        Assert.True(File.Exists(marker), "an existing file must never acquire delete-on-close while its ownership is being checked");
        Assert.Equal("nth Setup claim v1\n", File.ReadAllText(marker));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Taking_a_claim_refuses_a_foreign_marker_even_when_the_unit_has_provenance()
    {
        var folder = Path.Combine(area.SetupRoot, "1.0.0");
        area.Put(Path.Combine(folder, ".nth-setup-owner"), "nth Setup owned unit v1\n");
        var marker = area.Put(Path.Combine(folder, SetupFolders.ClaimFileName), "foreign marker");
        var error = Record.Exception(() => { using var claim = FolderClaim.Take(folder); });
        Assert.True(File.Exists(marker), "own unit provenance does not authorize opening its foreign claim for delete");
        Assert.Equal("foreign marker", File.ReadAllText(marker));
        Assert.IsAssignableFrom<IOException>(error);
    }
}
