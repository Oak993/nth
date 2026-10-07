// The installed Windows SDK UIAutomationClient.h ABI, called from the driver's MTA while the UI thread pumps.
using System.Runtime.InteropServices;
using Avalonia;

internal static class NativeUiaProbe
{
    internal static Dictionary<string, string> PropertiesAt(PixelPoint point)
    {
        var clsid = new Guid("ff48dba4-60ef-4201-aa87-54103eef594e");
        var iid = new Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, 0, 1, ref iid, out var automation));
        try
        {
            var fromPoint = Method<FromPoint>(automation, 7); // IUnknown + CompareElements/Ids/Root/Handle
            Marshal.ThrowExceptionForHR(fromPoint(automation, point, out var element));
            try
            {
                var get = Method<Property>(element, 10); // IUIAutomationElement::GetCurrentPropertyValue
                var variant = Marshal.AllocHGlobal(24);
                try
                {
                    for (var i = 0; i < 24; i++) Marshal.WriteByte(variant, i, 0);
                    var properties = new Dictionary<string, string>();
                    foreach (var (id, name) in new[] { (30005, "Name"), (30012, "ClassName"), (30003, "ControlType"), (30020, "NativeHandle") })
                    {
                        Marshal.ThrowExceptionForHR(get(element, id, variant));
                        var kind = Marshal.ReadInt16(variant);
                        properties[name] = kind == 8 ? Marshal.PtrToStringBSTR(Marshal.ReadIntPtr(variant, 8))
                            : kind == 3 ? Marshal.ReadInt32(variant, 8).ToString(System.Globalization.CultureInfo.InvariantCulture) : "VT" + kind.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        VariantClear(variant);
                    }
                    return properties;
                }
                finally { VariantClear(variant); Marshal.FreeHGlobal(variant); }
            }
            finally { Marshal.Release(element); }
        }
        finally { Marshal.Release(automation); }
    }
    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int FromPoint(nint instance, PixelPoint point, out nint element);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Property(nint instance, int id, nint value);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint value);
    [DllImport("oleaut32.dll")] private static extern int VariantClear(nint value);
}
