// PORT-ONLY: SK-5 WIN32_FIND_STREAM_DATA ABI for named-stream verification; one native data type per file.
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Skills.Store;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct StoreStreamData
{
    public long Size;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string Name;
}
