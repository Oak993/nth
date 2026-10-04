using System.Runtime.InteropServices;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Marrow.App.Tests.Window.Native;

// IDropTarget (objidl: 00000122-0000-0000-C000-000000000046), as OLE calls it on the object a window registered with RegisterDragDrop.
[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface NativeOleDropTarget
{
    [PreserveSig] int DragEnter([In, MarshalAs(UnmanagedType.Interface)] ComDataObject data, int keyState, NativeWindowPoint point, [In, Out] ref int effect);
    [PreserveSig] int DragOver(int keyState, NativeWindowPoint point, [In, Out] ref int effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop([In, MarshalAs(UnmanagedType.Interface)] ComDataObject data, int keyState, NativeWindowPoint point, [In, Out] ref int effect);
}
