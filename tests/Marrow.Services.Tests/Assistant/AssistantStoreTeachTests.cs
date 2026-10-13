using System.Diagnostics;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10, 15.3; ED-NEW-dextra-as19-1, -7): the store's teach recordings. Only the named files of a recording
// are read or written, every path is resolved fully and stays strictly inside the recording's own folder, a folder or a file that is a
// link is refused and never followed, whole files are written temp-then-rename, a line cut short by a crash is not read, and a recording
// is deleted by removing its folder.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-1")]
public sealed class AssistantStoreTeachTests : IDisposable
{
    private readonly TeachRig rig = new();

    public void Dispose() => rig.Dispose();

    private static string Id => "t0123456789ab";

    [Theory]
    [InlineData("recording.json")]
    [InlineData("events.jsonl")]
    [InlineData("narration.jsonl")]
    [InlineData("draft.json")]
    [InlineData("frames.jsonl")]
    [InlineData("frames/0007.jpg")]
    public void The_named_files_of_a_recording_are_written_and_read_back(string name)
    {
        rig.Store.WriteTeachFile(TeachRig.Assistant, Id, name, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, rig.Store.ReadTeachFile(TeachRig.Assistant, Id, name));
        Assert.Contains(name, rig.Store.TeachFiles(TeachRig.Assistant, Id));
    }

    [Theory]
    [InlineData("../recording.json")]
    [InlineData("frames/../recording.json")]
    [InlineData("frames/../../x.jpg")]
    [InlineData("..\\recording.json")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("recording.json.")]
    [InlineData("Recording.json")]
    [InlineData("frames/0007.png")]
    [InlineData("frames/7.jpg/x")]
    [InlineData("other/0007.jpg")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public void A_name_outside_the_set_is_refused_for_every_operation(string name)
    {
        Assert.Throws<ArgumentException>(() => rig.Store.WriteTeachFile(TeachRig.Assistant, Id, name, [1]));
        Assert.Throws<ArgumentException>(() => rig.Store.ReadTeachFile(TeachRig.Assistant, Id, name));
        Assert.Throws<ArgumentException>(() => rig.Store.DeleteTeachFile(TeachRig.Assistant, Id, name));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("T0123")]
    [InlineData("")]
    [InlineData("con")]
    public void A_recording_or_assistant_id_that_is_not_an_id_is_refused(string id)
    {
        Assert.Throws<ArgumentException>(() => rig.Store.WriteTeachFile(TeachRig.Assistant, id, "recording.json", [1]));
        Assert.Throws<ArgumentException>(() => rig.Store.WriteTeachFile(id, Id, "recording.json", [1]));
        Assert.Throws<ArgumentException>(() => rig.Store.DeleteTeachRecording(TeachRig.Assistant, id));
    }

    [Fact]
    public void A_file_is_written_temp_then_rename_and_leaves_no_temporary_file()
    {
        rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "draft.json", [1]);
        rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "draft.json", [2, 2]);
        Assert.Equal(new byte[] { 2, 2 }, rig.Store.ReadTeachFile(TeachRig.Assistant, Id, "draft.json"));
        Assert.Empty(Directory.EnumerateFiles(rig.RecordingFolder(Id), ".*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_line_cut_short_by_a_crash_is_not_read_and_complete_lines_are()
    {
        rig.Store.AppendTeachLine(TeachRig.Assistant, Id, "events.jsonl", "{\"a\":1}");
        rig.Store.AppendTeachLine(TeachRig.Assistant, Id, "events.jsonl", "{\"a\":2}");
        File.AppendAllText(Path.Combine(rig.RecordingFolder(Id), "events.jsonl"), "{\"a\":3");
        Assert.Equal(["{\"a\":1}", "{\"a\":2}"], rig.Store.ReadTeachLines(TeachRig.Assistant, Id, "events.jsonl"));
    }

    [Fact]
    public void A_line_has_no_line_break_and_a_whole_file_takes_no_line()
    {
        Assert.Throws<ArgumentException>(() => rig.Store.AppendTeachLine(TeachRig.Assistant, Id, "events.jsonl", "a\nb"));
        Assert.Throws<ArgumentException>(() => rig.Store.AppendTeachLine(TeachRig.Assistant, Id, "draft.json", "{}"));
        Assert.Throws<ArgumentException>(() => rig.Store.ReadTeachLines(TeachRig.Assistant, Id, "draft.json"));
        Assert.Throws<InvalidDataException>(() => rig.Store.AppendTeachLine(TeachRig.Assistant, Id, "events.jsonl", new string('x', AssistantStore.MaxTeachLineBytes)));
    }

    [Fact]
    public void A_file_over_the_size_limit_is_not_written_and_a_missing_one_reads_as_nothing()
    {
        Assert.Throws<InvalidDataException>(() => rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "frames/0001.jpg", new byte[AssistantStore.MaxTeachFileBytes + 1]));
        Assert.Null(rig.Store.ReadTeachFile(TeachRig.Assistant, Id, "frames/0002.jpg"));
        Assert.Empty(rig.Store.ReadTeachLines(TeachRig.Assistant, Id, "events.jsonl"));
    }

    [Fact]
    public void A_recording_is_deleted_by_removing_its_folder_and_only_its_folder()
    {
        rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "recording.json", [1]);
        rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "frames/0001.jpg", TeachRig.Jpeg());
        rig.Store.WriteTeachFile(TeachRig.Assistant, "t000000000002", "recording.json", [9]);
        Assert.Equal(["t000000000002", Id], rig.Store.TeachRecordingIds(TeachRig.Assistant));
        Assert.True(rig.Store.TeachBytes(TeachRig.Assistant, Id) > 0);
        Assert.True(rig.Store.DeleteTeachRecording(TeachRig.Assistant, Id));
        Assert.False(Directory.Exists(rig.RecordingFolder(Id)));
        Assert.Equal(["t000000000002"], rig.Store.TeachRecordingIds(TeachRig.Assistant));
        Assert.False(rig.Store.DeleteTeachRecording(TeachRig.Assistant, Id));
    }

    [Fact]
    public void A_recording_folder_that_is_a_junction_is_refused_and_its_target_is_never_touched()
    {
        var target = Path.Combine(rig.TeachRoot, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "mine");
        Directory.CreateDirectory(Path.Combine(rig.TeachRoot, TeachRig.Assistant));
        var junction = rig.RecordingFolder(Id);
        Assert.True(MakeJunction(junction, target), "the test could not make a junction");
        Assert.Throws<IOException>(() => rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "recording.json", [1]));
        Assert.Throws<IOException>(() => rig.Store.AppendTeachLine(TeachRig.Assistant, Id, "events.jsonl", "{}"));
        Assert.Throws<IOException>(() => rig.Store.ReadTeachFile(TeachRig.Assistant, Id, "recording.json"));
        Assert.Throws<IOException>(() => rig.Store.DeleteTeachRecording(TeachRig.Assistant, Id));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.Equal(["keep.txt"], Directory.EnumerateFiles(target).Select(Path.GetFileName));
    }

    [Fact]
    public void A_frames_folder_that_is_a_junction_is_refused()
    {
        var target = Path.Combine(rig.TeachRoot, "elsewhere");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(rig.RecordingFolder(Id));
        Assert.True(MakeJunction(Path.Combine(rig.RecordingFolder(Id), "frames"), target), "the test could not make a junction");
        Assert.Throws<IOException>(() => rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "frames/0001.jpg", TeachRig.Jpeg()));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public void A_staging_folder_is_made_beside_the_recordings_and_only_such_a_folder_is_removed()
    {
        var stage = rig.Store.NewTeachStaging(TeachRig.Assistant, Id);
        Assert.True(Directory.Exists(stage));
        Assert.Equal(".stage-" + Id, Path.GetFileName(stage));
        Assert.Empty(rig.Store.TeachRecordingIds(TeachRig.Assistant));
        rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "recording.json", [1]);
        Assert.Throws<IOException>(() => rig.Store.DeleteTeachStaging(TeachRig.Assistant, rig.RecordingFolder(Id)));
        Assert.Throws<IOException>(() => rig.Store.DeleteTeachStaging(TeachRig.Assistant, Path.GetTempPath()));
        rig.Store.DeleteTeachStaging(TeachRig.Assistant, stage);
        Assert.False(Directory.Exists(stage));
        rig.Store.NewTeachStaging(TeachRig.Assistant, Id);
        File.WriteAllText(Path.Combine(rig.Store.NewTeachStaging(TeachRig.Assistant, Id), "old.txt"), "x");
        Assert.False(File.Exists(Path.Combine(rig.Store.NewTeachStaging(TeachRig.Assistant, Id), "old.txt")));
        rig.Store.NewTeachStaging(TeachRig.Assistant, "t000000000002");
        Assert.Equal(2, rig.Store.RemoveTeachStagings(TeachRig.Assistant));
        Assert.True(Directory.Exists(rig.RecordingFolder(Id)));
    }

    [Fact]
    public void Temporary_files_a_crash_left_in_the_recordings_are_removed()
    {
        rig.Store.WriteTeachFile(TeachRig.Assistant, Id, "recording.json", [1]);
        File.WriteAllText(Path.Combine(rig.RecordingFolder(Id), ".recording.json." + Guid.NewGuid().ToString("N") + ".tmp"), "half");
        Assert.Equal(1, rig.Store.RemoveTeachTempFiles());
        Assert.Equal(0, rig.Store.RemoveTeachTempFiles());
    }

    private static bool MakeJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        process.WaitForExit(10_000);
        return process.ExitCode == 0 && Directory.Exists(link);
    }
}
