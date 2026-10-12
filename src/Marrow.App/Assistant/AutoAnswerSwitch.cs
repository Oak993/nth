// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.3 point 2, 16 "Ordering rule"; unit AS-7; ED-NEW-dextra-as7-8). No Swift twin.
// Whether nth may answer for the owner: the engine's Allow answers a provider approval and runs an nth-dextra tool alone
// only while this is on. It is off until the injection self-check (InjectionSelfCheck.Run, the same engine, classifier
// and privacy check nth runs) passes in this process; a check that fails or throws leaves it off for the session. Off is
// the default everywhere: only Dextra's start (AppModel.StartDextra) asks for AfterCorpus.
using System.Globalization;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public sealed class AutoAnswerSwitch
{
    public const string Checking = "Off while nth runs its injection checks.";
    public const string Never = "Off: nth answers nothing for you.";
    public const string Passed = "On: nth's injection checks passed, so it answers what your rules allow.";

    private volatile InjectionReport? report;
    private volatile string status;

    private AutoAnswerSwitch(string status, Task? ready = null)
    {
        this.status = status;
        Ready = ready ?? Task.CompletedTask;
    }

    /// <summary>Never on (the default of every controller that is not Dextra's own).</summary>
    public static AutoAnswerSwitch Off { get; } = new(Never);

    /// <summary>
    /// On once <paramref name="check"/> (the injection self-check unless a test swaps it) passes; it runs at once on the
    /// thread pool, and until it has passed the switch is off.
    /// </summary>
    public static AutoAnswerSwitch AfterCorpus(Func<InjectionReport>? check = null)
    {
        var run = check ?? (() => InjectionSelfCheck.Run());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var made = new AutoAnswerSwitch(Checking, gate.Task);
        _ = Task.Run(() =>
        {
            try { made.Settle(run); }
            finally { gate.SetResult(); }
        });
        return made;
    }

    /// <summary>Done when the check has settled (at once for <see cref="Off"/>). It never faults.</summary>
    public Task Ready { get; }

    /// <summary>Whether the engine's Allow may answer alone now.</summary>
    public bool IsOn => report is { Passed: true };

    /// <summary>The check's report, once it ran.</summary>
    public InjectionReport? Report => report;

    /// <summary>One line for Settings and the log: on, off, or still checking, and why.</summary>
    public string Status => status;

    private void Settle(Func<InjectionReport> run)
    {
        InjectionReport result;
        try { result = run(); }
        catch (OperationCanceledException) { result = Failed("the check was cancelled"); }
        catch (Exception error) { result = Failed("the check could not run: " + error.GetType().Name); }
        status = result.Passed
            ? Passed
            : $"Off: nth's injection checks failed ({result.Failures.Count.ToString(CultureInfo.InvariantCulture)} problems), so it answers nothing for you.";
        report = result;
    }

    private static InjectionReport Failed(string why) => new(0, 0, new ValueList<string>([why]));
}
