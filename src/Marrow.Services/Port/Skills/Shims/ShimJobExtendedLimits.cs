using System.Runtime.InteropServices;
namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: DL-6 Windows Job Object ABI.
[StructLayout(LayoutKind.Sequential)]
internal struct ShimJobExtendedLimits
{
    public ShimJobBasicLimits Basic;
    public ShimJobIoCounters Io;
    public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
}
