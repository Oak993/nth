using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

public sealed class HandshakeCostSamplingRig : IDisposable
{
    public HandshakeCostSamplingRig()
    {
        Accounts = new(new InMemorySettingsStore());
        Account = Accounts.Add(ProviderKind.Codex).Id;
        Reset = Clock.GetUtcNow().AddHours(4);
        Scheduler = new(Clock, (_, _) => Task.FromResult<PlanLimits?>(Report), jitter: () => TimeSpan.Zero);
        Limits = new(Accounts, Scheduler, new(), Clock);
        Report = MakeReport(20);
    }
    public AccountStore Accounts { get; }
    public Guid Account { get; }
    public ManualTimeProvider Clock { get; } = new();
    public DateTimeOffset Reset { get; }
    public AccountLimitsScheduler Scheduler { get; }
    public HandshakeLimits Limits { get; }
    public PlanLimits? Report { get; set; }
    public PlanLimits MakeReport(double percent) => new() { PlanName = "Pro", Windows = new([new() { Id = "five", Title = "5-hour limit", Percent = percent, ResetsAt = Reset, DurationMinutes = 300 }]) };
    public IDisposable Begin()
    {
        var method = typeof(HandshakeLimits).GetMethod("BeginHandshakeCost");
        Assert.NotNull(method);
        return (IDisposable)method.Invoke(Limits, [Account, null])!;
    }
    public static void Complete(IDisposable observation)
    {
        var method = observation.GetType().GetMethod("Complete");
        Assert.NotNull(method);
        method.Invoke(observation, null);
    }
    public void Observe(AccountPlanLimitsSnapshot snapshot)
    {
        var method = typeof(HandshakeLimits).GetMethod("ObserveHandshakeCosts");
        Assert.NotNull(method);
        method.Invoke(Limits, [snapshot]);
    }
    public double? Cost => Limits.HandshakeCostPercent(Account.ToString("D"));
    public void Dispose() => Scheduler.Dispose();
}
