// PORT-ONLY: Tactus, the writes waiting while the user types (ED-NEW-pcctl-37; DESIGN.md 2.9, 4.5 "Waiting for you to
// type"). The newest open wait is the one the pill names; ending it names the one before; each begin and end is one change;
// a wait ended twice ends once.
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-37")]
public sealed class PcTypingWaitsTests
{
    private const string Outlook = "exe:c:\\office\\outlook.exe";
    private const string Excel = "exe:c:\\office\\excel.exe";

    [Fact]
    public void The_newest_open_wait_is_named_and_each_begin_and_end_is_one_change()
    {
        var waits = new PcTypingWaits();
        var seen = new List<PcTypingWait?>();
        waits.Changed += () => seen.Add(waits.Current);
        Assert.Null(waits.Current);

        var subject = waits.Begin(new PcTypingWait(Outlook, "Subject"));
        var cell = waits.Begin(new PcTypingWait(Excel, "B7"));
        Assert.Equal(new PcTypingWait(Excel, "B7"), waits.Current);
        cell.Dispose();
        cell.Dispose();
        Assert.Equal(new PcTypingWait(Outlook, "Subject"), waits.Current);
        subject.Dispose();

        Assert.Null(waits.Current);
        Assert.Equal([new PcTypingWait(Outlook, "Subject"), new PcTypingWait(Excel, "B7"), new PcTypingWait(Outlook, "Subject"), null], seen);
    }
}
