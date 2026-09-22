using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// F.PathIdentity (IMPLEMENTING.md 3.13 R-PATH-1 and 4.3): NSString and URL path semantics with both Windows
// separators, one absoluteness predicate and one normalization (ED-019, ED-026, ED-036, ED-038, ED-039, ED-045).
// Expected values come from the Foundation behaviour the pinned Swift relies on (call sites cited per test) and
// from the literal text of the decisions; none is taken from running the port.
//
// Proposed port-only surface these tests fix (static members of SwiftPath):
//   LastPathComponent(path), DeletingLastPathComponent(path), AppendingPathComponent(path, component),
//   PathExtension(path), StandardizingPath(path), ExpandingTildeInPath(path),
//   StandardizedFileURLPath(path, relativeTo) = URL(fileURLWithPath:relativeTo:).standardizedFileURL.path,
//   IsAbsolute(path), Normalize(path), TailUnder(path, root), SamePath(a, b).
// Where no decision fixes which separator a result carries, the test compares through Normalize, so either
// separator passes and only the Swift semantics are asserted.
public sealed class SwiftPathTests
{
    private static string Home => Environment.GetEnvironmentVariable("USERPROFILE")!;

    // ---- lastPathComponent (kept from the adopted prototype) -------------------------------------------------

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData("a/b/", "b")]
    [InlineData(@"a\b\", "b")]
    [InlineData("/tmp/scratch.tiff", "scratch.tiff")]
    [InlineData("/tmp/", "tmp")]
    [InlineData("scratch///", "scratch")]
    [InlineData("/", "/")]
    [InlineData(@"\", @"\")]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData(@"C:\dir\file.txt", "file.txt")]
    [InlineData(@"C:\dir/sub\file.txt", "file.txt")]
    public void LastPathComponent_follows_NSString_with_both_separators(string path, string expected)
    {
        // NSString.lastPathComponent: "/tmp/" is "tmp", "scratch///" is "scratch", "/" is "/", "" is "".
        // Callers: Storage.swift:188, AppUpdater.swift:313, CodingSamples (Attachment name, Storage.swift:86).
        Assert.Equal(expected, SwiftPath.LastPathComponent(path));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void Path_GetFileName_drops_the_last_component_that_Foundation_keeps()
    {
        Assert.Equal("", System.IO.Path.GetFileName("a/b/")); // the pitfall R-PATH-1 names
        Assert.Equal("b", SwiftPath.LastPathComponent("a/b/"));
    }

    // ---- deletingLastPathComponent ----------------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData("/tmp/scratch.tiff", "/tmp")]
    [InlineData("/tmp/lock/", "/tmp")]
    [InlineData("/tmp/", "/")]
    [InlineData("/tmp", "/")]
    [InlineData("/", "/")]
    [InlineData("scratch.tiff", "")]
    [InlineData("", "")]
    [InlineData(@"\tmp\scratch.tiff", @"\tmp")]
    [InlineData(@"\tmp\lock\", @"\tmp")]
    [InlineData(@"\tmp", @"\")]
    [InlineData(@"\", @"\")]
    [InlineData(@"a\b\", "a")]
    [InlineData(@"a/b", "a")]
    public void DeletingLastPathComponent_follows_NSString_with_both_separators(string path, string expected)
    {
        // NSString.deletingLastPathComponent: "/tmp/lock/" gives "/tmp", "/tmp" gives "/", "/" stays "/",
        // "scratch.tiff" gives "". Callers: DiffParser.swift:24, Git.swift:467, AppModel+Hydra.swift:338.
        Assert.Equal(expected, SwiftPath.DeletingLastPathComponent(path));
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-036")]
    [InlineData(@"C:\Users\a", @"C:\Users")]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\Users\", @"C:\")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("C:/Users/a", "C:/Users")]
    [InlineData("C:/Users", "C:/")]
    [InlineData("C:/", "C:/")]
    [InlineData(@"\\server\share\dir\file", @"\\server\share\dir")]
    [InlineData(@"\\server\share\dir", @"\\server\share")]
    [InlineData(@"\\server\share", @"\\server\share")]
    public void DeletingLastPathComponent_stops_at_the_drive_root_and_the_share_root(string path, string expected)
    {
        // ED-036: the upward walk (AppModel+Hydra.swift:332-340, `if dir == "/"`) ends where
        // deletingLastPathComponent stops shortening the path, "at C:\ or \\server\share".
        Assert.Equal(expected, SwiftPath.DeletingLastPathComponent(path));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    public void Upward_walk_from_a_drive_path_reaches_the_drive_root_and_stays_there()
    {
        // AppModel+Hydra.swift:333-340 walks up with deletingLastPathComponent; ED-036 replaces `dir == "/"`
        // with "the path stops shortening".
        var seen = Walk(@"C:\a\b\c");
        Assert.Equal(new[] { @"C:\a\b\c", @"C:\a\b", @"C:\a", @"C:\" }, seen);
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    public void Upward_walk_from_a_UNC_path_stops_at_the_share_and_never_reaches_the_server()
    {
        var seen = Walk(@"\\server\share\a\b");
        Assert.Equal(new[] { @"\\server\share\a\b", @"\\server\share\a", @"\\server\share" }, seen);
    }

    [Theory]
    [Trait("Decision", "ED-036")]
    [InlineData(@"\\?\C:\a\b")]
    [InlineData(@"C:/a/b/c/")]
    [InlineData(@"\\server\share\x\")]
    public void Upward_walk_always_reaches_a_non_empty_fixed_point(string start)
    {
        // The walk has no `dir == "/"` exit on Windows (ED-036), so it must end on its own: every step either
        // shortens the path or returns it unchanged, and the fixed point is never "" (the source's
        // `if dir.isEmpty { dir = "/" }` guard at AppModel+Hydra.swift:339 has no drive-root analogue).
        var dir = start;
        for (var step = 0; step < 32; step++)
        {
            var parent = SwiftPath.DeletingLastPathComponent(dir);
            if (parent == dir)
            {
                Assert.NotEqual("", dir);
                return;
            }
            Assert.True(parent.Length < dir.Length, $"'{dir}' -> '{parent}' did not shorten");
            dir = parent;
        }
        Assert.Fail($"no fixed point within 32 steps from '{start}'");
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void Path_GetDirectoryName_differs_from_Foundation_at_roots_and_trailing_separators()
    {
        Assert.Null(System.IO.Path.GetDirectoryName(@"C:\"));             // Foundation keeps the root
        Assert.Equal(@"C:\a", System.IO.Path.GetDirectoryName(@"C:\a\")); // Foundation gives C:\
        Assert.Equal(@"C:\", SwiftPath.DeletingLastPathComponent(@"C:\"));
        Assert.Equal(@"C:\", SwiftPath.DeletingLastPathComponent(@"C:\a\"));
    }

    // ---- appendingPathComponent -------------------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData(@"C:\tmp", "scratch.tiff", "C:/tmp/scratch.tiff")]
    [InlineData(@"C:\tmp\", "scratch.tiff", "C:/tmp/scratch.tiff")]
    [InlineData("C:/tmp/", "scratch.tiff", "C:/tmp/scratch.tiff")]
    [InlineData(@"C:\", "scratch.tiff", "C:/scratch.tiff")]
    [InlineData(@"C:\r", @"sub\a.txt", "C:/r/sub/a.txt")]
    [InlineData(@"C:\r", "sub/a.txt", "C:/r/sub/a.txt")]
    [InlineData(@"\\srv\share", "a.txt", "//srv/share/a.txt")]
    [InlineData("a", "b", "a/b")]
    [InlineData(@"a\", "b", "a/b")]
    public void AppendingPathComponent_joins_with_exactly_one_separator(string path, string component, string expected)
    {
        // NSString.appendingPathComponent: "/tmp" and "/tmp/" plus "scratch.tiff" both give "/tmp/scratch.tiff",
        // "/" gives "/scratch.tiff". Callers: TouchedPaths.swift:24, AppModel+HydraMerge.swift:496, :499, :540,
        // ThreadRuntime.swift:3376. Which separator is inserted is not fixed by any decision: compared normalized.
        Assert.Equal(expected, SwiftPath.Normalize(SwiftPath.AppendingPathComponent(path, component)));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void AppendingPathComponent_onto_an_empty_path_is_the_component_itself()
    {
        // NSString: ("" as NSString).appendingPathComponent("scratch.tiff") is "scratch.tiff".
        Assert.Equal("scratch.tiff", SwiftPath.AppendingPathComponent("", "scratch.tiff"));
    }

    [Theory]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-038")]
    [Trait("Decision", "ED-045")]
    [InlineData(@"C:\r", @"\a.txt", "C:/r/a.txt")]
    [InlineData(@"C:\r", "/a.txt", "C:/r/a.txt")]
    [InlineData(@"C:\r\", @"\sub\a.txt", "C:/r/sub/a.txt")]
    public void A_leading_bare_separator_is_joined_onto_the_root(string root, string component, string expected)
    {
        // ED-036/038/045: "A bare leading \ is NOT treated as absolute; it is joined onto the root". NSString
        // itself joins "/a" onto "/tmp" as "/tmp/a" (appendingPathComponent never treats a component as rooted).
        Assert.Equal(expected, SwiftPath.Normalize(SwiftPath.AppendingPathComponent(root, component)));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-038")]
    public void Path_Combine_lets_a_bare_backslash_escape_the_root()
    {
        Assert.Equal(@"\a.txt", System.IO.Path.Combine(@"C:\r", @"\a.txt")); // the pitfall: the root is dropped
        Assert.Equal("C:/r/a.txt", SwiftPath.Normalize(SwiftPath.AppendingPathComponent(@"C:\r", @"\a.txt")));
    }

    // ---- pathExtension ----------------------------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData("/tmp/scratch.tiff", "tiff")]
    [InlineData(".scratch.tiff", "tiff")]
    [InlineData("/tmp/scratch", "")]
    [InlineData("/tmp/", "")]
    [InlineData("/tmp/scratch..tiff", "tiff")]
    [InlineData(@"C:\dir\photo.PNG", "PNG")]
    [InlineData(@"C:\dir\archive.tar.gz", "gz")]
    [InlineData(@"C:\dir.d\noext", "")]
    [InlineData("C:/dir.d/noext", "")]
    [InlineData(@"C:\dir\.bashrc", "")]
    [InlineData(@"C:\dir\", "")]
    [InlineData(@"C:\dir\file.txt\", "txt")]
    [InlineData("name.", "")]
    [InlineData("", "")]
    public void PathExtension_is_read_from_the_last_component_only(string path, string expected)
    {
        // NSString.pathExtension: "/tmp/scratch.tiff" "tiff", ".scratch.tiff" "tiff", "/tmp/scratch" "",
        // "/tmp/" "", "/tmp/scratch..tiff" "tiff"; a leading dot starts no extension. Case is kept: callers
        // lowercase themselves (Storage.swift:99, AttachmentPreview.swift:15, :130, DownloadsPopover.swift:70).
        // A dot in a parent folder behind a backslash must not count.
        Assert.Equal(expected, SwiftPath.PathExtension(path));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void Path_GetExtension_treats_a_dotfile_name_as_an_extension()
    {
        Assert.Equal(".bashrc", System.IO.Path.GetExtension(@"C:\dir\.bashrc")); // the pitfall
        Assert.Equal(".PNG", System.IO.Path.GetExtension("photo.PNG"));           // and it keeps the dot
        Assert.Equal("", SwiftPath.PathExtension(@"C:\dir\.bashrc"));
        Assert.Equal("PNG", SwiftPath.PathExtension("photo.PNG"));
    }

    // ---- absoluteness (one predicate) -------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-038")]
    [Trait("Decision", "ED-045")]
    [InlineData(@"C:\x", true)]
    [InlineData("C:/x", true)]
    [InlineData(@"c:\x", true)]
    [InlineData("z:/", true)]
    [InlineData(@"C:\", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server\share\dir\f.txt", true)]
    [InlineData(@"\\server/share", true)]
    [InlineData(@"\\?\C:\x", true)]
    [InlineData(@"\\?\", true)]
    [InlineData(@"\x", false)]
    [InlineData(@"\", false)]
    [InlineData("/x", false)]
    [InlineData("/", false)]
    [InlineData("C:", false)]
    [InlineData("C:x", false)]
    [InlineData(@"1:\x", false)]
    [InlineData(@"CC:\x", false)]
    [InlineData(@" C:\x", false)]
    [InlineData(@"\\server", false)]
    [InlineData(@"\\server\", false)]
    [InlineData("x", false)]
    [InlineData(@"x\y", false)]
    [InlineData("", false)]
    public void IsAbsolute_is_drive_rooted_or_UNC_or_a_long_path_prefix(string path, bool expected)
    {
        // ED-036/038/045: absolute iff /^[A-Za-z]:[\\/]/, or /^\\\\[^\\/]+[\\/][^\\/]+/, or begins \\?\ ;
        // a bare leading "\" is not absolute; case-insensitive only in the drive letter. Replaces
        // `hasPrefix("/")` at TouchedPaths.swift:24, TurnDiff.swift:34, :41, AppModel+Hydra.swift:329,
        // AppModel+HydraMerge.swift:498, :537.
        Assert.Equal(expected, SwiftPath.IsAbsolute(path));
    }

    [Theory]
    [Trait("Decision", "ED-038")]
    [Trait("Decision", "ED-039")]
    [InlineData("~")]
    [InlineData("~/x")]
    [InlineData(@"~\x")]
    [InlineData("~name/x")]
    [InlineData("https://example.com/a")]
    public void Tilde_and_URL_forms_stay_separate_clauses_at_the_call_site(string path)
    {
        // The shared predicate is the three forms ED-045 names. "~" and "://" stay the call sites' own literal
        // clauses (AppModel+HydraMerge.swift:498, :537; TouchedPaths.swift:21-22 rejects "://" and expands "~"
        // before the test at :24), so "~name/x", which expansion leaves alone, is still joined onto the root
        // exactly as the source joins it.
        Assert.False(SwiftPath.IsAbsolute(path));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-036")]
    public void Path_IsPathRooted_calls_a_bare_backslash_rooted()
    {
        Assert.True(System.IO.Path.IsPathRooted(@"\x")); // the pitfall: it would escape the checkout
        Assert.False(SwiftPath.IsAbsolute(@"\x"));
    }

    // ---- normalization (one step) -----------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-045")]
    [Trait("Decision", "ED-039")]
    [InlineData(@"c:\Users\A\file.TXT", "C:/Users/A/file.TXT")]
    [InlineData("C:/a", "C:/a")]
    [InlineData(@"c:\", "C:/")]
    [InlineData(@"\\?\c:\a\b", "C:/a/b")]
    [InlineData(@"\\?\C:\", "C:/")]
    [InlineData(@"\\server\Share\X", "//server/Share/X")]
    [InlineData(@"a\b", "a/b")]
    [InlineData(@"\a", "/a")]
    [InlineData(@"C:\a\", "C:/a/")]
    [InlineData(@"C:\a\..\b", "C:/a/../b")]
    [InlineData("", "")]
    public void Normalize_is_forward_slashes_with_the_long_prefix_stripped_and_the_drive_letter_upper_cased(string path, string expected)
    {
        // ED-045: "one path-normalization step (backslashes to forward slashes, \\?\ stripped, drive letter
        // upper-cased) applied before any comparison". Nothing else: the rest of the case, trailing separators and
        // dot segments are left to the comparison and to standardization.
        Assert.Equal(expected, SwiftPath.Normalize(path));
    }

    // ---- standardizingPath and standardizedFileURL.path ---------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-045")]
    [InlineData(@"C:\a\.\b", "C:/a/b")]
    [InlineData(@"C:\a\\b", "C:/a/b")]
    [InlineData(@"C:\a\b\..\c", "C:/a/c")]
    [InlineData(@"C:\a\b\", "C:/a/b")]
    [InlineData("C:/a/b/", "C:/a/b")]
    [InlineData(@"C:\", "C:/")]
    [InlineData(@"\\srv\share\a\..\b", "//srv/share/b")]
    [InlineData(@"\\srv\share\a\\b\", "//srv/share/a/b")]
    [InlineData(@"a\..\b", "a/../b")]
    [InlineData(@"a\.\b\", "a/b")]
    [InlineData("", "")]
    public void StandardizingPath_reduces_dots_and_empty_components_and_drops_the_trailing_separator(string path, string expected)
    {
        // NSString.standardizingPath: "//" and "/./" become one separator, ".." is resolved in absolute paths only
        // (a relative path keeps it), a trailing separator is removed, the root keeps its own. The UNC lead-in
        // "\\" is not an empty component. Callers: TouchedPaths.swift:23, :25, AppModel+Hydra.swift:323, :332,
        // AppModel+HydraMerge.swift:191, :202.
        Assert.Equal(expected, SwiftPath.Normalize(SwiftPath.StandardizingPath(path)));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    public void StandardizingPath_expands_a_leading_tilde()
    {
        // NSString.standardizingPath begins by expanding an initial tilde (expandingTildeInPath).
        Assert.Equal(SwiftPath.Normalize(Home), SwiftPath.Normalize(SwiftPath.StandardizingPath("~")));
        Assert.Equal(SwiftPath.Normalize(Home) + "/b", SwiftPath.Normalize(SwiftPath.StandardizingPath(@"~\a\..\b")));
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-045")]
    [InlineData(@"src\a.txt", @"C:\Repo", "C:/Repo/src/a.txt")]
    [InlineData("src/a.txt", @"C:\Repo\", "C:/Repo/src/a.txt")]
    [InlineData(@".\a.txt", @"C:\Repo", "C:/Repo/a.txt")]
    [InlineData(@"..\other\a", @"C:\Repo\sub", "C:/Repo/other/a")]
    [InlineData(@"D:\x\y.txt", @"C:\Repo", "D:/x/y.txt")]
    [InlineData(@"\\?\D:\x", @"C:\Repo", "D:/x")]
    [InlineData(@"\\srv\share\d\.\f", @"C:\Repo", "//srv/share/d/f")]
    [InlineData(@"\a.txt", @"C:\Repo", "C:/Repo/a.txt")]
    [InlineData(@"C:\Repo\sub\..\a.txt", @"C:\Other", "C:/Repo/a.txt")]
    public void StandardizedFileURLPath_resolves_a_relative_path_against_the_directory(string path, string directory, string expected)
    {
        // URL(fileURLWithPath: path, relativeTo: directory).standardizedFileURL.path: a relative path is resolved
        // against the directory, an absolute one ignores it, "." and ".." are removed. The bare "\" form is not
        // absolute (ED-045) and so is resolved against the directory. Callers: TouchedPaths.swift:9,
        // TurnDiff.swift:8, ThreadRuntime.swift:3518.
        var result = SwiftPath.StandardizedFileURLPath(path, directory);
        Assert.Equal(expected, SwiftPath.Normalize(result));
        Assert.True(SwiftPath.IsAbsolute(result), $"'{result}' is not absolute");
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData(@"C:\Repo\sub\", "C:/Repo/sub")]
    [InlineData("C:/Repo/sub/", "C:/Repo/sub")]
    [InlineData(@"C:\Repo\.\sub\..\x", "C:/Repo/x")]
    [InlineData(@"C:\", "C:/")]
    [InlineData(@"\\srv\share\d\", "//srv/share/d")]
    public void StandardizedFileURLPath_has_no_trailing_separator_except_at_the_root(string path, string expected)
    {
        // URL.path never ends in a separator except for the root ("/" stays "/"); TouchedPaths.swift:10 and
        // NativeFileTools.swift:27 append the separator themselves. Callers: AppModel.swift:517,
        // NativeFileTools.swift:22, :26, TouchedPaths.swift:8.
        Assert.Equal(expected, SwiftPath.Normalize(SwiftPath.StandardizedFileURLPath(path, null)));
    }

    // ---- tilde (ED-036) ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-036")]
    public void ExpandingTildeInPath_turns_a_lone_tilde_into_USERPROFILE()
    {
        Assert.Equal(Home, SwiftPath.ExpandingTildeInPath("~"));
    }

    [Theory]
    [Trait("Decision", "ED-036")]
    [InlineData("~/a/b.txt")]
    [InlineData(@"~\a\b.txt")]
    public void ExpandingTildeInPath_expands_a_tilde_followed_by_either_separator(string path)
    {
        // ED-036: 'expand a leading "~" that is the whole string or is followed by \ or / to %USERPROFILE%'.
        // Callers: TouchedPaths.swift:22, AppModel+Hydra.swift:321, NativeFileTools.swift:24, Shell.swift:418.
        Assert.Equal(SwiftPath.Normalize(Home) + "/a/b.txt", SwiftPath.Normalize(SwiftPath.ExpandingTildeInPath(path)));
    }

    [Theory]
    [Trait("Decision", "ED-036")]
    [InlineData("~name/x")]
    [InlineData(@"~name\x")]
    [InlineData("~name")]
    [InlineData(@"a\~")]
    [InlineData("a~b")]
    [InlineData(@"C:\x")]
    [InlineData("")]
    public void ExpandingTildeInPath_leaves_every_other_form_untouched(string path)
    {
        // ED-036: 'leave any other "~name" form unexpanded', as NSString does for a user that does not exist.
        Assert.Equal(path, SwiftPath.ExpandingTildeInPath(path));
    }

    // ---- containment (one comparison) -------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-019")]
    [Trait("Decision", "ED-026")]
    [Trait("Decision", "ED-039")]
    [InlineData(@"C:\Repo\src\a.swift", @"C:\Repo", @"src\a.swift")]
    [InlineData("c:/repo/Src/A.swift", @"C:\Repo", "Src/A.swift")]
    [InlineData(@"C:\REPO\Src\A.swift", "c:/repo", @"Src\A.swift")]
    [InlineData(@"C:\Repo\a", @"C:\Repo\", "a")]
    [InlineData(@"C:\Repo\a", "C:/Repo/", "a")]
    [InlineData(@"C:\Repo\", @"C:\Repo", "")]
    [InlineData(@"\\?\C:\Repo\a.txt", @"C:\Repo", "a.txt")]
    [InlineData(@"C:\Repo\a.txt", @"\\?\C:\Repo", "a.txt")]
    [InlineData(@"\\Server\Share\x\y", @"\\server\share", @"x\y")]
    [InlineData(@"C:\a", @"C:\", "a")]
    [InlineData("C:/a/b", "c:/", "a/b")]
    public void TailUnder_compares_folded_and_normalized_and_returns_the_callers_own_tail(string path, string root, string expected)
    {
        // ProviderSession.swift:194-195 and TouchedPaths.swift:26-28: prefix = root ending in one separator, then
        // the rest of the path. ED-019/026: compare on the forward-slash form, folded for case (NTFS), and "return
        // the untouched tail of the caller's original string"; ED-039(2): ordinal, drive letter upper-cased, any
        // \\?\ prefix stripped. A root already ending in a separator is used as it is (`root.hasSuffix("/")`).
        Assert.Equal(expected, SwiftPath.TailUnder(path, root));
    }

    [Theory]
    [Trait("Decision", "ED-019")]
    [Trait("Decision", "ED-039")]
    [InlineData(@"C:\Repo2\a", @"C:\Repo")]
    [InlineData(@"C:\Repository\a", @"C:\Repo")]
    [InlineData(@"C:\Repo", @"C:\Repo")]
    [InlineData(@"D:\Repo\a", @"C:\Repo")]
    [InlineData(@"src\a.swift", @"C:\Repo")]
    [InlineData(@"\\server\share2\a", @"\\server\share")]
    [InlineData(@"C:\Other\Repo\a", @"C:\Repo")]
    [InlineData("", @"C:\Repo")]
    public void TailUnder_is_nil_outside_the_root_and_for_the_root_itself(string path, string root)
    {
        // `path.hasPrefix(root + "/")`: a sibling that merely shares the spelling ("/Repo2") is outside, and the
        // root itself without a trailing separator is not under itself (ProviderSession.swift:194-195 returns the
        // path whole; TouchedPaths.swift:27 returns nil).
        Assert.Null(SwiftPath.TailUnder(path, root));
    }

    [Fact]
    [Trait("Decision", "ED-019")]
    [Trait("Decision", "ED-039")]
    public void TailUnder_folds_non_ASCII_letters_too()
    {
        // ED-019 "locale-invariant lowercase fold" and ED-039 "ordinal" case-insensitive agree on these letters.
        Assert.Equal("a.txt", SwiftPath.TailUnder("C:\\\u00DCber\\a.txt", "c:\\\u00FCber"));
    }

    [Fact]
    [Trait("Decision", "ED-019")]
    public void An_ordinal_forward_slash_prefix_test_misses_a_Windows_path_inside_the_root()
    {
        // The literal port of ProviderSession.swift:194-195 reports the file as outside: the failure ED-019 fixes.
        const string root = @"C:\Repo";
        Assert.False(@"c:\repo\a.swift".StartsWith(root + "/", StringComparison.Ordinal));
        Assert.Equal(@"a.swift", SwiftPath.TailUnder(@"c:\repo\a.swift", root));
    }

    [Theory]
    [Trait("Decision", "ED-019")]
    [Trait("Decision", "ED-039")]
    [Trait("Decision", "ED-045")]
    [InlineData(@"C:\Repo", "c:/repo/", true)]
    [InlineData(@"C:\Repo", @"C:\Repo", true)]
    [InlineData(@"\\?\C:\Repo", @"C:\Repo", true)]
    [InlineData(@"C:\Repo\sub\..", @"C:\Repo", true)]
    [InlineData(@"\\Server\Share\X", @"\\server\share\x\", true)]
    [InlineData(@"C:\Repo", @"C:\Repo2", false)]
    [InlineData(@"C:\Repo", @"D:\Repo", false)]
    [InlineData(@"C:\Repo\a", @"C:\Repo", false)]
    public void SamePath_compares_standardized_normalized_and_folded(string a, string b, bool expected)
    {
        // `(p as NSString).standardizingPath == standard` (AppModel+Hydra.swift:326) and
        // `standardized.path == root.path` (NativeFileTools.swift:27), under the one comparison of ED-019/039/045.
        Assert.Equal(expected, SwiftPath.SamePath(a, b));
        Assert.Equal(expected, SwiftPath.SamePath(b, a));
    }

    private static List<string> Walk(string start)
    {
        var seen = new List<string> { start };
        var dir = start;
        for (var step = 0; step < 32; step++)
        {
            var parent = SwiftPath.DeletingLastPathComponent(dir);
            if (parent == dir) return seen;
            seen.Add(parent);
            dir = parent;
        }
        return seen;
    }
}
