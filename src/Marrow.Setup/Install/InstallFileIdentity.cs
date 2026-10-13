using System.Runtime.InteropServices;

namespace Marrow.Setup.Install;

// PORT-ONLY: ED-NEW-inst-c7-1. FILE_ID_INFO: volume identity and the filesystem's 128-bit file ID.
[StructLayout(LayoutKind.Sequential)]
public readonly record struct InstallFileIdentity(ulong Volume, ulong Low, ulong High);
