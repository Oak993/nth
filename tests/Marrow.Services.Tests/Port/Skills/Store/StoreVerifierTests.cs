using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Store;

// PORT-ONLY: DL-5; PLAN 2.1 whole-tree set comparison; ONDEMAND 3.3 and 3.5. No vendor fixture.
[Trait("Feature", "SK-5")]
[Trait("Rule", "R-TEST-9")]
[Trait("Spec", "DL-5")]
public sealed class StoreVerifierTests
{
    [Theory]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData("tools", ItemKind.Tool)]
    [InlineData("store", ItemKind.Skillset)]
    [InlineData("store", ItemKind.Plugin)]
    public void The_complete_locked_tree_verifies_in_both_data_layouts(string area, ItemKind kind)
    {
        using var f = new StoreFixture();
        var item = f.Create(area, kind: kind);
        Assert.True(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
    }

    [Theory]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData("json.py")]
    [InlineData("sample/scripts/__pycache__/run.cpython-314.pyc")]
    [InlineData(".hidden")]
    public void Any_extra_file_is_damage_even_when_hidden_or_bytecode(string extra)
    {
        using var f = new StoreFixture();
        var item = f.Create();
        f.Tamper(item, extra, [1]);
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
    }

    [Theory]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData("empty")]
    [InlineData("sample-workspace")]
    [InlineData("sample/scripts/__pycache__")]
    public void An_empty_extra_folder_is_damage(string extra)
    {
        using var f = new StoreFixture();
        var item = f.Create();
        f.Trees.Unlock(item.Folder);
        Directory.CreateDirectory(Path.Combine(item.Folder, extra));
        f.Trees.Lock(item.Folder);
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
    }

    [Theory]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_changed_size_or_same_size_changed_hash_is_damage(bool sameSize)
    {
        using var f = new StoreFixture();
        var item = f.Create();
        f.Tamper(item, "sample/native/fixture.exe", sameSize ? [9, 9, 9, 9] : [9]);
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_missing_file_or_missing_root_is_damage()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        f.Trees.Unlock(item.Folder);
        File.Delete(Path.Combine(item.Folder, "LICENSE.txt"));
        f.Trees.Lock(item.Folder);
        var verifier = new StoreVerifier(f.Protection);
        Assert.False(verifier.Verify(item.Folder, item.Manifest).IsValid);
        Assert.False(verifier.Verify(Path.Combine(f.Root, "absent"), item.Manifest).IsValid);
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Missing_deny_entries_read_only_attributes_or_stream_inspection_fail_closed()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var verifier = new StoreVerifier(f.Protection);
        f.Fake.Unlock(item.Folder);
        Assert.False(verifier.Verify(item.Folder, item.Manifest).IsValid);
        f.Trees.Lock(item.Folder);
        File.SetAttributes(Path.Combine(item.Folder, "LICENSE.txt"), FileAttributes.Normal);
        Assert.False(verifier.Verify(item.Folder, item.Manifest).IsValid);
        f.Trees.Lock(item.Folder);
        f.Fake.Refuse = true;
        try { Assert.False(verifier.Verify(item.Folder, item.Manifest).IsValid); }
        finally { f.Fake.Refuse = false; }
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_named_stream_reported_by_the_platform_is_damage()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        f.Fake.Streams.Add(Path.Combine(item.Folder, "LICENSE.txt"));
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
    }

    [Theory]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_file_or_directory_symlink_is_refused_without_following_it(bool directory)
    {
        using var f = new StoreFixture();
        var item = f.Create();
        f.Trees.Unlock(item.Folder);
        if (directory) TestLinks.Directory(Path.GetDirectoryName(f.Root)!, Path.Combine(item.Folder, "link"), f.Root);
        else TestLinks.File(Path.Combine(item.Folder, "link"), Path.Combine(f.Root, "not-read"));
        Assert.False(new StoreVerifier(f.Protection).Verify(item.Folder, item.Manifest).IsValid);
        Assert.Throws<IOException>(() => f.Trees.Lock(item.Folder));
    }

    [Theory]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/drive")]
    [InlineData("a:stream")]
    [InlineData("a\\b")]
    [InlineData("a/./b")]
    [InlineData("a/../b")]
    [InlineData("a//b")]
    public void Unsafe_manifest_paths_are_refused_before_IO(string path)
    {
        Assert.Throws<ArgumentException>(() => new StoreManifest([new CatalogueFile { Path = path, Size = 1, Sha256 = new string('a', 64) }]));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void A_manifest_is_an_immutable_case_insensitive_set_and_not_an_on_disk_trust_root()
    {
        var list = new List<CatalogueFile> { new() { Path = "a", Size = 1, Sha256 = new string('a', 64) } };
        var manifest = new StoreManifest(list);
        list.Clear();
        Assert.Single(manifest.Files);
        Assert.Throws<ArgumentException>(() => new StoreManifest([manifest.Files[0], manifest.Files[0] with { Path = "A" }]));
        Assert.Throws<ArgumentException>(() => new StoreManifest([]));
        Assert.Throws<ArgumentException>(() => new StoreManifest([manifest.Files[0] with { Sha256 = "unknown" }]));
    }

    [Fact]
    [Trait("Feature", "SK-5")]
    [Trait("Rule", "R-TEST-9")]
    public void Seal_checks_binaries_on_every_launch_and_full_verify_also_checks_text()
    {
        using var f = new StoreFixture();
        var item = f.Create();
        var verifier = new StoreVerifier(f.Protection);
        Assert.True(verifier.Verify(item.Folder, item.Manifest, sealOnly: true).IsValid);
        f.Tamper(item, "sample/scripts/run.py", new byte[StoreFixture.Content["sample/scripts/run.py"].Length]);
        Assert.True(verifier.Verify(item.Folder, item.Manifest, sealOnly: true).IsValid);
        Assert.False(verifier.Verify(item.Folder, item.Manifest).IsValid);
    }
}
