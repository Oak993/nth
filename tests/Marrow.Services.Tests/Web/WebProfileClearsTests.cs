using System.Text.Json.Nodes;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W10, "Clear browsing data" and "Forget agent data" (E:/Droppy/work/design/web-surfaces/DESIGN.md 8.1, 13.4; ED-NEW-web-w10-6). The record of clears
// that wait for a profile's next page: written down before anything is carried out, versioned, with unknown fields kept and a newer file left alone.
[Trait("Decision", "ED-NEW-web-w10-6")]
public sealed class WebProfileClearsTests : IDisposable
{
    private readonly string folder;
    private readonly ManualTimeProvider time = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    public WebProfileClearsTests()
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) data = Path.GetTempPath();
        folder = Path.Combine(data, "WebProfileClears", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string FilePath => Path.Combine(folder, "nth", WebProfileClears.FileName);

    private static string Agent(string name) => WebProfiles.AgentProfilePrefix + name;

    private static string Hex(int number) => Agent(number.ToString("x16", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void A_request_for_browse_or_preview_waits_until_that_profile_is_cleared()
    {
        var clears = new WebProfileClears(null, time);
        Assert.False(clears.IsPending(WebProfiles.BrowseProfile));
        Assert.True(clears.Request(WebProfiles.BrowseProfile));
        Assert.True(clears.IsPending(WebProfiles.BrowseProfile));
        Assert.False(clears.IsPending(WebProfiles.PreviewProfile));
        Assert.Equal([WebProfiles.BrowseProfile], clears.PendingProfiles);
        clears.Complete(WebProfiles.BrowseProfile);
        Assert.False(clears.IsPending(WebProfiles.BrowseProfile));
        Assert.Empty(clears.PendingProfiles);
    }

    [Theory]
    [InlineData("private")]
    [InlineData("artifacts")]
    [InlineData("")]
    [InlineData("agent-0123456789abcdef")]
    [InlineData("../browse")]
    public void A_profile_that_keeps_nothing_or_is_not_one_of_the_two_is_refused(string name)
    {
        var clears = new WebProfileClears(null, time);
        Assert.False(clears.Request(name));
        Assert.False(clears.IsPending(name));
        Assert.Empty(clears.PendingProfiles);
    }

    [Fact]
    public void Forgetting_agent_data_reaches_every_agent_profile_once_each()
    {
        var clears = new WebProfileClears(null, time);
        Assert.False(clears.IsPending(Agent("0000000000000001")));
        clears.RequestAllAgents();
        Assert.True(clears.AgentsRequested);
        Assert.True(clears.IsPending(Agent("0000000000000001")));
        Assert.True(clears.IsPending(Agent("0000000000000002")));
        clears.Complete(Agent("0000000000000001"));
        Assert.False(clears.IsPending(Agent("0000000000000001")));
        Assert.True(clears.IsPending(Agent("0000000000000002")));
        // a later request reaches the first one again, even at the same instant
        clears.RequestAllAgents();
        Assert.True(clears.IsPending(Agent("0000000000000001")));
        Assert.False(clears.IsPending(WebProfiles.BrowseProfile));
    }

    [Fact]
    public void A_request_is_in_the_file_before_anything_is_carried_out_and_a_new_run_reads_it()
    {
        var first = WebProfileClears.ForFolder(folder, time);
        Assert.True(first.IsPersistent);
        Assert.True(first.Request(WebProfiles.PreviewProfile));
        first.RequestAllAgents();
        Assert.True(File.Exists(FilePath)); // the intent is on disk before the clear runs

        // the crash between the clear and its outcome: the next run still has the request
        var second = WebProfileClears.ForFolder(folder, time);
        Assert.True(second.IsPending(WebProfiles.PreviewProfile));
        Assert.True(second.IsPending(Agent("00000000000000aa")));
        second.Complete(WebProfiles.PreviewProfile);
        second.Complete(Agent("00000000000000aa"));

        var third = WebProfileClears.ForFolder(folder, time);
        Assert.False(third.IsPending(WebProfiles.PreviewProfile));
        Assert.False(third.IsPending(Agent("00000000000000aa")));
        Assert.True(third.IsPending(Agent("00000000000000bb")));
    }

    [Fact]
    public void The_file_carries_a_version_and_unknown_fields_survive_a_load_and_save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{\"version\":1,\"pending\":{\"browse\":\"2026-10-13T08:00:00.0000000Z\"},\"futureField\":{\"keep\":[1,2,3]}}");
        var clears = WebProfileClears.ForFolder(folder, time);
        Assert.True(clears.IsPending(WebProfiles.BrowseProfile));
        Assert.True(clears.Request(WebProfiles.PreviewProfile));
        var saved = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        Assert.Equal(WebProfileClears.FormatVersion, saved["version"]!.GetValue<int>());
        Assert.Equal("[1,2,3]", saved["futureField"]!["keep"]!.ToJsonString());
        Assert.NotNull(saved["pending"]!["browse"]);
        Assert.NotNull(saved["pending"]!["preview"]);
    }

    [Fact]
    public void A_file_from_a_newer_nth_is_read_as_empty_and_left_exactly_as_it_is()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        const string newer = "{\"version\":99,\"pending\":{\"browse\":\"2026-10-13T08:00:00Z\"},\"somethingNew\":true}";
        File.WriteAllText(FilePath, newer);
        var clears = WebProfileClears.ForFolder(folder, time);
        Assert.False(clears.IsPending(WebProfiles.BrowseProfile));
        Assert.True(clears.Request(WebProfiles.PreviewProfile)); // held for this run
        Assert.True(clears.IsPending(WebProfiles.PreviewProfile));
        clears.Complete(WebProfiles.PreviewProfile);
        Assert.Equal(newer, File.ReadAllText(FilePath));
        Assert.Null(clears.ToJson());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"version\":1,\"pending\":[1],\"agents\":7}")]
    [InlineData("{\"version\":1,\"pending\":{\"private\":\"2026-10-13T08:00:00Z\",\"browse\":\"yesterday\",\"../x\":\"2026-10-13T08:00:00Z\"}}")]
    public void A_damaged_file_or_one_naming_other_profiles_reads_as_no_requests(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, text);
        var clears = WebProfileClears.ForFolder(folder, time);
        Assert.Empty(clears.PendingProfiles);
        Assert.False(clears.AgentsRequested);
        Assert.False(clears.IsPending(WebProfiles.BrowseProfile));
    }

    [Fact]
    public void The_agent_profiles_remembered_as_cleared_are_capped_and_the_oldest_go_first()
    {
        var clears = new WebProfileClears(null, time);
        clears.RequestAllAgents();
        for (var i = 0; i < WebProfileClears.MaxAgentProfiles + 5; i++) clears.Complete(Hex(i));
        var saved = JsonNode.Parse(clears.ToJson()!)!["agents"]!["cleared"]!.AsObject();
        Assert.Equal(WebProfileClears.MaxAgentProfiles, saved.Count);
        Assert.False(saved.ContainsKey(Hex(0)));
        Assert.True(saved.ContainsKey(Hex(WebProfileClears.MaxAgentProfiles + 4)));
    }

    [Fact]
    public void A_file_that_cannot_be_written_keeps_the_request_in_memory_and_logs_without_the_path()
    {
        var blocker = Path.Combine(folder, "nth");
        File.WriteAllText(blocker, "a file where the folder should be");
        var lines = new List<string>();
        using var sink = Marrow.Core.Port.Logger.Sink.Override((message, _) => lines.Add(message));
        var clears = WebProfileClears.ForFolder(folder, time);
        Assert.True(clears.Request(WebProfiles.BrowseProfile));
        Assert.True(clears.IsPending(WebProfiles.BrowseProfile));
        var line = Assert.Single(lines);
        Assert.StartsWith("web: the profile clears file could not be written", line, StringComparison.Ordinal);
        Assert.DoesNotContain(folder, line, StringComparison.OrdinalIgnoreCase);
    }
}
