using System;
using System.IO;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.UI.Tests.Port;

// Source under test: DroppyCode/UI/Markdown/LinkParagraphView.swift (B3 52bc792) via PA-0020/ED-203/ED-204. The
// two generic Windows openers (this file's OpenWithDefaultApp and Marrow.Services.Port.Workspace.ShellOpen) hand
// any string straight to ShellExecuteW; ED-203 hardens both, and ED-204 adds the reveal-vs-open decision
// MarkdownLinkOpener uses for a resolved local-file path (OD-0026).
public sealed class ShellWorkspaceTests
{
    // ---- ED-203: refuse before calling ShellExecuteW ----

    [Fact]
    [Trait("Decision", "ED-203")]
    public void A_bare_relative_string_with_no_scheme_is_refused()
    {
        Assert.False(ShellWorkspace.ShouldAttemptShellExecute("calc"));
    }

    [Fact]
    [Trait("Decision", "ED-203")]
    public void A_scheme_less_two_slash_host_string_is_refused()
    {
        Assert.False(ShellWorkspace.ShouldAttemptShellExecute("//host/share"));
    }

    [Fact]
    [Trait("Decision", "ED-203")] // this changes nothing on the Mac's own inputs
    public void An_absolute_windows_path_is_accepted_because_its_drive_letter_is_a_scheme()
    {
        Assert.True(ShellWorkspace.ShouldAttemptShellExecute(@"C:\Users\Bob\Downloads\file.pdf"));
    }

    [Fact]
    [Trait("Decision", "ED-203")]
    public void An_absolute_url_with_a_scheme_is_accepted()
    {
        Assert.True(ShellWorkspace.ShouldAttemptShellExecute("https://example.com"));
    }

    [Fact]
    [Trait("Decision", "ED-203")]
    public void An_empty_string_is_refused()
    {
        Assert.False(ShellWorkspace.ShouldAttemptShellExecute(""));
    }

    [Fact]
    [Trait("Decision", "ED-203")] // the wired Action short-circuits before reaching ShellExecuteW
    public void OpenWithDefaultApp_does_not_throw_for_a_refused_string()
    {
        ShellWorkspace.OpenWithDefaultApp("calc");
    }

    // ---- review A29 findings 1, 4, 6: the gate's rooted-path and drive-letter cases ----

    [Fact]
    [Trait("Decision", "ED-203")] // finding 1: a drive-relative path resolves against whatever the process's
                                  // current directory on that drive happens to be, exactly the relative reference
                                  // NSWorkspace.shared.open refuses on the source's own platform
    public void A_drive_relative_string_is_refused()
    {
        Assert.False(ShellWorkspace.ShouldAttemptShellExecute("C:tool.exe"));
        Assert.False(ShellWorkspace.ShouldAttemptShellExecute("C:../tool.exe"));
    }

    [Fact]
    [Trait("Decision", "ED-203")] // finding 1: RFC 3986 parses the doubled slash as an authority, but this is the
                                  // same absolute drive-letter path MarkdownLinkOpener.LocalFilePath reconstructs
    public void A_doubled_slash_drive_letter_string_is_accepted()
    {
        Assert.True(ShellWorkspace.ShouldAttemptShellExecute("C://Windows/System32/calc.exe"));
    }

    [Fact]
    [Trait("Decision", "ED-203")] // finding 4: MarkdownLinkOpener's own local-file branch hands this seam a bare
                                  // "/"-rooted path (the source's own POSIX shape) with no scheme at all; refusing
                                  // it here as "relative" broke opening such a link on Windows
    public void A_rooted_scheme_less_path_is_accepted()
    {
        Assert.True(ShellWorkspace.ShouldAttemptShellExecute("/abs/a.swift:12"));
    }

    [Fact]
    [Trait("Decision", "ED-203")] // finding 6: a UNC path from a trusted caller (AttachmentPreview, DownloadsPopover)
                                  // is not the credential-leak shape ED-203 exists to stop - MarkdownLinkOpener.IsUnc
                                  // already refuses a UNC target from an unreviewed chat link before it ever reaches
                                  // this seam, so a UNC string arriving here (e.g. a redirected Downloads folder) is
                                  // exactly what NSWorkspace.shared.open opens on the source's own platform
    public void A_unc_path_from_a_trusted_caller_is_accepted()
    {
        Assert.True(ShellWorkspace.ShouldAttemptShellExecute(@"\\server\share\Downloads"));
    }

    // ---- ED-204: extension floor list ----

    [Fact]
    [Trait("Decision", "ED-204")]
    public void An_extension_on_the_fixed_floor_is_dangerous_without_asking_the_platform()
    {
        Assert.True(ShellWorkspace.IsDangerousExtension(".exe"));
        Assert.True(ShellWorkspace.IsDangerousExtension(".EXE")); // case-insensitive
        Assert.True(ShellWorkspace.IsDangerousExtension(".ps1"));
        Assert.True(ShellWorkspace.IsDangerousExtension(".appref-ms"));
    }

    [Fact]
    [Trait("Decision", "ED-204")]
    public void No_extension_is_never_dangerous()
    {
        var previous = ShellWorkspace.IsDangerousFileType;
        try
        {
            ShellWorkspace.IsDangerousFileType = _ => throw new InvalidOperationException("must not be asked");
            Assert.False(ShellWorkspace.IsDangerousExtension(""));
        }
        finally
        {
            ShellWorkspace.IsDangerousFileType = previous;
        }
    }

    [Fact]
    [Trait("Decision", "ED-204")] // an extension off the floor falls through to AssocIsDangerous
    public void An_extension_off_the_floor_asks_the_platform_seam()
    {
        var previous = ShellWorkspace.IsDangerousFileType;
        try
        {
            var asked = "";
            ShellWorkspace.IsDangerousFileType = extension => { asked = extension; return true; };
            Assert.True(ShellWorkspace.IsDangerousExtension(".txt"));
            Assert.Equal(".txt", asked);
        }
        finally
        {
            ShellWorkspace.IsDangerousFileType = previous;
        }
    }

    [Fact]
    [Trait("Decision", "ED-204")] // pins the API binding: WINDOWS-ONLY (shlwapi.dll)
    public void AssocIsDangerous_agrees_with_the_floor_on_exe_and_disagrees_on_txt()
    {
        Assert.True(ShellWorkspace.IsDangerousFileType(".exe"));
        Assert.False(ShellWorkspace.IsDangerousFileType(".txt"));
    }

    // ---- ED-204: extension extraction ----

    [Theory]
    [Trait("Decision", "ED-204")]
    [InlineData(@"C:\Users\Bob\file.exe", ".exe")]
    [InlineData(@"C:\Users\Bob\report.v2.exe", ".exe")] // the last dot wins
    [InlineData(@"C:\Users\Bob\noext", "")]
    [InlineData(@"C:\Users\Bob.Q\noext", "")] // a dot in a directory segment does not count
    [InlineData("/tmp/x/report.txt", ".txt")]
    [InlineData("", "")]
    public void The_extension_is_the_last_dot_led_segment_after_the_last_separator(string path, string expected)
    {
        Assert.Equal(expected, ShellWorkspace.ExtensionOf(path));
    }

    // ---- ED-204: reveal vs. open ----

    [Fact]
    [Trait("Decision", "ED-204")]
    public void A_directory_is_never_revealed_even_with_a_dangerous_looking_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "marrow-shellworkspace-" + Guid.NewGuid(), "evil.exe");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(ShellWorkspace.ShouldReveal(dir));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    [Trait("Decision", "ED-204")]
    public void A_nonexistent_path_with_a_floor_extension_is_revealed()
    {
        var path = Path.Combine(Path.GetTempPath(), "marrow-shellworkspace-" + Guid.NewGuid() + ".exe");
        Assert.True(ShellWorkspace.ShouldReveal(path));
    }

    [Fact]
    [Trait("Decision", "ED-204")]
    public void A_path_with_a_safe_extension_is_not_revealed()
    {
        var previous = ShellWorkspace.IsDangerousFileType;
        try
        {
            ShellWorkspace.IsDangerousFileType = _ => false;
            var path = Path.Combine(Path.GetTempPath(), "marrow-shellworkspace-" + Guid.NewGuid() + ".txt");
            Assert.False(ShellWorkspace.ShouldReveal(path));
        }
        finally
        {
            ShellWorkspace.IsDangerousFileType = previous;
        }
    }

    // ---- review A29 finding 3: the extension must be read off the name Windows will actually resolve to ----

    [Theory]
    [Trait("Decision", "ED-204")]
    [InlineData("tool.exe", "tool.exe")] // unchanged: nothing to canonicalize
    [InlineData("tool.exe ", "tool.exe")] // CreateFileW drops a trailing space
    [InlineData("tool.exe.", "tool.exe")] // and a trailing dot
    [InlineData("tool.exe  .. ", "tool.exe")] // repeated, in either order
    [InlineData("tool.exe::$DATA", "tool.exe")] // the unnamed data stream of the same file
    [InlineData("tool.exe::$data", "tool.exe")] // case-insensitive
    public void CanonicalFileName_resolves_to_the_name_Windows_would_actually_open(string literal, string expected)
    {
        Assert.Equal(expected, ShellWorkspace.CanonicalFileName(literal));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // a trailing "%20" (decoded to a space) must not hide a dangerous extension:
                                  // ExtensionOf(".exe path with a trailing space") is ".exe ", which matches
                                  // neither the floor list nor, presumably, the real AssocIsDangerous
    public void A_trailing_space_does_not_hide_a_dangerous_extension()
    {
        var path = Path.Combine(Path.GetTempPath(), "marrow-shellworkspace-" + Guid.NewGuid() + ".exe");
        Assert.True(ShellWorkspace.ShouldReveal(path + " "));
    }

    [Fact]
    [Trait("Decision", "ED-204")] // an NTFS alternate-data-stream suffix addresses the same underlying file
    public void An_alternate_data_stream_suffix_does_not_hide_a_dangerous_extension()
    {
        var path = Path.Combine(Path.GetTempPath(), "marrow-shellworkspace-" + Guid.NewGuid() + ".exe");
        Assert.True(ShellWorkspace.ShouldReveal(path + "::$DATA"));
    }
}
