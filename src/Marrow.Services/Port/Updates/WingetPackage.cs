// PORT-ONLY: UE-3; an installed package matched by winget, not a catalogue entry.
namespace Marrow.Services.Port.Updates;

public sealed record WingetPackage(string Id, string Title, string Version, string? Available, string Source);
