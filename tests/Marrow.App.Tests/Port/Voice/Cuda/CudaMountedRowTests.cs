using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port.Settings;
namespace Marrow.App.Tests.Port.Voice.Cuda;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class CudaMountedRowTests(HeadlessSession headless)
{
    [Fact]
    public void The_mounted_row_keeps_the_picked_words_and_the_download_bar_and_installed_check()
    {
        headless.OnUiThread(() =>
        {
            var row = new CudaOfferView(_ => { }, () => { });
            row.Apply(CudaOfferStates.Offer("644 MB"));
            Assert.Equal("NVIDIA CUDA", row.Title);
            Assert.Equal("Optional, for NVIDIA cards. 644 MB from nuget.org and NVIDIA", row.Detail);
            Assert.True(row.Download.IsVisible);
            Assert.Equal(2, row.Bar.MeasureForTest());
            row.Apply(CudaOfferStates.Downloading("644 MB", .375));
            Assert.Equal("38%", row.ProgressText);
            Assert.True(row.Cancel.IsVisible);
            Assert.Equal("Cancel the download", Avalonia.Automation.AutomationProperties.GetName(row.Cancel));
            row.Apply(CudaOfferStates.Installed("644 MB"));
            Assert.Equal("Installed. Restart Nth to use it", row.Detail);
            Assert.True(row.Installed.IsVisible);
            Assert.False(row.Download.IsVisible);
            row.Apply(CudaOfferStates.Failed("644 MB"));
            Assert.Equal("The download didn’t finish. Nothing was installed", row.Detail);
            Assert.True(row.Download.IsVisible);
        });
    }
}
