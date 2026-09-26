using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

[Collection("Headless")]
public sealed class LinkParagraphViewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-142")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:106-133
    public void Inline_code_uses_the_monospace_face_one_point_smaller() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("`code`", pointSize: 13);
        var block = (SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;
        var found = false;

        foreach (var inline in block.Inlines!)
        {
            if (inline is Run run && run.Text == "code")
            {
                Assert.Equal(12, run.FontSize);
                found = true;
            }
        }

        Assert.True(found);
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:90-100 (B3 :92 MarkdownLinkOpener.open)
    public void A_valid_url_link_value_is_handled_through_MarkdownLinkOpener()
    {
        var previous = ShellWorkspace.OpenWithDefaultApp;
        string? opened = null;
        ShellWorkspace.OpenWithDefaultApp = path => opened = path;
        try
        {
            var coordinator = new LinkParagraphView.Coordinator();
            Assert.True(coordinator.TextView(new LinkTextView(), SwiftURL.Create("https://example.com")!, 0));
            Assert.Equal("https://example.com", opened);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:95-98 (B3 :96 MarkdownLinkOpener.open)
    public void A_string_link_value_that_parses_as_a_url_is_handled_through_MarkdownLinkOpener()
    {
        var previous = ShellWorkspace.OpenWithDefaultApp;
        string? opened = null;
        ShellWorkspace.OpenWithDefaultApp = path => opened = path;
        try
        {
            var coordinator = new LinkParagraphView.Coordinator();
            Assert.True(coordinator.TextView(new LinkTextView(), "https://example.com", 0));
            Assert.Equal("https://example.com", opened);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-152")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:498-507
    public void Repeated_measurements_at_the_same_width_return_the_cached_height() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("A paragraph with text.");
        var view = (LinkTextView)paragraph.Child!;

        var first = view.HeightForWidth(120);
        var second = view.HeightForWidth(120);

        Assert.True(first > 0);
        Assert.Equal(first, second);
    });

    [Fact]
    [Trait("Req", "PR-THEME-180")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:121, 152
    public void Dimmed_prose_uses_label_color_with_reduced_alpha() => headless.OnUiThread(() =>
    {
        var normalParagraph = new LinkParagraphView("prose");
        var dimmedParagraph = new LinkParagraphView("prose", dimmed: true);
        var normalBlock = (SelectableTextBlock)((LinkTextView)normalParagraph.Child!).Child!;
        var dimmedBlock = (SelectableTextBlock)((LinkTextView)dimmedParagraph.Child!).Child!;
        Run? normalRun = null;
        Run? dimmedRun = null;

        foreach (var inline in normalBlock.Inlines!)
            if (inline is Run run) { normalRun = run; break; }

        foreach (var inline in dimmedBlock.Inlines!)
            if (inline is Run run) { dimmedRun = run; break; }

        var normalColor = Assert.IsType<SolidColorBrush>(normalRun!.Foreground).Color;
        var dimmedColor = Assert.IsType<SolidColorBrush>(dimmedRun!.Foreground).Color;
        Assert.InRange(
            System.Math.Abs((int)dimmedColor.A - (int)System.Math.Round(normalColor.A * 0.7)),
            0,
            1);
    });

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:247-249
    public void Hydra_head_attribute_key_keeps_its_exact_name()
    {
        Assert.Equal("droppycode.hydraHead", LinkParagraphView.HydraHead);
    }

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:90-100
    public void A_link_value_that_is_not_a_url_or_string_is_not_handled()
    {
        var coordinator = new LinkParagraphView.Coordinator();

        Assert.False(coordinator.TextView(new LinkTextView(), new object(), 0));
    }

    [Fact]
    [Trait("Req", "PR-THEME-152")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:498-500
    public void A_nonpositive_width_measures_zero_height()
    {
        var view = new LinkTextView();

        Assert.Equal(0, view.HeightForWidth(0));
        Assert.Equal(0, view.HeightForWidth(-1));
    }

    [Fact]
    [Trait("Req", "PR-THEME-143")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:179-223, via UI.Markdown.MentionScanner (N10)
    public void A_head_mention_still_renders_as_a_glyph_and_a_bold_name_through_MentionScanner() => headless.OnUiThread(() =>
    {
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var paragraph = new LinkParagraphView("Ping Hank now", mentions: [persona]);
        var block = (SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;

        var sawGlyph = false;
        Run? nameRun = null;
        foreach (var inline in block.Inlines!)
        {
            if (inline is InlineUIContainer) sawGlyph = true;
            else if (inline is Run run && run.Text == "Hank") nameRun = run;
        }

        Assert.True(sawGlyph);
        Assert.NotNull(nameRun);
        Assert.Equal(FontWeight.Bold, nameRun!.FontWeight);
    });

    // ---- PR-THEME-185: MarkdownLinkOpener.Open's own decision procedure (OD-0026, ED-203, ED-204) ----

    [Fact]
    [Trait("Req", "PR-THEME-185")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:228-229
    public void The_posix_local_file_gate_needs_no_scheme_host_query_or_fragment_and_an_absolute_path()
    {
        Assert.Equal("/abs/a.swift:12", MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("/abs/a.swift:12")!));
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:228-229: a host means the gate is closed
    public void A_scheme_less_two_slash_host_shape_does_not_qualify_as_a_local_file()
    {
        Assert.Null(MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("//host/share")!));
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:228-229: a query closes the gate
    public void A_query_on_an_otherwise_local_shaped_url_goes_to_the_default_opener()
    {
        Assert.Null(MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("/abs/a.swift?x=1")!));
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:228-229: a fragment closes the gate
    public void A_fragment_on_an_otherwise_local_shaped_url_goes_to_the_default_opener()
    {
        Assert.Null(MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("/abs/a.swift#x")!));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // OD-0026: a forward-slash Windows drive-letter path takes the local-file branch too
    public void A_forward_slash_drive_letter_path_qualifies_as_a_local_file()
    {
        Assert.Equal("C:/Users/x/a.cs:12", MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("C:/Users/x/a.cs:12")!));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // OD-0026: a backslash Windows drive-letter path takes the local-file branch too
    public void A_backslash_drive_letter_path_qualifies_as_a_local_file()
    {
        var url = SwiftURL.Create(@"C:\Users\x\a.cs:12")!;
        Assert.Equal(@"C:\Users\x\a.cs:12", MarkdownLinkOpener.LocalFilePath(url));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // OD-0026: WINDOWS-ONLY: url.Path reads IsFileURL, which asks ICU whenever Scheme is set
    public void The_file_url_form_of_a_drive_letter_path_qualifies_as_a_local_file()
    {
        var url = SwiftURL.Create("file:///C:/Users/x/a.cs:12")!;
        Assert.Equal("C:/Users/x/a.cs:12", MarkdownLinkOpener.LocalFilePath(url));
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // an ordinary external link never qualifies
    public void An_external_url_does_not_qualify_as_a_local_file()
    {
        Assert.Null(MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("https://example.com/a")!));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // \host\... is never opened or revealed
    public void A_raw_backslash_unc_path_is_recognized_as_unc()
    {
        var url = SwiftURL.Create(@"\\host\share\a.txt")!;
        Assert.True(MarkdownLinkOpener.IsUnc(url));
        Assert.Null(MarkdownLinkOpener.LocalFilePath(url));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // WINDOWS-ONLY: file://host/... is never opened or revealed
    public void A_file_url_with_a_host_is_recognized_as_unc()
    {
        Assert.True(MarkdownLinkOpener.IsUnc(SwiftURL.Create("file://host/share/a.txt")!));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // a drive-letter path is not inspected for a mapped network share
    public void A_drive_letter_path_is_never_treated_as_unc()
    {
        Assert.False(MarkdownLinkOpener.IsUnc(SwiftURL.Create("C:/Users/x/a.cs")!));
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")]
    public void A_posix_local_path_is_never_treated_as_unc()
    {
        Assert.False(MarkdownLinkOpener.IsUnc(SwiftURL.Create("/abs/a.swift")!));
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:239-241: exists and is not a directory
    public void The_suffix_is_stripped_when_the_full_path_is_missing_but_the_stripped_path_is_a_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.cs");
        File.WriteAllText(file, "x");
        try
        {
            Assert.Equal(file, MarkdownLinkOpener.ApplyLineSuffixRule(file + ":12"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // ":0" is not "a positive integer"
    public void A_zero_suffix_is_left_unstripped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.cs");
        File.WriteAllText(file, "x");
        try
        {
            var withSuffix = file + ":0";
            Assert.Equal(withSuffix, MarkdownLinkOpener.ApplyLineSuffixRule(withSuffix));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // a negative number is not "a positive integer" either
    public void A_negative_suffix_is_left_unstripped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.cs");
        File.WriteAllText(file, "x");
        try
        {
            var withSuffix = file + ":-1";
            Assert.Equal(withSuffix, MarkdownLinkOpener.ApplyLineSuffixRule(withSuffix));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // a non-numeric suffix leaves the path unstripped
    public void A_non_numeric_suffix_is_left_unstripped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.cs");
        File.WriteAllText(file, "x");
        try
        {
            var withSuffix = file + ":abc";
            Assert.Equal(withSuffix, MarkdownLinkOpener.ApplyLineSuffixRule(withSuffix));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // a directory prefix is kept: it exists, but it is not a file
    public void A_directory_prefixed_path_is_left_unstripped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        var sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            var withSuffix = sub + ":12";
            Assert.Equal(withSuffix, MarkdownLinkOpener.ApplyLineSuffixRule(withSuffix));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // the full path opens as given when it already exists
    public void A_path_that_exists_as_given_is_left_unstripped_even_with_a_colon_suffix_shape()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var literal = Path.Combine(dir, "b.cs:5");
        File.WriteAllText(literal, "x");
        try
        {
            Assert.Equal(literal, MarkdownLinkOpener.ApplyLineSuffixRule(literal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // no colon at all: the path is returned unchanged
    public void A_path_with_no_colon_is_left_unchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        var missing = Path.Combine(dir, "missing.cs");
        Assert.Equal(missing, MarkdownLinkOpener.ApplyLineSuffixRule(missing));
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // a UNC target is never opened or revealed
    public void Open_does_nothing_for_a_unc_link()
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var called = false;
        ShellWorkspace.OpenWithDefaultApp = _ => called = true;
        ShellWorkspace.RevealInFileExplorer = _ => called = true;
        try
        {
            MarkdownLinkOpener.Open(SwiftURL.Create(@"\\host\share\a.txt")!);
            Assert.False(called);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // an external link goes to the default opener with the URL's own string
    public void Open_hands_an_external_link_to_the_default_opener()
    {
        var previous = ShellWorkspace.OpenWithDefaultApp;
        string? opened = null;
        ShellWorkspace.OpenWithDefaultApp = path => opened = path;
        try
        {
            MarkdownLinkOpener.Open(SwiftURL.Create("https://example.com/a")!);
            Assert.Equal("https://example.com/a", opened);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previous;
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-185")] // a safe local file opens with the default app
    public void Open_opens_a_safe_local_file_with_the_default_app()
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        string? opened = null;
        var revealed = false;
        ShellWorkspace.OpenWithDefaultApp = path => opened = path;
        ShellWorkspace.RevealInFileExplorer = _ => revealed = true;
        try
        {
            MarkdownLinkOpener.Open(SwiftURL.Create("/abs/a.swift:12")!);
            Assert.Equal("/abs/a.swift:12", opened);
            Assert.False(revealed);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }

    [Fact]
    [Trait("Decision", "ED-204")] // a dangerous local file type is revealed, not opened
    public void Open_reveals_a_local_file_of_a_dangerous_type_instead_of_opening_it()
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        string? revealed = null;
        var opened = false;
        ShellWorkspace.OpenWithDefaultApp = _ => opened = true;
        ShellWorkspace.RevealInFileExplorer = path => revealed = path;
        try
        {
            MarkdownLinkOpener.Open(SwiftURL.Create("/abs/tool.exe")!);
            Assert.Equal("/abs/tool.exe", revealed);
            Assert.False(opened);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }
}
