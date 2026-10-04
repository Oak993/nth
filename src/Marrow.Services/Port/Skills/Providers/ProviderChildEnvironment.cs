// PORT-ONLY: SK-11f / DL-11. Not implemented yet.
namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderChildEnvironment
{
    public static IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> environment) => throw new NotImplementedException();
    public static IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> environment, string dataRoot) => throw new NotImplementedException();
}
