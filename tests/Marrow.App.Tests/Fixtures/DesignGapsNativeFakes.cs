using System.Numerics;

namespace Marrow.App.Tests.Fixtures;

// The WinUI object graph DesignGapsNativePresentationTests binds SettingsNativePresentation to (ED-NEW-design-gaps-4): only the
// field and member names matter to the reflection binder; the types sit here so the test file holds one class (R-TEST-2/3).
internal sealed class DesignGapsFakePlatform { public readonly DesignGapsFakeSurface _glSurface = new(); }
internal sealed class DesignGapsFakeSurface { public readonly DesignGapsFakeNativeWindow _window = new(); }
internal sealed class DesignGapsFakeNativeWindow
{
    public readonly DesignGapsFakeShared _shared = new();
    public readonly DesignGapsFakeNativeVisual _blur = new() { Opacity = .8f };
    public readonly DesignGapsFakeTarget _target = new();
}
internal sealed class DesignGapsFakeShared { public object SyncRoot { get; } = new(); }
internal sealed class DesignGapsFakeTarget { public DesignGapsFakeNativeVisual Root { get; } = new() { Opacity = .9f, CenterPoint = new Vector3(5, 6, 0) }; }
internal sealed class DesignGapsFakeNativeVisual : IDisposable
{
    public float Opacity { get; set; } = 1;
    public Vector3 Scale { get; set; } = Vector3.One;
    public Vector3 CenterPoint { get; set; }
    public int Disposals { get; private set; }
    public void SetOpacity(float value) => Opacity = value;
    public void SetScale(Vector3 value) => Scale = value;
    public void SetCenterPoint(Vector3 value) => CenterPoint = value;
    public void Dispose() => Disposals++;
}
