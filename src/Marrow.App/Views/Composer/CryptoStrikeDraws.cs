using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Marrow.App.Views.Composer;

public sealed class CryptoStrikeDraws : IStrikeDraws
{
    public static CryptoStrikeDraws Shared { get; } = new();
    public double Next()
    {
        Span<byte> bytes = stackalloc byte[4]; RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes) / 4294967296d;
    }
}
