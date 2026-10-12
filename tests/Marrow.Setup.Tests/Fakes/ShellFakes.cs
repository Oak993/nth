using Avalonia;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Shell;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>Windows' settings as a test sets them; <see cref="SystemSettings.Refresh"/> reads <see cref="Look"/>.</summary>
public sealed class FakeLookSource(SystemLook look) : ISystemLookSource
{
    public SystemLook Look { get; set; } = look;

    public int Reads { get; private set; }

    public SystemLook Read()
    {
        Reads++;
        return Look;
    }
}

/// <summary>Records what the guard asks of Windows' shutdown block.</summary>
public sealed class FakeShutdownBlocker : IShutdownBlocker
{
    public string? Reason { get; private set; }

    public List<string> Calls { get; } = [];

    public void Block(string reason)
    {
        Reason = reason;
        Calls.Add("block:" + reason);
    }

    public void Release()
    {
        Reason = null;
        Calls.Add("release");
    }
}

/// <summary>A stand-in window for the message hook: client space is screen space minus an origin, at a scale.</summary>
public sealed class FakeCaptionSurface : ICaptionSurface
{
    public PixelPoint Origin { get; set; } = new(100, 200);

    public double Scale { get; set; } = 1;

    public double ClientWidth { get; set; } = 600;

    public bool ClosePaused { get; set; }

    public int Hover { get; private set; }

    public int Pressed { get; private set; }

    public int Refusals { get; private set; }

    public int SettingsReads { get; private set; }

    public Point ScreenToClient(PixelPoint screen) => new((screen.X - Origin.X) / Scale, (screen.Y - Origin.Y) / Scale);

    public Point ClientPixelsToClient(PixelPoint client) => new(client.X / Scale, client.Y / Scale);

    public void SetCaptionHover(int hit) => Hover = hit;

    public void SetCaptionPressed(int hit) => Pressed = hit;

    public void CloseRefused() => Refusals++;

    public void SettingsMayHaveChanged() => SettingsReads++;
}

/// <summary>Records the hook's Win32 calls.</summary>
public sealed class FakeCaptionCalls : ICaptionCalls
{
    public List<int> Commands { get; } = [];

    public int Tracks { get; private set; }

    public bool Captured { get; private set; }

    public void PostSystemCommand(nint window, int command) => Commands.Add(command);

    public bool TrackNonClientLeave(nint window)
    {
        Tracks++;
        return true;
    }

    public void Capture(nint window) => Captured = true;

    public void ReleaseCapture() => Captured = false;
}

/// <summary>Uninstall entries by package id (a test hive in memory: nothing reads or writes the real HKCU).</summary>
public sealed class FakeUninstallKeys : IUninstallKeys
{
    public Dictionary<string, string> Locations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? InstallLocation(string packageId) => Locations.GetValueOrDefault(packageId);
}

/// <summary>Records which window a second start brought forward.</summary>
public sealed class FakeWindowFocuser : IWindowFocuser
{
    public List<(nint Window, int ProcessId)> Focused { get; } = [];

    public void Focus(nint window, int processId) => Focused.Add((window, processId));
}
