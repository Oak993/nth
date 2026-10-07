// PORT-ONLY: MA-3b-6 opinions B :4804-4811, :4855; OD-0115 button only, OD-0120 confirm before sending.
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.Core.Port.MultiAccount.Opinions;
public sealed class AskOthersModel : IDisposable
{
    private readonly Guid thread;
    private readonly IThreadAccountPort current;
    private readonly IReadOnlyList<IThreadAccountPort> ports;
    private readonly IOpinionsPort opinions;
    private readonly OtherServiceConsentStore consent;
    private readonly Func<ProviderKind,IReadOnlyList<ModelOption>> models;
    private readonly Func<ThreadAccount,bool> hasRoom;
    private readonly List<Guid> picked=[];
    public AskOthersModel(Guid thread,IThreadAccountPort current,IReadOnlyList<IThreadAccountPort> ports,IOpinionsPort opinions,
        OtherServiceConsentStore consent,Func<ProviderKind,IReadOnlyList<ModelOption>>? models=null,Func<ThreadAccount,bool>? hasRoom=null)
    {
        if(thread==Guid.Empty)throw new ArgumentException("A thread is required.",nameof(thread));
        this.thread=thread;this.current=current;this.ports=ports.Append(current).Distinct().ToArray();this.opinions=opinions;this.consent=consent;
        this.models=models??(_=>[]);this.hasRoom=hasRoom??(a=>!a.IsOut);
        foreach(var port in this.ports)port.Changed+=Refresh;
    }
    public event Action? Changed;
    public bool Busy {get;private set;}
    public AskConfirmation? Confirmation {get;private set;}
    public string? LastAskedNote {get;private set;}
    public Guid? LastRound {get;private set;}
    public string ButtonAria=>"Ask others for this message";
    public string ButtonTitle=>ButtonAria;
    public string ListHeading=>"Also ask, for this message only";
    public string AllLabel=>"@all";
    public string AllDetail=>"Everyone with room";
    public string DontAskAgainLabel=>"Don't ask again";
    public IReadOnlyList<Guid> Picked=>picked.AsReadOnly();
    // The active account also produces private Answer/Combine turns in E1; both must be safe.
    public IReadOnlyList<OpinionPickRow> Rows=>!ConversationHeadCapabilities.Supports(current.Service)?[]:
        ports.SelectMany(p=>p.Accounts).DistinctBy(a=>a.Id).Where(a=>a.Enabled&&a.Id!=current.ActiveAccountId&&a.CanBeConversationHead)
        .Select(a=>new OpinionPickRow(a,ModelName(a),hasRoom(a),picked.Contains(a.Id))).ToArray();
    private string ModelName(ThreadAccount a)
    {
        var options=models(a.Service);var model=a.Defaults.Model is {} id?options.FirstOrDefault(m=>m.Id==id):options.FirstOrDefault(m=>m.IsDefault)??options.FirstOrDefault();
        return model?.Name??a.Defaults.Model??"";
    }
    public bool Shown=>Rows.Count>0;
    public bool AllSelected=>Rows.Any(r=>r.CanAsk)&&Rows.Where(r=>r.CanAsk).All(r=>picked.Contains(r.Account.Id));
    public string ChipsText=>picked.Count==0?"":"Ask also: "+string.Join("·",PickedRows().Select(a=>a.Mono));
    private IReadOnlyList<ThreadAccount> PickedRows()=>picked.Select(id=>Rows.FirstOrDefault(r=>r.Account.Id==id)?.Account).Where(a=>a is not null).Select(a=>a!).ToArray();
    public void Toggle(Guid id)
    {
        if(Busy||Confirmation is not null)return;
        if(Rows.FirstOrDefault(r=>r.Account.Id==id) is not {CanAsk:true})return;
        if(!picked.Remove(id))picked.Add(id);Changed?.Invoke();
    }
    public void ToggleAll()
    {
        if(Busy||Confirmation is not null)return;
        var all=Rows.Where(r=>r.CanAsk).Select(r=>r.Account.Id).ToArray();var already=AllSelected;
        picked.Clear();if(!already)picked.AddRange(all);Changed?.Invoke();
    }
    private void Refresh()
    {
        var valid=Rows.Where(r=>r.CanAsk).Select(r=>r.Account.Id).ToHashSet();picked.RemoveAll(id=>!valid.Contains(id));
        if(Confirmation is {} pending&&pending.Accounts.Any(id=>!valid.Contains(id)))Confirmation=null;
        Changed?.Invoke();
    }
    public void SetDontAskAgain(bool value)
    {
        if(Busy||Confirmation is null)return;Confirmation=Confirmation with {DontAskAgain=value};Changed?.Invoke();
    }
    public async Task<AskSendResult> SendAsync(string message,CancellationToken ct=default)
    {
        if(Busy)return AskSendResult.Busy;
        if(Confirmation is not null)return AskSendResult.NeedsConfirmation;
        ct.ThrowIfCancellationRequested();Busy=true;
        try
        {
            Refresh();var rows=PickedRows();if(rows.Count==0){LastAskedNote=null;return AskSendResult.NoSelection;}
            if(string.IsNullOrWhiteSpace(message))throw new ArgumentException("A message is required.",nameof(message));
            var other=rows.Where(a=>a.Service!=current.Service&&!consent.Permits(ConsentPurpose.Opinions,a.Service)).ToArray();
            if(other.Length>0)
            {
                Confirmation=new(message,Array.AsReadOnly(rows.Select(a=>a.Id).ToArray()),Array.AsReadOnly(other));return AskSendResult.NeedsConfirmation;
            }
            return await Ask(message,rows,remember:false,ct);
        }
        finally {Busy=false;Changed?.Invoke();}
    }
    public async Task<AskSendResult> ConfirmAsync(bool accepted,CancellationToken ct=default)
    {
        if(Busy)return AskSendResult.Busy;
        ct.ThrowIfCancellationRequested();Busy=true;
        try
        {
            Refresh();if(Confirmation is not {} pending)return AskSendResult.NoSelection;
            if(!accepted){Confirmation=null;return AskSendResult.Canceled;}
            var rows=pending.Accounts.Select(id=>Rows.Single(r=>r.Account.Id==id).Account).ToArray();
            return await Ask(pending.Message,rows,pending.DontAskAgain,ct);
        }
        finally {Busy=false;Changed?.Invoke();}
    }
    private async Task<AskSendResult> Ask(string message,IReadOnlyList<ThreadAccount> rows,bool remember,CancellationToken ct)
    {
        if(remember)foreach(var service in rows.Where(a=>a.Service!=current.Service).Select(a=>a.Service).Distinct())consent.Remember(ConsentPurpose.Opinions,service);
        var round=await opinions.AskAsync(thread,message,rows.Select(a=>a.Id).ToArray(),ct);
        LastRound=round;LastAskedNote="Also asked: "+string.Join(" ",rows.Select(a=>a.Mono));picked.Clear();Confirmation=null;
        return AskSendResult.Sent;
    }
    public void Dispose(){foreach(var port in ports)port.Changed-=Refresh;}
}
