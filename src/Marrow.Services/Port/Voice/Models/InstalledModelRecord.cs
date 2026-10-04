// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): one entry of installed.json (V-9.3).
namespace Marrow.Services.Port.Voice.Models;

/// <summary>What installed.json records of one file: id, bytes, SHA-256, when it was verified and the file's own last-write time (both UTC).</summary>
public sealed record InstalledModelRecord(string Id, long Bytes, string Sha256, DateTime VerifiedUtc, DateTime FileTimeUtc);
