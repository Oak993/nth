// PORT-ONLY test fixture (SK-11f, DL-14). An offer whose availability a test can turn off between two requests.
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class AvailabilityFlip(params string[] catalogue) : IToolRequestOffer
{
    public bool IsAvailable { get; set; } = true;
    public bool Available => IsAvailable;
    public bool InCatalogue(string name) => catalogue.Contains(name, StringComparer.OrdinalIgnoreCase);
    public Task<ToolRequestOutcome> Ask(string name, string reason, CancellationToken cancellation) => throw new InvalidOperationException("The offer must not be asked.");
}
