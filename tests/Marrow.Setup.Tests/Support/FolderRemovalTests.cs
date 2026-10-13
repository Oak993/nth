using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 ("Uninstall flow": the data folder only when the switch is on), unit U6
// (ED-NEW-inst-u6-1). Removing a folder tree the way an uninstaller must, in a temporary folder: everything goes, read-only
// files and empty folders included; a junction inside is removed as a link and its target is left whole; a folder that is
// itself a link is not followed; a file in use stays, with its folders, and is reported; a missing folder is not an error;
// a relative path or a drive's root is refused before anything is touched.
public sealed class FolderRemovalTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Everything_goes_read_only_files_and_empty_folders_included()
    {
        using var area = new SweepArea();
        var folder = Path.Combine(area.Base, "data");
        area.Put(Path.Combine(folder, "a.json"));
        File.SetAttributes(area.Put(Path.Combine(folder, "Logs", "nth.log")), FileAttributes.ReadOnly | FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(folder, "empty", "deeper"));
        File.SetAttributes(Path.Combine(folder, "empty"), FileAttributes.Directory | FileAttributes.ReadOnly);

        var result = FolderRemoval.Remove(folder + Path.DirectorySeparatorChar);

        Assert.True(result.Existed);
        Assert.True(result.Gone);
        Assert.Equal(6, result.Removed); // two files, three folders and the folder itself
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_junction_inside_is_removed_as_a_link_and_what_it_points_to_is_left()
    {
        using var area = new SweepArea();
        var outside = Path.Combine(area.Base, "projects", "app");
        var keep = area.Put(Path.Combine(outside, "main.c"));
        var folder = Path.Combine(area.Base, "data");
        area.Put(Path.Combine(folder, "a.json"));
        SweepArea.MakeJunction(Path.Combine(folder, "worktree"), outside);

        var result = FolderRemoval.Remove(folder);

        Assert.True(result.Gone);
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_folder_that_is_itself_a_link_is_not_followed()
    {
        using var area = new SweepArea();
        var target = Path.Combine(area.Base, "elsewhere");
        var keep = area.Put(Path.Combine(target, "keep.txt"));
        var link = Path.Combine(area.Base, "data");
        SweepArea.MakeJunction(link, target);

        var result = FolderRemoval.Remove(link);

        Assert.True(result.Gone);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_file_in_use_stays_with_its_folders_and_is_reported_once()
    {
        using var area = new SweepArea();
        var folder = Path.Combine(area.Base, "data");
        var open = area.Put(Path.Combine(folder, "threads", "a.json"));
        area.Put(Path.Combine(folder, "threads", "b.json"));
        area.Put(Path.Combine(folder, "library.json"));
        area.Hold(open);

        var result = FolderRemoval.Remove(folder);

        Assert.False(result.Gone);
        Assert.Equal([open], result.Kept);
        Assert.Equal(2, result.Removed);
        Assert.True(File.Exists(open));
        Assert.False(File.Exists(Path.Combine(folder, "threads", "b.json")));
        Assert.False(File.Exists(Path.Combine(folder, "library.json")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_missing_folder_is_already_gone_and_a_file_in_its_place_is_left()
    {
        using var area = new SweepArea();
        var absent = FolderRemoval.Remove(Path.Combine(area.Base, "never"));
        Assert.False(absent.Existed);
        Assert.True(absent.Gone);
        Assert.Equal(0, absent.Removed);

        var file = area.Put(Path.Combine(area.Base, "data"));
        var result = FolderRemoval.Remove(file);
        Assert.Equal([file], result.Kept);
        Assert.True(File.Exists(file));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_relative_path_or_a_drives_root_is_refused()
    {
        Assert.Throws<ArgumentException>(() => FolderRemoval.Remove(@"Local\nth"));
        Assert.Throws<ArgumentException>(() => FolderRemoval.Remove(""));
        Assert.Throws<ArgumentException>(() => FolderRemoval.Remove(@"C:\"));
        Assert.Throws<ArgumentException>(() => FolderRemoval.Remove(@"Z:\"));
    }
}
