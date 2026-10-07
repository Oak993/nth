using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Skills.Custom;

internal static partial class CustomSourcePaths
{
    internal const int MaxFiles = 4096;
    internal const long MaxBytes = 128 * 1024 * 1024;
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static void Require(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\', StringComparison.Ordinal) || path.IndexOfAny([':', '"', '%', '\0']) >= 0 || path.Any(char.IsControl)) throw new InvalidDataException("Unsafe custom-source path.");
        foreach (var part in path.Split('/'))
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(['<', '>', '|', '?', '*']) >= 0 || Device().IsMatch(part))
                throw new InvalidDataException("Unsafe custom-source path.");
        }
    }
    internal static Dictionary<string, byte[]> Validate(IReadOnlyDictionary<string, byte[]> files)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var (path, bytes) in files)
        {
            Require(path);
            size = checked(size + bytes.LongLength);
            if (size > MaxBytes || result.Count >= MaxFiles) throw new InvalidDataException("Custom source exceeds the file or byte limit.");
            if (!result.TryAdd(path, bytes.ToArray())) throw new InvalidDataException("Custom source has duplicate Windows paths.");
        }
        foreach (var path in result.Keys)
            for (var at = path.IndexOf('/', StringComparison.Ordinal); at >= 0; at = path.IndexOf('/', at + 1))
                if (result.ContainsKey(path[..at])) throw new InvalidDataException("A file occupies a parent directory.");
        return result;
    }
    internal static Dictionary<string, byte[]> Unzip(byte[] bytes, bool github)
    {
        if (bytes.LongLength > MaxBytes) throw new InvalidDataException("Custom archive exceeds the byte limit.");
        using var memory = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string? top = null;
        long size = 0;
        foreach (var entry in zip.Entries)
        {
            var mode = (entry.ExternalAttributes >> 16) & 0xf000;
            if (mode is 0xa000 or 0x6000 or 0x2000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Custom archives cannot contain links or device entries.");
            var path = entry.FullName.TrimEnd('/');
            Require(path);
            if (github)
            {
                var slash = path.IndexOf('/', StringComparison.Ordinal);
                var current = slash < 0 ? path : path[..slash];
                top ??= current;
                if (top != current) throw new InvalidDataException("GitHub archive has multiple roots.");
                if (slash < 0) { if (entry.FullName.EndsWith('/')) continue; throw new InvalidDataException("GitHub archive has a root file."); }
                path = path[(slash + 1)..];
            }
            if (entry.FullName.EndsWith('/')) continue;
            size = checked(size + entry.Length);
            if (size > MaxBytes || files.Count >= MaxFiles) throw new InvalidDataException("Custom archive exceeds the file or byte limit.");
            using var stream = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = stream.Read(buffer)) > 0)
            {
                if (output.Length + count > entry.Length) throw new InvalidDataException("Archive entry exceeded its declared length.");
                output.Write(buffer, 0, count);
            }
            if (output.Length != entry.Length || !files.TryAdd(path, output.ToArray())) throw new InvalidDataException("Custom archive has a truncated or duplicate entry.");
        }
        return Validate(files);
    }
    internal static string Fingerprint(IReadOnlyDictionary<string, byte[]> files) => Hash(Encoding.UTF8.GetBytes(string.Join('\n', files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + Hash(p.Value)))));
    internal static string Repository(string input)
    {
        var value = input.Trim();
        if (value.StartsWith("https://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host != "github.com" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("Use a public GitHub repository URL or owner/repository.");
            value = uri.AbsolutePath.Trim('/');
        }
        if (value.EndsWith(".git", StringComparison.Ordinal)) value = value[..^4];
        if (!RepositoryName().IsMatch(value)) throw new ArgumentException("Use a public GitHub repository URL or owner/repository.");
        return value;
    }
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Device();
    [GeneratedRegex(@"^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryName();
}
