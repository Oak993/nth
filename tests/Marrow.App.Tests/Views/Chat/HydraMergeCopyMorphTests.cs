using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.HydraMergeCardProbe;

namespace Marrow.App.Tests.Views.Chat;

// HydraMergePopover.swift:190, :202-203: "Copy link" becomes "Copied" inside withAnimation(.snappy(duration: 0.22)),
// on a Text with .contentTransition(.numericText()), so the words morph rather than swap.
[Collection("Headless")]
public sealed class HydraMergeCopyMorphTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-094")]
    public void The_copy_words_morph_into_copied() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var popover = new HydraMergePopover(Parse(FilesLine()));
        var copy = Descendants<Button>(popover).First(candidate => Text(candidate, "Copy link") is not null);
        var label = Assert.IsType<StackPanel>(copy.Content).Children.OfType<MorphingTextBlock>().Single();
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        runtime.RunUntilIdle();
        Assert.Equal("Copied", label.Text);
        Assert.True(label.IsMorphing);
    });
}
