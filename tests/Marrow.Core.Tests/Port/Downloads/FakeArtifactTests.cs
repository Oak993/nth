using System.IO.Compression;
using System.Security.Cryptography;
using Marrow.Testing.Downloads;

namespace Marrow.Core.Tests.Port.Downloads;

// SK-0 (DL-0, skills-ONDEMAND-PLAN.md section 7): the fixture artifacts the fake download server serves. No network, no file.
public sealed class FakeArtifactTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public void A_pattern_artifact_is_the_same_bytes_on_every_call_and_differs_by_seed()
    {
        var first = FakeArtifact.Pattern(4096, seed: 3);
        var again = FakeArtifact.Pattern(4096, seed: 3);
        var other = FakeArtifact.Pattern(4096, seed: 4);

        Assert.Equal(4096, first.Length);
        Assert.Equal(first.Bytes, again.Bytes);
        Assert.NotEqual(first.Bytes, other.Bytes);
        Assert.NotEqual(first.Sha256Hex, other.Sha256Hex);
        Assert.False(first.Bytes.All(b => b == first.Bytes[0]), "the pattern is not a run of one byte");
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public void The_pinned_hash_is_the_sha256_of_the_bytes_in_lower_case_hex()
    {
        var artifact = FakeArtifact.FromText("abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", artifact.Sha256Hex);
        Assert.Equal(SHA256.HashData(artifact.Bytes), artifact.Sha256);
        Assert.Equal(3, artifact.Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public void A_zip_artifact_holds_the_entries_as_given_and_is_the_same_bytes_on_every_call()
    {
        var entries = new (string Path, byte[] Content)[]
        {
            ("skills/a/SKILL.md", "---\nname: a\n---\n"u8.ToArray()),
            ("../escape.txt", "x"u8.ToArray()),
        };
        var zip = FakeArtifact.Zip(entries);

        Assert.Equal(zip.Bytes, FakeArtifact.Zip(entries).Bytes);
        using var archive = new ZipArchive(new MemoryStream(zip.Bytes));
        Assert.Equal(["skills/a/SKILL.md", "../escape.txt"], archive.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(archive.Entries[0].Open());
        Assert.Equal("---\nname: a\n---\n", reader.ReadToEnd());
    }
}
