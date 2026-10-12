using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// Dextra AS-13: feeds the usage calibrator one interval at a time. Each call spends <c>tokens</c> on the account (as one model), moves
/// the 5-hour window by <c>points</c> one minute after the last reading, and observes it; the clock is the test's and starts the
/// window's reset five hours on.
/// </summary>
public sealed class CalibrationFeeder(UsageCalibrator calibrator, ManualTimeProvider clock, string account, string model)
{
    private readonly DateTimeOffset resets = clock.GetUtcNow().AddHours(5);

    public double Percent { get; private set; } = 10;

    public DateTimeOffset At { get; private set; } = clock.GetUtcNow();

    /// <summary>The first reading, which opens the first interval.</summary>
    public void Start() => calibrator.Observe(account, Window(), false);

    public void Interval(double points, double tokens, bool outside = false, string? spentModel = null)
    {
        calibrator.Spend(account, spentModel ?? model, (long)tokens);
        Percent += points;
        At = At.AddMinutes(1);
        calibrator.Observe(account, Window(), outside);
    }

    private ReserveWindow Window() => new("w", ReserveWindowKind.FiveHour, Percent, At, resets);
}
