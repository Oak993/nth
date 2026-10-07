// PORT-ONLY: VT-15 / WV-1, OD-0098 D3 with consent. One preparation and question shared by Welcome and every composer of these settings.
using System.Runtime.CompilerServices;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.App.Port.Voice;

public sealed class VoiceFirstRun(VoicePreparation preparation, VoiceSpeedQuestion question, IVoiceModelSource models)
{
    private static readonly ConditionalWeakTable<AppSettings, VoiceFirstRun> All = new();
    private VoicePrepItem? progress;

    public VoiceSpeedQuestion Question { get; } = question;
    public VoicePrepItem? Progress => Volatile.Read(ref progress);
    public event Action? Changed;
    internal bool CanListen => Question.State is not (VoiceQuestionState.Yes or VoiceQuestionState.Slow)
        && preparation.IsPrepared(out var record) && VoicePreparation.UsableResult(record) && !preparation.NeedsSlowAnswer(record!);

    internal static VoiceFirstRun? Find(AppSettings settings) => All.TryGetValue(settings, out var flow) ? flow : null;
    internal static void Bind(AppSettings settings, VoiceFirstRun flow) => All.Add(settings, flow);

    // A click that does preparation displays its progress and never opens the microphone. The next click starts listening.
    // Welcome's running download is joined through VoicePreparation's single-flight operation, with its latest real progress replayed.
    public async Task<bool> OrbAsync(CancellationToken cancellationToken)
    {
        if (Question.State == VoiceQuestionState.Slow) return false;
        if (CanListen) return true;
        await Question.AtOrbAsync(new ImmediateProgress(this), cancellationToken);
        return false;
    }

    public SpeechModel? Model(string? id) => ModelCatalog.Find(id) is { } file ? new SpeechModel(file.Id, models.PathFor(file)) : null;

    private sealed class ImmediateProgress(VoiceFirstRun owner) : IProgress<VoicePrepItem>
    {
        public void Report(VoicePrepItem item)
        {
            Volatile.Write(ref owner.progress, item);
            owner.Changed?.Invoke();
        }
    }
}
