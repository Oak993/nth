// PORT-ONLY: AC-3 step 2; wait at most 50 ms before ShowMain, retain late disk work.
namespace Marrow.Services.Port.ModelsPalette;
public static class PalettePrimeBudget
{
    public static async Task<bool> BeforeWindow(Task warm,TimeProvider? time=null)
    {
        if(warm.IsCompleted){await warm.ConfigureAwait(false);return true;}
        using var stop=new CancellationTokenSource();
        var deadline=Task.Delay(TimeSpan.FromMilliseconds(50),time??TimeProvider.System,stop.Token);
        var ready=await Task.WhenAny(warm,deadline).ConfigureAwait(false)==warm;
        stop.Cancel();if(ready)await warm.ConfigureAwait(false);return ready;
    }
}
