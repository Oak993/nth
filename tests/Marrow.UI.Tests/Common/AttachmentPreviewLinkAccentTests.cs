using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;
using UiChrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Common;

// OD-0088 (ED-253): the attachment previews' links are `.buttonStyle(.link)` buttons (UI/Common/AttachmentPreview.swift:356,
// :361 "Show in Finder" and "Open", :425 the tool image's "Open"), whose label SwiftUI draws in the view's tint; the window root
// tints every tree with the theme's accent (App/Window/WindowManager.swift:186-192 ThemeTint `.tint(theme.spec.accent)`, a
// presented tree restating it, UI/Chrome/Popovers.swift:7-12), so they wear Chrome.accent, which the port gives the user's pick.
// Before, they drew the Windows accent whatever the theme or pick.
[Collection("Headless")]
public sealed class AttachmentPreviewLinkAccentTests(HeadlessSession headless)
{
    private static TextBlock Link(Control preview, string title) =>
        VisualQuery.Descendants<Button>(preview).Select(b => b.Content as TextBlock).Single(t => t?.Text == title)!;

    [Fact]
    [Trait("Decision", "ED-253")]
    public void The_preview_links_wear_the_picked_accent_and_follow_a_later_pick() => headless.OnUiThread(() =>
    {
        var (theme, choice) = (ThemeManager.Current, AccentChoice.Current);
        var path = Path.Combine(Marrow.Testing.TestDataDirectory.Read()!, Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, new byte[10]);
        var window = new Window { Width = 600, Height = 600, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            ThemeManager.Current = AppTheme.System;
            AccentChoice.Current = "orchid";
            var large = new AttachmentLargePreview(new Attachment { Name = "file.bin", Path = path, MimeType = "application/octet-stream" });
            var tool = new ToolImagePreview(path);
            window.Content = new StackPanel { Children = { large, tool } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var links = new[] { Link(large, "Show in File Explorer"), Link(large, "Open"), Link(tool, "Open") };
            foreach (var link in links)
                Assert.Equal(Color.Parse("#E298C6"), Assert.IsAssignableFrom<ISolidColorBrush>(link.Foreground).Color); // Orchid, dark
            AccentChoice.Current = "windows"; // the default: the system accent under System, as the source's nil tint
            Dispatcher.UIThread.RunJobs();
            foreach (var link in links)
                Assert.Equal(UiChrome.Accent.Resolve(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(link.Foreground).Color);
            ThemeManager.Current = AppTheme.Dracula; // a named theme: its own accent
            Dispatcher.UIThread.RunJobs();
            foreach (var link in links)
                Assert.Equal(Color.Parse("#BD93F9"), Assert.IsAssignableFrom<ISolidColorBrush>(link.Foreground).Color);
        }
        finally
        {
            window.Close();
            AccentChoice.Current = choice;
            ThemeManager.Current = theme;
            Dispatcher.UIThread.RunJobs();
        }
    });
}
