using System.Runtime.InteropServices;
namespace Marrow.Services.Port.Skills.Shims;

// PORT-ONLY: DL-6 Windows Job Object ABI.
[StructLayout(LayoutKind.Sequential)]
internal struct ShimJobIoCounters
{
    public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
}
