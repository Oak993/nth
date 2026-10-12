// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Concurrency", 12.6b "resume behind the RAM gate"; unit AS-5;
// ED-NEW-dextra-as5-2, -10). No Swift twin. The one gate every mission agent (a lead, a head, a helper, the reviewer)
// passes before its chat starts: at most the owner's number of local CLI agents at once, and no new local agent under
// the free-memory floor (fail closed when memory cannot be read). An agent that may not start yet waits, first come first
// served, with the line the board shows; a slot is freed when its agent ends, stops or pauses for good.
using System.Runtime.InteropServices;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>Reads the machine's memory; null when it cannot.</summary>
public interface IMemoryReader
{
    MemoryReading? Read();
}

/// <summary>GlobalMemoryStatusEx.</summary>
public sealed class WindowsMemoryReader : IMemoryReader
{
    public static WindowsMemoryReader Instance { get; } = new();

    public MemoryReading? Read()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? new MemoryReading(status.TotalPhys, status.AvailPhys) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}

public sealed class AgentGate(Func<MissionLimits> limits, IMemoryReader memory, TimeProvider? time = null)
{
    /// <summary>How often a waiting agent looks at the memory again.</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly HashSet<Guid> running = [];
    private readonly LinkedList<Waiter> waiting = new();
    private ITimer? timer;

    private sealed class Waiter(Guid agent, TaskCompletionSource admitted)
    {
        public Guid Agent { get; } = agent;
        public TaskCompletionSource Admitted { get; } = admitted;
        public string? Line { get; set; }
    }

    /// <summary>Raised when an agent starts waiting, is let in, or its waiting line changes.</summary>
    public event Action? Changed;

    /// <summary>The local agents holding a slot now.</summary>
    public int Running
    {
        get { lock (gate) return running.Count; }
    }

    /// <summary>Whether the agent holds a slot.</summary>
    public bool Holds(Guid agent)
    {
        lock (gate) return running.Contains(agent);
    }

    /// <summary>Why the agent waits, or null when it does not.</summary>
    public string? Waiting(Guid agent)
    {
        lock (gate) return waiting.FirstOrDefault(w => w.Agent == agent)?.Line;
    }

    /// <summary>
    /// Lets the agent start: at once for an API agent (it runs in nth and is limited by budget), else when a slot is free
    /// and memory is above the floor, in the order agents asked. Cancelling stops the wait.
    /// </summary>
    public Task Enter(Guid agent, AgentPlace place, CancellationToken cancellationToken = default)
    {
        if (place == AgentPlace.Api) return Task.CompletedTask;
        Waiter waiter;
        lock (gate)
        {
            if (running.Contains(agent)) return Task.CompletedTask;
            if (waiting.FirstOrDefault(w => w.Agent == agent) is { } already) return already.Admitted.Task.WaitAsync(cancellationToken);
            waiter = new Waiter(agent, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            waiting.AddLast(waiter);
        }
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() =>
            {
                bool removed;
                lock (gate) removed = waiting.Remove(waiter);
                if (removed)
                {
                    waiter.Admitted.TrySetCanceled(cancellationToken);
                    Changed?.Invoke();
                }
            });
        Pump();
        return waiter.Admitted.Task;
    }

    /// <summary>Frees the agent's slot (it ended, stopped, or was let go while paused), and lets the next one in.</summary>
    public void Leave(Guid agent)
    {
        bool freed;
        lock (gate)
        {
            freed = running.Remove(agent);
            var queued = waiting.FirstOrDefault(w => w.Agent == agent);
            if (queued is not null)
            {
                waiting.Remove(queued);
                queued.Admitted.TrySetCanceled();
                freed = true;
            }
        }
        if (freed) Pump();
    }

    /// <summary>Looks again: lets in every waiting agent that may start now, in order, and updates the others' lines.</summary>
    public void Pump()
    {
        var admitted = new List<Waiter>();
        var changed = false;
        lock (gate)
        {
            while (waiting.First is { } first)
            {
                var reading = memory.Read();
                var verdict = MissionGate.Admit(AgentPlace.Local, running.Count, reading, limits());
                if (!verdict.Admitted)
                {
                    foreach (var w in waiting)
                    {
                        changed |= w.Line != verdict.Waiting;
                        w.Line = verdict.Waiting;
                    }
                    break;
                }
                // Close to the floor, one agent per look: the memory read cannot see an agent that has not started yet.
                if (admitted.Count > 0 && reading!.FreeFraction < limits().EffectiveFreeRamFloor * 2) break;
                waiting.RemoveFirst();
                running.Add(first.Value.Agent);
                admitted.Add(first.Value);
                changed = true;
            }
            if (waiting.Count > 0) timer ??= clock.CreateTimer(_ => Pump(), null, Recheck, Recheck);
            else
            {
                timer?.Dispose();
                timer = null;
            }
        }
        foreach (var w in admitted) w.Admitted.TrySetResult();
        if (changed) Changed?.Invoke();
    }
}
