// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): one model download in progress (V-9.5).
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

public sealed class ModelDownload
{
    internal ModelDownload() { }

    public double Fraction => throw new NotImplementedException();

    public Task<ModelDownloadResult> Completion => throw new NotImplementedException();

    public DownloadJob Job => throw new NotImplementedException();

    public event Action<double>? FractionChanged;

    public void Pause() => throw new NotImplementedException();

    public void Resume() => throw new NotImplementedException();

    public void Cancel() => throw new NotImplementedException();

    private void Unused() => FractionChanged?.Invoke(0);
}
