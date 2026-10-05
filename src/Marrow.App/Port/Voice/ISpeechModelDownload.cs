// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-14): one model download as the Voice typing page sees it.
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary>A started download: its fraction (the row's progress), how it ended, and the cross that cancels it (the partial is kept, V-9.5 item 4).</summary>
internal interface ISpeechModelDownload
{
    /// <summary>0 to 1.</summary>
    double Fraction { get; }

    /// <summary>Raised on any thread, at most ten times a second (V-9.5 item 2).</summary>
    event Action<double>? FractionChanged;

    /// <summary>How it ended.</summary>
    Task<ModelDownloadStatus> Completion { get; }

    /// <summary>Stops the transfer and keeps the partial.</summary>
    void Cancel();
}
