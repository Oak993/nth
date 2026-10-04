// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): DXGI_QUERY_VIDEO_MEMORY_INFO (V-10.1 Graphics row, free memory).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>DXGI_QUERY_VIDEO_MEMORY_INFO: free memory is <c>Budget - CurrentUsage</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DxgiVideoMemoryInfo
{
    public ulong Budget;
    public ulong CurrentUsage;
    public ulong AvailableForReservation;
    public ulong CurrentReservation;
}
