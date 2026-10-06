// PORT-ONLY: UE-3; filtered Click-to-Run configuration (no account or licensing data).
namespace Marrow.Services.Port.Updates;

public sealed record OfficeInstallation(string Version, string? Channel, string ClientPath, bool ClientExists);
