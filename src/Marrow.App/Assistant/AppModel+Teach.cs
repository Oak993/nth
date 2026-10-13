// PORT-ONLY: Dextra (DESIGN.md rev 5, 10, 15.1, 16.2; unit AS-19; ED-NEW-dextra-as19-11). No Swift twin.
// Teach mode as the app holds it: one TeachController over Dextra's controller and store, started by AS-8 with the rest of Dextra. It lives in
// AppModel, not in a window, so a recording and its strip survive the window closing. AS-8's home menu calls Teach.Start() for "Teach", and
// the voice command "Watch me do this" (AS-18) does the same; the strip, the draft view and the questions on the decision queue do the rest.
using Marrow.App.Assistant;
using Marrow.App.Assistant.Teach;
using Marrow.App.Safety;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Skills.Custom;

namespace Marrow.App;

public sealed partial class AppModel
{
    /// <summary>Teach mode while Dextra runs; null before <see cref="StartTeach"/>.</summary>
    public TeachController? Teach { get; private set; }

    /// <summary>
    /// The stop scope for a teach recording: pass it to StartDextra's <c>stopScopes</c> so Stop all ends a recording. StartTeach binds it.
    /// </summary>
    public TeachStopScope TeachScope { get; } = new();

    /// <summary>
    /// Starts teach mode over Dextra's controller and store. The model that writes a draft up is a reader mission on <paramref name="provider"/>
    /// and <paramref name="model"/> in the owner's first project (none: drafts come straight from the recording). Pass
    /// <paramref name="indicator"/> to replace the recording strip (the default is the strip window; a recording starts only if it goes up), and
    /// <paramref name="platform"/> to replace the Windows hooks, UI Automation and screen frames (a test does), <paramref name="executor"/> for
    /// Tactus's replay of a trial step (null until it lands) and <paramref name="browser"/> for Visus's steps (null until it lands). Settles
    /// what the last run left (a crash mid-recording or mid-save, recordings past their two weeks). Returns the controller; AppModel disposes it.
    /// </summary>
    public TeachController StartTeach(AssistantController controller, AssistantStore store, string assistantId, GlobalStop? stopKey,
        ProviderKind provider = ProviderKind.Claude, string? model = null, ICustomAddBackend? skills = null, Func<IReadOnlyList<OwnItem>>? ownSkills = null,
        Action? skillsChanged = null, Func<TeachPlatform>? platform = null, ITeachStepExecutor? executor = null, IBrowserSteps? browser = null,
        ITeachIndicator? indicator = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(assistantId);
        DisposeTeach();
        var draftModel = new MissionDraftModel(controller, new MissionDraftModelOptions
        {
            AssistantId = assistantId,
            Provider = provider,
            Model = model,
            OnMain = work => MainActor.Post(work),
            Project = () => Projects.Select(p => new MissionProject(p.Id, p.Name, p.Path)).FirstOrDefault(p => Directory.Exists(p.Path)),
        });
        var made = new TeachController(new TeachControllerOptions
        {
            Store = store,
            AssistantId = assistantId,
            Platform = platform ?? (() => WindowsTeachPlatform.Create(browser)),
            Queue = controller.Queue,
            Model = draftModel,
            StopKey = stopKey,
            Skills = skills ?? new TeachSkillsBackend(),
            OwnSkills = ownSkills ?? TeachSkillsBackend.Skills,
            SkillsChanged = skillsChanged,
            Executor = executor,
            Indicator = indicator ?? new Marrow.App.Assistant.Views.TeachStripHost(),
            Audit = controller.Audit,
            Faulted = message => controller.Runner.ReportFault(message),
        });
        TeachScope.Bind(made);
        made.Settle();
        Teach = made;
        return made;
    }

    private void DisposeTeach()
    {
        TeachScope.Bind(null);
        Teach?.Dispose();
        Teach = null;
    }
}
