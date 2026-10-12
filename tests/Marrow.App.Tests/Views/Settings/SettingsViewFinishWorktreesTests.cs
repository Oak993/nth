using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView.swift:803-849 (SourceControlSettingsPage, the "Worktrees and pull requests" section at :836-847); Services/Store/Storage.swift:30;
// UI/Chrome/Chrome.swift:1006-1087 (ChromeCard, ChromeSection, ChromeRow, ChromeRowDivider).
[Collection("Headless")]
public sealed class SettingsViewFinishWorktreesTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-GIT-150")] // App/Views/Settings/SettingsView.swift:836-847; Services/Store/Storage.swift:30
    [Trait("Req", "UI-GIT-059")] // App/Views/Settings/SettingsView.swift:836-847; UI/Chrome/Chrome.swift:1006-1087
    public void Worktrees_and_pull_requests_section_states_the_worktrees_folder_with_a_glass_Reveal_and_the_gh_and_glab_sentence_with_no_control()
    {
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            var sourceControl = RowButton(view, "Source control"); Reveal(w, h, sourceControl); Click(w, sourceControl); /* OD-0140: with Tactus listed after Skills the sidebar scrolls to this row, as a person scrolls it (OD-0134) */ var page = Assert.Single(Nodes<SourceControlSettingsPage>(view));
            // :808,:818,:836 the page's sections in order; the last is headed "Worktrees and pull requests".
            var sections = Nodes<ChromeSection>(page); Assert.Equal(new[] { "Text generation", "Commit message instructions", "Worktrees and pull requests" }, sections.Select(s => Text(s).First())); var section = sections[2];
            // Chrome.swift:1037-1044 the header .body semibold in secondary text, 10pt above the card (sectionHeaderSpacing); :1011-1026 the card a 16pt continuous shape filled with overlay(0.03).
            var header = Nodes<TextBlock>(section).First(); Assert.Equal("Worktrees and pull requests", header.Text); Font(header, Chrome.SettingsSectionTitleSize, FontWeight.SemiBold); Ink(header, header.Foreground, Chrome.SecondaryText);
            var card = Assert.Single(Nodes<ChromeCard>(section)); Assert.Equal(10, TopLeft(card, section).Y - (TopLeft(header, section).Y + header.Bounds.Height), 3); var shape = Assert.IsType<Border>(card.Child); Assert.Equal(new CornerRadius(16), shape.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(shape)); Ink(shape, shape.Background, Chrome.Overlay(0.03));
            // :837-846 two rows with a divider between them, in that order.
            var rows = Nodes<ChromeRow>(card); Assert.Equal(new[] { "Worktrees", "Pull requests" }, rows.Select(r => Text(r).First())); var divider = Assert.Single(Nodes<ChromeRowDivider>(card));
            Assert.True(TopLeft(divider, card).Y >= TopLeft(rows[0], card).Y + rows[0].Bounds.Height - 0.01); Assert.True(TopLeft(rows[1], card).Y >= TopLeft(divider, card).Y + divider.Bounds.Height - 0.01); Assert.Equal(new Thickness(16, 0, 0, 0), divider.Margin);
            // Row 1: the title "Worktrees", the resolved worktrees directory as its detail (Storage.swift:30 worktreesDirectory), and one button labelled "Reveal" at the trailing edge.
            var folder = System.IO.Path.Combine(h.Home, ".nth", "worktrees"); Assert.Equal(folder, Storage.WorktreesDirectory); CheckRow(rows[0], "Worktrees", folder);
            var reveal = Assert.Single(Nodes<Button>(rows[0])); Assert.Equal(new[] { "Reveal" }, Text(reveal)); Assert.Equal(TopLeft(rows[0], card).X + rows[0].Bounds.Width - 10, TopLeft(reveal, card).X + reveal.Bounds.Width, 3); // 10pt rowControlTrailingPadding
            // :840 .buttonStyle(.glass): a regular continuous capsule of glass that answers presses.
            var effect = SwiftGlass.GetEffect(Assert.Single(reveal.GetVisualDescendants().OfType<Control>(), c => SwiftGlass.GetEffect(c) is not null))!; Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.True(effect.IsInteractive);
            // SOURCE FACT (the reference runtime, S 199.5, frames/w-1-settings-tour-198.0/stills/win_199.5.png): that glass is the system `.glass` style under the window's accent
            // tint, not Chrome's neutral chromeGlassCapsule: an accent capsule 22pt high with a white 13pt label and 13pt either side of it (64 x 22 in the capture).
            Assert.Equal(Chrome.Accent, effect.TintColor); Assert.Equal(1, effect.TintOpacity); Assert.Equal(22, reveal.Bounds.Height, 3);
            var label = Assert.Single(reveal.GetVisualDescendants().OfType<TextBlock>()); Assert.Equal(13, label.FontSize); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); Assert.Equal(label.Bounds.Width + 2 * 13, reveal.Bounds.Width, 3);
            var surface = Assert.Single(reveal.GetVisualDescendants().OfType<Border>(), b => b.Height == 22); Ink(surface, surface.Background, Chrome.Accent); Assert.Equal(new CornerRadius(11), surface.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface));
            // Row 2: "Pull requests" with exactly the sentence that names gh and glab (and, as upstream, not tea), and no control at all (:844 EmptyView).
            var sentence = "Opened with the GitHub CLI (gh) or GitLab CLI (glab) from your PATH."; CheckRow(rows[1], "Pull requests", sentence); Assert.DoesNotContain("tea", Text(rows[1])[1], StringComparison.OrdinalIgnoreCase);
            None<Button>(rows[1], "in the pull requests row"); None<TextBox>(rows[1], "in the pull requests row"); None<SmallSwitch>(rows[1], "in the pull requests row"); var accessory = Assert.IsType<Border>(Assert.IsType<Grid>(Assert.IsType<Border>(rows[1].Child).Child).Children[1]); Assert.Null(accessory.Child);
            // Reveal opens that folder in the file browser: a real click on the button hands the resolved path to the platform, once per press.
            var opened = new List<string>(); using var platform = Marrow.UI.Common.Workspace.Platform.Override(new Marrow.UI.Common.Workspace.Backend { Reveal = path => opened.Add(path) });
            Click(w, reveal); Assert.Equal(new[] { folder }, opened); Click(w, reveal); Assert.Equal(new[] { folder, folder }, opened);
            // The sentence's row and the detail are static text: pressing on them opens nothing.
            Click(w, rows[1]); Click(w, Assert.IsType<Grid>(Assert.IsType<Border>(rows[0].Child).Child).Children[0]); Assert.Equal(2, opened.Count);
        }, height: 1100);
    }
}
