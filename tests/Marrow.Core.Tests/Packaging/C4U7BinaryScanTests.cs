using System.Text;
using Marrow.Core.Tests.Fixtures;
using Nth.Build;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4U7BinaryScanTests
{
    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Branding_scan_finds_utf16_word_at_eof_of_an_exact_one_megabyte_file()
    {
        var folder = PackagingRig.TempFolder("c4-eof");
        try
        {
            var bytes = new byte[1_048_576];
            var word = Encoding.Unicode.GetBytes("Marrow");
            word.CopyTo(bytes, bytes.Length - word.Length);
            var path = Path.Combine(folder.FullName, "setup-string-fixture.bin");
            File.WriteAllBytes(path, bytes);
            Assert.Single(BinaryTextScan.FindWord(path, "Marrow", ascii: false, utf16: true));
        }
        finally { folder.Delete(recursive: true); }
    }
}
