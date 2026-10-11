using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Ignis;

// PORT-ONLY: ED-NEW-ignisadvisor-1. Ignis's only part in the advisor: honest accounting. Each advisor call is priced at the
// advising model's own rates, booked as Advice (never a saving, never keep-warm's spend) and shown as one chat line. Engine,
// points, ledger and runtime are the offline fakes; nothing reaches a model provider.
[Trait("Decision", "ED-NEW-ignisadvisor-1")]
public sealed class IgnisAdvisorControllerTests : IDisposable
{
    private readonly TestRuntime main = new();
    private readonly FakeIgnisPoints points = new();
    private readonly FakeSavingsLedger ledger = new();
    private readonly FakeIgnisEngine engine = new();
    private readonly FakeIgnisEnvironment environment = new();
    private readonly FakeIgnisPort port = new();
    private readonly IgnisSettings settings;
    private readonly IgnisController controller;

    // The platform documentation's example advisor call: 823 tokens in, 1,612 out, on the advising model.
    private const decimal ExampleCost = (823 * 5m + 1612 * 25m) / 1_000_000m;

    public IgnisAdvisorControllerTests()
    {
        settings = new IgnisSettings(main.Defaults);
        controller = main.OnMain(() => new IgnisController(settings, points, ledger, engine, environment));
    }

    public void Dispose() => main.Dispose();

    private Guid Thread => port.ThreadID;

    private static IgnisTurnUsage Call(string? model) => new(ProviderKind.Claude, model, null, 823, 0, 0, 0, 0, 1612, null, null, true);

    private void Observe(IgnisTurnUsage usage) => main.OnMain(() =>
    {
        controller.Attach(port);
        controller.Observe(Thread, usage);
        controller.TurnFinished(Thread);
    });

    [Fact]
    public void An_advisor_call_is_booked_as_advice_at_the_advisors_rates_with_one_chat_line_and_no_saving()
    {
        var usage = IgnisSamples.Usage(read: 40_000, model: IgnisSamples.Sonnet) with { AdvisorCalls = new([Call(IgnisSamples.Opus)]) };
        Observe(usage);

        var advice = Assert.Single(ledger.Entries, entry => entry.Kind == IgnisLedgerKind.Advice);
        Assert.Equal(ExampleCost, advice.Amount); // Opus Test's own input and output prices, not the chat model's
        Assert.Null(advice.Source);
        Assert.Equal(Thread, advice.ThreadID);
        // The only saving is the chat's own reuse: the advisor adds none.
        Assert.All(ledger.Entries.Where(entry => entry.Kind == IgnisLedgerKind.Saved), entry => Assert.Equal(IgnisSource.Reuse, entry.Source));
        Assert.DoesNotContain(ledger.Entries, entry => entry.Kind == IgnisLedgerKind.Spent);

        var line = Assert.Single(port.Lines, e => e.Kind == IgnisEventKind.AdvisorAsked);
        Assert.Equal(("Opus Test", ExampleCost, IgnisAmountKind.Cost, Thread), (line.Model, line.Amount, line.AmountKind, line.ThreadID));
        Assert.Contains(ledger.Recorded, e => e.Id == line.Id);
        Assert.Equal("Asked Opus Test for advice", IgnisWords.EventLine(line));

        var today = main.OnMain(() => ledger.ChatDay(Thread, DateOnly.FromDateTime(SwiftDate.Now.ToLocalTime().DateTime)));
        Assert.Equal((ExampleCost, 1), (today.Advice, today.AdviceCalls));
        Assert.Equal(today.Reuse + today.KeptWarm + today.TidyUp + today.SmartHelpers + today.LongerMemory, today.Saved); // Advice is not in Saved
        Assert.DoesNotContain(ExampleCost, new[] { today.Saved, today.Spent });
    }

    [Fact]
    public void Advice_is_booked_even_when_Ignis_is_off_for_the_chat_since_the_user_chose_the_model()
    {
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        Observe(IgnisSamples.Usage(read: 40_000, model: IgnisSamples.Sonnet) with { AdvisorCalls = new([Call(IgnisSamples.Opus), Call(IgnisSamples.Opus)]) });

        Assert.Equal(2, ledger.Entries.Count(entry => entry.Kind == IgnisLedgerKind.Advice));
        Assert.DoesNotContain(ledger.Entries, entry => entry.Kind != IgnisLedgerKind.Advice); // Off still earns and spends nothing
        Assert.Equal(2, port.Lines.Count(e => e.Kind == IgnisEventKind.AdvisorAsked));
    }

    [Fact]
    public void A_model_without_prices_books_the_call_with_no_amount_and_names_the_model_as_reported()
    {
        Observe(IgnisSamples.Usage(read: 1_000, model: IgnisSamples.Sonnet) with { AdvisorCalls = new([Call("claude-unknown-9")]) });

        Assert.Equal(0m, Assert.Single(ledger.Entries, entry => entry.Kind == IgnisLedgerKind.Advice).Amount);
        var line = Assert.Single(port.Lines, e => e.Kind == IgnisEventKind.AdvisorAsked);
        Assert.Null(line.Amount);
        Assert.Equal("Asked claude-unknown-9 for advice", IgnisWords.EventLine(line));
    }

    [Fact]
    public void A_turn_without_advisor_calls_books_no_advice()
    {
        Observe(IgnisSamples.Usage(read: 40_000, model: IgnisSamples.Sonnet));
        Assert.DoesNotContain(ledger.Entries, entry => entry.Kind == IgnisLedgerKind.Advice);
        Assert.DoesNotContain(port.Lines, e => e.Kind == IgnisEventKind.AdvisorAsked);
    }

    [Fact]
    public void Hosted_advice_uses_the_reported_dollars_else_its_tokens_at_the_advisors_prices()
    {
        var opus = new AdvisorChoice(ProviderKind.Claude, IgnisSamples.Opus);
        var opusPoints = points.Find(ProviderKind.Claude, IgnisSamples.Opus);
        Assert.Equal(0.25m, IgnisController.AdviceCost(opusPoints, new AdvisorAdvice("a", opus, 823, 1612, 0, 0.25)));
        Assert.Equal(ExampleCost, IgnisController.AdviceCost(opusPoints, new AdvisorAdvice("a", opus, 823, 1612, 0, null)));
        // cached tokens are inside the input: 823 in, 400 of them read from the cache at the cache-read price
        Assert.Equal((423 * 5m + 400 * 0.5m + 1612 * 25m) / 1_000_000m, IgnisController.AdviceCost(opusPoints, new AdvisorAdvice("a", opus, 823, 1612, 400, null)));
        // an unusable dollar figure falls back to the tokens; no prices and no dollars: unknown
        Assert.Equal(ExampleCost, IgnisController.AdviceCost(opusPoints, new AdvisorAdvice("a", opus, 823, 1612, 0, double.NaN)));
        Assert.Equal(ExampleCost, IgnisController.AdviceCost(opusPoints, new AdvisorAdvice("a", opus, 823, 1612, 0, -1)));
        Assert.Null(IgnisController.AdviceCost(null, new AdvisorAdvice("a", opus, 823, 1612, 0, null)));
    }

    [Fact]
    public void Hosted_advice_reaches_the_ledger_and_the_chat_line_and_none_without_a_chat()
    {
        main.OnMain(() => controller.Attach(port));
        var changed = new List<Guid>();
        main.OnMain(() => controller.Changed += changed.Add);

        main.OnMain(() => controller.AdviceAsked(null, new AdvisorAdvice("a", new AdvisorChoice(ProviderKind.Claude, IgnisSamples.Opus), 823, 1612, 0, null)));
        Assert.Empty(ledger.Entries);
        Assert.Empty(port.Lines);

        main.OnMain(() => controller.AdviceAsked(Thread, new AdvisorAdvice("a", new AdvisorChoice(ProviderKind.Claude, IgnisSamples.Opus), 823, 1612, 0, null)));
        Assert.Equal(ExampleCost, Assert.Single(ledger.Entries, entry => entry.Kind == IgnisLedgerKind.Advice).Amount);
        var line = Assert.Single(port.Lines);
        Assert.Equal((IgnisEventKind.AdvisorAsked, "Opus Test", ExampleCost), (line.Kind, line.Model, line.Amount));
        Assert.Contains(Thread, changed);
    }

    [Fact]
    public void The_report_names_todays_advice_only_when_there_was_some()
    {
        Observe(IgnisSamples.Usage(read: 40_000, model: IgnisSamples.Sonnet));
        var before = main.OnMain(() => IgnisReportText.Build(controller.Status(Thread), ledger, SwiftDate.Now));
        Assert.DoesNotContain("Advice:", before, StringComparison.Ordinal);

        Observe(IgnisSamples.Usage(read: 40_000, model: IgnisSamples.Sonnet) with { AdvisorCalls = new([Call(IgnisSamples.Opus)]) });
        var after = main.OnMain(() => IgnisReportText.Build(controller.Status(Thread), ledger, SwiftDate.Now));
        Assert.Contains(IgnisWords.AdviceReportLine(1, ExampleCost), after, StringComparison.Ordinal);
        Assert.Equal("Advice: 1 call cost " + IgnisWords.Money(ExampleCost), IgnisWords.AdviceReportLine(1, ExampleCost));
        Assert.Equal("Advice: 3 calls cost " + IgnisWords.Money(0.12m), IgnisWords.AdviceReportLine(3, 0.12m));
    }
}
