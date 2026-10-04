// SK-11f / WU-12 (R-TEST-9). The real door to the user's own skills: plain files only, never through a link, on a throwaway folder.
using Marrow.Services.Port.Skills.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class DiskUserSkillFilesTests : IDisposable
{
    private readonly string root = Path.Combine(TestDataDirectory.Read()!, "sk11f-disk-" + Guid.NewGuid().ToString("N"));

    public DiskUserSkillFilesTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(string relative, string text)
    {
        var file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    [Fact, Trait("Rule", "R-TEST-9")]
    public void Folders_holding_a_SKILL_md_are_listed_in_name_order_and_files_are_read_as_text()
    {
        Write("b/SKILL.md", "b");
        Write("a/SKILL.md", "a");
        Write("a/references/x.txt", "REF");
        Write("empty/readme.md", "not a skill");
        var disk = new DiskUserSkillFiles();
        Assert.Equal([Path.Combine(root, "a"), Path.Combine(root, "b")], disk.SkillFolders(root));
        Assert.Equal("REF", disk.ReadText(Path.Combine(root, "a", "references", "x.txt")));
        Assert.Null(disk.ReadText(Path.Combine(root, "a", "references", "missing.txt")));
        Assert.Null(disk.ReadText(Path.Combine(root, "a", "references")));
        Assert.Empty(disk.SkillFolders(Path.Combine(root, "no-such-folder")));
    }

    [Fact, Trait("Rule", "R-TEST-10")]
    public void A_link_is_never_followed_out_of_the_folder()
    {
        var outside = Write("outside/secret.txt", "OUTSIDE SENTINEL");
        Write("a/SKILL.md", "a");
        var link = Path.Combine(root, "a", "link.txt");
        try { File.CreateSymbolicLink(link, outside); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; } // Windows without the privilege: SK-5's NTFS cases cover links
        var directoryLink = Path.Combine(root, "dirlink");
        Directory.CreateSymbolicLink(directoryLink, Path.Combine(root, "outside"));
        var disk = new DiskUserSkillFiles();
        Assert.Null(disk.ReadText(link));
        Assert.Null(disk.ReadText(Path.Combine(directoryLink, "secret.txt")));
        Assert.DoesNotContain(directoryLink, disk.SkillFolders(root));
    }
}
