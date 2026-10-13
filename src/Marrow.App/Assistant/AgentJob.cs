// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6 "What Stop all does" and "Afterwards", 15.4; unit AS-5; ED-NEW-dextra-as5-5).
// No Swift twin. One Job Object for each agent of a mission (a lead, a head, a helper, the reviewer), made the first time
// its chat launches a provider and kept until the agent is stopped or its mission ends. ThreadRuntime hands the job to the
// session (SessionConfiguration.AgentJob), so the CLI and every process it starts run in it; Stop terminates it, and an
// agent counts as stopped only once its job has no live process.
using Marrow.Core.Port;

namespace Marrow.App.Assistant;

/// <summary>An agent's job: the real Job Object, or a test's stand-in.</summary>
public interface IAgentJob : IDisposable
{
    /// <summary>The Windows job the agent's processes run in; null for a stand-in.</summary>
    JobObject? Native { get; }

    /// <summary>How many of its processes are alive now.</summary>
    int LiveProcesses { get; }

    /// <summary>Ends every process in it at once.</summary>
    void Terminate();
}

/// <summary>Makes agent jobs. Throws when it cannot: then the agent does not start (fail closed).</summary>
public interface IAgentJobFactory
{
    IAgentJob Create();
}

/// <summary>Real kill-on-close Job Objects.</summary>
public sealed class WindowsAgentJobFactory : IAgentJobFactory
{
    public static WindowsAgentJobFactory Instance { get; } = new();

    public IAgentJob Create() => new WindowsAgentJob(JobObject.Create());

    private sealed class WindowsAgentJob(JobObject job) : IAgentJob
    {
        public JobObject? Native => job;

        public int LiveProcesses => job.ActiveProcesses;

        public void Terminate() => job.Terminate();

        public void Dispose() => job.Dispose();
    }
}

/// <summary>The jobs of every mission agent, by chat.</summary>
public sealed class AgentJobs(IAgentJobFactory factory)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, IAgentJob> jobs = [];

    /// <summary>The agent's job, made on first use; a job that was ended is replaced by a new one.</summary>
    public IAgentJob For(Guid thread)
    {
        lock (gate)
        {
            if (jobs.TryGetValue(thread, out var held) && held.Native is not { IsClosed: true }) return held;
            if (held is not null)
            {
                jobs.Remove(thread);
                held.Dispose();
            }
            var made = factory.Create();
            jobs[thread] = made;
            return made;
        }
    }

    /// <summary>The agent's job when it has one.</summary>
    public IAgentJob? Existing(Guid thread)
    {
        lock (gate) return jobs.GetValueOrDefault(thread);
    }

    /// <summary>Ends the agent's process tree. The job stays held until the agent's processes are gone (<see cref="Settled"/>).</summary>
    public void Terminate(Guid thread)
    {
        IAgentJob? job;
        lock (gate) job = jobs.GetValueOrDefault(thread);
        job?.Terminate();
    }

    /// <summary>
    /// Whether the agent has no live process: no job, or a job with none. A settled job is let go, so the agent's next
    /// launch (on Resume) gets a fresh one.
    /// </summary>
    public bool Settled(Guid thread)
    {
        IAgentJob? job;
        lock (gate) job = jobs.GetValueOrDefault(thread);
        if (job is null) return true;
        if (job.LiveProcesses > 0) return false;
        lock (gate)
        {
            if (jobs.TryGetValue(thread, out var held) && ReferenceEquals(held, job)) jobs.Remove(thread);
        }
        job.Dispose();
        return true;
    }

    /// <summary>Ends the agent and lets its job go (the mission ended): kill-on-close takes whatever still runs.</summary>
    public void End(Guid thread)
    {
        IAgentJob? job;
        lock (gate)
        {
            if (!jobs.Remove(thread, out job)) return;
        }
        job.Terminate();
        job.Dispose();
    }

    /// <summary>The chats that hold a job now.</summary>
    public IReadOnlyList<Guid> Threads
    {
        get { lock (gate) return [.. jobs.Keys]; }
    }
}
