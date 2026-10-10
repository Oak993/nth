// PORT-ONLY: MA-3b-10, page cards :2925-2942, :3999-4100, :4224-4226, :4698; OD-0116.
using System.Globalization;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.Core.Port.MultiAccount.Cards;
public sealed class LimitCardModel
{
    private readonly HandshakeEvent state;
    private readonly DecisionRequest? request;
    private readonly ICardPort port;
    private readonly ThreadAccount outgoing;
    private readonly ThreadAccount? incoming;
    private readonly Dictionary<DecisionKind,string> selections=[];
    private readonly string reset;
    private readonly DecisionKind actionKind;
    private RememberScope scope;
    public LimitCardModel(HandshakeEvent state,IReadOnlyList<ThreadAccount> accounts,ICardPort port,TimeZoneInfo? zone=null)
    {
        this.state=state;request=state.Request;this.port=port;
        outgoing=accounts.FirstOrDefault(a=>a.Id.ToString("D")== (request?.OutgoingAccountId??state.OutgoingAccountId))??throw new ArgumentException("Outgoing account is missing.",nameof(accounts));
        incoming=accounts.FirstOrDefault(a=>a.Id.ToString("D")== (request?.IncomingAccountId??state.IncomingAccountId));
        reset=(request?.ResetsAt??state.ResumesAt) is {} at?TimeZoneInfo.ConvertTime(at,zone??TimeZoneInfo.Local).ToString("h:mm tt",CultureInfo.InvariantCulture):"";
        Kind=request is null?state.To==HandshakePhase.Waiting?CardKind.Waiting:CardKind.Continued
            :request.FirstTime&&request.Kinds.Contains(DecisionKind.OnLimit)?CardKind.FirstTime
            :state.Reason==HandshakeReason.EarlierAccountBack||request.Kinds.Contains(DecisionKind.EarlierAccount)?CardKind.EarlierAccount
            :request.Kinds.Contains(DecisionKind.OtherService)?CardKind.OtherService
            :request.Kinds.Contains(DecisionKind.MidReply)?CardKind.MidReply
            :request.Kinds.Contains(DecisionKind.OnLimit)?CardKind.Limit:CardKind.BringOnly;
        actionKind=Kind switch {CardKind.OtherService=>DecisionKind.OtherService,CardKind.MidReply=>DecisionKind.MidReply,CardKind.EarlierAccount=>DecisionKind.EarlierAccount,CardKind.BringOnly=>DecisionKind.Bring,_=>DecisionKind.OnLimit};
        if(ShowBring&&Kind!=CardKind.FirstTime) selections[DecisionKind.Bring]=DecisionValues.MarrowSummary;
    }
    public CardKind Kind {get;}
    public bool Answered {get;private set;}
    public bool Answering {get;private set;}
    public event Action? Changed;
    public string? AfterNote {get;private set;}
    public bool ShowBring=>request?.Kinds.Contains(DecisionKind.Bring)??false;
    public string BringLabel=>"Bring:";
    public string RememberLabel=>"Remember:";
    public IReadOnlyList<string> RememberOptions=>["Just this time","This thread","All threads"];
    public RememberScope Scope {get=>scope;set {if(!Enum.IsDefined(value))throw new ArgumentOutOfRangeException(nameof(value));if(Answered||Answering)return;scope=value;Changed?.Invoke();}}
    public Bring? SelectedBring=>selections.TryGetValue(DecisionKind.Bring,out var value)?DecisionValues.ToBring(value):null;
    private string Next=>incoming?.Name??"";
    private string Service=>outgoing.Service==ProviderKind.Claude?"Claude":outgoing.Service.DisplayName;
    private string ResetSuffix=>reset.Length>0?" · resets "+reset:"";
    public string Title=>Kind switch
    {
        CardKind.FirstTime=>outgoing.Name+" ran out. How should Nth handle this?",
        CardKind.OtherService=>"All your "+Service+" accounts are out."+(reset.Length>0?" The first is back at "+reset+".":""),
        CardKind.MidReply=>outgoing.Name+" reached its 5-hour limit while writing this reply"+ResetSuffix,
        CardKind.EarlierAccount=>Next+" has reset · you are on "+outgoing.Name,
        CardKind.BringOnly=>"Moving this thread to "+Next,
        CardKind.Waiting=>WaitNote(),
        CardKind.Continued=>"Continued on "+Next,
        _=>outgoing.Name+" reached its 5-hour limit"+ResetSuffix,
    };
    public string? Explanation=>Kind switch
    {
        CardKind.OtherService=>Next+" is another service: it will see this conversation.",
        CardKind.MidReply=>"Either way, the next account reads the whole conversation once before going on, which uses a little of its limit. Finish now picks up from the last finished step (a half-done step may run again). Stop waits for your next message.",
        _=>null,
    };
    public IReadOnlyList<CardButton> Buttons=>Kind switch
    {
        CardKind.FirstTime=>[new("switch","Keep me going",false),new("ask","Ask me each time",false),new("wait","Just wait",false)],
        CardKind.OtherService=>[new("wait","Wait for "+Service,true),new("switch","Use "+Next+" for now",false)],
        CardKind.MidReply=>[new("finish","Finish now on "+Next,true),new("stop","Stop, use it from my next message",false)],
        CardKind.EarlierAccount=>[new("goBack","Go back to "+Next,true),new("stay","Stay on "+outgoing.Name,false)],
        CardKind.BringOnly=>[new("continue","Continue on "+Next,true)],
        CardKind.Waiting or CardKind.Continued=>[],
        _=>incoming is null?[new("wait","Wait",false)]:[new("switch","Switch to "+Next,true),new("wait","Wait",false)],
    };
    public IReadOnlyList<CardOptionGroup> Groups=>request is null?[]:request.Kinds.Where(k=>k!=actionKind||k==DecisionKind.Bring).Select(k=>new CardOptionGroup(k,GroupLabel(k),Options(k))).ToArray();
    private static string GroupLabel(DecisionKind kind)=>kind switch {DecisionKind.Bring=>"Bring:",DecisionKind.MidReply=>"If a limit hits mid-reply",DecisionKind.OtherService=>"When every account of a service is out",DecisionKind.EarlierAccount=>"When an earlier account is back",_=>"When an account runs out"};
    private IReadOnlyList<CardButton> Options(DecisionKind kind)=>DecisionValues.Allowed(kind).Where(v=>v!=DecisionValues.Ask).Select(v=>new CardButton(v,OptionLabel(kind,v),false,selections.GetValueOrDefault(kind)==v)).ToArray();
    private static string OptionLabel(DecisionKind kind,string value)=>(kind,value) switch
    {
        (DecisionKind.Bring,DecisionValues.Full)=>"Full conversation",(DecisionKind.Bring,DecisionValues.MarrowSummary)=>"Nth’s summary",(DecisionKind.Bring,DecisionValues.AiSummary)=>"AI summary",
        (DecisionKind.MidReply,DecisionValues.Finish)=>"Finish the reply on the next account",(DecisionKind.MidReply,DecisionValues.Stop)=>"Stop, use the next one from my next message",
        (DecisionKind.OtherService,DecisionValues.Switch)=>"Switch to another service",(DecisionKind.EarlierAccount,DecisionValues.GoBack)=>"Go back to it",(DecisionKind.EarlierAccount,DecisionValues.Stay)=>"Stay until this one runs out",
        (_,DecisionValues.Wait)=>"Wait",(_,DecisionValues.Switch)=>"Switch automatically",_=>throw new ArgumentException("Invalid choice."),
    };
    public void Select(DecisionKind kind,string value)
    {
        if(Answered||Answering)return;
        if(request is null||!request.Kinds.Contains(kind)||!DecisionValues.Allowed(kind).Contains(value)||value==DecisionValues.Ask)throw new ArgumentException("The card did not offer this choice.");
        selections[kind]=value;Changed?.Invoke();
    }
    public async Task AnswerAsync(string value,CancellationToken ct=default)
    {
        if(Answered||Answering)return;
        if(request is null||!Buttons.Any(b=>b.Value==value))throw new ArgumentException("The card did not offer this button.",nameof(value));
        if(port.State(request.ThreadId)?.Request != request)throw new InvalidOperationException("This card is no longer waiting for this answer.");
        var chosen=new Dictionary<DecisionKind,string>(selections);
        if(actionKind!=DecisionKind.Bring)chosen[actionKind]=value;
        if(value is "switch" or "finish" or "goBack" or "continue")
            foreach(var kind in request.Kinds.Where(k=>k is DecisionKind.OnLimit or DecisionKind.OtherService or DecisionKind.EarlierAccount))
                if(!chosen.ContainsKey(kind))throw new InvalidOperationException("Choose whether to move to this account first.");
        if(actionKind==DecisionKind.Bring&&!chosen.ContainsKey(DecisionKind.Bring))throw new InvalidOperationException("Choose what to bring first.");
        Answering=true;Changed?.Invoke();
        try
        {
            await port.AnswerAsync(request.ThreadId,chosen.OrderBy(c=>c.Key).Select(c=>new Choice(c.Key,c.Value)).ToArray(),scope,ct);
            Answered=true;
            AfterNote=value=="wait"?WaitNote():value is "switch" or "finish" or "goBack" or "continue"?ContinueNote(chosen):null;
        }
        finally {Answering=false;Changed?.Invoke();}
    }
    private string Source=>scope==RememberScope.ThisThread?" (this thread’s choice)":scope==RememberScope.AllThreads?" (your Settings)":"";
    private string WaitNote()=>"Waiting for "+outgoing.Name+ResetSuffix+" · your message sends then"+Source+" · Switch now";
    private string ContinueNote(IReadOnlyDictionary<DecisionKind,string> choices)=>"Continued on "+Next+" · "+OptionLabel(DecisionKind.Bring,choices.GetValueOrDefault(DecisionKind.Bring)??DecisionValues.MarrowSummary)+Source+" · What was handed over";
}
