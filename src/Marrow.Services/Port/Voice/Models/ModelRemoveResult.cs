// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): the answer of Remove (Q-V-13 B, store side).
namespace Marrow.Services.Port.Voice.Models;

/// <summary>Removed; InUse (the selected model is never removed); NotInstalled (nothing to remove).</summary>
public enum ModelRemoveResult { Removed, InUse, NotInstalled }
