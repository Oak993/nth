using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.Core.Port;

// PORT-ONLY. Windows 10 1903 and later ship ICU as %SystemRoot%\System32\icu.dll with unversioned C exports;
// .NET's own globalization loads the same library on Windows, so this adds no dependency and no package.
// Used only where .NET has no API for a Unicode property Swift reads: full case mapping, Alphabetic,
// Numeric_Type, White_Space (ED-073). The ICU version is Windows' own, not Apple's: a named, bounded deviation.
// Source: draft A (proto-A Support/IcuNative.cs).
internal static class IcuNative
{
    private const string Library = "icu.dll";
    private const int BufferOverflow = 15; // U_BUFFER_OVERFLOW_ERROR

    internal const int PropertyAlphabetic = 0;       // UCHAR_ALPHABETIC
    internal const int PropertyWhiteSpace = 31;      // UCHAR_WHITE_SPACE
    internal const int PropertyNumericType = 0x1009; // UCHAR_NUMERIC_TYPE

    [DllImport(Library, EntryPoint = "u_strToLower", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int StrToLower([Out] char[] dest, int destCapacity, string src, int srcLength,
        [MarshalAs(UnmanagedType.LPStr)] string locale, ref int errorCode);

    [DllImport(Library, EntryPoint = "u_strToUpper", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int StrToUpper([Out] char[] dest, int destCapacity, string src, int srcLength,
        [MarshalAs(UnmanagedType.LPStr)] string locale, ref int errorCode);

    [DllImport(Library, EntryPoint = "u_strFoldCase", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int StrFoldCase([Out] char[] dest, int destCapacity, string src, int srcLength,
        uint options, ref int errorCode);

    [DllImport(Library, EntryPoint = "u_hasBinaryProperty")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool HasBinaryProperty(int codePoint, int property);

    [DllImport(Library, EntryPoint = "u_getIntPropertyValue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetIntPropertyValue(int codePoint, int property);

    internal enum CaseMap { Lower, Upper, Fold }

    /// <summary>
    /// Maps ONE scalar with ICU's root locale (no Turkish or Lithuanian tailoring). Called per scalar so that
    /// no context rule (Final_Sigma) can apply: Swift's lowercased()/uppercased() append each scalar's
    /// full mapping, context-free.
    /// </summary>
    internal static string Map(Rune scalar, CaseMap map)
    {
        var source = scalar.ToString();
        var buffer = new char[8];
        var error = 0;
        var length = Invoke(map, buffer, source, ref error);
        if (error == BufferOverflow)
        {
            buffer = new char[length];
            error = 0;
            length = Invoke(map, buffer, source, ref error);
        }
        if (error > 0) throw new InvalidOperationException($"ICU case mapping failed with error {error}.");
        return new string(buffer, 0, length);
    }

    private static int Invoke(CaseMap map, char[] buffer, string source, ref int error) => map switch
    {
        CaseMap.Lower => StrToLower(buffer, buffer.Length, source, source.Length, "", ref error),
        CaseMap.Upper => StrToUpper(buffer, buffer.Length, source, source.Length, "", ref error),
        CaseMap.Fold => StrFoldCase(buffer, buffer.Length, source, source.Length, 0, ref error),
    };
}
