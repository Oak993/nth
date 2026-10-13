using System.Text;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 16.1 "Task Scheduler"; ED-NEW-dextra-as10-15): the schtasks.exe host. Every call is an argument list
// (no shell), the name is checked before it reaches a command line, the task file is UTF-16 with a byte order mark and is deleted
// afterwards, and a refusal comes back as the first line schtasks said. The real program runs only in the Native test.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-15")]
public sealed class SchtasksHostTests
{
    private const string Windows = @"C:\Windows\System32";

    private static ProcessOutcome Fine(string output = "") => new(0, output, "");

    [Fact]
    public async Task Register_writes_a_utf16_task_file_asks_schtasks_to_create_it_and_deletes_the_file()
    {
        var runner = new FakeProcessRunner((_, _) => Fine("SUCCESS"));
        var host = new SchtasksHost(runner, Windows);
        await host.Register("duty-d1", "<Task>é</Task>", CancellationToken.None);
        var (file, arguments) = Assert.Single(runner.Runs);
        Assert.Equal(@"C:\Windows\System32\schtasks.exe", file);
        Assert.Equal(["/Create", "/TN", @"\nth\duty-d1", "/XML", arguments[4], "/F"], arguments);
        Assert.False(File.Exists(arguments[4])); // gone after the run
        var bytes = runner.XmlFile!;
        Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes.Take(2).ToArray()); // UTF-16 little-endian with a byte order mark
        Assert.Equal("<Task>é</Task>", Encoding.Unicode.GetString(bytes.Skip(2).ToArray()));
    }

    [Fact]
    public async Task A_refusal_is_the_first_line_schtasks_said()
    {
        var runner = new FakeProcessRunner((_, _) => new ProcessOutcome(1, "", "ERROR: Access is denied.\r\nMore words."));
        var host = new SchtasksHost(runner, Windows);
        var error = await Assert.ThrowsAsync<WakeEntryException>(() => host.Register("duty-d1", "<x/>", CancellationToken.None));
        Assert.Equal("ERROR: Access is denied.", error.Message);
    }

    [Theory]
    [InlineData("other-task")]
    [InlineData(@"duty-..\..\x")]
    [InlineData("duty-a b")]
    [InlineData("duty-")]
    [InlineData("")]
    public async Task A_name_that_is_not_nths_never_reaches_a_command_line(string name)
    {
        var runner = new FakeProcessRunner((_, _) => Fine());
        var host = new SchtasksHost(runner, Windows);
        await Assert.ThrowsAsync<WakeEntryException>(() => host.Register(name, "<x/>", CancellationToken.None));
        await Assert.ThrowsAsync<WakeEntryException>(() => host.Delete(name, CancellationToken.None));
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task List_reads_only_the_names_under_nth_that_nth_made()
    {
        const string csv = "\"\\nth\\duty-d1\",\"10/14/2026 8:00:00 AM\",\"Ready\"\r\n\"\\nth\\duty-d2\",\"N/A\",\"Disabled\"\r\n"
            + "\"\\nth\\someone-else\",\"N/A\",\"Ready\"\r\n\"\\Other\\duty-d3\",\"N/A\",\"Ready\"\r\n\r\nINFO: junk line\r\n";
        var runner = new FakeProcessRunner((_, _) => Fine(csv));
        var host = new SchtasksHost(runner, Windows);
        Assert.Equal(["duty-d1", "duty-d2"], await host.List(CancellationToken.None));
        Assert.Equal(["/Query", "/FO", "CSV", "/NH", "/TN", @"\nth\"], Assert.Single(runner.Runs).Arguments);
    }

    [Fact]
    public async Task A_missing_folder_is_no_entries_yet()
    {
        var runner = new FakeProcessRunner((_, _) => new ProcessOutcome(1, "", "ERROR: The system cannot find the path specified."));
        Assert.Empty(await new SchtasksHost(runner, Windows).List(CancellationToken.None));
    }

    [Fact]
    public async Task Delete_asks_for_the_task_by_its_full_path_and_forgives_one_that_is_already_gone()
    {
        var runner = new FakeProcessRunner((_, _) => new ProcessOutcome(1, "", "ERROR: The system cannot find the file specified."));
        var host = new SchtasksHost(runner, Windows);
        await host.Delete("duty-d1", CancellationToken.None);
        Assert.Equal(["/Delete", "/TN", @"\nth\duty-d1", "/F"], Assert.Single(runner.Runs).Arguments);
        var denied = new SchtasksHost(new FakeProcessRunner((_, _) => new ProcessOutcome(1, "", "ERROR: Access is denied.")), Windows);
        await Assert.ThrowsAsync<WakeEntryException>(() => denied.Delete("duty-d1", CancellationToken.None));
    }
}
