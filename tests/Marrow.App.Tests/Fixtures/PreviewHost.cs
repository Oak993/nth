using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A window with an overlay layer for popups and the real presenter kept in it, for the Customization preview's tests (CZ-3).</summary>
internal static class PreviewHost
{
    internal static void Show(Control content, Action<HostWindow, ReviewOverlayPresenter> body, double width = 700, double height = 500, TestRuntime? runtime = null, ManualFrameSource? frames = null)
    {
        using var own = runtime is null ? new TestRuntime() : null;
        using var bound = FrameClock.Source.Override(frames ?? new ManualFrameSource());
        var overlay = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var presenter = Presentation.Presenter.Override(overlay);
        var window = new HostWindow { Width = width, Height = height, Content = content };
        SettingsProviderPopupLayers.Install(window);
        // with a runtime of the test's own, the window is shown and closed on its main loop: what the page tracks (the app's settings) is read there
        void OnMain(Action action) { if (runtime is null) action(); else runtime.OnMain(action); }
        OnMain(() => { window.Show(); window.UpdateLayout(); });
        try { body(window, overlay); } finally { OnMain(window.Close); }
    }
}
