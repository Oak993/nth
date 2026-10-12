using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// Web surfaces W23 on the owner's PC (DESIGN 13.4, run alone, never in the ordinary filters; the weekly Canary job runs the second class).
// These reach Microsoft or the real WebView2 runtime. They start no window, and they never run the downloaded bootstrapper: running it
// installs a runtime onto the PC, which is the clean-VM matrix's job (scripts/vm/Invoke-NthCleanVmMatrix.ps1).
[Trait("Decision", "ED-NEW-web-w23-2")]
[Trait("Feature", "W-23")]
public sealed class WebRuntimeInstallerNativeTests
{
    [Fact]
    public async Task The_real_bootstrapper_downloads_over_https_and_passes_the_signature_and_signer_check_and_is_not_run()
    {
        var source = new HttpBootstrapperSource(client: null, tempRoot: null, allowTestHost: true);
        string folder;
        using (var file = await source.DownloadAsync(CancellationToken.None))
        {
            folder = Path.GetDirectoryName(file.Path)!;
            var length = new FileInfo(file.Path).Length;
            Assert.True(WebRuntimeBootstrapper.IsAcceptableSize(length), length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var result = new WindowsAuthenticodeCheck().Check(file.Path);
            Assert.True(result.Valid, "Windows does not verify the bootstrapper's Authenticode signature");
            Assert.True(WebRuntimeBootstrapper.IsAcceptableSigner(result.Subject), result.Subject);
        }

        Assert.False(Directory.Exists(folder), "the download folder is removed with the file");
    }
}
