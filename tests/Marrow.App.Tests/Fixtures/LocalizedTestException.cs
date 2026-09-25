using Marrow.Core.Support;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

internal sealed class LocalizedTestException(string description) : Exception(description), ILocalizedError
{
    public string? ErrorDescription { get; } = description;
}
