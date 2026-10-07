namespace Marrow.Services.Port.Skills.Custom;

internal sealed record CheckedCustomItem(CustomAddCandidate Candidate, CustomAddKind Kind, CustomProvenance Provenance, Dictionary<string, byte[]> Files, string? Executable, string? Command);
