using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

public sealed class AutoContinueAccountTests
{
    [Fact]
    [Trait("Decision", "ED-406")]
    public void Unknown_reset_reads_the_account_that_hit_the_limit_and_never_the_default_reader()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        var account = new UsageAccountKey(t.Provider, "B");
        UsageAccountKey? readAccount = null;
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            app.Runtime(t.Id);
            app.AutoContinue.ReadAccountLimits = key => { readAccount = key; return Task.FromResult<PlanLimits?>(null); };
            app.AutoContinue.ReadLimitsForTesting = _ => throw new InvalidOperationException("Wrong account");
            app.AutoContinue.NoteLimit(t.Id, null, account);
            Assert.Equal(account, app.AutoContinue.LimitedAccount(t.Id));
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() => Assert.Equal(account, readAccount));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void A_changed_account_during_the_wait_prevents_auto_continue_on_the_successor()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        var account = new UsageAccountKey(t.Provider, "A");
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            app.Runtime(t.Id);
            app.AutoContinue.CurrentAccount = _ => account;
            app.AutoContinue.FormatForTesting = _ => "reset";
            app.AutoContinue.NoteLimit(t.Id, h.Main.Time.GetUtcNow().AddMinutes(-2), account);
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() => account = new UsageAccountKey(t.Provider, "B"));
        h.Main.Advance(TimeSpan.FromSeconds(5));
        h.Main.OnMain(() =>
        {
            Assert.False(app.AutoContinue.Outcomes.ContainsKey(t.Id));
            Assert.Empty(app.Runtime(t.Id).FollowUps);
        });
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void Session_events_keep_the_observed_account_and_old_sessions_cannot_replace_it()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        h.Main.OnMain(() =>
        {
            var runtime = app.Runtime(t.Id);
            var a = new UsageAccountKey(t.Provider, "A");
            var b = new UsageAccountKey(t.Provider, "B");
            var first = new ThreadRuntimeSessionProbe();
            var second = new ThreadRuntimeSessionProbe();
            runtime.Observe(first, a);
            first.Emit(new ProviderEvent.UsageLimit(null));
            Assert.Equal(a, app.AutoContinue.LimitedAccount(t.Id));
            runtime.Observe(second, b);
            second.Emit(new ProviderEvent.UsageLimit(null));
            first.Emit(new ProviderEvent.UsageLimit(h.Main.Time.GetUtcNow().AddHours(10)));
            Assert.Equal(b, app.AutoContinue.LimitedAccount(t.Id));
        });
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void An_explicit_reader_failure_is_an_unknown_reset_instead_of_an_unhandled_task_error()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            app.Runtime(t.Id);
            app.AutoContinue.ReadAccountLimits = _ => Task.FromException<PlanLimits?>(new Marrow.Services.Port.Usage.LimitsPollException(TimeSpan.FromMinutes(10)));
            app.AutoContinue.NoteLimit(t.Id, null, new UsageAccountKey(t.Provider, "A"));
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() => Assert.False(app.AutoContinue.ResumesAt.ContainsKey(t.Id)));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void A_provider_change_before_turn_finish_never_reads_the_successors_default_quota()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p) with { Provider = ProviderKind.Claude };
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            var runtime = app.Runtime(t.Id);
            var session = new ThreadRuntimeSessionProbe();
            runtime.Observe(session);
            session.Emit(new ProviderEvent.UsageLimit(null));
            app.UpdateThread(t.Id, thread => thread with { Provider = ProviderKind.Codex });
            app.AutoContinue.ReadLimitsForTesting = _ => throw new InvalidOperationException("Wrong provider quota");
            Assert.False(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void A_provider_change_during_the_wait_cannot_resume_from_a_stale_session_identity()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p) with { Provider = ProviderKind.Claude };
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            var runtime = app.Runtime(t.Id);
            runtime.Observe(new ThreadRuntimeSessionProbe());
            app.AutoContinue.FormatForTesting = _ => "reset";
            app.AutoContinue.NoteLimit(t.Id, h.Main.Time.GetUtcNow().AddMinutes(-2), UsageAccountKey.Default(t.Provider));
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() => app.UpdateThread(t.Id, thread => thread with { Provider = ProviderKind.Codex }));
        h.Main.Advance(TimeSpan.FromSeconds(5));
        h.Main.OnMain(() => Assert.False(app.AutoContinue.Outcomes.ContainsKey(t.Id)));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task A_composed_scheduler_also_owns_the_default_accounts_unknown_reset_read()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        var account = UsageAccountKey.Default(t.Provider);
        var calls = 0;
        using var scheduler = new Marrow.Services.Port.Usage.AccountLimitsScheduler(h.Main.Time, (_, _) =>
        {
            calls++;
            return Task.FromResult<PlanLimits?>(null);
        }, jitter: () => TimeSpan.Zero);
        await scheduler.SignedInAsync(account);
        h.Main.OnMain(() =>
        {
            app.Settings.AutoContinueAfterLimit = true;
            app.Runtime(t.Id);
            app.AutoContinue.UsageLimits = scheduler;
            app.AutoContinue.ReadLimitsForTesting = _ => throw new InvalidOperationException("Bypassed scheduler");
            app.AutoContinue.NoteLimit(t.Id, null, account);
            Assert.True(app.AutoContinue.TurnFinished(t.Id, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() => Assert.Equal(1, calls));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void An_unknown_time_from_a_different_account_does_not_keep_the_previous_accounts_reset()
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("P", h.NewFolder("p"));
        var t = AppModelSamples.Thread(p);
        h.WriteLibrary([p], [t]);
        var app = h.MakeModel();
        h.Main.OnMain(() =>
        {
            var thread = t.Id;
            var a = new UsageAccountKey(t.Provider, "A");
            var b = new UsageAccountKey(t.Provider, "B");
            app.Settings.AutoContinueAfterLimit = true;
            app.Runtime(thread);
            app.AutoContinue.ReadAccountLimits = key => { Assert.Equal(b, key); return Task.FromResult<PlanLimits?>(null); };
            app.AutoContinue.NoteLimit(thread, h.Main.Time.GetUtcNow().AddHours(1), a);
            app.AutoContinue.NoteLimit(thread, null, b);
            Assert.Equal(b, app.AutoContinue.LimitedAccount(thread));
            Assert.True(app.AutoContinue.TurnFinished(thread, TurnStatus.Failed, false));
        });
        h.Main.OnMain(() =>
        {
            Assert.False(app.AutoContinue.ResumesAt.ContainsKey(t.Id));
            app.AutoContinue.Cancel(t.Id);
            Assert.Null(app.AutoContinue.LimitedAccount(t.Id));
        });
    }
}
