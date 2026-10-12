using System.Runtime.InteropServices;
using Marrow.Services.Port.ModelsPalette;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: lean pass guard (ED-NEW-lean-18b). The palette engine's BelowNormal thread also runs under EcoQoS, and
// EcoQoS(false) hands a borrowed thread (SkillGenerations' pool thread) back to Windows. Reads the thread's own
// THREAD_POWER_THROTTLING_STATE; no window, no native UI.
[Trait("Decision","ED-NEW-lean-18b")]
public sealed class PaletteEcoQoSTests
{
    [StructLayout(LayoutKind.Sequential)]private struct PowerThrottling{public uint Version;public uint ControlMask;public uint StateMask;}
    [DllImport("kernel32.dll",SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadInformation(IntPtr thread,int informationClass,ref PowerThrottling information,uint size);
    [DllImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentThread();
    private static (uint Control,uint State) Read()
    {
        var state=new PowerThrottling{Version=1};
        if(!GetThreadInformation(GetCurrentThread(),3,ref state,(uint)Marshal.SizeOf<PowerThrottling>()))throw new InvalidOperationException("GetThreadInformation failed: "+Marshal.GetLastPInvokeError());
        return (state.ControlMask&1,state.StateMask&1);
    }
    [Fact]
    public async Task The_palette_engine_thread_runs_below_normal_under_EcoQoS()
    {
        using var executor=new PaletteExecutor();var seen=new TaskCompletionSource<(ThreadPriority,(uint,uint))>(TaskCreationOptions.RunContinuationsAsynchronously);
        executor.Enqueue(()=>{try{seen.TrySetResult((Thread.CurrentThread.Priority,Read()));}catch(InvalidOperationException error){seen.TrySetException(error);}});
        Assert.Equal((ThreadPriority.BelowNormal,(1u,1u)),await seen.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
    [Fact]
    public void EcoQoS_off_hands_the_thread_back_to_Windows()
    {
        (uint,uint) on=default,off=default;bool setOn=false,setOff=false;Exception? failed=null;
        var thread=new Thread(()=>{try{setOn=PaletteExecutor.EcoQoS(true);on=Read();setOff=PaletteExecutor.EcoQoS(false);off=Read();}catch(InvalidOperationException error){failed=error;}});
        thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failed);
        Assert.Equal((true,(1u,1u),true,(0u,0u)),(setOn,on,setOff,off));
    }
}
