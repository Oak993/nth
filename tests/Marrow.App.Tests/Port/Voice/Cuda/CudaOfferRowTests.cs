using Marrow.App.Port.Voice.Cuda;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Port.Voice.Cuda;

// VT-17 (OD-0106 CUDA A; voice-SPEC V-9.2, V-16.3.3): the row's words and states are the page's look A: "NVIDIA CUDA", 644 MB from nuget.org and NVIDIA, a Download capsule, and the
// model list's state column and 2 px bar for progress, cancel and the end states. The page's script is read from the repository.
public sealed class CudaOfferRowTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN look A; Req Q-V-9 C
    public void The_rows_strings_are_the_ones_the_page_draws_in_look_A()
    {
        var js = VoiceNotesPage.Script();
        foreach (var piece in new[] { "<b>NVIDIA CUDA</b>", "'Optional, for NVIDIA cards. '", "' from nuget.org and NVIDIA'", "'Downloading from nuget.org and NVIDIA'",
            "'Installed. Restart Nth to use it'", "'The download didn’t finish. Nothing was installed'", "title=\"Cancel the download\"", "'<button class=\"czbtn\" data-a=\"dl\">Download</button>'",
            "<span>Installed</span>", "const SIZE = '644 MB'" })
            Assert.Contains(piece, js, StringComparison.Ordinal);
        var row = CudaOfferStates.Offer("644 MB");
        Assert.Equal("NVIDIA CUDA", row.Title);
        Assert.Equal("Optional, for NVIDIA cards. 644 MB from nuget.org and NVIDIA", row.Detail);
        Assert.Equal("Download", row.ActionText);
        Assert.Equal(CudaOfferAction.Download, row.Action);
        Assert.False(row.ShowsBar);
        Assert.False(row.ShowsCheck);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN look A
    public void The_downloading_row_shows_the_percentage_the_cancel_x_and_the_bar()
    {
        var row = CudaOfferStates.Downloading("644 MB", 0.424);

        Assert.Equal(("Downloading from nuget.org and NVIDIA", "42%", CudaOfferAction.Cancel), (row.Detail, row.StateText, row.Action));
        Assert.Equal("Cancel the download", row.CancelTitle);
        Assert.True(row.ShowsBar);
        Assert.Equal(0.424, row.Progress, 9);
        Assert.Equal("43%", CudaOfferStates.Downloading("644 MB", 0.425).StateText); // Math.round: half up
        Assert.Equal("100%", CudaOfferStates.Downloading("644 MB", 1.0).StateText);
        Assert.Equal("0%", CudaOfferStates.Downloading("644 MB", 0.0).StateText);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN look A
    [Trait("Decision", "ED-267")] // ED-351, numbered by the lead at merge
    public void The_installed_row_says_to_restart_and_the_failed_row_offers_the_download_again()
    {
        var installed = CudaOfferStates.Installed("644 MB");
        Assert.Equal(("Installed. Restart Nth to use it", "Installed", CudaOfferAction.None, true), (installed.Detail, installed.StateText, installed.Action, installed.ShowsCheck));
        Assert.False(installed.ShowsBar);

        var failed = CudaOfferStates.Failed("644 MB");
        Assert.Equal(("The download didn’t finish. Nothing was installed", "Download", CudaOfferAction.Download), (failed.Detail, failed.ActionText, failed.Action));
        Assert.False(failed.ShowsBar);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Oracle D-VN look A; Req V-9.2
    public void The_size_is_the_three_pinned_files_added_and_shown_as_the_model_list_shows_sizes()
    {
        var total = CudaRuntimePack.Cuda12.Bytes + CudaCompanionPins.Bytes;

        Assert.Equal(250_058_371L + 391_101_865L + 2_474_121L, total);
        Assert.Equal("644 MB", ModelCatalog.SizeText(total));
        Assert.Equal("644 MB", CudaOfferStates.SizeText(CudaRuntimePack.Cuda12, new FailClosedCudaCompanions()));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // pin
    public void The_companion_sizes_are_voice_pins_jsons()
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(PinsJson()));
        var c = json.RootElement.GetProperty("cuda").GetProperty("nvidiaCompanions");

        Assert.Equal(CudaCompanionPins.Bytes, c.GetProperty("libcublas").GetProperty("bytes").GetInt64() + c.GetProperty("cuda_cudart").GetProperty("bytes").GetInt64());
    }

    private static string PinsJson([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "..", "src", "Marrow.Services", "Port", "Voice", "Assets", "voice-pins.json"));
}
