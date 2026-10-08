using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Window;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;
using P = Marrow.App.Tests.Window.Native.SettingsOwnershipNativeProbe;

namespace Marrow.App.Tests.Window.Native;

[Collection("WindowManagerNative")]
public sealed class SettingsOwnershipNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Decision", "ED-NEW-T45")]
    public void Real_Settings_and_its_input_stay_above_Main_follow_owner_minimise_restore_and_close_once()
    {
        native.Run(_ =>
        {
            var manager = native.OnUi(() =>
            {
                var manager = new WindowManager { MainContent = () => new Border() };
                manager.ShowMain(); var main = manager.MainWindow!; native.Register(main);
                main.Template = new FuncControlTemplate<Avalonia.Controls.Window>((w, _) => new ContentPresenter { Content = w.Content });
                main.ApplyTemplate(); main.UpdateLayout();
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(main));
                manager.ShowSettings(); var settings = manager.SettingsWindow!; native.Register(settings);
                settings.Template = new FuncControlTemplate<Avalonia.Controls.Window>((w, _) => new ContentPresenter { Content = w.Content });
                settings.ApplyTemplate(); settings.UpdateLayout();
                return manager;
            }, TimeSpan.FromSeconds(30));
            var main = native.OnUi(() => manager.MainWindow!);
            var settings = native.OnUi(() => manager.SettingsWindow!);
            var mh = native.OnUi(() => main.TryGetPlatformHandle()!.Handle);
            var sh = native.OnUi(() => settings.TryGetPlatformHandle()!.Handle);
            N.Until(() => native.OnUi(() => manager.SettingsOpening is { Done: true } && ((SettingsShell)settings.Content!).Input is not null), "Real Settings did not complete Rise", 15000);
            var input = native.OnUi(() => ((SettingsShell)settings.Content!).Input!.Handle);
            Assert.Equal(mh, P.GetWindow(sh, 4)); Assert.Equal(sh, P.GetWindow(input, 4));
            native.OnUi(() => { Assert.False(main.Topmost); Assert.False(settings.Topmost); WindowManagerNativeForeground.Acquire(mh, native.ThreadId); });
            Assert.True(P.Above(sh, mh)); Assert.True(P.Above(input, mh));
            P.Send(mh, N.WM_SYSCOMMAND, (nint)N.SC_MINIMIZE);
            try { N.Until(() => N.IsIconic(mh) && !N.IsWindowVisible(sh) && !N.IsWindowVisible(input), "Owned Settings/input survived owner minimisation"); }
            catch (TimeoutException error) { throw new InvalidOperationException($"Owner hide: mainIconic={N.IsIconic(mh)}, settingsVisible={N.IsWindowVisible(sh)}, inputVisible={N.IsWindowVisible(input)}", error); }
            P.Send(mh, N.WM_SYSCOMMAND, (nint)N.SC_RESTORE);
            N.Until(() => !N.IsIconic(mh) && N.IsWindowVisible(sh) && N.IsWindowVisible(input), "Owned Settings/input did not restore with Main");
            Assert.True(P.Above(sh, mh)); Assert.True(P.Above(input, mh));
            native.OnUi(() =>
            {
                var shell = (SettingsShell)settings.Content!;
                var expected = settings.PointToScreen(new Point(shell.Inset, shell.Inset));
                var size = PixelSize.FromSize(new Size(shell.Client.Width, shell.Client.Height), settings.RenderScaling);
                Assert.True(N.GetWindowRect(input, out var rectangle));
                Assert.Equal(expected.X, rectangle.Left); Assert.Equal(expected.Y, rectangle.Top);
                Assert.Equal(expected.X + size.Width, rectangle.Right); Assert.Equal(expected.Y + size.Height, rectangle.Bottom);
                WindowManagerNativeForeground.Acquire(mh, native.ThreadId);
                Assert.Equal(mh, N.GetForegroundWindow()); Assert.False(settings.IsActive);
            });
            var point = native.OnUi(() => settings.GetVisualDescendants().OfType<CaptionButton>().Single(b => b.ButtonKind == CaptionButton.Kind.Close).PointToScreen(new Point(1, 1)));
            Assert.Equal(N.HTCLOSE, N.Hit(input, new NativeWindowPoint(point.X, point.Y)));
            // Actual bridge delivery through the owned proxy, with Main active. The
            // separate 60 fps tour supplies physical SDK clicks at the same corner.
            P.Send(input, N.WM_NCLBUTTONDOWN, N.HTCLOSE, P.Pack(point));
            P.Send(input, N.WM_NCLBUTTONUP, N.HTCLOSE, P.Pack(point));
            N.Until(() => !N.IsWindowVisible(sh) && !N.IsWindowVisible(input), "The first inactive close did not hide Settings/input", 6000);
            Assert.True(N.IsWindowVisible(mh)); Assert.False(N.IsIconic(mh));
            P.Send(mh, N.WM_SYSCOMMAND, (nint)N.SC_MINIMIZE); N.Until(() => N.IsIconic(mh), "Main did not minimise with closed Settings");
            P.Send(mh, N.WM_SYSCOMMAND, (nint)N.SC_RESTORE); N.Until(() => !N.IsIconic(mh), "Main did not restore with closed Settings");
            Assert.False(N.IsWindowVisible(sh)); Assert.False(N.IsWindowVisible(input));
            native.OnUi(() => manager.ShowSettings());
            Assert.Equal(mh, P.GetWindow(sh, 4)); Assert.Equal(sh, P.GetWindow(input, 4));
        }, factType: typeof(SettingsOwnershipNativeTests));
    }
}
