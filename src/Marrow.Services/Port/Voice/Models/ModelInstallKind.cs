// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): install state per model id (voice-SPEC V-9.4).
namespace Marrow.Services.Port.Voice.Models;

/// <summary>V-9.4: NotInstalled, Downloading(fraction), Installed, InUse (installed and selected).</summary>
public enum ModelInstallKind { NotInstalled, Downloading, Installed, InUse }
