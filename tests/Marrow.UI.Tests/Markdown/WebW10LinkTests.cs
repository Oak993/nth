using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-1")]
public sealed class WebW10LinkTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_prose_click_reaches_the_inherited_chat_route_with_Control_state(bool control) => headless.OnUiThread(() =>
    {
        var field = typeof(MarkdownView).GetField("ChatLinkActionProperty");
        Assert.NotNull(field);
        var property = Assert.IsType<AttachedProperty<Func<SwiftURL, KeyModifiers, bool>?>>(field.GetValue(null));
        var paragraph = new LinkParagraphView("see [link](https://chat-route.invalid/article) here");
        var parent = new Border { Child = paragraph };
        var opened = new List<(string, KeyModifiers)>();
        parent.SetValue(property, (url, modifiers) => { opened.Add((url.AbsoluteString, modifiers)); return true; });
        var previous = ShellWorkspace.OpenWithDefaultApp;
        var shell = new List<string>(); ShellWorkspace.OpenWithDefaultApp = shell.Add;
        var window = new Window { Content = parent, Width = 500, Height = 220 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            var view = Assert.IsType<LinkTextView>(paragraph.Child);
            var block = Assert.IsType<SelectableTextBlock>(view.Child);
            var offset = 0;
            foreach (var inline in block.Inlines!)
            {
                if (inline is Run run)
                {
                    if ((run.Text ?? "").Contains("link", StringComparison.Ordinal)) break;
                    offset += (run.Text ?? "").Length;
                }
                else if (inline is InlineUIContainer) offset++;
            }
            var at = block.TextLayout.HitTestTextRange(offset + 1, 1).First().Center;
            Assert.Equal(SwiftURL.Create("https://chat-route.invalid/article"), view.Link(at));
            var point = block.TranslatePoint(at, window)!.Value;
            window.MouseDown(point, MouseButton.Left, control ? RawInputModifiers.Control : RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, control ? RawInputModifiers.Control : RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(("https://chat-route.invalid/article", control ? KeyModifiers.Control : KeyModifiers.None), Assert.Single(opened));
            Assert.Empty(shell);
        }
        finally { window.Close(); ShellWorkspace.OpenWithDefaultApp = previous; }
    });
}
