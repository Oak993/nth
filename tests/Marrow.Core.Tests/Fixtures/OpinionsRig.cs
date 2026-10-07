using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Testing.MultiAccountSettings;
namespace Marrow.Core.Tests.Fixtures;
// PORT-ONLY: page opinions sample M2/C1/L1, real in-memory stores and only job-0 recording fakes.
internal sealed class OpinionsRig
{
    internal AccountsRig Accounts {get;}=new();
    internal Guid Thread {get;}=Guid.NewGuid();
    internal Guid Lead {get;}
    internal Guid M2 {get;}
    internal Guid C1 {get;}
    internal Guid L1 {get;}
    internal FakeThreadAccountPort Claude {get;}
    internal FakeThreadAccountPort Codex {get;}
    internal FakeThreadAccountPort Local {get;}
    internal FakeOpinionsPort Port {get;}=new();
    internal OtherServiceConsentStore Consent {get;}
    internal OpinionsRig()
    {
        Lead=Accounts.Connect(ProviderKind.Claude,"Max").Id;M2=Accounts.ConnectAnother(ProviderKind.Claude,"Max").Id;
        C1=Accounts.Connect(ProviderKind.Codex).Id;Accounts.ConnectAnother(ProviderKind.Codex);
        L1=Accounts.Connect(ProviderKind.Local).Id;Accounts.ConnectAnother(ProviderKind.Local);
        Accounts.Accounts.SetDefaults(M2,new(){Model="Opus 5.5"});Accounts.Accounts.SetDefaults(C1,new(){Model="GPT-5.5"});Accounts.Accounts.SetDefaults(L1,new(){Model="Qwen Coder 32B"});
        Claude=new(Accounts,ProviderKind.Claude);Codex=new(Accounts,ProviderKind.Codex);Local=new(Accounts,ProviderKind.Local);Consent=new(Accounts.Settings);
    }
    internal AskOthersModel Model()=>new(Thread,Claude,[Claude,Codex,Local],Port,Consent);
}
