using System.Runtime.InteropServices;
namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: DL-6 Windows Job Object ABI.
[StructLayout(LayoutKind.Sequential)]
internal struct ShimJobBasicLimits
{
    public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
    public uint LimitFlags;
    public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public nuint Affinity;
    public uint PriorityClass, SchedulingClass;
}
