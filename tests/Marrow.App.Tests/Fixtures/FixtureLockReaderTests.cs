using System.Security.Cryptography;
using System.Text;

namespace Marrow.App.Tests.Fixtures;

public sealed class FixtureLockReaderTests
{
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void An_abandoned_publication_lock_still_delivers_the_complete_provider_record()
    {
        var folder = Folder();
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "call-0.args"), "login\0"u8.ToArray());
            using var publication = new Mutex(false, LockName(folder));
            var owner = new Thread(() => publication.WaitOne());
            owner.Start();
            Assert.True(owner.Join(TimeSpan.FromSeconds(30)));
            Assert.Equal([["login"]], new ProviderProbeScript("probe.exe", folder).RecordedCalls().Select(c => c.ToList()).ToList());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    [Trait("Rule", "R-TEST-5")]
    public void A_live_publication_owner_blocks_the_provider_reader_until_the_record_is_published()
    {
        var folder = Folder();
        using var publication = new Mutex(false, LockName(folder));
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        var reader = new Thread(() =>
        {
            try { Assert.Equal([["login"]], new ProviderProbeScript("probe.exe", folder).RecordedCalls().Select(c => c.ToList()).ToList()); }
            catch (Exception error) { failure = error; }
            finally { completed.Set(); }
        }) { IsBackground = true };
        publication.WaitOne();
        try
        {
            reader.Start();
            Assert.True(SpinWait.SpinUntil(() => (reader.ThreadState & ThreadState.WaitSleepJoin) != 0 || completed.IsSet,
                TimeSpan.FromSeconds(30)), "the reader never reached the publication lock");
            Assert.False(completed.IsSet, "the reader bypassed the live publication owner");
            File.WriteAllBytes(Path.Combine(folder, "call-0.args"), "login\0"u8.ToArray());
        }
        finally
        {
            publication.ReleaseMutex();
            Assert.True(reader.Join(TimeSpan.FromSeconds(30)), "the reader did not finish after the owner released");
            Directory.Delete(folder, recursive: true);
        }
        Assert.Null(failure);
        Assert.True(completed.IsSet);
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "marrow-app-fixture-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string LockName(string folder)
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return "Local\\MarrowProviderProbe-" + BitConverter.ToString(SHA256.HashData(identity)).Replace("-", "", StringComparison.Ordinal);
    }
}
