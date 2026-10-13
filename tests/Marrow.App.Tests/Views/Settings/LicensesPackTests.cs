using System.IO.Compression;
using System.Security.Cryptography;

namespace Marrow.App.Tests.Views.Settings;

// PR-SETTINGS-249's windows_path: "Ship all three documents with the installer". The installer is Velopack's, and what it installs and updates from is the full
// package scripts/package-windows.ps1 builds (OD-0062, ED-235) from the publish folder: LicensesShippingTests proves the build output and that the project copies the
// three documents to the publish root, and this reads the package itself. The package is a local build product (artifacts/releases, gitignored), so a checkout
// that has not run the script has none to read and this test then has nothing to check; the script stops a pack without the documents, so a package that exists
// holds them. Run on this PC after `powershell -File scripts/package-windows.ps1` (rec-settings-win): the full nupkg's lib/app holds all three, byte for byte.
public sealed class LicensesPackTests
{
    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-249")] // App/Views/Settings/SettingsView.swift:1012-1016 (the three files); windows_path: ship all three with the installer
    public void A_package_built_by_the_packaging_script_holds_the_three_documents_beside_the_executable_byte_for_byte()
    {
        var releases = Path.Combine(RepositoryRoot(), "artifacts", "releases");
        var packages = Directory.Exists(releases) ? Directory.GetFiles(releases, "*-full.nupkg") : [];
        var addendum = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "THIRD_PARTY_NOTICES.md"));
        foreach (var package in packages)
        {
            using var archive = ZipFile.OpenRead(package);
            byte[] Read(string name)
            {
                var entry = archive.GetEntry("lib/app/" + name);
                Assert.True(entry is not null, $"{Path.GetFileName(package)} holds no lib/app/{name}");
                using var stream = entry!.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            Assert.NotNull(archive.GetEntry("lib/app/nth.exe")); // the documents sit beside the executable the app reads them from (AppContext.BaseDirectory)
            Assert.Equal(LicensesShippingTests.LicenseSha256, Sha256(Read("LICENSE")));
            Assert.Equal(LicensesShippingTests.TrademarkSha256, Sha256(Read("TRADEMARK.md")));
            var notices = Read("THIRD_PARTY_NOTICES.md");
            Assert.Equal(LicensesShippingTests.UpstreamNoticesLength + 1 + addendum.Length, notices.Length);
            Assert.Equal(LicensesShippingTests.UpstreamNoticesSha256, Sha256(notices[..LicensesShippingTests.UpstreamNoticesLength]));
            Assert.Equal((byte)'\n', notices[LicensesShippingTests.UpstreamNoticesLength]);
            Assert.Equal(addendum, notices[(LicensesShippingTests.UpstreamNoticesLength + 1)..]);
        }
    }
}
