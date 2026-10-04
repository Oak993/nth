namespace Marrow.Core.Port.SelfUpdate;

/// <summary>
/// A newer Marrow the feed offers (UE-1): the version, the release notes the publisher wrote (markdown) and the package's size in bytes,
/// each as the feed gives it.
/// </summary>
public sealed record SelfUpdateOffer(string Version, string? NotesMarkdown, long? SizeBytes);
