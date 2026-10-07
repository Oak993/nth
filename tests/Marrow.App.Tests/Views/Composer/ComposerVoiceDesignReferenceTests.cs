using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, OD-0125: the locked renders of the approved design's pill that V-T133 judges the port against are exactly the files the manifest lists, each with the SHA-256 it was made with.
public sealed class ComposerVoiceDesignReferenceTests
{
    [Fact]
    [Trait("Decision", "ED-571")]
    public void Every_reference_is_the_locked_file_and_nothing_else_is_in_the_folder()
    {
        var folder = VoicePillDesignReference.Folder();
        var files = VoicePillDesignReference.Files();
        Assert.NotEmpty(files);
        foreach (var (name, hash) in files) Assert.True(VoicePillDesignReference.Sha256(Path.Combine(folder, name)) == hash, $"{name} is not the locked reference");
        var present = Directory.GetFiles(folder).Select(Path.GetFileName).Where(n => n != "manifest.json").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal).ToArray(), present);
    }
}
