// PORT-ONLY, owner-ruled addition (OD-0091, OD-0096; first-release voice typing, VT-8, voice-SPEC V-11.3): the two voice orbs' programs in GR-1's warm-up registry,
// registered from this file (GpuWarmup.cs is not edited): they join every warm-up that begins after Register and FirstRunPreparation's plan, so the Getting ready
// screen compiles them.
using Marrow.App.Port.FirstRun;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port.Voice.Orb;

namespace Marrow.App.Port.Voice;

/// <summary>Registers the orb programs with <see cref="GpuWarmup"/>.</summary>
public static class VoiceWarmup
{
    /// <summary>
    /// The first-run step the orbs belong to: "Voice orbs", ordered after the fast mode's lightning (50) and before the voice hardware check (60). ENGINEERING DECISION
    /// (VT-8): W1 names no step for them; the registry takes a step by id, name and order, and the Getting ready screen shows the name.
    /// </summary>
    public static FirstRunStep Step { get; } = new("orbs", "Voice orbs", 55);

    /// <summary>The program names, one per preset (the shader is one SkSL program; each draw is that preset's own frame).</summary>
    public static IReadOnlyList<string> ProgramNames { get; } = ["voice-orb-siri", "voice-orb-membrane"];

    private static readonly Lock Gate = new();
    private static readonly List<IDisposable> registrations = [];

    /// <summary>Adds both programs to the registry (once; later calls do nothing until <see cref="Unregister"/>).</summary>
    public static void Register()
    {
        lock (Gate)
        {
            if (registrations.Count > 0) return;
            registrations.Add(GpuWarmup.Register(GpuProgram.Draw(ProgramNames[0], Step, context => OrbWarm.Draw(context.Gpu, context.Scaling, VoiceOrbKind.Siri))));
            registrations.Add(GpuWarmup.Register(GpuProgram.Draw(ProgramNames[1], Step, context => OrbWarm.Draw(context.Gpu, context.Scaling, VoiceOrbKind.Membrane))));
        }
    }

    /// <summary>Takes both programs out of the registry (tests).</summary>
    public static void Unregister()
    {
        lock (Gate)
        {
            foreach (var registration in registrations) registration.Dispose();
            registrations.Clear();
        }
    }
}
