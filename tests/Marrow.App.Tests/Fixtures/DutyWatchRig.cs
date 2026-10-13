using Marrow.App.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-15: a duty watch over a queue, a book and a log that is a list, on a clock the test holds.</summary>
internal sealed class DutyWatchRig
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    public DutyWatchRig(DutyHealthBook? book = null, Func<AuditEntry, bool>? auditFails = null)
    {
        var clock = new ManualTimeProvider(T0);
        Queue = new DecisionQueue(clock);
        Book = book ?? new DutyHealthBook();
        Watch = new DutyWatch(new DutyWatchOptions
        {
            Queue = Queue,
            Book = Book,
            Audit = entry =>
            {
                if (auditFails?.Invoke(entry) == true) throw new IOException("The activity log's disk is full.");
                Audit.Add(entry);
            },
            Time = clock,
        });
        Watch.Faulted += Faults.Add;
    }

    public DutyWatch Watch { get; }

    public DecisionQueue Queue { get; }

    public DutyHealthBook Book { get; }

    public List<AuditEntry> Audit { get; } = [];

    public List<string> Faults { get; } = [];
}
