using Marrow.Setup.Install;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Payload staging") and 4's first step; unit U3 (ED-NEW-inst-u3-1).
// The payload is written whole to "nth Setup Engine.exe" in the setup's named folder (never a GUID in Temp), through a
// .partial file, so a half-written engine never exists under its name; staging starts once however often it is asked;
// a build without the payload, or a write that fails, leaves no engine and says why.
public sealed class EngineStagingTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "nth-u3-stage-" + Guid.NewGuid().ToString("N")[..8], "nth Setup", "1.1.0");

    public void Dispose()
    {
        var top = Path.GetDirectoryName(Path.GetDirectoryName(folder))!;
        if (Directory.Exists(top)) Directory.Delete(top, recursive: true);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_payload_is_written_whole_under_the_engine_s_name_in_the_named_folder()
    {
        var payload = Enumerable.Range(0, 3_500_000).Select(i => (byte)(i % 251)).ToArray();
        var staging = new EngineStaging(folder, () => new MemoryStream(payload));
        Assert.Equal(0, staging.Fraction);
        var task = staging.StageAsync();
        Assert.Same(task, staging.StageAsync());
        await task;
        Assert.True(staging.IsComplete);
        Assert.Null(staging.Error);
        Assert.Equal(1, staging.Fraction);
        Assert.Equal(payload.Length, staging.StagedBytes);
        Assert.Equal(payload.Length, staging.TotalBytes);
        Assert.Equal(Path.Combine(folder, "nth Setup Engine.exe"), staging.EnginePath);
        Assert.Equal(payload, File.ReadAllBytes(staging.EnginePath));
        Assert.Equal(["nth Setup Engine.exe"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_run_s_folder_is_claimed_before_anything_is_written_in_it()
    {
        var seen = new List<string>();
        var staging = new EngineStaging(folder, () => new MemoryStream(new byte[1000]), () =>
        {
            seen.Add(Directory.Exists(folder) ? "folder existed" : "folder not yet made");
            return folder;
        });
        await staging.StageAsync();
        Assert.Equal(["folder not yet made"], seen);
        Assert.True(staging.IsComplete);
        Assert.True(File.Exists(staging.EnginePath));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_folder_another_setup_holds_is_not_written_and_says_why()
    {
        var staging = new EngineStaging(folder, () => new MemoryStream(new byte[1000]), () => throw new IOException("held by another setup"));
        await staging.StageAsync();
        Assert.False(staging.IsComplete);
        Assert.IsType<IOException>(staging.Error);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_build_without_the_payload_stages_nothing_and_says_why()
    {
        var staging = new EngineStaging(folder, () => null);
        await staging.StageAsync();
        Assert.False(staging.IsComplete);
        Assert.IsType<InvalidOperationException>(staging.Error);
        Assert.False(File.Exists(staging.EnginePath));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_write_that_fails_halfway_leaves_neither_the_engine_nor_its_partial_file()
    {
        var staging = new EngineStaging(folder, () => new FailingStream(2_000_000));
        await staging.StageAsync();
        Assert.False(staging.IsComplete);
        Assert.IsType<IOException>(staging.Error);
        Assert.False(File.Exists(staging.EnginePath));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_failed_staging_or_an_engine_taken_since_is_staged_again_when_asked_again()
    {
        var opened = 0;
        var payload = new byte[300_000];
        var staging = new EngineStaging(folder, () => ++opened == 1 ? new FailingStream(100_000) : new MemoryStream(payload));
        await staging.StageAsync();
        Assert.NotNull(staging.Error);
        await staging.StageAsync();
        Assert.Null(staging.Error);
        Assert.True(staging.IsComplete);
        Assert.Equal(payload.Length, staging.StagedBytes);
        var whole = staging.StageAsync();
        Assert.Same(whole, staging.StageAsync());
        File.Delete(staging.EnginePath);
        await staging.StageAsync();
        Assert.Equal(3, opened);
        Assert.True(File.Exists(staging.EnginePath));
        Assert.Equal(payload.Length, staging.StagedBytes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_payload_s_resource_name_is_the_one_the_project_embeds()
    {
        var project = File.ReadAllText(Path.Combine(Repository(), "src", "Marrow.Setup", "Marrow.Setup.csproj"));
        Assert.Contains("LogicalName=\"" + EngineStaging.PayloadResource + "\"", project, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }
}
