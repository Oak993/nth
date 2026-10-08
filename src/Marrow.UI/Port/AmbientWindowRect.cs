using System.Runtime.InteropServices;
using Avalonia;

namespace Marrow.UI.Port;

[StructLayout(LayoutKind.Sequential)]
internal struct AmbientWindowRect
{
    public int Left, Top, Right, Bottom;
    public readonly Rect Rect => new(Left, Top, Right - Left, Bottom - Top);
}
