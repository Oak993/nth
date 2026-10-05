using Marrow.App.Port.Skills.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Tests.Port.Skills.Import;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A temp home, the library, the controller and the model of the Import skills sheet, optionally with a detector over a watch that listens to nothing (SK-18 fixtures).</summary>
internal sealed class ImportRig : IDisposable
{
    public ImportRig(Func<Task<string?>>? pick = null, bool detect = false)
    {
        Home = new ForeignHome();
        Time = new ManualTimeProvider();
        Library = new ImportedSkillsLibrary(Home.SkillsRoot, Time);
        Watch = new RecordingWatch();
        if (detect) Detector = new ImportDetector(() => new ForeignSkillScanner(Home.Home).Scan(), Library, Home.Home, Watch, null, Time);
        Controller = new ImportController(folders => new ForeignSkillScanner(Home.Home, folders).Scan(), Library, () => Model!.PickFolder?.Invoke() ?? Task.FromResult<string?>(null));
        Model = new ImportModel(Controller, Detector) { PickFolder = pick };
    }

    public ForeignHome Home { get; }
    public ManualTimeProvider Time { get; }
    public RecordingWatch Watch { get; }
    public ImportDetector? Detector { get; }
    public ImportedSkillsLibrary Library { get; }
    public ImportController Controller { get; }
    public ImportModel? Model { get; }

    public void Fill()
    {
        Home.Skill(".claude/skills", "alpha", "alpha", "Alpha skill.");
        Home.Skill(".claude/skills", "beta", "beta", "Beta skill.");
        Home.Skill(".codex/skills", "gamma", "gamma", "Gamma skill.");
        Home.Raw(".cursor/rules/delta.mdc", "---\ndescription: Delta rule\n---\nbody\n");
    }

    public void Dispose()
    {
        Detector?.Dispose();
        Home.Dispose();
    }
}
