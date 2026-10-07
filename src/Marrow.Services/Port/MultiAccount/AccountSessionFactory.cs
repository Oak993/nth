// PORT-ONLY: OD-0114; ED-403. Detached and live launches share one account-bound construction boundary.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Port.Local;

namespace Marrow.Services.Port.MultiAccount;

public static class AccountSessionFactory
{
    // Scoped offline construction seam; all production launches use the same account boundary.
    public static Ambient<Func<ProviderRegistry, SessionConfiguration, Guid, bool, AccountSessionLaunch>> Launcher { get; } = new(Build);
    /// <summary>The one place a provider session object is built from its prepared configuration; tests override it for offline launches.</summary>
    public static Ambient<Func<SessionConfiguration, ProviderSession>> Constructor { get; } = new(Construct);

    private static ProviderSession Construct(SessionConfiguration configuration) => Marrow.Services.Port.Skills.Providers.SkillSessionFactory.Create(configuration);

    public static AccountSessionLaunch Create(ProviderRegistry registry, SessionConfiguration configuration, Guid accountId, bool freshContext = false)
        => Launcher.Current(registry, configuration, accountId, freshContext);

    private static AccountSessionLaunch Build(ProviderRegistry registry, SessionConfiguration configuration, Guid accountId, bool freshContext)
    {
        var source = freshContext ? configuration with
        {
            ResumeID = null, ResumeAt = null, ResumePath = null, Transcript = ValueList<TranscriptMessage>.Empty,
        } : configuration;
        var prepared = registry.PrepareSession(source, accountId, useAccountDefaults: false);
        // Local needs the registry's resolved server; every other service goes through the one construction seam (staging's CliSessionFactory.Current is this same ambient).
        ProviderSession session = prepared.Provider == ProviderKind.Local
            ? new LocalSession(prepared, registry.Local.Resolve(prepared.Model)
                ?? throw new InvalidOperationException("That local server or model is unavailable. Refresh the model list and select it again."))
            : Constructor.Current(prepared);
        return new(prepared, session);
    }
}
