// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the hardware source double (V-2.4, V-T45).
namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// <see cref="IHardwareSource"/> over a <see cref="ScriptedMachine"/>. It records every read (<see cref="Calls"/>), tells <see cref="Called"/> before answering, and throws the
/// exception in <see cref="Failures"/> for a member it names. It reads no registry key, memory status or adapter.
/// </summary>
public sealed class ScriptedHardwareSource(ScriptedMachine machine) : IHardwareSource
{
    public List<string> Calls { get; } = [];

    public List<string> DriverVersionsAskedFor { get; } = [];

    /// <summary>Member name to the exception that member throws.</summary>
    public Dictionary<string, Exception> Failures { get; } = [];

    /// <summary>Told the member name just before it answers.</summary>
    public Action<string>? Called { get; set; }

    public string ProcessorName() => Read(nameof(ProcessorName), machine.Processor);

    public int PhysicalCores() => Read(nameof(PhysicalCores), machine.Cores);

    public long TotalPhysicalBytes() => Read(nameof(TotalPhysicalBytes), machine.MemoryBytes);

    public IReadOnlyList<GpuAdapterInfo> Adapters() => Read(nameof(Adapters), machine.Adapters);

    public string DriverVersion(GpuAdapterInfo adapter)
    {
        DriverVersionsAskedFor.Add(adapter.Description);
        return Read(nameof(DriverVersion), machine.Driver);
    }

    private T Read<T>(string member, T value)
    {
        Calls.Add(member);
        Called?.Invoke(member);
        if (Failures.TryGetValue(member, out var error)) throw error;
        return value;
    }
}
