using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e), :1249-1265 (part 2 of the unit).
// Covers BlockHover (already declared, not stub) and CodeBlockCopyButton's body (UI-THEME-036), which does not
// depend on either sibling MARROW_STUB (it only builds the already-implemented CopyButton).
[Collection("Headless")]
public sealed class CodeBlockCopyButtonTests(HeadlessSession headless)
{
    private static object NewBlockHover()
    {
        var type = typeof(MarkdownBlockView).Assembly.GetType("Marrow.UI.Markdown.BlockHover")!;
        return System.Activator.CreateInstance(type, nonPublic: true)!;
    }

    private static object New(string text, object hover)
    {
        var type = typeof(MarkdownBlockView).Assembly.GetType("Marrow.UI.Markdown.CodeBlockCopyButton")!;
        var ctor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)[0];
        return ctor.Invoke([text, hover]);
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 200, Height = 100, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "UI-THEME-036")] // DroppyCode/UI/Markdown/MarkdownView.swift:1261-1264
    public void Opacity_is_zero_while_not_hovering_and_one_while_hovering()
    {
        headless.OnUiThread(() =>
        {
            var hover = NewBlockHover();
            var isHoveringProperty = hover.GetType().GetProperty("IsHovering")!;
            var control = (Control)New("some code", hover);
            var window = Show(control);

            Assert.Equal(0, control.Opacity); // :1263 hover.isHovering ? 1 : 0, starts false

            isHoveringProperty.SetValue(hover, true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, control.Opacity);

            isHoveringProperty.SetValue(hover, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, control.Opacity);
        });
    }
}
