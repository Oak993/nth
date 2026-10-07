using System.Reflection;
using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class ParityTailLicensesTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Theory]
    [Trait("Req", "PR-SETTINGS-249")] // SettingsView.swift:1046-1056: each unreadable resource falls back independently; UTF-8 is strict.
    [InlineData("LICENSE")]
    [InlineData("THIRD_PARTY_NOTICES")]
    [InlineData("TRADEMARK")]
    public void An_invalid_UTF8_document_falls_back_by_resource_name_without_losing_the_other_documents(string broken)
    {
        using var harness = new AppModelHarness();
        var directory = harness.NewFolder("licenses");
        var resources = new[] { "LICENSE", "THIRD_PARTY_NOTICES", "TRADEMARK" };
        foreach (var resource in resources)
        {
            var filename = resource == "LICENSE" ? resource : resource + ".md";
            // An invalid sequence must not silently become U+FFFD; the other documents remain intact.
            File.WriteAllBytes(Path.Combine(directory, filename), resource == broken ? [0xC3, 0x28] : Encoding.UTF8.GetBytes("# " + resource));
        }
        var read = typeof(LicensesRow).GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static, [typeof(string), typeof(string)])!;
        foreach (var resource in resources)
            Assert.Equal(resource == broken ? $"The {resource} file is missing from this copy of the app." : "# " + resource,
                Assert.IsType<string>(read.Invoke(null, [resource, directory])));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-249")] // SettingsView.swift:1012-1043: one row, one Show button, one below-row scroll presentation.
    public void The_About_credits_have_one_licenses_entry_and_the_three_documents_share_one_presentation() =>
        With(() => new AboutSettingsPage(), (view, _, _, window) =>
        {
            var licenses = Assert.Single(Nodes<LicensesRow>(view));
            CheckRow(Assert.Single(Nodes<Marrow.UI.Chrome.ChromeRow>(licenses)), "Licenses",
                "AGPL-3.0 with attribution terms, and the notices for what the app builds on.");
            var show = Assert.Single(Nodes<Avalonia.Controls.Button>(licenses));
            Assert.Equal("Show", Assert.Single(Text(show)));
            var presenter = new RecordingPresenter();
            using var presentation = Marrow.UI.Port.Presentation.Presenter.Override(presenter);
            Click(window, show);
            var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
            Assert.Equal(Avalonia.Controls.PlacementMode.Bottom, popup.Placement);
            var content = Open(popup);
            var scroll = Assert.Single(Nodes<Avalonia.Controls.ScrollViewer>(content), control => control.Width == 480);
            Assert.Equal(440, scroll.Height);
            var blocks = Assert.IsType<Avalonia.Controls.StackPanel>(scroll.Content);
            Assert.Equal(18, blocks.Spacing);
            Assert.Equal(new Avalonia.Thickness(18), blocks.Margin);
            Assert.Equal(new[] { "Marrow", "Third-party notices", "Trademarks" }, blocks.Children.Select(block => Text(block).First()));
            foreach (var block in blocks.Children)
            {
                var column = Assert.IsType<Avalonia.Controls.StackPanel>(block);
                Assert.Equal(8, column.Spacing);
                Font(Assert.IsType<Avalonia.Controls.TextBlock>(column.Children[0]), 13, Avalonia.Media.FontWeight.SemiBold);
                var markdown = Assert.IsType<Marrow.UI.Markdown.MarkdownView>(column.Children[1]);
                Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(markdown));
                Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownBlockSelection(markdown));
            }
        });
}
