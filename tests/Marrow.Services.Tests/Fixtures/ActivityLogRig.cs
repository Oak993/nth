using System.Text;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-21): an audit log in a throwaway folder, written by the real writer under a manual clock (UTC as the
/// local zone), for the Activity view's reader, filter, lineage and exports. <see cref="Close"/> frees the writer so a
/// test can edit the day files the way anything with write access to the folder could.
/// </summary>
public sealed class ActivityLogRig : IDisposable
{
    private AuditLog? log;

    public ActivityLogRig(DateTimeOffset? start = null)
    {
        Clock = new ManualTimeProvider(start ?? new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero));
    }

    public AssistantTempRoot Temp { get; } = new();

    public ManualTimeProvider Clock { get; }

    public ActivityLogSource Source => new(Temp.AuditDirectory, Temp.Keys);

    public string Directory => Temp.AuditDirectory;

    public string DayFile(string day) => Path.Combine(Temp.AuditDirectory, day + ".jsonl");

    /// <summary>Appends one action through the real writer and returns where it landed.</summary>
    public AuditReceipt Add(
        string assistant = "dextra",
        string? mission = "m-1",
        string? agent = "thread-1",
        string tool = "shell",
        string actionClass = "Run",
        string? target = null,
        string? result = "ok",
        string? allowedBy = null,
        IReadOnlyList<AuditInput>? inputs = null,
        decimal? costUsd = null,
        long? tokens = null)
    {
        log ??= AuditLog.Open(Temp.Roots, Temp.Keys, Clock);
        return log.Append(new AuditEntry
        {
            Assistant = assistant,
            Mission = mission,
            Agent = agent,
            Tool = tool,
            ActionClass = actionClass,
            Target = target,
            Result = result,
            AllowedBy = allowedBy,
            Inputs = inputs ?? [],
            CostUsd = costUsd,
            Tokens = tokens,
        });
    }

    /// <summary>Appends <paramref name="count"/> actions as one batch (one flush), each built by <paramref name="make"/> from its index.</summary>
    public void AddMany(int count, Func<int, AuditEntry> make)
    {
        log ??= AuditLog.Open(Temp.Roots, Temp.Keys, Clock);
        log.AppendBatch(Enumerable.Range(0, count).Select(make).ToList());
    }

    public void Advance(TimeSpan by) => Clock.Advance(by);

    /// <summary>Releases the writer (its lock and the day's file).</summary>
    public void Close()
    {
        log?.Dispose();
        log = null;
    }

    public List<string> Lines(string day) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(DayFile(day))).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    public void Rewrite(string day, IEnumerable<string> lines) =>
        File.WriteAllText(DayFile(day), string.Concat(lines.Select(l => l + "\n")), new UTF8Encoding(false));

    public void Dispose()
    {
        Close();
        Temp.Dispose();
    }
}
