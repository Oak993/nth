using System.Security.Cryptography;
using System.Text;

namespace Marrow.Core.Support;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3a and 13 unit U4, VELOPACK_CHECK.md "Old branches" item 2 and 3,
/// ED-NEW-inst-u4-3). The hold nth Setup keeps while Velopack's <c>Setup.exe</c> runs, and the wait nth does at start
/// while a hold on its own install root exists.
/// <para>
/// Why: Velopack stops every process in the root when <c>Setup.exe</c> starts and again after each hook (install.rs:106;
/// util.rs:59-60), so an nth the user opens during the install would be killed with its window up. With the hold, an nth
/// that starts in the root waits (no window, at most <see cref="MaxStartWait"/>) and is stopped by Velopack before it
/// ever shows. The same hold is how a second setup learns that another install of the same root is running
/// (<see cref="InstallerGate.InProgressExitCode"/>, silent exit 4).
/// </para>
/// <para>
/// It is a per-session named mutex (<c>Local\nth.Installing.&lt;key&gt;</c>), key = the first 16 hex digits of the SHA-256
/// of the upper-case full root path without its trailing separator. A mutex, not an event, so a setup that dies leaves it
/// abandoned and waiting copies of nth carry on instead of waiting out the ten minutes. A mutex belongs to the thread that
/// took it, and a setup holds it across awaits, so <see cref="Hold"/> owns it on a thread of its own.
/// </para>
/// </summary>
public static class InstallHold
{
    /// <summary>The longest nth waits at start for an install of its own root to finish.</summary>
    public static readonly TimeSpan MaxStartWait = TimeSpan.FromMinutes(10);

    /// <summary>The kernel object's name for this install root.</summary>
    public static string MutexName(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot)).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
        return "Local\\nth.Installing." + key;
    }

    /// <summary>
    /// Takes the hold for this root, or returns null when another setup holds it. Dispose the hold when
    /// <c>Setup.exe</c> has exited (every path: finished, failed, cancelled).
    /// </summary>
    public static Hold? TryAcquire(string installRoot)
    {
        var hold = new Hold(MutexName(installRoot));
        if (hold.Start()) return hold;
        hold.Dispose();
        return null;
    }

    /// <summary>True while a setup holds this root.</summary>
    public static bool IsHeld(string installRoot)
    {
        if (!Mutex.TryOpenExisting(MutexName(installRoot), out var mutex)) return false;
        using (mutex)
        {
            try
            {
                if (!mutex.WaitOne(0)) return true;
            }
            catch (AbandonedMutexException)
            {
                // The setup that held it is gone: the hold is free.
            }
            mutex.ReleaseMutex();
            return false;
        }
    }

    /// <summary>
    /// nth's wait at start: returns true at once when no setup holds this root, and when one does, once it lets go (or dies).
    /// Returns false when it still holds after <paramref name="timeout"/>.
    /// </summary>
    public static bool WaitWhileHeld(string installRoot, TimeSpan timeout)
    {
        if (!Mutex.TryOpenExisting(MutexName(installRoot), out var mutex)) return true;
        using (mutex)
        {
            try
            {
                if (!mutex.WaitOne(timeout)) return false;
            }
            catch (AbandonedMutexException)
            {
                // Died while holding: this thread owns it now, which is a way of saying it is free.
            }
            mutex.ReleaseMutex();
            return true;
        }
    }

    /// <summary>A taken hold. Disposing it lets nth waiting at start carry on.</summary>
    public sealed class Hold : IDisposable
    {
        private readonly string name;
        private readonly ManualResetEventSlim ready = new(false);
        private readonly ManualResetEventSlim release = new(false);
        private Thread? thread;
        private bool acquired;
        private int disposed;

        internal Hold(string name) => this.name = name;

        internal bool Start()
        {
            thread = new Thread(Own) { IsBackground = true, Name = "nth install hold" };
            thread.Start();
            ready.Wait();
            return acquired;
        }

        private void Own()
        {
            Mutex? mutex = null;
            try
            {
                mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
                if (createdNew)
                {
                    acquired = true;
                }
                else
                {
                    try
                    {
                        acquired = mutex.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true; // the setup that held it died: this one takes over
                    }
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
            {
                acquired = false;
            }
            finally
            {
                ready.Set();
            }
            if (mutex is null) return;
            if (acquired)
            {
                release.Wait();
                mutex.ReleaseMutex();
            }
            mutex.Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            release.Set();
            thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
