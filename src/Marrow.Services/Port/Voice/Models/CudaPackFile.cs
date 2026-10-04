// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): one file of the CUDA runtime pack (Q-V-9 C).
namespace Marrow.Services.Port.Voice.Models;

public sealed record CudaPackFile(string Name, long Bytes);
