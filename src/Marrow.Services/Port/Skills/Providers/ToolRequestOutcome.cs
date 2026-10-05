// PORT-ONLY: SK-11f / DL-14. The user's answer to a request_tool offer: "installed at <path>" or "declined" (skills-ONDEMAND-PLAN.md 5.1 S4).
namespace Marrow.Services.Port.Skills.Providers;

public sealed record ToolRequestOutcome
{
    private ToolRequestOutcome() { }

    /// <summary>The installed path, or null when the user declined.</summary>
    public string? InstalledPath { get; private init; }

    public static ToolRequestOutcome Declined { get; } = new();

    public static ToolRequestOutcome Installed(string path) => new() { InstalledPath = path };
}
