// PORT-ONLY: SK-17. The existing ThreadRuntime CLI switch, with an async-flow factory seam for offline launch tests.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Runtime;

internal static class CliSessionFactory
{
    public static Ambient<Func<SessionConfiguration, ProviderSession>> Current { get; } = new(Create);
    private static ProviderSession Create(SessionConfiguration configuration) => Marrow.Services.Port.Skills.Providers.SkillSessionFactory.Create(configuration);
}
