// PORT-ONLY (owner finding F2, 2026-10-08): the owner signed in to Claude Code from Marrow's Login button (a terminal runs the
// provider's login command), finished in the browser, closed the terminal, and Marrow still showed "Sign in". Droppy only
// re-checks on Check providers again; here every Login button also starts a quiet re-check every 3 seconds for up to 10
// minutes, which stops as soon as the provider reports signed in (ProviderRegistry.RecheckSignIn). One watch per provider.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Common;

namespace Marrow.App.Port;

internal static class ProviderLoginWatch
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);
    private static readonly Dictionary<ProviderKind, TaskHandle> watches = [];

    /// <summary>Opens the provider's login command in a terminal, then watches for the sign-in to land.</summary>
    public static void Open(ProviderRegistry providers, ProviderKind provider)
    {
        Workspace.OpenTerminal(provider.LoginCommand);
        Watch(providers, provider);
    }

    internal static void Watch(ProviderRegistry providers, ProviderKind provider)
    {
        if (watches.Remove(provider, out var previous)) previous.Cancel();
        watches[provider] = MainActor.Spawn(async () =>
        {
            for (var waited = TimeSpan.Zero; waited < Limit; waited += Interval)
            {
                if (!await SwiftTask.TrySleep(Interval)) return;
                if (await providers.RecheckSignIn(provider)) break;
            }
            watches.Remove(provider);
        });
    }
}
