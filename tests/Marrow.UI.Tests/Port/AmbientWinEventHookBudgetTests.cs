using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-6 (c): the window-event hooks the ambient states install, read through the SetHook seam. Two windows' states
// alive at once; the budget is the number of event ids the process hears (each hook's range, first..last, summed).
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class AmbientWinEventHookBudgetTests(HeadlessSession headless)
{
    // Event ids hooked in the process with two windows' ambient states. Measured (Debug, headless): 65,586 before the fix
    // (one 3..0x801B hook per window), so the first limit was 98,379; 9 after (one set of five ranges), the limit now 11.
    private const long HookedEventIds = 11;

    [Fact]
    public void Two_windows_hear_only_the_narrow_event_ranges_through_one_hook_set() => headless.OnUiThread(() =>
    {
        var installed = new List<(uint First, uint Last)>();
        var setHook = AmbientWindowState.SetHook;
        AmbientWindowState.SetHook = (first, last, _) => { installed.Add((first, last)); return 0; };
        AmbientWindowState.TestFactory = w => new AmbientWindowState(w);
        var first = new Window(); var second = new Window();
        AmbientWindowState? a = null, b = null;
        try
        {
            a = AmbientWindowState.Acquire(first); b = AmbientWindowState.Acquire(second);
            Assert.NotNull(a); Assert.NotNull(b);
            var ids = installed.Sum(range => (long)range.Last - range.First + 1);
            Assert.InRange(ids, 1, HookedEventIds);
            // one hook set for the process, whatever the number of windows, and never the location change (0x800B)
            Assert.Equal(AmbientWindowState.Events.Ranges, installed);
            Assert.DoesNotContain(installed, range => range.First <= 0x800B && 0x800B <= range.Last);
            a!.Dispose(); a = null;
            Assert.True(AmbientWindowState.Events.Hooked, "the second window still listens");
            b!.Dispose(); b = null;
            Assert.False(AmbientWindowState.Events.Hooked, "the last listener gone, the hooks are removed");
        }
        finally
        {
            a?.Dispose(); b?.Dispose(); first.Close(); second.Close(); Dispatcher.UIThread.RunJobs();
            AmbientWindowState.TestFactory = null; AmbientWindowState.SetHook = setHook;
        }
    });
}
