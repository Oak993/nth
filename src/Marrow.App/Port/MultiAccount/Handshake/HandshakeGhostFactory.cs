// PORT-ONLY: MA-4-3. The ghost is the same capsule style with the incoming account/relay step, as page :3293-3347.
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Runtime;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Shared;
namespace Marrow.App.Port.MultiAccount.Handshake;
internal static class HandshakeGhostFactory
{
    internal static Func<Guid, CapsuleContent?> For(ThreadRuntime runtime)
    {
        return id =>
        {
            if (runtime.App is not { } app || runtime.Thread is not { } thread) return null;
            var account = app.Settings.Accounts.Accounts.FirstOrDefault(a => a.Id == id); if (account is null) return null;
            var relay = thread.PillRelay;
            if (relay is not null)
            {
                var pill = app.Settings.Pills.Pills.FirstOrDefault(p => p.Id == relay.PillId); var step = pill?.Steps.Select((s, n) => (s, n)).Where(x => x.s.AccountId == id.ToString("D")).Select(x => (int?)x.n).FirstOrDefault();
                relay = step is { } index ? relay with { StepIndex = index, AccountId = id.ToString("D") } : null;
            }
            var actions = new AppAccountActions(app); var model = new ComposerCapsuleModel(app.Settings.Accounts, app.Settings.Pills, app.IsAccountConnected, actions.Usage);
            var content = model.For(new CapsuleSubject(account.Service, id, relay, false, null));
            if (content is not null) return content;
            var usage = actions.Usage(id); var use = usage?.NoFiveHour == true ? usage.Week ?? 0 : usage?.FiveHour ?? 0;
            var name = app.Settings.Accounts.DisplayName(id) ?? app.Settings.Accounts.DefaultName(id);
            return new CapsuleContent(AccountMono.Of(name, account.CustomName is not null), name, use, CapsuleLayout.IsHot(use), "Account: " + name, null);
        };
    }
}
