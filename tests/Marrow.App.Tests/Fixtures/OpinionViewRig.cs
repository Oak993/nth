using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Testing.MultiAccountSettings;
namespace Marrow.App.Tests.Fixtures;
// PORT-ONLY: MA-3b-6 view fixtures; real stores, job-0 fakes, no provider calls.
internal sealed class OpinionViewRig
{
    internal AccountsRig Accounts { get; } = new();
    internal Guid Thread { get; } = Guid.NewGuid();
    internal Guid Lead { get; }
    internal Guid Other { get; }
    internal Guid Cross { get; }
    internal FakeThreadAccountPort Current { get; }
    internal FakeThreadAccountPort Codex { get; }
    internal FakeOpinionsPort Port { get; } = new();
    internal OtherServiceConsentStore Consent { get; }
    internal OpinionViewRig()
    {
        Lead = Accounts.Connect(ProviderKind.Claude, "Max").Id;
        Other = Accounts.ConnectAnother(ProviderKind.Claude, "Max").Id;
        Cross = Accounts.Connect(ProviderKind.Codex).Id;
        Accounts.Accounts.Add(ProviderKind.Codex); // page calls the connected account C1; second account remains disconnected.
        Accounts.Accounts.SetDefaults(Other, new() { Model = "Opus 5.5" });
        Accounts.Accounts.SetDefaults(Cross, new() { Model = "GPT-5.5" });
        Current = new(Accounts, ProviderKind.Claude); Codex = new(Accounts, ProviderKind.Codex);
        Consent = new(Accounts.Settings);
    }
    internal AskOthersModel Model() { Current.Refresh(); Codex.Refresh(); return new(Thread, Current, [Current, Codex], Port, Consent); }
}
