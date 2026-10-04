// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the Win32 MEMORYSTATUSEX (V-10.1 Memory row).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>MEMORYSTATUSEX of kernel32 <c>GlobalMemoryStatusEx</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MemoryStatusEx
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhys;
    public ulong AvailPhys;
    public ulong TotalPageFile;
    public ulong AvailPageFile;
    public ulong TotalVirtual;
    public ulong AvailVirtual;
    public ulong AvailExtendedVirtual;
}
