using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    [Trait("Req", "PR-THEME-152")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:62-65, :515-549; ED-223
    public void A_live_window_resize_never_oscillates_between_paragraph_heights() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("A selectable paragraph with [a link](https://example.com) and enough prose to wrap across several lines as the window edge is dragged.")
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Window { Content = paragraph, Width = 420, Height = 400 };
        window.Show();
        try
        {
            var heights = new System.Collections.Generic.Dictionary<double, double>();
            double[] widths = [420, 380, 340, 300, 260, 220, 180, 160, 160.25, 160.5, 180, 220, 260, 300, 340, 380, 420];
            foreach (var width in widths)
            {
                window.Width = width;
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var height = paragraph.Bounds.Height;
                Assert.True(height > 0);
                for (var pass = 0; pass < 4; pass++)
                {
                    // Exercise subsequent layout/dispatcher turns at this same proposed width: the Swift's
                    // second, frame-late measurement must not make the paragraph hop to the previous height.
                    paragraph.InvalidateMeasure();
                    window.UpdateLayout();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.Equal(height, paragraph.Bounds.Height);
                    Assert.Equal(height, paragraph.DesiredSize.Height);
                }
                if (heights.TryGetValue(width, out var outboundHeight)) Assert.Equal(outboundHeight, height);
                heights[width] = height;
            }
            Assert.True(heights[160] > heights[420]); // The drag really crosses wrapping boundaries.
            window.UpdateLayout(); // End of the drag: no deferred height change.
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(heights[420], paragraph.Bounds.Height);
        }
        finally { window.Close(); }
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

    // ---- review A29 finding 1: a doubled separator, a drive-relative form, and "file:" without the leading "/" ----

    [Fact]
    [Trait("Decision", "ED-204")] // a doubled slash right after the drive letter parses as an authority (a Host),
                                  // not into Path, but Windows treats "C://x" and "C:/x" as the same path
    public void A_doubled_slash_after_the_drive_letter_still_qualifies_as_a_local_file()
    {
        var url = SwiftURL.Create("C://Windows/System32/calc.exe")!;
        Assert.Equal("Windows", url.Host); // pins the parse this fix depends on
        Assert.Equal("C:/Windows/System32/calc.exe", MarkdownLinkOpener.LocalFilePath(url));
    }

    [Fact]
    [Trait("Decision", "ED-203")] // a drive-relative form is not rooted: Windows would resolve it against
                                  // whatever the process's current directory on that drive happens to be
    public void A_drive_relative_path_does_not_qualify_as_a_local_file()
    {
        Assert.Null(MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("C:tool.exe")!));
        Assert.Null(MarkdownLinkOpener.LocalFilePath(SwiftURL.Create("C:../tool.exe")!));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // "file:C:/..." omits the "//" a file URL usually carries; SwiftURL then parses
                                  // no authority at all, and Path starts directly at the drive letter
    public void The_file_url_form_of_a_drive_letter_path_with_no_authority_marker_qualifies_as_a_local_file()
    {
        var url = SwiftURL.Create("file:C:/x/a.exe")!;
        Assert.Null(url.Host);
        Assert.Equal("C:/x/a.exe", MarkdownLinkOpener.LocalFilePath(url));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // a query does not exempt a Windows drive-letter shape the way it does the
                                  // source's own POSIX rule: it is exactly as attacker-controllable as the rest
                                  // of a chat-authored link, and ShellExecuteW's own file: URI handling resolves
                                  // this to the local path regardless
    public void A_file_url_drive_letter_path_with_a_query_still_qualifies_as_a_local_file()
    {
        var url = SwiftURL.Create("file:///C:/x/a.exe?q")!;
        Assert.Equal("q", url.Query);
        Assert.Equal("C:/x/a.exe", MarkdownLinkOpener.LocalFilePath(url));
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

    // ---- review A29 finding 2: shapes Windows treats as UNC that RFC 3986 does not parse as \\host\... ----

    [Fact]
    [Trait("Decision", "ED-204")] // a doubled forward slash parses as an empty authority (Host nullified by the
                                  // "empty host, no user/password/port" bincompat rule), so it survives into Path
                                  // still doubled; .NET's own File.Exists/Directory.Exists resolve it as \\host\...
    public void A_doubled_forward_slash_prefix_is_recognized_as_unc()
    {
        var url = SwiftURL.Create("////host/share/a.exe")!;
        Assert.Null(url.Host); // pins the parse this fix depends on
        Assert.True(MarkdownLinkOpener.IsUnc(url));
        // Open() checks IsUnc first and never reaches LocalFilePath for this shape (see
        // Open_does_nothing_for_a_doubled_slash_unc_link); LocalFilePath's own POSIX branch, unchanged from the
        // source's literal "/"-rooted rule, still matches a "//"-led path taken on its own.
    }

    [Fact]
    [Trait("Decision", "ED-204")] // a mixed leading pair ("/\" or "\/") is just as much a UNC prefix to Windows'
                                  // own path resolution as "\\" or "//"; SwiftURL parses no authority for either
                                  // (doubleSlashExists requires two literal '/', and '\' is not a scheme-scan break
                                  // character), so this reaches Path unrecognized unless checked explicitly
    public void A_mixed_slash_backslash_prefix_is_recognized_as_unc()
    {
        Assert.True(MarkdownLinkOpener.IsUnc(SwiftURL.Create(@"/\host\share\a.exe")!));
        Assert.True(MarkdownLinkOpener.IsUnc(SwiftURL.Create(@"\/host/share/a.exe")!));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // "file:////host/..." parses an empty authority the same way the scheme-less
                                  // doubled-slash form does, then the doubled slash re-emerges at the head of Path
    public void A_file_url_with_a_doubled_slash_path_is_recognized_as_unc()
    {
        var url = SwiftURL.Create("file:////host/share/a.exe")!;
        Assert.Null(url.Host); // pins the parse this fix depends on
        Assert.True(MarkdownLinkOpener.IsUnc(url));
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
    [Trait("Req", "PR-THEME-185")] // a safe local file opens with the default app. The seam is mocked here, as in
                                  // every other test in this class (R-TEST-10); that the REAL gate also accepts
                                  // this exact bare, scheme-less "/"-rooted string (review A29 finding 4: it does
                                  // not, by itself, "carry a scheme") is asserted separately, without invoking
                                  // ShellExecuteW, by ShellWorkspaceTests.A_rooted_scheme_less_path_is_accepted.
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

    [Fact]
    [Trait("Decision", "ED-204")] // review A29 finding 1: a doubled slash right after the drive letter must still
                                  // reach the reveal-vs-open decision, not ShellWorkspace.OpenWithDefaultApp raw
    public void Open_reveals_a_doubled_slash_drive_letter_executable_instead_of_opening_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-linkopener-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "tool.exe");
        File.WriteAllText(file, "x");
        var doubledSlashUrl = file[0].ToString() + "://" + file[2..].Replace('\\', '/').TrimStart('/');
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        string? revealed = null;
        var opened = false;
        ShellWorkspace.OpenWithDefaultApp = _ => opened = true;
        ShellWorkspace.RevealInFileExplorer = path => revealed = path;
        try
        {
            MarkdownLinkOpener.Open(SwiftURL.Create(doubledSlashUrl)!);
            Assert.NotNull(revealed);
            Assert.True(File.Exists(revealed)); // the reconstructed path still resolves to the same file
            Assert.False(opened);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-204")] // review A29 finding 2: a doubled-forward-slash UNC form is refused exactly like
                                  // "\\host\..." rather than reaching File.Exists (an SMB access) or the shell
    public void Open_does_nothing_for_a_doubled_slash_unc_link()
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var called = false;
        ShellWorkspace.OpenWithDefaultApp = _ => called = true;
        ShellWorkspace.RevealInFileExplorer = _ => called = true;
        try
        {
            MarkdownLinkOpener.Open(SwiftURL.Create("////host/share/a.exe")!);
            Assert.False(called);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }

    // ---- real pointer input on the paragraph path (PR-THEME-145, PR-THEME-146) ----
    // fix/links: a verifier probe drove window.MouseDown/MouseUp over a LinkParagraphView and found a left click on a
    // link opened nothing and a click on a head mention never reached onOpenHead (the press only moved the selection).
    // These tests press the real headless pointer, so the press travels the same route a user's click does.

    // A real layout pass and real pointer input need a hosting window (as LinkHoverTests.Show).
    private static Window ShowForPointer(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // The text layout's own box for one character of the first Run whose text contains `text`, `into` characters in.
    // Offsets follow the block's inline sequence: a Run counts its characters, an InlineUIContainer one.
    private static Rect CharacterRect(SelectableTextBlock block, string text, int into = 1)
    {
        var offset = 0;
        foreach (var inline in block.Inlines!)
        {
            if (inline is Run run)
            {
                var value = run.Text ?? "";
                var found = value.IndexOf(text, StringComparison.Ordinal);
                if (found >= 0) return block.TextLayout.HitTestTextRange(offset + found + into, 1).First();
                offset += value.Length;
            }
            else if (inline is InlineUIContainer)
            {
                offset++;
            }
        }
        throw new InvalidOperationException("no run holds " + text);
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window window, Control host, Point at, MouseButton button, RawInputModifiers modifiers)
    {
        var point = host.TranslatePoint(at, window)!.Value;
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:444-451, :90-100, :228-231 (B3)
    // :444-451 a press off a head mention goes to super.mouseDown, NSTextView's own link click, which asks the
    // delegate's clickedOnLink (:90-100) to open the URL through MarkdownLinkOpener.open; a URL with a scheme fails the
    // :228-229 guard and goes to NSWorkspace.shared.open(url) unchanged (:230).
    public void A_real_left_click_on_a_link_opens_it_through_MarkdownLinkOpener() => headless.OnUiThread(() =>
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var paragraph = new LinkParagraphView("see [link](https://link-click.invalid/x) here");
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            var point = CharacterRect(block, "link").Center;
            Assert.Equal(SwiftURL.Create("https://link-click.invalid/x"), view.Link(point)); // set up: the point is on the link

            Press(window, block, point, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(["https://link-click.invalid/x"], opened);
        }
        finally
        {
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:444-451, :203-216 (B3); Core/Models/Hydra.swift:445
    // :446-449 a press on a head mention calls onOpenHead with the mention's .hydraHead URL (:210, :216 target.url,
    // Hydra.swift:445 droppycode-head://<UUID>) and returns without super.mouseDown, so the selection stays as it was.
    public void A_real_click_on_a_head_mention_opens_the_head_and_leaves_the_selection_alone() => headless.OnUiThread(() =>
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };
        var heads = new List<SwiftURL>();
        var paragraph = new LinkParagraphView("Ping Hank now", mentions: [persona], targets: targets, onOpenHead: heads.Add);
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            block.SelectAll();
            Settle(window); // the layout and render pass a user's next click comes after
            var start = block.SelectionStart;
            var end = block.SelectionEnd;
            Assert.NotEqual(start, end); // set up: a selection the click must leave alone
            var point = CharacterRect(block, "Hank").Center;
            Assert.Equal(target.Url, view.HeadMention(point)?.Url); // set up: the point is on the head mention

            Press(window, block, point, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal([target.Url], heads);
            Assert.Equal(start, block.SelectionStart);
            Assert.Equal(end, block.SelectionEnd);
            Assert.Empty(opened);
        }
        finally
        {
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-146")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:458-461, :463-471 (B3)
    // :458-461 "Control-click reaches the menu path; a link opens the popover there too": menu(for:) presents the link
    // popover (:463-471 LinkPopover.shared.show) and returns no menu, so the click is not a link open. The port's
    // counterpart of a control-click is a Ctrl+left press (LinkTextView.OnPointerPressed).
    public void A_real_ctrl_left_click_on_a_link_presents_the_link_popover_and_opens_nothing() => headless.OnUiThread(() =>
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var paragraph = new LinkParagraphView("see [link](https://link-click.invalid/x) here");
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            var point = CharacterRect(block, "link").Center;
            Assert.True(view.HasLink(point)); // set up: the point is on the link

            Press(window, block, point, MouseButton.Left, RawInputModifiers.Control);

            var call = Assert.Single(recorder.Calls);
            Assert.True(call.Presented);
            Assert.Empty(opened);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-146")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:430-435, :453-456, :463-471 (B3)
    // :453-456 rightMouseDown presents the link popover and goes no further, "so the click never moves the selection"
    // (:432-434); nothing is opened.
    public void A_real_right_click_on_a_link_presents_the_link_popover_and_leaves_the_selection_alone() => headless.OnUiThread(() =>
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var paragraph = new LinkParagraphView("see [link](https://link-click.invalid/x) here");
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            block.SelectAll();
            Settle(window); // the layout and render pass a user's next click comes after
            var start = block.SelectionStart;
            var end = block.SelectionEnd;
            Assert.NotEqual(start, end); // set up: a selection the click must leave alone
            var point = CharacterRect(block, "link").Center;
            Assert.True(view.HasLink(point)); // set up: the point is on the link

            Press(window, block, point, MouseButton.Right, RawInputModifiers.None);

            var call = Assert.Single(recorder.Calls);
            Assert.True(call.Presented);
            Assert.Empty(opened);
            Assert.Equal(start, block.SelectionStart);
            Assert.Equal(end, block.SelectionEnd);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:313, :444-451 (B3)
    // :313 isSelectable = true, and :450 a press off a head mention goes to super.mouseDown, NSTextView's own text
    // behaviour: a drag across prose selects the characters it crosses and opens nothing.
    public void A_real_drag_across_prose_still_selects_it_and_opens_nothing() => headless.OnUiThread(() =>
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var heads = new List<SwiftURL>();
        var paragraph = new LinkParagraphView("first words and more", onOpenHead: heads.Add);
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            // One point into each character: the caret goes before it.
            var fromRect = CharacterRect(block, "words", into: 0);
            var toRect = CharacterRect(block, "more", into: 1);
            var from = block.TranslatePoint(new Point(fromRect.X + 1, fromRect.Center.Y), window)!.Value;
            var to = block.TranslatePoint(new Point(toRect.X + 1, toRect.Center.Y), window)!.Value;
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("words and m", block.SelectedText);
            Assert.Empty(opened);
            Assert.Empty(heads);
            Assert.Empty(recorder.Calls);
        }
        finally
        {
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:444-451, :90-100 (B3)
    // :450 a press on prose (no head mention under it) goes to super.mouseDown; with no link under the point there is
    // no clickedOnLink (:90-100), so nothing is opened, no head is opened and no popover is shown.
    public void A_real_click_on_plain_prose_opens_nothing() => headless.OnUiThread(() =>
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };
        var heads = new List<SwiftURL>();
        var paragraph = new LinkParagraphView("plain words then Hank and [link](https://link-click.invalid/x)",
            mentions: [persona], targets: targets, onOpenHead: heads.Add);
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            var point = CharacterRect(block, "plain").Center;
            Assert.False(view.HasLink(point)); // set up: prose, not a link
            Assert.Null(view.HeadMention(point)); // set up: prose, not a head mention

            Press(window, block, point, MouseButton.Left, RawInputModifiers.None);

            Assert.Empty(opened);
            Assert.Empty(heads);
            Assert.Empty(recorder.Calls);
        }
        finally
        {
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    });

    // ---- fix/links round 2: which button opens a head, and a right-click outside the selection ----
    // The verifier found that a right or middle press on a head mention called onOpenHead, and that a right-click on a
    // link still moved a selection that did not hold the link (the earlier right-click test clicked inside SelectAll).

    // Presses `button` with `modifiers` on the head mention in "Ping Hank now" (no link in the paragraph), with the
    // opener seams stubbed and the popover presenter recorded. Returns what onOpenHead received, what was opened, how
    // many popover calls were recorded, and the head mention's own URL.
    private static (List<SwiftURL> Heads, List<string> Opened, int Popovers, SwiftURL Target) PressOnHeadMention(
        MouseButton button, RawInputModifiers modifiers)
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF0000);
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };
        var heads = new List<SwiftURL>();
        var paragraph = new LinkParagraphView("Ping Hank now", mentions: [persona], targets: targets, onOpenHead: heads.Add);
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            var point = CharacterRect(block, "Hank").Center;
            Assert.Equal(target.Url, view.HeadMention(point)?.Url); // set up: the point is on the head mention
            Assert.False(view.HasLink(point)); // set up: not a link, so no press here presents the link popover

            Press(window, block, point, button, modifiers);

            return (heads, opened, recorder.Calls.Count, target.Url);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:444-456, :463-471 (B3)
    // Only mouseDown (:444-451, the left button) looks for a head mention and calls onOpenHead. rightMouseDown
    // (:453-456) asks presentLinkPopover (:463-471), which finds no link here and returns false, and then goes to
    // super.rightMouseDown. Neither path calls onOpenHead.
    public void A_real_right_press_on_a_head_mention_does_not_open_the_head() => headless.OnUiThread(() =>
    {
        var (heads, opened, popovers, _) = PressOnHeadMention(MouseButton.Right, RawInputModifiers.None);

        Assert.Empty(heads);
        Assert.Empty(opened);
        Assert.Equal(0, popovers);
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:275-550, :444-451 (B3)
    // LinkTextView (:275-550) overrides mouseDown (:444-451) and rightMouseDown (:453-456) but not otherMouseDown, so
    // a middle press goes to NSTextView's own handling and never reaches the head check. onOpenHead is not called.
    public void A_real_middle_press_on_a_head_mention_does_not_open_the_head() => headless.OnUiThread(() =>
    {
        var (heads, opened, popovers, _) = PressOnHeadMention(MouseButton.Middle, RawInputModifiers.None);

        Assert.Empty(heads);
        Assert.Empty(opened);
        Assert.Equal(0, popovers);
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:444-451 (B3); App/Views/Chat/MessagePopoverMenu.swift:63-65, :73-79 (B3)
    // The source treats a control-click as a .leftMouseDown carrying the control flag and handles it in
    // mouseDown(with:) (MessagePopoverMenu.swift:63-65, :73-79). LinkTextView.mouseDown checks for a head mention
    // before anything else (:446-449), so a control-click on a head opens it. Guards the round 2 fix: the head branch
    // is limited to a left press, and a Ctrl+left press is still a left press.
    public void A_real_ctrl_left_click_on_a_head_mention_still_opens_the_head() => headless.OnUiThread(() =>
    {
        var (heads, opened, popovers, target) = PressOnHeadMention(MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal([target], heads);
        Assert.Empty(opened);
        Assert.Equal(0, popovers);
    });

    [Fact]
    [Trait("Req", "PR-THEME-146")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:430-435, :453-456, :463-471 (B3)
    // :453-456 rightMouseDown presents the link popover (:463-471) and returns without super.rightMouseDown, "so the
    // click never moves the selection" (:432-434). Here the selection is prose after the link and does not hold the
    // click point, so a click that moved the selection to the point would show.
    public void A_real_right_click_on_a_link_outside_the_selection_leaves_the_selection_alone() => headless.OnUiThread(() =>
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var paragraph = new LinkParagraphView("see [link](https://link-click.invalid/x) here and more words");
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            var start = block.Inlines!.Text!.IndexOf("more", StringComparison.Ordinal);
            var end = start + "more".Length;
            block.SelectionStart = start;
            block.SelectionEnd = end;
            Settle(window); // the layout and render pass a user's next click comes after
            Assert.Equal("more", block.SelectedText); // set up: a selection that does not hold the link
            var point = CharacterRect(block, "link").Center;
            Assert.True(view.HasLink(point)); // set up: the point is on the link

            Press(window, block, point, MouseButton.Right, RawInputModifiers.None);

            var call = Assert.Single(recorder.Calls);
            Assert.True(call.Presented);
            Assert.Empty(opened);
            Assert.Equal(start, block.SelectionStart);
            Assert.Equal(end, block.SelectionEnd);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    });

    // ---- fix/links round 3: a press LinkTextView handles must not leave the text block tracking a drag ----
    // The round 2 verifier found that a handled press (a head, a link, a Ctrl+left popover) left the mouse device's
    // implicit capture on the SelectableTextBlock. Avalonia 12.1.2's SelectableTextBlock.OnPointerMoved extends the
    // selection whenever it holds the capture with the left button down, from a SelectionStart its own press handler
    // never set. A real mouse or trackpad nearly always moves a pixel between down and up, so these clicks move 1 px,
    // with the button held, before the release. ENGINEERING DECISION: a 1 px move is still a click, so its outcome must
    // be the still click's; AppKit's own drag threshold is not in the pinned source, and none is modelled here.

    // Builds a fresh paragraph through `make` (given the list onOpenHead fills), selects "more", then clicks `button`
    // with `modifiers` on the centre of the text `on`, moving `moveBy` px right between the press and the release.
    private static (List<string> Opened, int Popovers, List<SwiftURL> Heads, int SelectionStart, int SelectionEnd, int MoreStart)
        ClickWithMoreSelected(
        Func<List<SwiftURL>, LinkParagraphView> make, string on, MouseButton button, RawInputModifiers modifiers, double moveBy)
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var heads = new List<SwiftURL>();
        var paragraph = make(heads);
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        var window = ShowForPointer(paragraph);
        try
        {
            var start = block.Inlines!.Text!.IndexOf("more", StringComparison.Ordinal);
            block.SelectionStart = start;
            block.SelectionEnd = start + "more".Length;
            Settle(window); // the layout and render pass a user's next click comes after
            Assert.Equal("more", block.SelectedText); // set up: a selection that does not hold the click point

            var press = block.TranslatePoint(CharacterRect(block, on).Center, window)!.Value;
            var release = press + new Vector(moveBy, 0);
            var held = modifiers | button switch
            {
                MouseButton.Left => RawInputModifiers.LeftMouseButton,
                MouseButton.Right => RawInputModifiers.RightMouseButton,
                _ => RawInputModifiers.MiddleMouseButton,
            };
            window.MouseDown(press, button, modifiers);
            if (moveBy != 0) window.MouseMove(release, held);
            window.MouseUp(release, button, modifiers);
            Dispatcher.UIThread.RunJobs();

            return (opened, recorder.Calls.Count, heads, block.SelectionStart, block.SelectionEnd, start);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }

    private static LinkParagraphView LinkParagraph(List<SwiftURL> heads) =>
        new("see [link](https://link-click.invalid/x) here and more words", onOpenHead: heads.Add);

    private static readonly HydraMentionTarget JitterHead =
        new() { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };

    private static LinkParagraphView HeadParagraph(List<SwiftURL> heads) =>
        new("Ping Hank now and more words",
            mentions: [new HydraPersona("Hank", "dragon-test-asset", 0xFF0000)],
            targets: new Dictionary<string, HydraMentionTarget> { ["Hank"] = JitterHead },
            onOpenHead: heads.Add);

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:444-451 (B3)
    // :446-449 mouseDown calls onOpenHead and returns without super.mouseDown, so NSTextView never tracks the press and
    // the selection cannot move, however the pointer moves before the release.
    public void A_head_click_that_moves_a_pixel_opens_the_head_once_and_leaves_the_selection_alone() => headless.OnUiThread(() =>
    {
        var click = ClickWithMoreSelected(HeadParagraph, "Hank", MouseButton.Left, RawInputModifiers.None, moveBy: 1);

        Assert.Equal([JitterHead.Url], click.Heads);
        Assert.Empty(click.Opened);
        Assert.Equal(0, click.Popovers);
        Assert.Equal(click.MoreStart, click.SelectionStart);
        Assert.Equal(click.MoreStart + "more".Length, click.SelectionEnd);
    });

    [Fact]
    [Trait("Req", "PR-THEME-145")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:444-451, :90-100 (B3)
    // :450 a press off a head mention goes to NSTextView's own link click, which opens through clickedOnLink (:90-100).
    // A 1 px move is still that click: the link opens once and the selection ends where the still click leaves it.
    public void A_link_click_that_moves_a_pixel_opens_the_link_once_like_a_still_click() => headless.OnUiThread(() =>
    {
        var still = ClickWithMoreSelected(LinkParagraph, "link", MouseButton.Left, RawInputModifiers.None, moveBy: 0);
        var moved = ClickWithMoreSelected(LinkParagraph, "link", MouseButton.Left, RawInputModifiers.None, moveBy: 1);

        Assert.Equal(["https://link-click.invalid/x"], moved.Opened);
        Assert.Equal(still.Opened, moved.Opened);
        Assert.Equal(0, moved.Popovers);
        Assert.Equal((still.SelectionStart, still.SelectionEnd), (moved.SelectionStart, moved.SelectionEnd));
    });

    [Fact]
    [Trait("Req", "PR-THEME-146")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:458-461, :463-471 (B3)
    // :458-461 a control-click on a link presents the link popover (:463-471) and opens nothing. A 1 px move is still
    // that click: one popover, nothing opened, and the selection ends where the still click leaves it.
    public void A_ctrl_left_click_on_a_link_that_moves_a_pixel_presents_the_popover_like_a_still_click() => headless.OnUiThread(() =>
    {
        var still = ClickWithMoreSelected(LinkParagraph, "link", MouseButton.Left, RawInputModifiers.Control, moveBy: 0);
        var moved = ClickWithMoreSelected(LinkParagraph, "link", MouseButton.Left, RawInputModifiers.Control, moveBy: 1);

        Assert.Equal(1, moved.Popovers);
        Assert.Empty(moved.Opened);
        Assert.Equal((still.SelectionStart, still.SelectionEnd), (moved.SelectionStart, moved.SelectionEnd));
    });

    [Fact]
    [Trait("Req", "PR-THEME-146")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:430-435, :453-456 (B3)
    // :453-456 rightMouseDown presents the popover and returns without super.rightMouseDown "so the click never moves
    // the selection" (:432-434), also when the pointer moves a pixel before the release. Guards the round 3 fix: the
    // released capture must not let the right release reach SelectableTextBlock's selection handling.
    public void A_right_click_on_a_link_that_moves_a_pixel_leaves_the_selection_alone() => headless.OnUiThread(() =>
    {
        var click = ClickWithMoreSelected(LinkParagraph, "link", MouseButton.Right, RawInputModifiers.None, moveBy: 1);

        Assert.Equal(1, click.Popovers);
        Assert.Empty(click.Opened);
        Assert.Equal(click.MoreStart, click.SelectionStart);
        Assert.Equal(click.MoreStart + "more".Length, click.SelectionEnd);
    });

    // ---- Review round 4 of the TimelineRows parity pass (2026-10-03): the paragraph path's own text block ----

    // :312-313 `isEditable = false; isSelectable = true`: an NSTextView draws a selection in the system's selected-text colour.
    // The paragraph draws into its own SelectableTextBlock (LinkTextView's constructor), which has no SelectionBrush of its own
    // with no control theme loaded (Avalonia 12.1.2), so a drag selected text that was never painted (a sent bubble with a bare
    // domain, a report paragraph with a link). It takes the attach-time brush the veiled text takes (StreamVeil.cs
    // VeiledText.SelectableText; ED-256 (5)).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-THEME-140 (selectable: a selection is drawn): DroppyCode/UI/Markdown/LinkParagraphView.swift:312-313
    public void A_selection_in_a_link_paragraph_is_drawn_in_the_system_selected_text_colour() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("words before [a link](https://selection-brush.invalid/x) and after");
        var window = new Window { Content = paragraph, Width = 420, Height = 200 };
        window.Show();
        try
        {
            var block = (SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;
            var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
            Assert.NotEqual(0, system.A);
            block.SelectAll();
            Assert.False(string.IsNullOrEmpty(block.SelectedText));
            Assert.True((block.SelectionBrush as ISolidColorBrush)?.Color == system,
                $"the selection is drawn with {block.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}");
        }
        finally { window.Close(); }
    });

    // :36-58 updateNSView: `revision` (:15-16, "Bumped when favicons finish loading, so the icons appear") is read (:40-41) and
    // compared with the coordinator's last one (:45); a new revision builds the string again, so an icon that reached the cache
    // after the paragraph was drawn now stands in front of its link with one space (:134-150), and the same revision again builds
    // nothing (:43-46). The host hands the new revision in from InlineText's @State faviconRevision (MarkdownView.swift, B3: :1077,
    // :1114), which fetchLinkAssets bumps once an icon lands (:1163).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-THEME-141 and PR-THEME-148 (the paragraph is rebuilt with the icon once the fetch lands): DroppyCode/UI/Markdown/LinkParagraphView.swift:36-58, :134-150
    public void A_new_revision_draws_the_paragraph_again_with_an_icon_that_arrived_since_and_the_same_revision_draws_nothing() => headless.OnUiThread(() =>
    {
        const string host = "revision-redraw.com";
        var icon = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        var paragraph = new LinkParagraphView("see [the page](https://revision-redraw.com/a) now");
        var block = (SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;
        InlineUIContainer[] Icons() => block.Inlines!.OfType<InlineUIContainer>().ToArray();
        Assert.Empty(Icons()); // drawn before the icon arrived
        FaviconCacheSeed.Seed(host, icon);
        try
        {
            paragraph.Update(revision: 0); // nothing new: the coordinator's inputs all match, so nothing is built
            Assert.Empty(Icons());
            paragraph.Update(revision: 1); // the favicon-load bump
            var container = Assert.Single(Icons());
            Assert.Same(icon, Assert.IsType<Image>(container.Child).Source);
            var inlines = block.Inlines!.ToList();
            var at = inlines.IndexOf(container);
            Assert.Equal(" ", Assert.IsType<Run>(inlines[at + 1]).Text);
            Assert.Equal("the page", Assert.IsType<Run>(inlines[at + 2]).Text);
            paragraph.Update(revision: 1); // the same revision: not built again
            Assert.Same(container, Assert.Single(Icons()));
        }
        finally { FaviconCacheSeed.Remove(host); }
    });

    // ---- Review round 5 of the TimelineRows parity pass (2026-10-03): the cached measurement ----

    // :490-494 render(_:): a new string drops the cached measurement (lastMeasuredHeight = 0; measuredWidth = 0), so height(forWidth:)
    // (:498-507) measures the new text instead of handing back the old text's height for the same width (:500). A favicon-load bump
    // draws the string again with the icon and a space in front of the link (:134-150), which can push the paragraph onto a second
    // line at a width the text alone fitted.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-THEME-152 (height(forWidth:) measures the text on screen): DroppyCode/UI/Markdown/LinkParagraphView.swift:490-494, :498-507
    public void A_string_drawn_again_drops_the_cached_height_so_an_icon_that_wraps_the_line_is_measured() => headless.OnUiThread(() =>
    {
        const string host = "measure-reset.com";
        var icon = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        var paragraph = new LinkParagraphView("see [the page](https://measure-reset.com/a) now");
        var view = (LinkTextView)paragraph.Child!;
        var block = (SelectableTextBlock)view.Child!;
        block.Measure(Size.Infinity);
        var width = Math.Ceiling(block.DesiredSize.Width) + 1; // the text alone fits on one line, with a point to spare
        var oneLine = view.HeightForWidth(width);
        Assert.True(oneLine > 0);
        Assert.Equal(oneLine, view.HeightForWidth(width)); // the same width again: the cached height (:500)
        FaviconCacheSeed.Seed(host, icon);
        try
        {
            paragraph.Update(revision: 1); // the icon and a space now stand in front of the link: wider than the width
            Assert.Single(block.Inlines!.OfType<InlineUIContainer>());
            block.Measure(Size.Infinity);
            Assert.True(block.DesiredSize.Width > width, "the icon did not widen the line past the width");
            Assert.True(view.HeightForWidth(width) > oneLine,
                $"the paragraph's height at {width} points is still the one-line height {oneLine} of the text drawn before the icon");
        }
        finally { FaviconCacheSeed.Remove(host); }
    });
}
