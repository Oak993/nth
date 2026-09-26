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
}
