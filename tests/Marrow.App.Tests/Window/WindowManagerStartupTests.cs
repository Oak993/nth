using System.Reflection;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Xunit;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class WindowManagerStartupTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-622")]
    public void Restored_boot_shell_precedes_model_and_content_and_is_reused_for_the_main_window() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        harness.Main.OnMain(() =>
        {
            var manager = new WindowManager();
            var calls = 0;
            var content = new Border();
            manager.MainContent = () => { calls++; return content; };
            var prepare = typeof(WindowManager).GetMethod("PrepareMainShell", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(prepare);
            var window = Assert.IsType<ThreadWindow>(prepare.Invoke(manager, null));
            try
            {
                Assert.Null(window.Content);
                Assert.Equal(0, calls);
                var created = typeof(WindowManager).GetProperty("HasCreatedModel", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(created);
                Assert.False((bool)created.GetValue(manager)!);
                var position = window.Position;
                var size = window.ClientSize;
                Assert.Same(window, manager.PrepareMain());
                Assert.Equal(1, calls);
                Assert.True((bool)created.GetValue(manager)!);
                Assert.NotNull(content.Parent);
                Assert.Equal(position, window.Position);
                Assert.Equal(size, window.ClientSize);
                Assert.Same(window, manager.PrepareMain());
                Assert.Equal(1, calls);
            }
            finally { window.Close(); }
        });
    });
}
