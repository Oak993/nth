using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// Diagnostic and positive control for this fixture only, not a product-parity assertion.
// Runtime contract inspection occurs inside the test body.
public static class SettingsHeadlessPointerExercise
{
    public static void Run(HeadlessSession headless, ITestOutputHelper output, bool startDrag) => headless.OnUiThread(() =>
    {
        var target = new Border { Background = Brushes.Transparent, Focusable = true };
        var window = new HostWindow { Width = 320, Height = 180, Content = target };
        try
        {
            window.Show();
            Drain(window);
            var original = window.PlatformImpl;
            Assert.NotNull(original);
            var contract = SettingsHeadlessPlatformAdapter.HeadlessContract;
            Assert.True(contract.IsInterface);
            Assert.False(contract.IsVisible); // pinned 12.1.2 IHeadlessWindow is internal
            Assert.True(contract.IsInstanceOfType(original));
            var names = contract.GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "DragDrop", "GetLastRenderedFrame", "KeyPress", "KeyRelease", "MouseDown",
                "MouseMove", "MouseUp", "MouseWheel", "SetRenderScaling", "TextInput" }, names);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Avalonia.Headless assembly={contract.Assembly.GetName().FullName}; MVID={contract.Module.ModuleVersionId}; contract={contract.FullName}; visible={contract.IsVisible}"));
            foreach (var method in contract.GetMethods().OrderBy(method => method.Name, StringComparer.Ordinal))
                output.WriteLine(method.ToString() ?? method.Name);

            using (var platform = new SettingsWindowPlatformScope(window))
            {
                Assert.True(contract.IsInstanceOfType(platform.Platform));
                Assert.Same(original, platform.Spy.Original);
                var moved = 0;
                var pressed = 0;
                var released = 0;
                var observedForwarding = new List<string>();
                platform.Spy.BeforeForward = method =>
                {
                    Assert.Same(platform.Platform, window.PlatformImpl);
                    observedForwarding.Add(method.Name);
                };
                target.PointerMoved += (_, _) =>
                {
                    Assert.Same(platform.Platform, window.PlatformImpl);
                    moved++;
                };
                target.PointerPressed += (_, e) =>
                {
                    Assert.Same(platform.Platform, window.PlatformImpl);
                    pressed++;
                    Assert.True(target.Focus());
                    if (startDrag) window.BeginMoveDrag(e);
                };
                target.PointerReleased += (_, _) =>
                {
                    Assert.Same(platform.Platform, window.PlatformImpl);
                    released++;
                };
                var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
                Assert.Same(target, window.InputHitTest(point));
                window.MouseMove(point, RawInputModifiers.None);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                Drain(window);

                Assert.True(moved > 0);
                Assert.Equal(1, pressed);
                Assert.Equal(1, released);
                Assert.True(target.IsFocused);
                Assert.Same(platform.Platform, window.PlatformImpl);
                Assert.Equal(new[] { "MouseMove", "MouseDown", "MouseUp" },
                    platform.Spy.HeadlessCalls.Where(name => name is "MouseMove" or "MouseDown" or "MouseUp"));
                Assert.Contains("MouseMove", observedForwarding);
                Assert.Contains("MouseDown", observedForwarding);
                Assert.Contains("MouseUp", observedForwarding);
                if (startDrag)
                {
                    Assert.Single(platform.Spy.Calls, name => name == "BeginMoveDrag");
                    Assert.Contains("BeginMoveDrag", observedForwarding);
                }
                else Assert.DoesNotContain("BeginMoveDrag", platform.Spy.Calls);

                // GetImpl is used for frame capture too. This checks transparent delegation, not
                // rendered pixel values; a backend is permitted to have no completed frame yet.
                using var frame = window.CaptureRenderedFrame();
                Assert.Contains("GetLastRenderedFrame", platform.Spy.HeadlessCalls);
                Assert.Same(platform.Platform, window.PlatformImpl);
            }
            Assert.Same(original, window.PlatformImpl);
        }
        finally { window.Close(); }
    });

    private static void Drain(HostWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
