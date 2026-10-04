// PORT-ONLY, owner-ruled addition (OD-0099 amendments; first-release skills downloads, SK-3): how one attempt reports why it ended.
namespace Marrow.Services.Port.Downloads;

/// <summary>An attempt's failure. <see cref="Transient"/> ones (unreachable, cut, timed out, 5xx) are retried when the options allow it.</summary>
internal sealed class DownloadFailureException(DownloadFailure failure, bool transient) : Exception(failure.Message)
{
    internal DownloadFailure Failure { get; } = failure;

    internal bool Transient { get; } = transient;
}
