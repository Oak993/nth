using System.Formats.Tar;
using System.IO.Compression;
namespace Marrow.Services.Tests.Port.Skills.Install;
internal static class TarFixture
{
    public static byte[] Package(params (string Name, string Text)[] files)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, true))
        using (var writer = new TarWriter(gzip))
            foreach (var (name, text) in files)
            {
                using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
            }
        return bytes.ToArray();
    }
}
