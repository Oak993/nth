// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): the row in each state (look A: its own row under the recommendation).
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice.Cuda;

/// <summary>
/// The words are the page's look A (vn-page.js, <c>card()</c>): "NVIDIA CUDA", then a detail line, then the model list's state column (V-16.3.3): a Download capsule, the percentage
/// with the cancel x and the 2 px bar, the check with "Installed". The size is shown as the model list shows sizes (V-9.2).
/// </summary>
public static class CudaOfferStates
{
    public const string Title = "NVIDIA CUDA";
    public const string CancelTitle = "Cancel the download";

    /// <summary>The offer's size: the pack plus NVIDIA's two archives, as V-9.2 shows sizes ("644 MB").</summary>
    public static string SizeText(CudaRuntimePack pack, ICudaCompanions companions) => ModelCatalog.SizeText(pack.Bytes + companions.Bytes);

    public static CudaOfferRow Offer(string size) => new(true, CudaOfferState.Offer, Title, "Optional, for NVIDIA cards. " + size + " from nuget.org and NVIDIA", "", 0, CudaOfferAction.Download, "Download", "", false, false);

    public static CudaOfferRow Downloading(string size, double progress)
    {
        _ = size;
        var shown = Math.Clamp(progress, 0, 1);
        return new CudaOfferRow(true, CudaOfferState.Downloading, Title, "Downloading from nuget.org and NVIDIA", Math.Floor(shown * 100 + 0.5).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%", shown,
            CudaOfferAction.Cancel, "", CancelTitle, false, true);
    }

    public static CudaOfferRow Installed(string size)
    {
        _ = size;
        return new CudaOfferRow(true, CudaOfferState.Installed, Title, "Installed. Restart nth to use it", "Installed", 0, CudaOfferAction.None, "", "", true, false);
    }

    public static CudaOfferRow Failed(string size)
    {
        _ = size;
        return new CudaOfferRow(true, CudaOfferState.Failed, Title, "The download didn’t finish. Nothing was installed", "", 0, CudaOfferAction.Download, "Download", "", false, false);
    }
}
