// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "Where it is kept", 16.1 the Credential Manager native gate; ED-NEW-dextra-as20-7).
// No Swift twin.
// The card's own seal before it reaches Credential Manager: DPAPI in user scope (CryptProtectData, no UI) with nth's own
// extra entropy, so another program running as the same user that reads nth's Credential Manager item still needs nth's
// entropy to open it (a hurdle, not a wall: the bank-side limit stays the true hard cap). Opened bytes land in a pinned
// array the caller zeroes; DPAPI's own output buffer is zeroed before it is freed.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Marrow.Services.Assistant;

/// <summary>Seals the card's bytes for keeping, and opens them again only for the moment of a fill. Internal: nothing outside the vault opens a card.</summary>
internal interface ICardSealer
{
    /// <summary>The sealed form of <paramref name="plain"/>. Throws when it cannot seal (nothing is kept then).</summary>
    byte[] Seal(byte[] plain);

    /// <summary>The opened bytes in a pinned array the caller must zero, or null when the seal does not open (wrong entropy, damaged).</summary>
    byte[]? Open(byte[] sealedBytes);
}

/// <summary>DPAPI in user scope with nth's own extra entropy (Windows only).</summary>
internal sealed class DpapiCardSealer : ICardSealer
{
    private const int UiForbidden = 0x1; // CRYPTPROTECT_UI_FORBIDDEN

    /// <summary>nth's extra entropy: fixed for nth, never stored beside the card.</summary>
    internal static byte[] Entropy => (byte[])entropy.Clone();

    private static readonly byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes("nth.dextra.card.v1:purchases are filled by nth alone"));

    public static DpapiCardSealer Instance { get; } = new();

    public byte[] Seal(byte[] plain) => Run(plain, entropy, protect: true) ?? throw new CryptographicException("Windows could not seal the card.");

    public byte[]? Open(byte[] sealedBytes) => Run(sealedBytes, entropy, protect: false);

    /// <summary>Opens with another entropy: the Native gate's proof that nth's entropy is needed.</summary>
    internal static byte[]? OpenWith(byte[] sealedBytes, byte[] otherEntropy) => Run(sealedBytes, otherEntropy, protect: false);

    private static byte[]? Run(byte[] input, byte[] extra, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The card is kept by Windows.");
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var extraHandle = GCHandle.Alloc(extra, GCHandleType.Pinned);
        try
        {
            var inBlob = new DataBlob { Size = input.Length, Data = inHandle.AddrOfPinnedObject() };
            var extraBlob = new DataBlob { Size = extra.Length, Data = extraHandle.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref inBlob, null, ref extraBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out var outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref extraBlob, IntPtr.Zero, IntPtr.Zero, UiForbidden, out outBlob);
            if (!ok) return null;
            try
            {
                var result = GC.AllocateArray<byte>(outBlob.Size, pinned: true);
                Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
                return result;
            }
            finally
            {
                Marshal.Copy(new byte[outBlob.Size], 0, outBlob.Data, outBlob.Size);
                LocalFree(outBlob.Data);
            }
        }
        finally
        {
            inHandle.Free();
            extraHandle.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", EntryPoint = "CryptProtectData", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
