// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): one graphics adapter as the probe reads it (V-10.1).
namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// A DXGI adapter: description, PCI vendor and device ids, dedicated video memory, free video memory (budget minus usage, <c>IDXGIAdapter3.QueryVideoMemoryInfo(LOCAL)</c>) and
/// whether DXGI marks it a software adapter.
/// </summary>
public sealed record GpuAdapterInfo(string Description, uint VendorId, uint DeviceId, long DedicatedBytes, long FreeBytes, bool IsSoftware);
