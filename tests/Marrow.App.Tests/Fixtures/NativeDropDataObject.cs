using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Marrow.App.Tests.Window.Native;

// A real OLE IDataObject (a COM callable wrapper over this class) holding CF_HDROP, the format Explorer drags files in: a DROPFILES
// header (20 bytes, fWide) and the wide paths, each ended by a NUL, the list ended by one more. The paths are read when asked, so
// a test can delete one between the drag-enter and the drop, as a user's file manager can.
[ComVisible(true)]
internal sealed class NativeDropDataObject(IReadOnlyList<string> paths) : ComDataObject
{
    internal const short CfHdrop = 15;
    private const int DvEFormatetc = unchecked((int)0x80040064);
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int OleEAdviseNotSupported = unchecked((int)0x80040003);
    private const int DataSSameFormatetc = 0x00040130;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;

    internal int GetDataCalls { get; private set; }

    private static bool Accepts(FORMATETC format) => format.cfFormat == CfHdrop && (format.tymed & TYMED.TYMED_HGLOBAL) != 0 && format.dwAspect == DVASPECT.DVASPECT_CONTENT;

    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        if (!Accepts(format)) Marshal.ThrowExceptionForHR(DvEFormatetc);
        GetDataCalls++;
        var list = Encoding.Unicode.GetBytes(string.Concat(paths.Select(path => path + "\0")) + "\0");
        var global = GlobalAlloc(GmemMoveable | GmemZeroInit, (nuint)(20 + list.Length));
        var memory = GlobalLock(global);
        Marshal.WriteInt32(memory, 0, 20); // pFiles: where the list starts
        Marshal.WriteInt32(memory, 16, 1); // fWide
        Marshal.Copy(list, 0, memory + 20, list.Length);
        GlobalUnlock(global);
        medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = global, pUnkForRelease = null };
    }

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => Marshal.ThrowExceptionForHR(ENotImpl);

    public int QueryGetData(ref FORMATETC format) => Accepts(format) ? 0 : DvEFormatetc;

    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = 0;
        return DataSSameFormatetc;
    }

    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) => Marshal.ThrowExceptionForHR(ENotImpl);

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET) Marshal.ThrowExceptionForHR(ENotImpl);
        return new NativeDropFormatEnumerator([new FORMATETC { cfFormat = CfHdrop, ptd = 0, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL }]);
    }

    public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
    {
        connection = 0;
        return OleEAdviseNotSupported;
    }

    public void DUnadvise(int connection) => Marshal.ThrowExceptionForHR(OleEAdviseNotSupported);

    public int EnumDAdvise(out IEnumSTATDATA? enumAdvise)
    {
        enumAdvise = null;
        return OleEAdviseNotSupported;
    }

    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint global);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint global);
}
