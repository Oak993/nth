using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Core.Tests.Port.Skills.Pages;

// Unit SK-15 (DL-17): the words of the pages are the owner's pick, words set S1 (Plain), as the final skills-downloads page draws them.
public sealed class LibraryWordsTests
{
    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Spec", "DL-17")]
    [Trait("Rule", "R-TEST-4")]
    public void The_Download_all_button_and_its_dialog_use_the_design_s_words()
    {
        Assert.Equal("Download all plugins · 32 MB", LibraryWords.DownloadAllButton(LibrarySection.Plugins, 32.2));
        Assert.Equal("All skills are here", LibraryWords.DownloadAllButton(LibrarySection.Skills, null));
        Assert.Equal("Download all skills?", LibraryWords.ConfirmTitle(LibrarySection.Skills));
        Assert.Equal("Downloading all plugins", LibraryWords.RunningTitle(LibrarySection.Plugins));
        Assert.Equal("Nothing downloads until you press Continue.", LibraryWords.ConfirmFoot);
        Assert.Equal("Cancel stops the rest. What has already downloaded stays.", LibraryWords.RunningFoot);
        Assert.Equal("10 of 12 downloaded", LibraryWords.PartialTitle(10, 12));
        Assert.Equal("2 didn’t download. The rest are on this PC.", LibraryWords.PartialSub(2));
        Assert.Equal("Retry 2", LibraryWords.RetryCount(2));
        Assert.Equal("Included · 4", LibraryWords.Included(4, 0, 0, 0));
        Assert.Equal("Included · 14 · 2 already here, skipped", LibraryWords.Included(14, 2, 0, 0));
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Spec", "DL-17")]
    [Trait("Rule", "R-TEST-4")]
    public void The_progress_line_reads_percent_megabytes_of_the_total_and_items_done()
    {
        Assert.Equal("43% · 14 MB of 32 MB · 3 of 4 done", LibraryWords.Progress(false, 43, 13.9, 32.2, 3, 4));
        Assert.Equal("Paused · 43% · 14 MB of 32 MB · 3 of 4 done", LibraryWords.Progress(true, 43, 13.9, 32.2, 3, 4));
        Assert.Equal("50% · 1.0 MB of 2.0 MB · 1 of 2 done", LibraryWords.Progress(false, 50, 1.0, 2.0, 1, 2));
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Spec", "DL-17")]
    [Trait("Rule", "R-TEST-4")]
    public void The_row_and_box_words_are_the_design_s()
    {
        Assert.Equal("Download · 8.1 MB", LibraryWords.Download(8.1));
        Assert.Equal("Download", LibraryWords.Download(null));
        Assert.Equal("Listed in every session: 10 skills", LibraryWords.ListedHeading(10));
        Assert.Equal("Listed in every session: 1 skill", LibraryWords.ListedHeading(1));
        Assert.Equal("4 of 4 listed", LibraryWords.ListedOf(4, 4));
        Assert.Equal("Not listed: not downloaded", LibraryWords.NotListed);
        Assert.Equal("In every session", LibraryWords.InEverySession);
        Assert.StartsWith("Every session lists each switched-on skill", LibraryWords.SwitchNote, StringComparison.Ordinal);
        Assert.EndsWith("Switches apply to new chats.", LibraryWords.SwitchNote, StringComparison.Ordinal);
        Assert.Equal("Already available through your providers", LibraryWords.ProvidersHeading);
        Assert.Equal("Checking…", LibraryWords.Checking);
        Assert.Equal("Didn’t download", LibraryWords.DidNotDownload);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Spec", "DL-17")]
    [Trait("Rule", "R-TEST-4")]
    public void No_word_of_the_pages_names_an_image_and_a_vendor_is_only_ever_plain_text()
    {
        var words = typeof(LibraryWords).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
        Assert.All(words, w => Assert.DoesNotMatch(@"\.(png|svg|ico|jpe?g|gif)\b|logo|icon", w));
    }
}
