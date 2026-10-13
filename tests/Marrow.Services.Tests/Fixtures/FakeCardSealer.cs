using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (Dextra AS-20): a stand-in for DPAPI that seals by XOR with a fixed key behind a marker, so a test can prove
/// the bytes Credential Manager receives are sealed and that a seal from elsewhere does not open. It keeps no copy of what
/// it seals. <see cref="Opened"/> counts the opens (the card is opened only for a fill).
/// </summary>
public sealed class FakeCardSealer : ICardSealer
{
    private static readonly byte[] Marker = "SEALED1:"u8.ToArray();

    public int Opened { get; private set; }

    public bool Refuse { get; set; }

    public byte[] Seal(byte[] plain)
    {
        if (Refuse) throw new System.Security.Cryptography.CryptographicException("refused");
        var sealedBytes = new byte[Marker.Length + plain.Length];
        Marker.CopyTo(sealedBytes, 0);
        for (var i = 0; i < plain.Length; i++) sealedBytes[Marker.Length + i] = (byte)(plain[i] ^ 0x5A);
        return sealedBytes;
    }

    public byte[]? Open(byte[] sealedBytes)
    {
        Opened++;
        if (sealedBytes.Length < Marker.Length || !sealedBytes.AsSpan(0, Marker.Length).SequenceEqual(Marker)) return null;
        var plain = GC.AllocateArray<byte>(sealedBytes.Length - Marker.Length, pinned: true);
        for (var i = 0; i < plain.Length; i++) plain[i] = (byte)(sealedBytes[Marker.Length + i] ^ 0x5A);
        return plain;
    }
}
