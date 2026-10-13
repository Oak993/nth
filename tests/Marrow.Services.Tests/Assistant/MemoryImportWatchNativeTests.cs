using Marrow.Core.Models;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-12 (DESIGN.md 8.4) NATIVE: the real file system watcher on a temporary folder reports a new markdown file, and the watch
// imports it. Run by the lead alone, with nothing else writing to the temp folder; the lean runs exclude Native tests.
[Trait("Decision", "ED-NEW-dextra-as12-15")]
[Trait("Feature", "AS-12")]
public sealed class MemoryImportWatchNativeTests : IDisposable
{
    private readonly MemoryRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    public async Task A_markdown_file_written_into_a_watched_folder_is_imported_and_waits_for_the_owner()
    {
        var folder = Path.Combine(rig.Temp.Folder, "claude-native");
        Directory.CreateDirectory(folder);
        var source = new MemoryImportSource("accn", "Claude N", ProviderKind.Claude, folder);
        var done = new TaskCompletionSource<ImportReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = new MemoryImportWatch(new MemoryImport(rig.Memory, () => MemoryRig.Main), () => [source], TimeProvider.System, TimeSpan.FromMilliseconds(200));
        watch.Imported += report => done.TrySetResult(report);
        watch.Start();
        await File.WriteAllTextAsync(Path.Combine(folder, "CLAUDE.md"), "- Deploy from the release branch only.\n");
        var report = await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, report.Quarantined);
        Assert.Single(rig.Memory.Quarantined(null));
    }
}
