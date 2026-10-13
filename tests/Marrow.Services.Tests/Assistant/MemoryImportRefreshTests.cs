using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Assistant;
[Trait("Decision", "ED-NEW-dextra-open-6")]
public sealed class MemoryImportRefreshTests
{
    [Fact]
    public void Runtime_imports_capture_the_current_accounts_on_MainActor_then_read_their_files_on_the_worker()
    {
        using var rig = new MemoryRig();
        using var runtime = new TestRuntime();
        var first = Path.Combine(rig.Temp.Folder, "first");
        var added = Path.Combine(rig.Temp.Folder, "added");
        Directory.CreateDirectory(first); Directory.CreateDirectory(added);
        var note = Path.Combine(added, "CLAUDE.md"); File.WriteAllText(note, "- Use the new account's notes for weekly invoices.\n");
        IReadOnlyList<MemoryImportSource> current = [new("added", "Added account", ProviderKind.Claude, added)];
        var metadataReads = 0;
        using var watch = new MemoryImportWatch(new MemoryImport(rig.Memory, () => MemoryRig.Main),
            () => [new("old", "Startup snapshot", ProviderKind.Claude, first)], rig.Clock,
            refreshSources: () => MainActor.Run(() => { Assert.True(MainActor.IsIsolated); metadataReads++; return current; }));
        var report = new TaskCompletionSource<ImportReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        watch.Imported += value => { Assert.False(MainActor.IsIsolated); report.TrySetResult(value); };
        watch.Faulted += why => report.TrySetException(new InvalidOperationException(why));
        runtime.Loop.Run(async () =>
        {
            watch.Notify(note); rig.Clock.Advance(MemoryImportWatch.DefaultWait);
            Assert.Equal(1, (await report.Task.WaitAsync(TimeSpan.FromSeconds(10))).FilesRead);
            current = [];
            report = new TaskCompletionSource<ImportReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            watch.Notify(note); rig.Clock.Advance(MemoryImportWatch.DefaultWait);
            Assert.Equal(0, (await report.Task.WaitAsync(TimeSpan.FromSeconds(10))).FilesRead);
        });
        Assert.Equal(2, metadataReads);
    }
}
