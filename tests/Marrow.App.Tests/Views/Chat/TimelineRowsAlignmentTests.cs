using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// SwiftUI's HStack centres its children on one line unless it names another alignment. Found rendering the turn file
// card beside 2-chat-use.mov at 32 s: the video's "Edited 13 files" sits on the badge's centre line (199-212 badge,
// 201-209 text), the port's title stood 6.5 points above it.
[Collection("Headless")]
public sealed class TimelineRowsAlignmentTests(HeadlessSession headless)
{
    private static double CentreY(Control control, Visual root) =>
        control.TranslatePoint(new Point(0, control.Bounds.Height / 2), root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-GIT-044")] // TimelineRows.swift:2741-2757 HStack(spacing: 8) { HStack(spacing: 8) { badge, title, stats, chevron } ... }
    public void The_file_card_puts_its_badge_title_stats_chevron_and_round_buttons_on_one_centre_line() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var files = Enumerable.Range(1, 13).Select(i => new FileChangeSummary.File($"src/file{i}.swift", 50, 3, false)).ToArray();
            var card = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>(files), 722, 46), true, () => { }, _ => { });
            var window = Show(card);
            try
            {
                var badge = card.GetVisualDescendants().OfType<SwiftImage>().First(image => image.Width == 18);
                var line = CentreY(badge, card);
                var title = TextNamed(card, "Edited 13 files");
                Assert.Equal(line, CentreY(title, card), tolerance: 0.5); // layout rounds an odd height to the half point
                var stats = card.GetVisualDescendants().OfType<DiffStatLabel>().Single();
                Assert.Equal(line, CentreY(stats, card), tolerance: 0.5);
                foreach (var glyph in card.GetVisualDescendants().OfType<MutedRoundGlyph>())
                    Assert.Equal(line, CentreY(glyph, card), tolerance: 0.5);
                // Each text's own glyph box, not just its slot: a stretched TextBlock draws at the top of its slot.
                Assert.Equal(title.DesiredSize.Height, title.Bounds.Height, 1);
            }
            finally { Close(window); }
        });
    });
}
