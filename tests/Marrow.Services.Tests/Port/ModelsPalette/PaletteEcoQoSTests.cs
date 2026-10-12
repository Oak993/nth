using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: lean pass guard (ED-NEW-lean-18b). The palette engine's BelowNormal thread also runs under EcoQoS, and
// EcoQoS(false) hands a borrowed thread (SkillGenerations' pool thread) back to Windows. Reads the thread's own
// THREAD_POWER_THROTTLING_STATE; no window, no native UI.
[Trait("Decision","ED-NEW-lean-18b")]
public sealed class PaletteEcoQoSTests
{
    [Fact]
    public async Task The_palette_engine_thread_runs_below_normal_under_EcoQoS()
    {
        using var executor=new PaletteExecutor();var seen=new TaskCompletionSource<(ThreadPriority,(uint,uint))>(TaskCreationOptions.RunContinuationsAsynchronously);
        executor.Enqueue(()=>{try{seen.TrySetResult((Thread.CurrentThread.Priority,ThreadPowerThrottling.Read()));}catch(InvalidOperationException error){seen.TrySetException(error);}});
        Assert.Equal((ThreadPriority.BelowNormal,(1u,1u)),await seen.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
    [Fact]
    public void EcoQoS_off_hands_the_thread_back_to_Windows()
    {
        (uint,uint) on=default,off=default;bool setOn=false,setOff=false;Exception? failed=null;
        var thread=new Thread(()=>{try{setOn=PaletteExecutor.EcoQoS(true);on=ThreadPowerThrottling.Read();setOff=PaletteExecutor.EcoQoS(false);off=ThreadPowerThrottling.Read();}catch(InvalidOperationException error){failed=error;}});
        thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failed);
        Assert.Equal((true,(1u,1u),true,(0u,0u)),(setOn,on,setOff,off));
    }
}
