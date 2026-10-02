using Marrow.Core.Models;

namespace Marrow.App.Tests.Window.Native;

// What GalleryContent.Seed put on disk: the ids a scene needs to pick a thread or a project by name.
internal sealed class GalleryData
{
    internal Project Web { get; init; } = null!;
    internal Project Api { get; init; } = null!;
    internal Project Notes { get; init; } = null!;

    /// <summary>The long conversation: Claude, Opus, High, with Hydra heads under it.</summary>
    internal ChatThread Main { get; init; } = null!;

    internal ChatThread Codex { get; init; } = null!;
    internal ChatThread Unread { get; init; } = null!;
    internal ChatThread Pinned { get; init; } = null!;
    internal HydraPair Pair { get; init; } = null!;

    /// <summary>The two Hydra heads of the main thread (Hank and Walter), as helper threads.</summary>
    internal ChatThread Hank { get; init; } = null!;

    internal ChatThread Walter { get; init; } = null!;
}
