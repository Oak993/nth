// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): how a model download ended (V-9.5).
namespace Marrow.Services.Port.Voice.Models;

/// <summary>Installed; Corrupt (size or hash differed, nothing was kept); Failed (any other failure); Cancelled (the partial is kept).</summary>
public enum ModelDownloadStatus { Installed, Corrupt, Failed, Cancelled }
