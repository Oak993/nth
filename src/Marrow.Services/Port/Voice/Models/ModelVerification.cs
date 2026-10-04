// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): what verifying a model before its load found (V-9.4).
namespace Marrow.Services.Port.Voice.Models;

/// <summary>
/// Trusted: installed.json matched the file's size and time, nothing was read. Rehashed: it did not (or there was no record) and the hash
/// was right. Corrupt: the hash was wrong, the file and its record are gone and it must not be loaded. Missing: no file.
/// </summary>
public enum ModelVerification { Trusted, Rehashed, Corrupt, Missing }
