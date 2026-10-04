// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): how a CUDA pack install ended.
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Voice.Models;

public sealed record CudaInstallResult(CudaInstallStatus Status, string? Folder, DownloadFailure? Failure);
