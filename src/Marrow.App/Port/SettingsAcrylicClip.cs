// PORT-ONLY RR-37: a persistent inner Acrylic clip must follow DPI after the opening animator is gone.
using System.Numerics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

internal sealed class SettingsAcrylicClip : IDisposable
{
    private readonly HostWindow window;
    private readonly IDisposable geometry;
    private readonly object sync;
    private readonly double inset;
    private readonly Size client;
    private readonly Action<Vector2> offset, size, radius;
    private bool disposed;

    internal SettingsAcrylicClip(HostWindow window, object geometry, object sync, double inset, Size client)
    {
        this.window = window; this.geometry = (IDisposable)geometry; this.sync = sync;
        this.inset = inset; this.client = client;
        try
        {
            var contract = geometry.GetType().GetInterfaces().FirstOrDefault(t => t.FullName == "Avalonia.Win32.WinRT.ICompositionRoundedRectangleGeometry") ?? geometry.GetType();
            offset = contract.GetMethod("SetOffset")!.CreateDelegate<Action<Vector2>>(geometry);
            size = contract.GetMethod("SetSize")!.CreateDelegate<Action<Vector2>>(geometry);
            radius = contract.GetMethod("SetCornerRadius")!.CreateDelegate<Action<Vector2>>(geometry);
            window.ScalingChanged += OnScaling;
            Update(window.RenderScaling);
        }
        catch
        {
            window.ScalingChanged -= OnScaling;
            this.geometry.Dispose();
            throw;
        }
    }

    internal void Update(double scale)
    {
        lock (sync)
        {
            if (disposed) return;
            var shell = window.Content as SettingsShell;
            var currentInset = shell?.Inset ?? inset;
            var currentClient = shell is null ? client : new Size(shell.Client.Width, shell.Client.Height);
            offset(new Vector2((float)(currentInset * scale)));
            size(new Vector2((float)(currentClient.Width * scale), (float)(currentClient.Height * scale)));
            radius(new Vector2((float)((shell?.ClipRadius ?? 8) * scale)));
        }
    }

    private void OnScaling(object? sender, EventArgs e)
    {
        Update(window.RenderScaling);
        // Run once after the backend's DPI/size notification handlers, too. This is not an animation.
        Dispatcher.UIThread.Post(() => Update(window.RenderScaling));
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            window.ScalingChanged -= OnScaling;
            geometry.Dispose();
        }
    }
}
