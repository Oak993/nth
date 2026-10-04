// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.Security.Cryptography;

namespace Marrow.Services.Port.Skills.Install;

public static class InstallHash
{
    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Sha256File(string path)
    {
        InstallPaths.NoReparseAncestors(path);
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
    public static void Verify(string path, long size, string sha256)
    {
        InstallPaths.NoReparseAncestors(path);
        if (!File.Exists(path) || size < 0 || new FileInfo(path).Length != size || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Install input did not match its pin.");
        using var input = File.OpenRead(path);
        if (!Convert.ToHexStringLower(SHA256.HashData(input)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Install input did not match its pin.");
    }
}
