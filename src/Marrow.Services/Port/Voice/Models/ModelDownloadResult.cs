// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): how a model download ended (V-9.5).
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

public sealed record ModelDownloadResult(ModelDownloadStatus Status, DownloadFailure? Failure);
