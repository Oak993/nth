// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the seam under the hardware probe: DXGI, Win32 and the registry (V-10.1, V-2.4).
namespace Marrow.Services.Port.Voice.Probe;

/// <summary>The raw reads of V-10.1. Production: <see cref="WindowsHardwareSource"/>. Tests: <see cref="ScriptedHardwareSource"/>. Each member is one step the probe reports.</summary>
public interface IHardwareSource
{
    /// <summary><c>ProcessorNameString</c> of HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0.</summary>
    string ProcessorName();

    /// <summary>Physical cores from <c>GetLogicalProcessorInformationEx(RelationProcessorCore)</c>.</summary>
    int PhysicalCores();

    /// <summary><c>GlobalMemoryStatusEx.ullTotalPhys</c>.</summary>
    long TotalPhysicalBytes();

    /// <summary>DXGI <c>EnumAdapters1</c>, in the system's order, software adapters included (flagged).</summary>
    IReadOnlyList<GpuAdapterInfo> Adapters();

    /// <summary>The driver version from the adapter's registry key.</summary>
    string DriverVersion(GpuAdapterInfo adapter);
}
