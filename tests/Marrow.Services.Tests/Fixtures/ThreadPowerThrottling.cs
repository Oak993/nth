using System.Runtime.InteropServices;

namespace Marrow.Services.Tests.Fixtures;

// Helper for Port/ModelsPalette/PaletteEcoQoSTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
// Reads the calling thread's own THREAD_POWER_THROTTLING_STATE (ThreadPowerThrottling, class 3).
public static class ThreadPowerThrottling
{
    [StructLayout(LayoutKind.Sequential)]private struct State{public uint Version;public uint ControlMask;public uint StateMask;}
    [DllImport("kernel32.dll",SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadInformation(IntPtr thread,int informationClass,ref State information,uint size);
    [DllImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentThread();

    /// <summary>The EcoQoS bit of the control mask and of the state mask.</summary>
    public static (uint Control,uint State) Read()
    {
        var state=new State{Version=1};
        if(!GetThreadInformation(GetCurrentThread(),3,ref state,(uint)Marshal.SizeOf<State>()))throw new InvalidOperationException("GetThreadInformation failed: "+Marshal.GetLastPInvokeError());
        return (state.ControlMask&1,state.StateMask&1);
    }
}
