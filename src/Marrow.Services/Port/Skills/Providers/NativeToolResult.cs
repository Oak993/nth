// PORT-ONLY: SK-11f / WU-12, DL-14. What one of Marrow's own tools hands back to the session.
using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Services.Port.Skills.Providers;

/// <param name="Title">The timeline row's title.</param>
/// <param name="Output">What the model reads.</param>
/// <param name="Failed">True when the row ends Failed.</param>
/// <param name="Events">The normalized events this call produced (DL-12).</param>
public sealed record NativeToolResult(string Title, string Output, bool Failed, IReadOnlyList<ToolEvent> Events);
