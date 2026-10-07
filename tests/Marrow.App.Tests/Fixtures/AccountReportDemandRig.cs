using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

public sealed class AccountReportDemandRig : IDisposable
{
    private readonly IDisposable reader;
    private readonly IDisposable clockScope;
    public AccountReportDemandRig()
    {
        H = new();
        Clock = new(H.Main.Time);
        clockScope = SwiftClock.Provider.Override(Clock);
        reader = MultiAccountRouting.LimitsReader.Override((app, key, ct) =>
        {
            Reads.Add(key);
            return Task.FromResult<PlanLimits?>(new()
            {
                Windows = new([new() { Id = "five", Title = "5-hour limit", DurationMinutes = 300, Percent = Percent }]),
            });
        });
        App = H.MakeModel();
        H.Main.OnMain(() =>
        {
            Source = App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            App.Settings.Accounts.MarkSignedIn(Source);
            Next = App.Settings.Accounts.Add(ProviderKind.Claude).Id;
            App.Settings.Accounts.MarkSignedIn(Next);
        });
        H.Bootstrap(App);
        Drain();
    }
    public AppModelHarness H { get; }
    public AppModel App { get; }
    public RoutingCallbackClock Clock { get; }
    public Guid Source { get; private set; }
    public Guid Next { get; private set; }
    public double Percent { get; set; } = 20;
    public List<UsageAccountKey> Reads { get; } = [];
    public IDisposable Observe()
    {
        var method = typeof(MultiAccountRouting).GetMethod("ObserveReports", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        return H.Main.OnMain(() => Assert.IsAssignableFrom<IDisposable>(method.Invoke(App.AccountRouting, null)));
    }
    public void Drain() => H.Main.Loop.Run(() => App.AccountRouting.DrainAsync(), AppModelHarness.Bound);
    public void Advance() { H.Main.OnMain(() => H.Main.Time.Advance(TimeSpan.FromMinutes(4))); Drain(); }
    public void Dispose() { clockScope.Dispose(); reader.Dispose(); H.Dispose(); }
}
