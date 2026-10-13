using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-22: what the delegate answered, for the Decisions tab's list; every Override is recorded and answered as the test says.</summary>
public sealed class FakeAnswered : IAnsweredSource
{
    public List<DelegateRecord> Records { get; } = [];

    public List<(Guid Answer, string Correction)> Overrides { get; } = [];

    /// <summary>What an Override answers; null: it is kept and sent.</summary>
    public Func<Guid, string, DelegateOverrideResult>? Answer { get; set; }

    public IReadOnlyList<DelegateRecord> Recent => [.. Records.OrderByDescending(r => r.At)];

    public event Action? Changed;

    public void Add(params DelegateRecord[] records)
    {
        Records.AddRange(records);
        Changed?.Invoke();
    }

    public DelegateOverrideResult Override(Guid answer, string correction)
    {
        Overrides.Add((answer, correction));
        var result = Answer?.Invoke(answer, correction) ?? new DelegateOverrideResult(true, true, null);
        if (result.Saved && Records.FindIndex(r => r.Id == answer) is >= 0 and var index)
        {
            Records[index] = Records[index] with { Correction = correction, CorrectedAt = HomeSamples.Now, CorrectionSent = result.Sent };
            Changed?.Invoke();
        }
        return result;
    }

    public static DelegateRecord Record(string answer = "Keep the existing chart style", string question = "Which chart style should the new report use?", string chat = "Pitch deck v3", int minutesAgo = 5) =>
        new(Guid.NewGuid(), HomeSamples.Now.AddMinutes(-minutesAgo), Guid.NewGuid(), chat, DelegateKind.Question, question, answer, "Dextra", "A decision says all reports keep one style.")
        {
            Confidence = 0.9,
        };
}
