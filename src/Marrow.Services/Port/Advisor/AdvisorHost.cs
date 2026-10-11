// PORT-ONLY: Ignis Advisor, the hosted route (ED-NEW-ignisadvisor-2). Runs one piece of advice: the head's question and
// context go to the advisor model as a one-shot request through nth's own provider machinery (IAdvisorRunner), the reply
// comes back capped as the tool's text, and the usage is priced from the Ignis points and handed to AdvisorSeams.Asked.
// Nothing here throws to the caller: a timeout, a missing runner or a provider error is a short error text, so the head
// carries on.
using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Port.Advisor;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Services.Port.Advisor;

/// <summary>What one advisor request answered, and its usage when the provider reported one.</summary>
public sealed record AdvisorRun(string Text, IgnisTurnUsage? Usage);

/// <summary>Runs one tool-less, non-interactive request on the advisor model with the user's own login or key.</summary>
public interface IAdvisorRunner
{
    Task<AdvisorRun> Run(AdvisorChoice choice, string prompt, CancellationToken cancellationToken);
}

/// <summary>The tool's result: the advice, or a short error text (Failed).</summary>
public sealed record AdvisorReply(string Text, bool Failed);

public static class AdvisorHost
{
    public const string SystemLine = "You are advising another AI coding model that is stuck. Give a concise, concrete recommendation.";

    /// <summary>The longest question, context and advice kept (characters); the rest is cut with a note.</summary>
    public const int MaximumQuestion = 8_000;
    public const int MaximumContext = 40_000;
    public const int MaximumAdvice = 12_000;

    /// <summary>The runner the app binds (a headless provider session on the advisor's provider); null: no advice.</summary>
    public static Ambient<Func<IAdvisorRunner?>> Runner { get; } = new(new Func<IAdvisorRunner?>(() => null));

    /// <summary>How long one piece of advice may take before the head is told to carry on.</summary>
    public static Ambient<Func<TimeSpan>> Timeout { get; } = new(new Func<TimeSpan>(() => TimeSpan.FromMinutes(3)));

    /// <summary>Told after each piece of advice, after AdvisorSeams.Asked: the app notes it in the Hydra lead's transcript.</summary>
    public static Ambient<Action<Guid?, AdvisorAdvice>> Noted { get; } = new(new Action<Guid?, AdvisorAdvice>((_, _) => { }));

    /// <summary>The prompt the advisor reads: the system line, then the question and the context.</summary>
    public static string Prompt(string question, string? context)
    {
        var text = SystemLine + "\n\nQuestion:\n" + Cap(question.Trim(), MaximumQuestion);
        if (context?.Trim() is { Length: > 0 } given) text += "\n\nContext:\n" + Cap(given, MaximumContext);
        return text;
    }

    /// <summary>
    /// Asks <paramref name="choice"/> for advice for <paramref name="thread"/>. Returns the advice, or a short error text;
    /// never throws, and never waits longer than <see cref="Timeout"/>.
    /// </summary>
    public static async Task<AdvisorReply> Ask(Guid? thread, AdvisorChoice choice, string? question, string? context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question)) return Failure("a question is required.");
        if (Runner.Current() is not { } runner) return Failure("no advisor is available right now. Carry on with your best judgement.");
        var limit = Timeout.Current();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AdvisorRun run;
        try
        {
            run = await runner.Run(choice, Prompt(question, context), cancel.Token).WaitAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await cancel.CancelAsync().ConfigureAwait(false);
            return Failure($"the advisor did not answer within {limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds. Carry on with your best judgement.");
        }
        catch (OperationCanceledException) { return Failure("the request was stopped."); }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            return Failure("the advisor could not answer (" + Cap(error.Message, 300) + "). Carry on with your best judgement.");
        }
        var advice = run.Text.Trim();
        if (advice.Length == 0) return Failure("the advisor returned no advice. Carry on with your best judgement.");
        advice = Cap(advice, MaximumAdvice);
        Report(thread, choice, advice, run.Usage);
        return new AdvisorReply(advice, false);
    }

    /// <summary>What the usage cost on the advisor's model, from the Ignis points; null when the model or usage is unknown.</summary>
    public static double? Price(AdvisorChoice choice, IgnisTurnUsage? usage)
    {
        if (usage is null) return null;
        try
        {
            if (IgnisRuntimeSeams.Points.Current()?.Find(choice.Provider, choice.Model) is not { } points) return null;
            return (double)IgnisMoney.Cost(points, usage);
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return null; }
    }

    private static void Report(Guid? thread, AdvisorChoice choice, string advice, IgnisTurnUsage? usage)
    {
        var advised = new AdvisorAdvice(advice, choice, usage?.TotalInput ?? 0, usage?.Output ?? 0, usage?.CacheRead ?? 0, Price(choice, usage));
        try { AdvisorSeams.Asked?.Invoke(thread, advised); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { } // The ledger's trouble is not the head's.
        try { Noted.Current(thread, advised); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { }
    }

    private static AdvisorReply Failure(string reason) => new("Error: " + reason, true);

    private static string Cap(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        var end = char.IsHighSurrogate(text[maximum - 1]) ? maximum - 1 : maximum; // never half a surrogate pair
        return text[..end] + "\n[cut: " + (text.Length - end).ToString(CultureInfo.InvariantCulture) + " more characters]";
    }
}
