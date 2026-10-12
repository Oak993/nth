using Marrow.Setup.Install;
using Marrow.Setup.Screens;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// A test hive for nth's uninstall entry (no test reads or writes the real HKCU): Velopack writes its own values after the
/// hook (<c>"&lt;root&gt;\Update.exe" --uninstall</c>), and the setup's step after exit 0 re-points them to
/// <c>"&lt;root&gt;\nth Uninstall.exe" --uninstall</c> (unit U5's values).
/// </summary>
public sealed class FakeUninstallHive
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Values => values;

    public int Writes { get; private set; }

    public void WriteVelopack(string root)
    {
        values["UninstallString"] = "\"" + Path.Combine(root, "Update.exe") + "\" --uninstall";
        values["QuietUninstallString"] = "\"" + Path.Combine(root, "Update.exe") + "\" --uninstall --silent";
        values["InstallLocation"] = root;
        Writes++;
    }

    public void Repoint(string root)
    {
        values["UninstallString"] = "\"" + Path.Combine(root, "nth Uninstall.exe") + "\" --uninstall";
        values["QuietUninstallString"] = "\"" + Path.Combine(root, "nth Uninstall.exe") + "\" --uninstall --silent";
        values["NoModify"] = "1";
        values["NoRepair"] = "1";
        Writes++;
    }

    public bool IsRepointed(string root) =>
        values.TryGetValue("UninstallString", out var value) && value.Contains(Path.Combine(root, "nth Uninstall.exe"), StringComparison.OrdinalIgnoreCase);
}

/// <summary>The setup's step after exit 0, re-pointing the test hive; it counts its calls and can be told to throw.</summary>
public sealed class FakeAfterSetupExit(FakeUninstallHive hive) : IAfterSetupExit
{
    public int Calls { get; private set; }

    public bool Throws { get; set; }

    public void Run(string root, string resultPath)
    {
        Calls++;
        if (Throws) throw new InvalidOperationException("the shell refused");
        hive.Repoint(root);
    }
}

/// <summary>A drive with a set number of free bytes (null: Windows cannot say).</summary>
public sealed class FakeDiskSpace(long? free) : IDiskSpace
{
    public long? Free { get; set; } = free;

    public List<string> Asked { get; } = [];

    public long? FreeBytes(string folder)
    {
        Asked.Add(folder);
        return Free;
    }
}

/// <summary>A 100 ms loop the test turns by hand on its own thread.</summary>
public sealed class ManualPollLoop : IPollLoop
{
    private Func<bool>? poll;

    public bool Running => poll is not null;

    public int Starts { get; private set; }

    public void Start(Func<bool> next)
    {
        poll = next ?? throw new ArgumentNullException(nameof(next));
        Starts++;
    }

    /// <summary>One turn; false once the loop has stopped.</summary>
    public bool Turn()
    {
        if (poll is null) return false;
        if (!poll()) poll = null;
        return poll is not null;
    }
}
