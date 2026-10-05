using Marrow.Services.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

/// <summary>A detector over a temp home with a watch that listens to nothing (SK-18 fixtures).</summary>
public sealed class DetectorRig : IDisposable
{
    public DetectorRig(string? state = null)
    {
        Home = new ForeignHome();
        Time = new ManualTimeProvider();
        Library = new ImportedSkillsLibrary(Home.SkillsRoot, Time);
        Watch = new RecordingWatch();
        State = state ?? Path.Combine(Home.Root, "marrow-data", "skills", "imported", "dismissed.json");
        Detector = Make();
    }

    public ForeignHome Home { get; }
    public ManualTimeProvider Time { get; }
    public ImportedSkillsLibrary Library { get; }
    public RecordingWatch Watch { get; }
    public string State { get; }
    public ImportDetector Detector { get; private set; }
    public int Scans { get; private set; }

    public ImportDetector Make() => Detector = new ImportDetector(() => { Scans++; return new ForeignSkillScanner(Home.Home).Scan(); }, Library, Home.Home, Watch, State, Time);

    public void Fill()
    {
        Home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
        Home.Skill(".claude/skills", "beta", "beta", "Beta skill.");
        Home.Skill(".codex/skills", "gamma", "gamma", "Gamma skill.");
    }

    public void Dispose()
    {
        Detector.Dispose();
        Home.Dispose();
    }
}
