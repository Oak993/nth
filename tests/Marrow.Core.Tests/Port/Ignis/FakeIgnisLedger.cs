using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.Core.Tests.Port.Ignis;

// Ignis U0 (OD-0134, ED-NEW-ignis-contract): a fake that proves the contract compiles for the units that implement it.
internal sealed class FakeIgnisLedger : ISavingsLedger
{
    public event Action? Changed { add { } remove { } }
    public void Record(IgnisLedgerEntry entry) { }
    public void Record(IgnisEvent @event) { }
    public IgnisDayTotals Day(DateOnly day) => IgnisDayTotals.Empty(day);
    public IgnisDayTotals ChatDay(Guid thread, DateOnly day) => IgnisDayTotals.Empty(day);
    public IReadOnlyList<IgnisDayTotals> Days(DateOnly from, DateOnly to) => [];
    public decimal AllTimeSaved => 0m;
    public DateOnly? Since => null;
    public IReadOnlyList<IgnisEvent> Events(Guid thread, DateOnly day) => [];
    public IgnisEvent? Event(string id) => null;
}
