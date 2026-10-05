// PORT-ONLY, owner-ruled addition (OD-0097, OD-0096; first-release voice typing, VT-9, voice-SPEC V-13.5): the voice glow's program in GR-1's warm-up registry, registered from this
// file (GpuWarmup.cs is not edited, as VoiceWarmup.cs does for the orbs): it joins every warm-up that begins after Register and FirstRunPreparation's plan, so the Getting ready
// screen compiles it.
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.UI.Port.Voice.Glow;

namespace Marrow.App.Port.Voice;

/// <summary>Registers the glow's program with <see cref="GpuWarmup"/>.</summary>
public static class VoiceGlowWarmup
{
    /// <summary>
    /// The first-run step the glow belongs to: "Voice glow", ordered right after "Voice orbs" (55) and before the voice hardware check (60). ENGINEERING DECISION (VT-9): the orbs'
    /// own step is VoiceWarmup's and its test names exactly the two orb programs under it, so the glow has a step of its own.
    /// </summary>
    public static FirstRunStep Step { get; } = new("glow", "Voice glow", 56);

    /// <summary>The program name (the glow's SkSL programs are drawn together by one frame of each theme).</summary>
    public static IReadOnlyList<string> ProgramNames { get; } = ["voice-glow"];

    private static readonly Lock Gate = new();
    private static readonly List<IDisposable> registrations = [];

    /// <summary>Adds the program to the registry (once; later calls do nothing until <see cref="Unregister"/>).</summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (registrations.Count > 0) return;
            registrations.Add(GpuWarmup.Register(GpuProgram.Draw(ProgramNames[0], Step, context => VoiceGlowWarm.Draw(context.Gpu, context.Scaling))));
        }
    }

    /// <summary>Takes the program out of the registry (tests).</summary>
    public static void Unregister()
    {
        lock (Gate)
        {
            foreach (var registration in registrations) registration.Dispose();
            registrations.Clear();
        }
    }
}
