// PORT-ONLY RR-37: rooted for the lifetime of the input HWND and its registered window class.
using System.Runtime.InteropServices;

namespace Marrow.App.Port;

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint SettingsInputProcedure(nint hwnd, uint message, nint wp, nint lp);
