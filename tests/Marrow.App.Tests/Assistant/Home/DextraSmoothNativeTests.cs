using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Tests.Window.Native;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Assistant.Home;

// Owed real HWND/compositor checks. Never run by DX-SMOOTH; the lead holds native.lock and controls focus.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-dextra-smooth-2")]
public sealed class DextraSmoothNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    public void Covered_and_minimized_marks_and_shimmers_stop_and_resume_on_foreground()
    {
        native.Run(_ =>
        {
            ArmillaMark mark = null!;
            TickText text = null!;
            var window = native.OnUi(() =>
            {
                var host = new Avalonia.Controls.Window { Width = 360, Height = 280, ShowInTaskbar = false };
                native.Register(host);
                host.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(host));
                mark = new ArmillaMark { MarkSize = 176, Kind = ArmillaKind.Live, State = ArmillaState.Thinking };
                text = new TickText { Text = "Working on a mission", Sheen = true, Width = 300 };
                host.Content = new StackPanel { Children = { mark, text } };
                host.Show(); host.Activate(); return host;
            });
            Front(window);
            N.Until(() => native.OnUi(() => mark.FramesDrawn > 3 && text.SheenFrames > 3), "visible Dextra did not draw");
            var cover = native.OnUi(() =>
            {
                var host = new Avalonia.Controls.Window { Width = 500, Height = 500, Position = window.Position, ShowInTaskbar = false };
                native.Register(host); host.Show(); host.Activate(); return host;
            });
            Front(cover);
            N.Until(() => native.OnUi(() => !mark.Ticking && !text.SheenTicking), "covered Dextra still asks for frames");
            var covered = native.OnUi(() => (mark.FramesDrawn, text.SheenFrames));
            Thread.Sleep(500); // Native only: allow physical compositor refreshes, not a fake-clock proof.
            Assert.Equal(covered, native.OnUi(() => (mark.FramesDrawn, text.SheenFrames)));
            native.OnUi(() => cover.Close()); Front(window);
            N.Until(() => native.OnUi(() => mark.FramesDrawn > covered.Item1 && text.SheenFrames > covered.Item2), "foreground Dextra did not resume");
            native.OnUi(() => window.WindowState = WindowState.Minimized);
            N.Until(() => native.OnUi(() => !mark.Ticking && !text.SheenTicking), "minimized Dextra still asks for frames");
            native.OnUi(() => window.WindowState = WindowState.Normal); Front(window);
            N.Until(() => native.OnUi(() => mark.Ticking && text.SheenTicking), "restored Dextra did not resume");
        }, nameof(Covered_and_minimized_marks_and_shimmers_stop_and_resume_on_foreground), typeof(DextraSmoothNativeTests));
    }

    private void Front(Avalonia.Controls.Window window) => native.OnUi(() =>
        WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("NATIVE_PRECONDITION: no HWND"), native.ThreadId));
}
