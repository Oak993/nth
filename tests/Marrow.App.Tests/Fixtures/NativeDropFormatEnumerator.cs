using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Marrow.App.Tests.Window.Native;

// IEnumFORMATETC over a fixed list, for NativeDropDataObject.
[ComVisible(true)]
internal sealed class NativeDropFormatEnumerator(FORMATETC[] formats) : IEnumFORMATETC
{
    private int next;

    public int Next(int count, FORMATETC[] result, int[] fetched)
    {
        var taken = 0;
        while (taken < count && next < formats.Length) result[taken++] = formats[next++];
        if (fetched is { Length: > 0 }) fetched[0] = taken;
        return taken == count ? 0 : 1;
    }

    public int Skip(int count)
    {
        next = Math.Min(formats.Length, next + count);
        return next < formats.Length ? 0 : 1;
    }

    public int Reset()
    {
        next = 0;
        return 0;
    }

    public void Clone(out IEnumFORMATETC newEnum) => newEnum = new NativeDropFormatEnumerator(formats) { next = next };
}
