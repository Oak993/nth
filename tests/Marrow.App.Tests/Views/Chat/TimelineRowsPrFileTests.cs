using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class TimelineRowsPrFileTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(12, null)]
    [InlineData(13, "and 1 more file")]
    [InlineData(20, "and 8 more files")]
    [Trait("Req", "PR-GIT-152")] // TimelineRows.swift:2741-2757,2829-2857
    public void Clicking_the_card_shows_the_first_twelve_files_in_order_below_the_title_with_literal_overflow(int count, string? overflow)
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var presenter = new TimelineRowsPrPopupPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            Avalonia.Controls.Window? window = null;
            TurnFileCard? card = null;
            Button? title = null;
            var files = Enumerable.Range(1, count).Select(i => new FileChangeSummary.File($"src/file{i}.swift", i, i + 1, i == 1)).ToArray();
            try
            {
                h.Main.OnMain(() =>
                {
                    card = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>(files), 1, 2), false, () => { }, _ => { });
                    window = Show(card);
                    title = LogicalQuery.SelfAndDescendants<Button>(card).Single(button => Equals(ToolTip.GetTip(button), "Show the files this turn changed"));
                    Assert.Equal(count != 0, title.IsEnabled);
                    Click(window, title);
                });
                h.Main.OnMain(() =>
                {
                    if (count == 0) { Assert.DoesNotContain(presenter.Calls, call => call.Presented); return; }
                    var popup = presenter.Calls.Last(call => call.Presented).Popup;
                    Assert.True(popup.IsOpen);
                    Assert.Equal(PlacementMode.Bottom, popup.Placement);
                    Assert.Same(title, popup.PlacementTarget);
                    var paths = LogicalQuery.SelfAndDescendants<MiddleTruncatingTextBlock>(popup.Child!).ToArray();
                    Assert.Equal(files.Take(12).Select(file => file.Path), paths.Select(path => path.FullText));
                    Assert.All(paths, path => Assert.Equal(1, path.MaxLines));
                    var rows = LogicalQuery.SelfAndDescendants<DockPanel>(popup.Child!).ToArray();
                    Assert.Equal(Math.Min(12, count), rows.Length);
                    Assert.Equal("Binary", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(rows[0]), text => text.Text == "Binary").Text);
                    Assert.Empty(LogicalQuery.SelfAndDescendants<DiffStatLabel>(rows[0]));
                    for (var i = 1; i < rows.Length; i++)
                    {
                        var stat = Assert.Single(LogicalQuery.SelfAndDescendants<DiffStatLabel>(rows[i]));
                        Assert.Equal(files[i].Additions, stat.Additions);
                        Assert.Equal(files[i].Deletions, stat.Deletions);
                        Assert.Equal(Dock.Right, DockPanel.GetDock(stat));
                        Assert.Equal(stat, rows[i].Children[0]);
                        Assert.Equal(paths[i], rows[i].Children[1]);
                    }
                    var more = LogicalQuery.SelfAndDescendants<TextBlock>(popup.Child!).Where(text => text.Text?.StartsWith("and ", StringComparison.Ordinal) == true).ToArray();
                    if (overflow is null) Assert.Empty(more); else Assert.Equal(overflow, Assert.Single(more).Text);
                });
            }
            finally { if (window is not null) h.Main.OnMain(() => Close(window)); }
        });
    }
}
