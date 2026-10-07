// PORT-ONLY: MA-3b-8, Usage B multi-account.html :4297-4350; OD-0115.
using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Core.Port.MultiAccount.Popover;
namespace Marrow.Core.Port.MultiAccount.Usage;
public sealed class UsagePanelModel : IDisposable
{
    private readonly IThreadAccountPort current;
    private readonly IReadOnlyList<IThreadAccountPort> ports;
    private readonly Func<Guid,double?> credits;
    private readonly TimeZoneInfo zone;
    public UsagePanelModel(IThreadAccountPort current, IReadOnlyList<IThreadAccountPort> ports, Func<Guid,double?>? credits=null, TimeZoneInfo? zone=null)
    {
        this.current=current; this.ports=ports.Append(current).Distinct().ToArray(); this.credits=credits??(_=>null); this.zone=zone??TimeZoneInfo.Local;
        foreach(var port in this.ports) port.Changed+=Refresh;
    }
    public event Action? Changed;
    private void Refresh()=>Changed?.Invoke();
    public void Dispose() { foreach(var port in ports) port.Changed-=Refresh; }
    public string Title=>"Usage";
    public string Subtitle=>"this thread’s account";
    public string FiveHourLabel=>"5-hour window";
    public string WeekLabel=>"This week";
    public string BackText=>"‹ Usage";
    public int MoreCount=>Math.Max(0,ports.SelectMany(p=>p.Accounts).Select(a=>a.Id).Distinct().Count()-1);
    public string AllAccountsText=>"All accounts · "+MoreCount.ToString(CultureInfo.InvariantCulture)+" more";
    public UsageAccountRow? Current=>current.Accounts.FirstOrDefault(a=>a.Id==current.ActiveAccountId) is {} a?Row(a):null;
    public string? NextText=>current.Accounts.FirstOrDefault(a=>a.Id!=current.ActiveAccountId&&a.Enabled&&!a.IsOut) is {} next?"Next if it runs out: "+next.Name:null;
    public IReadOnlyList<UsageServiceGroup> Groups=>ports.GroupBy(p=>p.Service).Select(g=>new UsageServiceGroup(g.Key,ServiceNames.Title(g.Key),g.SelectMany(p=>p.Accounts).DistinctBy(a=>a.Id).Select(Row).ToArray())).ToArray();
    private string Reset(DateTimeOffset? at)=>at is {} t?"resets "+TimeZoneInfo.ConvertTime(t,zone).ToString("h:mm tt",CultureInfo.InvariantCulture):"";
    private UsageAccountRow Row(ThreadAccount a)
    {
        var five=Reset(a.Usage?.FiveHourReset); var week=Reset(a.Usage?.WeekReset);
        var summary=a.NoLimit?"No limit · this PC":a.IsOut?"out · "+(a.NoFiveHour?week:five):a.NoFiveHour?week:five;
        var dim=!a.Enabled||a.IsOut;
        if(a.Service==ProviderKind.OpenRouter)
        {
            var left=credits(a.Id);
            summary=left is {} n&&double.IsFinite(n)?n>0?"$"+n.ToString("F2",CultureInfo.InvariantCulture)+" left":"no credits":"";
            dim=!a.Enabled||left<=0;
        }
        return new(a,a.Id==current.ActiveAccountId,dim,summary,
            PopoverUsageBar.Of(a.NoLimit||a.NoFiveHour||a.Service.IsAPIKeyBased?null:a.Usage?.FiveHour,a.IsOut),
            PopoverUsageBar.Of(a.NoLimit||a.Service.IsAPIKeyBased?null:a.Usage?.Week),five,week,a.NoFiveHour?"No 5-hour limit":null);
    }
}
