// PORT-ONLY: AC-3's dedicated BelowNormal actor target (never the render or UI thread); ED-617.
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Marrow.Core.Port;
namespace Marrow.Services.Port.ModelsPalette;
internal sealed class PaletteExecutor : IExecutor,IDisposable
{
    private readonly BlockingCollection<Action> jobs=new();
    private readonly Thread thread;
    public PaletteExecutor()
    {
        thread=new Thread(()=>
        {Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;Thread.CurrentThread.CurrentUICulture=CultureInfo.InvariantCulture;EcoQoS(true);
            foreach(var work in jobs.GetConsumingEnumerable())work();}){Name="nth palette engine",IsBackground=true,Priority=ThreadPriority.BelowNormal};thread.Start();
    }
    public void Enqueue(Action work){if(!jobs.IsAddingCompleted)try{jobs.Add(work);}catch(InvalidOperationException){}}
    public void Dispose(){jobs.CompleteAdding();if(Thread.CurrentThread!=thread)thread.Join(TimeSpan.FromSeconds(2));}
    // ED-NEW-lean-23: Windows EcoQoS for a thread that already runs BelowNormal, so the scheduler may keep it on efficient
    // cores at an efficient clock. on=false hands the choice back to Windows (a borrowed pool thread is put back that way).
    internal static bool EcoQoS(bool on)
    {
        if(!OperatingSystem.IsWindows())return false;
        var state=new PowerThrottling{Version=1,ControlMask=on?1u:0u,StateMask=on?1u:0u};
        return SetThreadInformation(GetCurrentThread(),3,ref state,(uint)Marshal.SizeOf<PowerThrottling>());
    }
    [StructLayout(LayoutKind.Sequential)]private struct PowerThrottling{public uint Version;public uint ControlMask;public uint StateMask;}
    [DllImport("kernel32.dll",SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadInformation(IntPtr thread,int informationClass,ref PowerThrottling information,uint size);
    [DllImport("kernel32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentThread();
}
