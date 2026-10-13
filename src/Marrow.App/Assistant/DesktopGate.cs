// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4 "Locked PC or owner away"; unit AS-10; ED-NEW-dextra-as10-16). No Swift twin.
// A duty that needs the desktop (Tactus) and fires on a locked PC does every step that needs none, then waits with "Waiting for you to
// unlock". Whatever acts on the desktop for a mission (Tactus's actions) asks this gate first. Locked, it waits, and the mission's
// line on the board says so. After the unlock it goes on at once if the owner is idle; if he is working it asks first ("Resume now?"),
// and asks again later if he said not yet. nth never unlocks the PC and never types a password: it waits. A Stop ends the wait.
using System.Runtime.InteropServices;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>What the gate reads of the desktop: whether it is locked, and how long since the owner touched it.</summary>
public interface IDesktopWatch
{
    bool IsLocked { get; }

    TimeSpan IdleFor { get; }
}

/// <summary>The real desktop: the input desktop cannot be opened while it is locked; the last input time is Windows'.</summary>
public sealed class SystemDesktopWatch : IDesktopWatch
{
    public bool IsLocked => OperatingSystem.IsWindows() && WorkstationLock.IsLocked();

    public TimeSpan IdleFor
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return TimeSpan.MaxValue;
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Tick)) : TimeSpan.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Tick;
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}

/// <summary>What Tactus (or anything that acts on the desktop) asks before it acts.</summary>
public interface IDesktopGate
{
    /// <summary>Completes when the desktop may be used for this mission; at once when it is unlocked and the owner is not in the way.</summary>
    Task WaitForDesktop(Guid mission, string title, CancellationToken cancellationToken);
}

public sealed class DesktopGate(IDesktopWatch watch, TimeProvider time, Func<Guid, string, CancellationToken, Task<bool>> ask, Action<Guid, string?> note) : IDesktopGate
{
    /// <summary>The line the board shows while a mission waits for the unlock.</summary>
    public const string WaitingLine = "Waiting for you to unlock";

    /// <summary>The line while it waits for the owner to say he is done at the desktop.</summary>
    public const string AskedLine = "Waiting for you: resume now?";

    /// <summary>How often a waiting mission looks at the desktop.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The owner has not touched the PC for this long: he is away, and the duty goes on without asking.</summary>
    public TimeSpan IdleToResume { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>After "not yet", it asks again no sooner than this.</summary>
    public TimeSpan AskAgainAfter { get; init; } = TimeSpan.FromMinutes(10);

    private readonly object gate = new();
    private readonly HashSet<Guid> waiting = [];

    /// <summary>Whether this mission waits at the gate now.</summary>
    public bool IsWaiting(Guid mission)
    {
        lock (gate) return waiting.Contains(mission);
    }

    public async Task WaitForDesktop(Guid mission, string title, CancellationToken cancellationToken)
    {
        if (!watch.IsLocked) return;
        lock (gate) waiting.Add(mission);
        note(mission, WaitingLine);
        using var question = new CancellationTokenSource();
        try
        {
            DateTimeOffset? lastAsked = null;
            Task<bool>? asking = null;
            while (true)
            {
                await Task.Delay(Poll, time, cancellationToken).ConfigureAwait(false);
                if (watch.IsLocked) continue;
                if (asking is { IsCompleted: true })
                {
                    var resume = await asking.ConfigureAwait(false);
                    asking = null;
                    if (resume) return;
                    lastAsked = time.GetUtcNow();
                }
                // Unlocked. An owner who is away (idle) is not in the way, whatever was asked; one who is working is asked first.
                if (watch.IdleFor >= IdleToResume) return;
                if (asking is not null) continue;
                if (lastAsked is { } at && time.GetUtcNow() - at < AskAgainAfter) continue;
                note(mission, AskedLine);
                asking = ask(mission, title, question.Token);
            }
        }
        finally
        {
            await question.CancelAsync().ConfigureAwait(false); // a question still open is withdrawn: the wait is over
            lock (gate) waiting.Remove(mission);
            note(mission, null);
        }
    }
}
