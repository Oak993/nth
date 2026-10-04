// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): model downloads on the shared downloader (V-9.5).
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

public sealed class ModelDownloader
{
    public static IReadOnlyList<string> AllowedHosts { get; } = ["huggingface.co"];

    public static IReadOnlyList<TimeSpan> RetryDelays { get; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    /// <summary>The downloader the models use: SK-3's, with Hugging Face on its list and V-9.5 item 6's retries.</summary>
    public static Downloader CreateDownloader(string partFolder, IEnumerable<string>? allowedHosts = null, HttpMessageHandler? handler = null) =>
        throw new NotImplementedException();

    public ModelDownloader(ModelStore store, Downloader downloader, Func<ModelFile, Uri>? urlFor = null) => throw new NotImplementedException();

    public ModelDownload Start(ModelFile file) => throw new NotImplementedException();

    public ModelDownload? Active(ModelFile file) => throw new NotImplementedException();
}
