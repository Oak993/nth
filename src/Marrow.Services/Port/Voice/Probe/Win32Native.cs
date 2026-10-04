// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the two Win32 reads of V-10.1.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>kernel32 imports: <c>GlobalMemoryStatusEx</c> and <c>GetLogicalProcessorInformationEx</c>.</summary>
public static class Win32Native
{
    /// <summary>LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore.</summary>
    public const int RelationProcessorCore = 0;

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLogicalProcessorInformationEx(int relationship, nint buffer, ref uint length);
}
