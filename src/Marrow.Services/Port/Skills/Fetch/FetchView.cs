// PORT-ONLY: first-release feature unit SK-16 (DL-19). What a surface sees of a download that is running for an item, whoever started it.
namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>A download in flight: its progress, 0 to 1 (1 means the bytes are in and the item is being checked), or null while the size of the work is unknown.</summary>
public sealed record FetchView(double? Progress);
