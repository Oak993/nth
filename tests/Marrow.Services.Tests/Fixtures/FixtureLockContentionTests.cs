using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Marrow.Services.Tests.Fixtures;

public sealed class FixtureLockContentionTests
{
    [Theory]
    [InlineData("provider")]
    [InlineData("pi")]
    [Trait("Rule", "R-TEST-5")]
    public void A_live_publication_owner_blocks_a_reader_until_the_owner_releases(string fixture)
    {
        var folder = Path.Combine(Path.GetTempPath(), "marrow-fixture-contention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var prefix = fixture == "provider" ? "MarrowProviderProbe-" : "MarrowPiStandIn-";
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var name = "Local\\" + prefix + BitConverter.ToString(SHA256.HashData(identity)).Replace("-", "", StringComparison.Ordinal);
        using var publication = new Mutex(false, name);
        using var completed = new ManualResetEventSlim();
        Exception? failure = null;
        var reader = new Thread(() =>
        {
            try
            {
                if (fixture == "provider")
                    Assert.Equal([["login"]], new ProviderProbeScript("probe.exe", folder).RecordedCalls().Select(c => c.ToList()).ToList());
                else
                {
                    var script = (PiScript)RuntimeHelpers.GetUninitializedObject(typeof(PiScript));
                    typeof(PiScript).GetField("<ControlDirectory>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .SetValue(script, folder);
                    Assert.Equal(["--mode", "rpc"], script.Arguments());
                }
            }
            catch (Exception error) { failure = error; }
            finally { completed.Set(); }
        }) { IsBackground = true };
        publication.WaitOne();
        try
        {
            reader.Start();
            // This reader has no other managed waits. Observe its blocked wait rather than
            // assume that a real-time pause was long enough for it to reach the lock.
            Assert.True(SpinWait.SpinUntil(() => (reader.ThreadState & ThreadState.WaitSleepJoin) != 0 || completed.IsSet,
                TimeSpan.FromSeconds(30)), "the reader never reached the publication lock");
            Assert.False(completed.IsSet, "the reader bypassed the live publication owner");
            File.WriteAllBytes(Path.Combine(folder, fixture == "provider" ? "call-0.args" : "argv.txt"),
                Encoding.UTF8.GetBytes(fixture == "provider" ? "login\0" : "--mode\0rpc\0"));
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
}
