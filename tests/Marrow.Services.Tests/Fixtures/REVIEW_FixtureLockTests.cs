// REVIEW (FLAKE-1 independent review): the fixture publication lock is now taken in every mode and by every reader.
// A stand-in process that is killed while it holds the named mutex (Shell and cancellation tests kill children) leaves it
// abandoned, and the next WaitOne throws AbandonedMutexException instead of reading, so a reader or the next stand-in launch
// fails: the same kind of intermittent failure FLAKE-1 exists to remove.
using System.Security.Cryptography;
using System.Text;

namespace Marrow.Services.Tests.Fixtures;

[Trait("Rule", "R-TEST-5")]
[Trait("Review", "FL-1")]
public sealed class REVIEW_FixtureLockTests
{
    private static void AbandonLock(string prefix, string folder)
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var name = "Local\\" + prefix + BitConverter.ToString(SHA256.HashData(identity)).Replace("-", "", StringComparison.Ordinal);
        var holder = new Thread(() => { var held = new Mutex(false, name); held.WaitOne(); /* the owner dies without releasing */ });
        holder.Start();
        holder.Join();
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "review-flake-1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void REVIEW_reading_provider_probe_records_survives_a_stand_in_that_died_holding_the_lock()
    {
        var folder = Folder();
        File.WriteAllBytes(Path.Combine(folder, "call-0.args"), "login\0"u8.ToArray());
        AbandonLock("MarrowProviderProbe-", folder);
        var calls = new ProviderProbeScript("probe.exe", folder).RecordedCalls();
        Assert.Equal([["login"]], calls.Select(c => c.ToList()).ToList());
    }

    [Fact]
    public void REVIEW_reading_pi_records_survives_a_stand_in_that_died_holding_the_lock()
    {
        // PiScript's constructor compiles the stand-in with the Windows csc, so build the instance without it.
        var folder = Folder();
        var script = (PiScript)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PiScript));
        typeof(PiScript).GetField("<ControlDirectory>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(script, folder);
        File.WriteAllBytes(Path.Combine(folder, "argv.txt"), "--mode\0rpc\0"u8.ToArray());
        AbandonLock("MarrowPiStandIn-", folder);
        Assert.Equal(["--mode", "rpc"], script.Arguments());
    }
}
