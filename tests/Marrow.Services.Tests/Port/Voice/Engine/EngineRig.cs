using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Engine;

/// <summary>VT-6: a <see cref="WhisperNetEngine"/> over a <see cref="ScriptedWhisperBackend"/> on the manual clock, with a scripted runtime list and core count.</summary>
public sealed class EngineRig : IDisposable
{
    public static readonly SpeechModel Turbo = new("large-v3-turbo-q8_0", "/models/large-v3-turbo-q8_0.bin");

    public static readonly SpeechModel Small = new("small.en-q8_0", "/models/small.en-q8_0.bin");

    public EngineRig(bool nvidia = true, IReadOnlyList<RuntimeKind>? installed = null, int cores = 8)
    {
        Facts = new RuntimeFacts(installed ?? [RuntimeKind.Cpu, RuntimeKind.Vulkan], nvidia, "/data");
        Cores = cores;
        Engine = new WhisperNetEngine(Backend, () => Facts, () => Cores);
    }

    public TestRuntime Runtime { get; } = new();

    public ScriptedWhisperBackend Backend { get; } = new();

    public RuntimeFacts Facts { get; set; }

    public int Cores { get; set; }

    public WhisperNetEngine Engine { get; }

    public static ReadOnlyMemory<float> Audio(double seconds) => new float[(int)(seconds * 16000)];

    /// <summary>Awaits a task the engine returns under a hang guard: a scripted engine never waits on a wall clock, so ten seconds only ever means a bug.</summary>
    public static async Task<T> Guard<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(10));

    public static async Task Guard(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(10));

    public void Dispose()
    {
        Engine.Dispose();
        Runtime.Dispose();
    }
}
