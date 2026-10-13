using Marrow.Setup.Install;

namespace Marrow.Setup.Tests.Install;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 4 ("Copying": bytes in <root>\packages + <root>\current) and
// VELOPACK_CHECK.md check 5; unit U3 (ED-NEW-inst-u3-1). Only this run's files in the root's folders are counted (not
// Update.exe and the stub beside them, not an old install's files before Velopack renames it away), a file still held
// open by its writer is counted at its real size, and the hook's result counts only when written during the run.
public sealed class InstallObserverTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-u3-obs-" + Guid.NewGuid().ToString("N")[..8], "nth")).FullName;

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void It_counts_this_run_s_files_in_the_root_s_folders_and_nothing_else()
    {
        var old = DateTime.UtcNow.AddHours(-1);
        Write(@"packages\NthApp-1.0.0-full.nupkg", 5000, old);
        Write(@"current\old.dll", 7000, old);
        var observer = new InstallObserver(root, logPath: null, 0, resultPath: null, DateTime.UtcNow);
        Assert.Equal(0, observer.Observe().CopiedBytes);
        Write("Update.exe", 40_000);
        Write("nth.exe", 30_000);
        Write(@"packages\NthApp-1.1.0-full.nupkg", 1000);
        Write(@"current\lib\voice.dll", 2000);
        Write(@"packages\VelopackTemp\tmp_1\skia.dll", 300);
        Assert.Equal(3300, observer.Observe().CopiedBytes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_file_still_being_written_is_counted_at_its_real_size()
    {
        var observer = new InstallObserver(root, logPath: null, 0, resultPath: null, DateTime.UtcNow);
        Directory.CreateDirectory(Path.Combine(root, "packages"));
        using var writer = new FileStream(Path.Combine(root, "packages", "NthApp-1.1.0-full.nupkg"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write(new byte[1 << 20]);
        writer.Flush(flushToDisk: true);
        Assert.Equal(1 << 20, observer.Observe().CopiedBytes);
        writer.Write(new byte[1 << 20]);
        writer.Flush(flushToDisk: true);
        Assert.Equal(2 << 20, observer.Observe().CopiedBytes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_missing_root_counts_nothing_and_the_result_counts_only_when_written_during_the_run()
    {
        var result = Path.Combine(Path.GetDirectoryName(root)!, "install-result.json");
        File.WriteAllText(result, "{}");
        File.SetLastWriteTimeUtc(result, DateTime.UtcNow.AddMinutes(-5));
        var observer = new InstallObserver(Path.Combine(root, "missing"), logPath: null, 0, result, DateTime.UtcNow);
        var first = observer.Observe();
        Assert.Equal(0, first.CopiedBytes);
        Assert.False(first.ResultWritten);
        File.WriteAllText(result, "{\"version\":1}");
        Assert.True(observer.Observe().ResultWritten);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_log_is_read_on_each_look()
    {
        var log = Path.Combine(Path.GetDirectoryName(root)!, "nth Setup Engine.log");
        File.WriteAllText(log, "[setup:1] [12:00:00] INFO earlier run: Creating shortcuts...\r\n");
        var observer = new InstallObserver(root, log, VelopackLogTail.LengthOf(log), null, DateTime.UtcNow);
        Assert.False(observer.Observe().Log.CreatingShortcuts);
        File.AppendAllText(log, "[setup:1] [12:00:09] INFO [velopack::install] Creating shortcuts...\r\n");
        Assert.True(observer.Observe().Log.CreatingShortcuts);
        Assert.Single(observer.RecentLog);
    }

    private void Write(string relative, int bytes, DateTime? made = null)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        if (made is not { } time) return;
        File.SetCreationTimeUtc(path, time);
        File.SetLastWriteTimeUtc(path, time);
    }
}
