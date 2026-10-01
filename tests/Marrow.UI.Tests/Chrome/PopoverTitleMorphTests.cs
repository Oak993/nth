using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Popovers.swift:307-311 the row title is a Text with .contentTransition(.numericText()); the confirmation swap runs
// under the snappy transaction of :263, so "Copy" morphs into "Copied" (MorphingTextBlock).
[Collection("Headless")]
public sealed class PopoverTitleMorphTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-SIDEBAR-106")] // the popover menu row's middle-truncated title
    public void A_confirming_rows_title_morphs_into_its_confirmation() => headless.OnUiThread(() =>
    {
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        var item = new PopoverItem("Copy", () => { }, confirmation: "Copied");
        var window = new Window { Width = 300, Height = 120, Content = item };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = item.GetVisualDescendants().OfType<MiddleTruncatingText>().Single();
            Assert.False(title.IsMorphing);
            var button = item.GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal("Copied", title.Text);
            Assert.True(title.IsMorphing);
        }
        finally { window.Close(); }
    });
}
