// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the DXGI factory import (V-10.1).
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>dxgi.dll <c>CreateDXGIFactory1</c>.</summary>
public static class DxgiNative
{
    /// <summary>IID_IDXGIFactory1.</summary>
    public static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [DllImport("dxgi.dll", ExactSpelling = true)]
    public static extern int CreateDXGIFactory1(in Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);
}
