// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): how a CUDA pack install ended.
namespace Marrow.Services.Port.Voice.Models;

/// <summary>Installed; Unpinned (no SHA-256 to verify with: nothing fetched); Corrupt (hash or contents differ); Failed; Cancelled.</summary>
public enum CudaInstallStatus { Installed, Unpinned, Corrupt, Failed, Cancelled }
