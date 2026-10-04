using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A window with an overlay layer for popups and the real presenter kept in it, for the Customization preview's tests (CZ-3).</summary>
internal static class PreviewHost
{
    internal static void Show(Control content, Action<HostWindow, ReviewOverlayPresenter> body, double width = 700, double height = 500)
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var overlay = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var presenter = Presentation.Presenter.Override(overlay);
        var window = new HostWindow { Width = width, Height = height, Content = content };
        SettingsProviderPopupLayers.Install(window);
        window.Show();
        window.UpdateLayout();
        try { body(window, overlay); } finally { window.Close(); }
    }
}
