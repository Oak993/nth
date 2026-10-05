// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-14): VT-7's download behind the page's seam.
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary><see cref="ISpeechModelDownload"/> over a <see cref="ModelDownload"/>.</summary>
internal sealed class ModelDownloadHandle : ISpeechModelDownload
{
    private readonly ModelDownload download;

    public ModelDownloadHandle(ModelDownload download)
    {
        this.download = download;
        Completion = Result(download);
    }

    public double Fraction => download.Fraction;

    public event Action<double>? FractionChanged
    {
        add => download.FractionChanged += value;
        remove => download.FractionChanged -= value;
    }

    public Task<ModelDownloadStatus> Completion { get; }

    public void Cancel() => download.Cancel();

    private static async Task<ModelDownloadStatus> Result(ModelDownload download) => (await download.Completion.ConfigureAwait(false)).Status;
}
