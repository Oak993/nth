using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// PORT-ONLY: CU-W10, DESIGN 5.11/13.4. Owner-scheduled; never run by CU-W10.
// Both URL effects are recorded locally; this cannot open a browser or contact a site.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-w10-1")]
public sealed class WebW10NativeTests(WindowManagerNativeSession native)
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    [Fact]
    public void A_native_Control_click_on_chat_prose_delivers_Control_to_the_chat_link_route() => native.Run(subject =>
    {
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        Avalonia.Controls.Window window = null!;
        SelectableTextBlock block = null!;
        var opened = new List<KeyModifiers>();
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var shell = new List<string>();
        var controlDown = false;
        try
        {
            ShellWorkspace.OpenWithDefaultApp = url => shell.Add("open:" + url);
            ShellWorkspace.RevealInFileExplorer = path => shell.Add("reveal:" + path);
            native.OnUi(() =>
            {
                subject.Main.Hide(); subject.Settings.Hide();
                var field = typeof(MarkdownView).GetField("ChatLinkActionProperty"); Assert.NotNull(field);
                var property = Assert.IsType<AttachedProperty<Func<SwiftURL, KeyModifiers, bool>?>>(field.GetValue(null));
                var paragraph = new LinkParagraphView("see [link](https://native-w10.invalid/article) here");
                var root = new Border { Child = paragraph };
                root.SetValue(property, (url, modifiers) => { opened.Add(modifiers); return true; });
                block = Assert.IsType<SelectableTextBlock>(Assert.IsType<LinkTextView>(paragraph.Child).Child);
                window = new Avalonia.Controls.Window { Content = root, Width = 500, Height = 220 };
                native.Register(window); window.Show();
            });
            N.Until(() => native.OnUi(() => block.Bounds.Width > 0), "chat prose did not lay out");
            native.OnUi(() => WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()!.Handle, native.ThreadId));
            N.Until(() => N.GetForegroundWindow() == native.OnUi(() => window.TryGetPlatformHandle()!.Handle), "owned test window is not foreground");
            var point = native.OnUi(() =>
            {
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
                return block.PointToScreen(block.TextLayout.HitTestTextRange(offset + 1, 1).First().Center);
            });
            N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
            keybd_event(0x11, 0, 0, 0);
            controlDown = true;
            try { N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP); }
            finally { keybd_event(0x11, 0, 2, 0); controlDown = false; }
            N.Until(() => native.OnUi(() => opened.Count == 1), "Control click did not reach the chat-link route");
            native.OnUi(() => Assert.Equal(KeyModifiers.Control, Assert.Single(opened)));
            native.OnUi(() => Assert.Empty(shell));
        }
        finally
        {
            if (controlDown) keybd_event(0x11, 0, 2, 0);
            if (window is not null) native.OnUi(window.Close);
            N.SetCursorPos(original.X, original.Y);
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }, factType: typeof(WebW10NativeTests));
}
