// PORT-ONLY: OD-0127 resting "Checked {when}" comes from the engine, not the lifetime of a Settings page.
// ED-627. This diagnostic clock never participates in palette/rank/seed determinism.
namespace Marrow.Services.Port.ModelsPalette;
public sealed partial class PaletteEngine
{
    private long lastCheckedTick;
    private int hasLiveCheck;
    public TimeSpan? LastCheckedAge
    {
        get
        {
            if(Volatile.Read(ref hasLiveCheck)!=0)return time.GetElapsedTime(Interlocked.Read(ref lastCheckedTick));
            var saved=Interlocked.Read(ref serverSeconds);
            if(saved<=0)return null;
            // A restored check predates this process's monotonic clock. Show the saved server date's age until
            // the first real in-process check calibrates it. Clock skew is clamped, and does not recolour anything.
            var elapsed=time.GetUtcNow()-DateTimeOffset.FromUnixTimeSeconds(saved);
            return elapsed<TimeSpan.Zero?TimeSpan.Zero:elapsed;
        }
    }
    private void MarkChecked(){Interlocked.Exchange(ref lastCheckedTick,time.GetTimestamp());Volatile.Write(ref hasLiveCheck,1);}
}
