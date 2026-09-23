using System.Globalization;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// F.PathIdentity (IMPLEMENTING.md 3.13 R-PATH-1 and 4.3): NSString and URL path semantics with both Windows
// separators, one absoluteness predicate and one normalization (ED-019, ED-026, ED-036, ED-038, ED-039, ED-045).
// Expected values come from the Foundation behaviour the pinned Swift relies on (call sites cited per test) and
// from the literal text of the decisions; none is taken from running the port. The .NET results asserted in the
// negative controls were observed on .NET 10 (Windows 11) in a scratch console program, not in the port.
//
// Proposed port-only surface these tests fix (static members of SwiftPath):
//   NSString forms: LastPathComponent(path), DeletingLastPathComponent(path), AppendingPathComponent(path, component),
//     PathExtension(path), StandardizingPath(path), ExpandingTildeInPath(path).
//   URL forms, each on the path string of a file URL and each returning what Swift reads back:
//     FileURLPathExtension(path) = URL(fileURLWithPath: path).pathExtension,
//     FileURLDeletingLastPathComponentPath(path) = URL(fileURLWithPath: path).deletingLastPathComponent().path,
//     FileURLAppendingPathComponentPath(path, component) = URL(fileURLWithPath: path).appendingPathComponent(component).path,
//     StandardizedFileURLPath(path, relativeTo) = URL(fileURLWithPath: path, relativeTo: dir).standardizedFileURL.path.
//   Rules: IsAbsolute(path), Normalize(path), TailUnder(path, root), SamePath(a, b).
// Where no decision fixes which separator a result carries, the test compares through Normalize, so either
// separator passes and only the Swift semantics are asserted.
//
// Almost every path in this file is a lexical fixture that does not exist on disk (C:\Repo, C:\r, \\server\share and
// the rest). On macOS, NSString.standardizingPath consults the file system (it resolves symbolic links for an absolute
// path that contains "..", and drops a leading /private only while the file exists, AppModel+HydraMerge.swift:197-200).
// ED-141: on Windows StandardizingPath and StandardizedFileURLPath are purely lexical; they never read the file system
// and never resolve a junction, symbolic link or /private, so lexical is the full port semantics for existing and
// missing paths alike (one row checks a real folder under MARROW_PORT_DATA_DIR).
//
// Rulings these rows rest on: ED-137 (the containment and identity comparison is kernel32 CompareStringOrdinal with
// bIgnoreCase, never OrdinalIgnoreCase or a lowercase fold); ED-138 (IsAbsolute is exactly the three forms of
// ED-036/ED-045; "~" and "://" stay at their call sites); ED-139 (\\?\UNC\ normalizes to //); ED-140 (URL.pathExtension
// follows swift-foundation, oracle items O-42 and O-43); ED-141 (lexical standardization); ED-142 (a path function
// keeps an absolute input absolute).
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
        // NSString callers: Shell.swift:188, DiffParser.swift:22, TerminalStore.swift:35, ComposerView.swift:461,
        // :1168, TimelineRows.swift:764, OpenAICompatibleSession.swift:939, TokenLedger.swift:257,
        // AttachmentPreview.swift:88, :423.
        Assert.Equal(expected, SwiftPath.LastPathComponent(path));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void Path_GetFileName_drops_the_last_component_that_Foundation_keeps()
    {
        Assert.Equal("", System.IO.Path.GetFileName("a/b/")); // the pitfall R-PATH-1 names
        Assert.Equal("b", SwiftPath.LastPathComponent("a/b/"));
    }

    // ---- deletingLastPathComponent (NSString) -----------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData("/tmp/scratch.tiff", "/tmp")]
    [InlineData("/tmp/lock/", "/tmp")]
    [InlineData("/tmp/", "/")]
    [InlineData("/tmp", "/")]
    [InlineData("/", "/")]
    [InlineData("scratch.tiff", "")]
    [InlineData("", "")]
    [InlineData("/tmp//scratch.tiff", "/tmp")]
    [InlineData(@"\tmp\scratch.tiff", @"\tmp")]
    [InlineData(@"\tmp\lock\", @"\tmp")]
    [InlineData(@"\tmp", @"\")]
    [InlineData(@"\", @"\")]
    [InlineData(@"\tmp\\scratch.tiff", @"\tmp")]
    [InlineData(@"a\b\", "a")]
    [InlineData(@"a/b", "a")]
    public void DeletingLastPathComponent_follows_NSString_with_both_separators(string path, string expected)
    {
        // NSString.deletingLastPathComponent: "/tmp/lock/" gives "/tmp", "/tmp" gives "/", "/" stays "/",
        // "scratch.tiff" gives "". NSString path methods fold a doubled separator before they act (the same fix
        // that makes "scratch///" give "scratch" above), so "/tmp//scratch.tiff" gives "/tmp", never "/tmp/": a port
        // that cuts at the last separator shows the doubled separator at DiffParser.swift:24-25, which appends "/"
        // to the parent. NSString callers: DiffParser.swift:24, Git.swift:467, AppModel+Hydra.swift:338.
        Assert.Equal(expected, SwiftPath.DeletingLastPathComponent(path));
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-036")]
    [InlineData(@"C:\Users\a", @"C:\Users")]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\Users\", @"C:\")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\a\\b", @"C:\a")]
    [InlineData("C:/Users/a", "C:/Users")]
    [InlineData("C:/Users", "C:/")]
    [InlineData("C:/", "C:/")]
    [InlineData(@"\\server\share\dir\file", @"\\server\share\dir")]
    [InlineData(@"\\server\share\dir", @"\\server\share")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData(@"\\?\C:\", @"\\?\C:\")]
    public void DeletingLastPathComponent_stops_at_the_drive_root_and_the_share_root(string path, string expected)
    {
        // ED-036: the upward walk (AppModel+Hydra.swift:332-340, `if dir == "/"`) ends where
        // deletingLastPathComponent stops shortening the path, "at C:\ or \\server\share". As NSString keeps "/"
        // for "/", a root keeps its own spelling: removing components never rewrites what is left, so the long
        // form \\?\C:\ is its own fixed point.
        Assert.Equal(expected, SwiftPath.DeletingLastPathComponent(path));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-045")]
    public void DeletingLastPathComponent_of_a_long_form_path_reaches_the_drive_root()
    {
        // ED-036 root "C:\" compared through the one normalization of ED-045 (\\?\ stripped).
        Assert.Equal("C:/", SwiftPath.Normalize(SwiftPath.DeletingLastPathComponent(@"\\?\C:\a")));
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

    [Fact]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-045")]
    public void Upward_walk_from_a_long_form_path_ends_at_the_drive_root()
    {
        // ED-036: the walk ends "at C:\ or \\server\share". A port that walks \\?\C: to \\? to "\" ends on a
        // non-empty fixed point too, and fails here.
        Assert.Equal("C:/", SwiftPath.Normalize(FixedPoint(@"\\?\C:\a\b")));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    public void Upward_walk_from_a_trailing_separator_share_path_ends_at_the_share()
    {
        // A port that goes on from the share to \\server and "\" fails here.
        Assert.Equal(@"\\server\share", FixedPoint(@"\\server\share\x\"));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    public void Upward_walk_from_a_forward_slash_drive_path_ends_at_the_drive_root()
    {
        Assert.Equal("C:/", FixedPoint("C:/a/b/c/"));
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

    // ---- appendingPathComponent (NSString) --------------------------------------------------------------------

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
        // "/" gives "/scratch.tiff". NSString callers: TouchedPaths.swift:24, AppModel+HydraMerge.swift:140, :253,
        // :496, :499, :540, ThreadRuntime.swift:3376, AttachmentPreview.swift:71. Which separator is inserted is not
        // fixed by any decision: compared normalized.
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

    // ---- pathExtension (NSString) -----------------------------------------------------------------------------

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
        // "/tmp/" "", "/tmp/scratch..tiff" "tiff"; a leading dot starts no extension. Case is kept: the NSString
        // callers lowercase themselves (AttachmentPreview.swift:15, :89, :130, :150). A dot in a parent folder
        // behind a backslash must not count.
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

    // ---- URL forms: pathExtension, deletingLastPathComponent().path, appendingPathComponent().path ------------
    // ED-140: URL.pathExtension follows swift-foundation's String.pathExtension on the last component, which differs
    // from NSString for a dot-file name (".bashrc"), a name ending in "." (the Storage.swift:99 "bin" fallback), a
    // "." or ".." stem and an extension holding a space or a bidi control. Those rows rest on oracle item O-42, and
    // URL.deletingLastPathComponent at a root on O-43; they carry the Oracle trait (the O-42 and O-43 tests below).

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData("/tmp/scratch.tiff", "tiff")]
    [InlineData(@"C:\Users\me\Pictures\photo.PNG", "PNG")]
    [InlineData(@"C:\Users\me\Downloads\archive.tar.gz", "gz")]
    [InlineData(@"C:\dir.d\noext", "")]
    [InlineData("C:/Users/me/.claude/projects/x/2026-09-22.jsonl", "jsonl")]
    [InlineData(@"C:\Staging\Marrow.app\", "app")]
    public void FileURL_pathExtension_is_read_from_the_last_component_and_keeps_its_case(string path, string expected)
    {
        // URL.pathExtension of the last path component, case kept (callers lowercase: Storage.swift:99, :184,
        // Wallpaper.swift:127, DownloadsPopover.swift:70; compare as written: Wallpaper.swift:99-100,
        // TokenLedger.swift:421 `== "jsonl"`). A directory URL's trailing separator does not hide the extension:
        // AppUpdater.swift:317 finds the bundle among directory contents by `$0.pathExtension == "app"`.
        Assert.Equal(expected, SwiftPath.FileURLPathExtension(path));
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData(@"C:\r\src\a.txt", "C:/r/src")]
    [InlineData("C:/r/src/a.txt", "C:/r/src")]
    [InlineData(@"C:\Users\me\.codex\config.toml", "C:/Users/me/.codex")]
    [InlineData(@"C:\Program Files\nodejs\node.exe", "C:/Program Files/nodejs")]
    [InlineData(@"C:\Repo\a\b\", "C:/Repo/a")]
    [InlineData(@"\\srv\share\d\f.txt", "//srv/share/d")]
    public void FileURL_deletingLastPathComponent_path_is_the_parent_without_a_trailing_separator(string path, string expected)
    {
        // URL.deletingLastPathComponent().path: the parent folder, and URL.path never ends in a separator except
        // at the root. Callers: NativeFileTools.swift:86 and MCPProviderConfig.swift:241 (the folder to create),
        // ProviderRegistry.swift:262 (the executable's folder, `.path`), Git.swift:71 and ThreadRuntime.swift:1971
        // (a directory URL walked up once per --show-prefix component).
        Assert.Equal(expected, SwiftPath.Normalize(SwiftPath.FileURLDeletingLastPathComponentPath(path)));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void FileURL_repository_root_walk_removes_one_component_per_prefix_component()
    {
        // Git.swift:68-73: `var root = directory; for _ in prefix.trimmed.split(separator: "/") {
        // root.deleteLastPathComponent() }`, with --show-prefix "a/b/" printed by git for C:\Repo\a\b.
        var root = @"C:\Repo\a\b";
        var components = "a/b/".Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        for (var i = 0; i < components; i++)
        {
            root = SwiftPath.FileURLDeletingLastPathComponentPath(root);
        }
        Assert.Equal("C:/Repo", SwiftPath.Normalize(root));
    }

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [InlineData(@"C:\dev", "usr/bin/git", "C:/dev/usr/bin/git")]
    [InlineData(@"C:\Repo", @"src\a.txt", "C:/Repo/src/a.txt")]
    [InlineData(@"C:\Repo", "src/a.txt", "C:/Repo/src/a.txt")]
    [InlineData(@"C:\Repo\", "a.txt", "C:/Repo/a.txt")]
    [InlineData(@"C:\Repo", "sub/", "C:/Repo/sub")]
    [InlineData(@"C:\Repo", @"..\outside.txt", "C:/Repo/../outside.txt")]
    [InlineData(@"\\srv\share", "a.txt", "//srv/share/a.txt")]
    public void FileURL_appendingPathComponent_path_joins_once_and_resolves_nothing(string path, string component, string expected)
    {
        // URL.appendingPathComponent(_:).path: a multi-component string is appended whole (Shell.swift:410,
        // "usr/bin/git"), exactly one separator between, no trailing separator in `.path`, and ".." is kept for the
        // standardization that follows (NativeFileTools.swift:25-26 appends model-supplied text, then
        // standardizes, then checks containment at :27). Storage.swift:100 appends "<uuid>.<ext>".
        Assert.Equal(expected, SwiftPath.Normalize(SwiftPath.FileURLAppendingPathComponentPath(path, component)));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void FileURL_appended_climb_is_removed_by_standardization()
    {
        // NativeFileTools.swift:22-26: root.appendingPathComponent(trimmed).standardizedFileURL; "..\outside.txt"
        // lands beside the root, where the check at :27 rejects it.
        var appended = SwiftPath.FileURLAppendingPathComponentPath(@"C:\Repo", @"..\outside.txt");
        Assert.Equal("C:/outside.txt", SwiftPath.Normalize(SwiftPath.StandardizedFileURLPath(appended, null)));
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
    [InlineData(@"\\\server\share", false)]
    [InlineData("C:", false)]
    [InlineData("C:x", false)]
    [InlineData(@"1:\x", false)]
    [InlineData("\U000000C4:\\x", false)] // U+00C4 LATIN CAPITAL LETTER A WITH DIAERESIS: [A-Za-z] only
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
        // a bare leading "\" is not absolute; case-insensitive only in the drive letter. Read literally: the UNC
        // form needs two backslashes (its forward-slash spelling "//server/share" is ED-173's fourth form, moved to
        // IsAbsolute_accepts_a_forward_slash_UNC_path below), its server name may not begin with a
        // separator (so "\\\server\share" is not), and the drive letter is ASCII (a char.IsLetter port fails the
        // U+00C4 row). Replaces `hasPrefix("/")` at TouchedPaths.swift:24, TurnDiff.swift:34, :41,
        // AppModel+Hydra.swift:329, AppModel+HydraMerge.swift:498, :537. ED-138: IsAbsolute is exactly these three
        // forms and replaces only `hasPrefix("/")`; the "~" and "://" clauses stay at their call sites.
        Assert.Equal(expected, SwiftPath.IsAbsolute(path));
    }

    [Theory]
    [Trait("Decision", "ED-173")]
    [InlineData("//server/share", true)] // was false under ED-138/ED-142; changed by ED-173
    [InlineData("//server/share/dir/f.txt", true)]
    [InlineData(@"//server\share", true)]
    [InlineData("//", false)]
    [InlineData("//server", false)]
    [InlineData("//server/", false)]
    [InlineData("///server/share", false)]
    public void IsAbsolute_accepts_a_forward_slash_UNC_path(string path, bool expected)
    {
        // ED-173 (amending ED-138's three forms and ED-142's committed row): IsAbsolute also accepts
        // /^\/\/[^\\/]+[\\/][^\\/]+/. TurnDiff.swift:34 and :41 test hasPrefix("/") on reportedPath's output
        // (TurnDiff.swift:8-9), which TouchedPaths.normalize forward-slashes (ED-019 (3)), so a UNC file outside the
        // root arrives as "//srv/share/...". A lone "//" or "//server" without a share stays false, as the backslash
        // form does, and the server name may not begin with a separator, as in the backslash form.
        Assert.Equal(expected, SwiftPath.IsAbsolute(path));
    }

    [Theory]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-038")]
    [Trait("Decision", "ED-045")]
    [InlineData("~")]
    [InlineData("~/x")]
    [InlineData(@"~\x")]
    [InlineData("~name/x")]
    [InlineData("https://example.com/a")]
    public void IsAbsolute_is_false_for_tilde_and_URL_forms_under_the_three_form_predicate(string path)
    {
        // ED-138 (amending ED-038): the exported predicate is the three-form IsAbsolute of ED-036/ED-045 (drive-rooted,
        // UNC, \\?\), none of which a "~" or "://" string matches. The "~" and "://" clauses stay at the call sites as
        // the Swift writes them: AppModel+HydraMerge.swift:498, :537 test `hasPrefix("/") || hasPrefix("~") ||
        // contains("://")`, NativeFileTools.swift:23 and ThreadRuntime.swift:3376 test "/" or "~", and
        // TouchedPaths.swift:21-24 rejects "://", expands "~" and then tests only `hasPrefix("/")`, so "~name/x" is
        // joined onto the root there.
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
        // dot segments are left to the comparison and to standardization. The \\?\UNC\ form is ED-139 (below).
        Assert.Equal(expected, SwiftPath.Normalize(path));
    }

    // ---- standardizingPath and standardizedFileURL.path ---------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-045")]
    [InlineData(@"C:\a\.\b", "C:/a/b")]
    [InlineData(@"C:\a\\b", "C:/a/b")]
    [InlineData(@"C:\a\b\..\c", "C:/a/c")]
    [InlineData(@"C:\..\a", "C:/a")]
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
        // NSString.standardizingPath on a path that does not exist: "//" and "/./" become one separator, ".." is
        // resolved in absolute paths only (a relative path keeps it), ".." at the root stays at the root ("/../a"
        // is "/a", so a port that pops the drive fails the C:\..\a row), a trailing separator is removed, the root
        // keeps its own. The UNC lead-in "\\" is not an empty component. Callers: TouchedPaths.swift:23, :25,
        // AppModel+Hydra.swift:323, :326, :332, AppModel+HydraMerge.swift:191, :192, :202.
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
    [InlineData(@"..\..\x", @"C:\Repo", "C:/x")]
    [InlineData(@"src\a.", @"C:\Repo", "C:/Repo/src/a.")]
    [InlineData(@"D:\x\y.txt", @"C:\Repo", "D:/x/y.txt")]
    [InlineData(@"\\?\D:\x", @"C:\Repo", "D:/x")]
    [InlineData(@"\a.txt", @"C:\Repo", "C:/Repo/a.txt")]
    [InlineData(@"C:\Repo\sub\..\a.txt", @"C:\Other", "C:/Repo/a.txt")]
    public void StandardizedFileURLPath_resolves_a_relative_path_against_the_directory(string path, string directory, string expected)
    {
        // URL(fileURLWithPath: path, relativeTo: directory).standardizedFileURL.path: a relative path is resolved
        // against the directory, an absolute one ignores it, whole "." and ".." segments are removed (RFC 3986:
        // ".." above the root stays at the root), and a trailing dot inside a name is kept ("a." stays "a."). The
        // bare "\" form is not absolute (ED-045) and so is resolved against the directory. Every expected value
        // here is drive-rooted, and both separators after "C:" satisfy the predicate, so the result is also
        // asserted absolute. Callers: TouchedPaths.swift:8-9, TurnDiff.swift:8, ThreadRuntime.swift:3518.
        var result = SwiftPath.StandardizedFileURLPath(path, directory);
        Assert.Equal(expected, SwiftPath.Normalize(result));
        Assert.True(SwiftPath.IsAbsolute(result), $"'{result}' is not absolute");
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-045")]
    public void StandardizedFileURLPath_keeps_a_UNC_path_and_ignores_the_directory()
    {
        // Compared normalized, as no decision fixes the separators inside the result. ED-142: the result keeps its
        // two-backslash UNC lead-in, so it stays absolute and TurnDiff.swift:34, :41 never drop a UNC file.
        Assert.Equal("//srv/share/d/f", SwiftPath.Normalize(SwiftPath.StandardizedFileURLPath(@"\\srv\share\d\.\f", @"C:\Repo")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.StandardizedFileURLPath(@"\\srv\share\d\.\f", @"C:\Repo")));
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    [Trait("Decision", "ED-045")]
    public void Path_GetFullPath_escapes_the_root_and_strips_a_trailing_dot()
    {
        // The obvious .NET stand-in for standardizedFileURL and standardizingPath. Observed on .NET 10, Windows 11:
        Assert.Equal(@"C:\a.txt", System.IO.Path.GetFullPath(@"\a.txt", @"C:\Repo"));      // "\" escapes the root
        Assert.Equal(@"C:\Repo\src\a", System.IO.Path.GetFullPath(@"src\a.", @"C:\Repo")); // GetFullPathNameW drops the dot
        Assert.Equal("C:/Repo/a.txt", SwiftPath.Normalize(SwiftPath.StandardizedFileURLPath(@"\a.txt", @"C:\Repo")));
        Assert.Equal("C:/Repo/src/a.", SwiftPath.Normalize(SwiftPath.StandardizedFileURLPath(@"src\a.", @"C:\Repo")));
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
        // Callers: TouchedPaths.swift:22, AppModel+Hydra.swift:321, AppModel+HydraMerge.swift:191,
        // NativeFileTools.swift:24, Shell.swift:418, AttachmentPreview.swift:69.
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
        // ProviderSession.swift:194-196 and TouchedPaths.swift:26-28: prefix = root ending in one separator, then
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
        // root itself without a trailing separator is not under itself (ProviderSession.swift:194-196 returns the
        // path whole; TouchedPaths.swift:27 returns nil).
        Assert.Null(SwiftPath.TailUnder(path, root));
    }

    [Fact]
    [Trait("Decision", "ED-019")]
    [Trait("Decision", "ED-039")]
    public void TailUnder_folds_non_ASCII_letters_too()
    {
        // U+00DC LATIN CAPITAL LETTER U WITH DIAERESIS against U+00FC LATIN SMALL LETTER U WITH DIAERESIS: a pair
        // NTFS treats as one name, and so does the ED-137 comparison (kernel32 CompareStringOrdinal, bIgnoreCase).
        // Letters where a wider fold and NTFS disagree (U+212A KELVIN SIGN, final sigma) have their own ED-137 rows.
        Assert.Equal("a.txt", SwiftPath.TailUnder("C:\\\U000000DCber\\a.txt", "c:\\\U000000FCber"));
    }

    [Fact]
    [Trait("Decision", "ED-019")]
    [Trait("Decision", "ED-026")]
    [Trait("Decision", "ED-039")]
    public void TailUnder_and_SamePath_fold_the_same_under_a_Turkish_current_culture()
    {
        // ED-019(1)/ED-026: a "locale-invariant" fold; ED-039(2): ordinal. Neither follows the current culture.
        // Under tr-TR, "I".ToLower() is U+0131 LATIN SMALL LETTER DOTLESS I, so a culture fold misses "C:\IMG".
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("\U00000131", "I".ToLower(CultureInfo.CurrentCulture)); // the culture pitfall
            Assert.Equal("a", SwiftPath.TailUnder(@"C:\IMG\a", @"c:\img"));
            Assert.True(SwiftPath.SamePath(@"C:\IMG", @"c:\img"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    [Trait("Decision", "ED-019")]
    public void An_ordinal_forward_slash_prefix_test_misses_a_Windows_path_inside_the_root()
    {
        // The literal port of ProviderSession.swift:194-196 reports the file as outside: the failure ED-019 fixes.
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
        // `(p as NSString).standardizingPath == standard` (AppModel+Hydra.swift:326), under the one comparison of
        // ED-019/039/045 made by ED-137. Standardization is lexical (ED-141, see the note at the top).
        Assert.Equal(expected, SwiftPath.SamePath(a, b));
        Assert.Equal(expected, SwiftPath.SamePath(b, a));
    }

    // ---- ED-137: CompareStringOrdinal (bIgnoreCase), never OrdinalIgnoreCase --------------------------------------

    [Fact]
    [Trait("Decision", "ED-039")]
    [Trait("Decision", "ED-137")]
    public void TailUnder_and_SamePath_keep_the_Kelvin_sign_apart_from_k()
    {
        // ED-137: NTFS keeps U+212A KELVIN SIGN and "k" distinct, and so does CompareStringOrdinal; the folder is not
        // the checkout. TouchedPaths.swift:26-27, ProviderSession.swift:194-196, AppModel+Hydra.swift:325-326.
        Assert.Null(SwiftPath.TailUnder("C:\\\U0000212A\\a", @"c:\k")); // U+212A KELVIN SIGN
        Assert.False(SwiftPath.SamePath("C:\\\U0000212A", @"c:\k"));    // U+212A KELVIN SIGN
    }

    [Fact]
    [Trait("Decision", "ED-039")]
    [Trait("Decision", "ED-137")]
    public void TailUnder_and_SamePath_keep_final_sigma_apart_from_capital_sigma()
    {
        // U+03C2 GREEK SMALL LETTER FINAL SIGMA against U+03A3 GREEK CAPITAL LETTER SIGMA (ED-137).
        Assert.Null(SwiftPath.TailUnder("C:\\\U000003C2\\a", "c:\\\U000003A3"));
        Assert.False(SwiftPath.SamePath("C:\\\U000003C2", "c:\\\U000003A3"));
        Assert.False(SwiftPath.SamePath("c:\\\U000003A3", "C:\\\U000003C2"));
    }

    [Fact]
    [Trait("Decision", "ED-039")]
    [Trait("Decision", "ED-137")]
    public void TailUnder_keeps_micro_sign_and_titlecase_dz_apart()
    {
        // U+00B5 MICRO SIGN against U+039C GREEK CAPITAL LETTER MU; U+01C5 LATIN CAPITAL LETTER D WITH SMALL LETTER Z
        // WITH CARON against U+01C6 LATIN SMALL LETTER DZ WITH CARON (ED-137).
        Assert.Null(SwiftPath.TailUnder("C:\\\U000000B5\\a", "c:\\\U0000039C"));
        Assert.Null(SwiftPath.TailUnder("C:\\\U000001C5\\a", "c:\\\U000001C6"));
    }

    [Fact]
    [Trait("Decision", "ED-039")]
    [Trait("Decision", "ED-137")]
    public void TailUnder_folds_small_sigma_to_capital_sigma()
    {
        // Positive control (ED-137): U+03C3 GREEK SMALL LETTER SIGMA is the same NTFS name as U+03A3.
        Assert.Equal("a", SwiftPath.TailUnder("C:\\\U000003C3\\a", "c:\\\U000003A3"));
    }

    [Fact]
    [Trait("Decision", "ED-137")]
    public void OrdinalIgnoreCase_calls_final_sigma_and_capital_sigma_equal()
    {
        // The pitfall ED-137 names: .NET's OrdinalIgnoreCase is wider than NTFS (409 BMP pairs on this PC).
        // string.Equals("\U000003C2", "\U000003A3", StringComparison.OrdinalIgnoreCase) is true (U+03C2, U+03A3); stated
        // through the comparer because xUnit2010 rejects Assert.True over string.Equals.
        Assert.Equal("\U000003C2", "\U000003A3", StringComparer.OrdinalIgnoreCase);
        Assert.False(SwiftPath.SamePath("C:\\\U000003C2", "C:\\\U000003A3"));
    }

    // ---- ED-139: the \\?\UNC\ long form ----------------------------------------------------------------------------

    [Theory]
    [Trait("Decision", "ED-045")]
    [Trait("Decision", "ED-139")]
    [InlineData(@"\\?\UNC\srv\Share\a", "//srv/Share/a")]
    [InlineData(@"\\?\unc\srv\share", "//srv/share")]
    public void Normalize_turns_the_long_UNC_prefix_into_two_slashes(string path, string expected)
    {
        // ED-139: a leading \\?\UNC\ ("UNC" in any ASCII case) becomes //; server and share keep their case.
        Assert.Equal(expected, SwiftPath.Normalize(path));
    }

    [Fact]
    [Trait("Decision", "ED-045")]
    [Trait("Decision", "ED-139")]
    public void The_long_UNC_form_is_the_same_path_as_its_short_form()
    {
        Assert.Equal(@"x\y", SwiftPath.TailUnder(@"\\?\UNC\Server\Share\x\y", @"\\server\share"));
        Assert.True(SwiftPath.SamePath(@"\\?\UNC\srv\share\d", @"\\srv\share\d"));
        Assert.True(SwiftPath.SamePath(@"\\srv\share\d", @"\\?\UNC\srv\share\d"));
        Assert.True(SwiftPath.IsAbsolute(@"\\?\UNC\srv\share\a"));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-139")]
    [Trait("Decision", "ED-142")]
    public void The_long_UNC_share_is_a_fixed_point_of_DeletingLastPathComponent()
    {
        // ED-139: \\?\UNC\srv\share is the same path as \\srv\share, so ED-036 applies to it: the upward walk of
        // AppModel+Hydra.swift:332-340 (dir = deletingLastPathComponent until it stops shortening) ends at the share.
        // ED-142: the root keeps its \\?\UNC\ lead-in. A port whose root is only the four-character \\?\ returns
        // \\?\UNC\srv here.
        Assert.Equal(@"\\?\UNC\srv\share", SwiftPath.DeletingLastPathComponent(@"\\?\UNC\srv\share"));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-139")]
    [Trait("Decision", "ED-142")]
    public void Upward_walk_from_a_long_UNC_path_stops_at_the_share()
    {
        // ED-036 via ED-139: the walk of AppModel+Hydra.swift:332-340 ends at the share, never at \\?\UNC\srv.
        Assert.Equal(@"\\?\UNC\srv\share", FixedPoint(@"\\?\UNC\srv\share\a\b"));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-139")]
    [Trait("Decision", "ED-142")]
    public void Dot_dot_never_climbs_above_a_long_UNC_share()
    {
        // Standardization (AppModel+Hydra.swift:323, :326; TouchedPaths.swift:23, :25) treats the share as the root,
        // as ED-036 does for \\srv\share; a port that pops the share returns "//srv/x".
        Assert.Equal("//srv/share/x", SwiftPath.Normalize(SwiftPath.StandardizingPath(@"\\?\UNC\srv\share\..\x")));
    }

    [Fact]
    [Trait("Decision", "ED-036")]
    [Trait("Decision", "ED-139")]
    [Trait("Decision", "ED-142")]
    public void A_long_UNC_share_dot_dot_is_the_same_path_as_the_share()
    {
        // AppModel+Hydra.swift:326 compares standardized paths; ".." at the long-form share stays at the share.
        Assert.True(SwiftPath.SamePath(@"\\?\UNC\srv\share\..", @"\\srv\share"));
    }

    // ---- ED-140: URL.pathExtension (swift-foundation) and deletingLastPathComponent at a root (O-42, O-43) --------

    [Theory]
    [Trait("Decision", "ED-140")]
    [Trait("Oracle", "O-42")]
    [InlineData(@"C:\dir\.bashrc")]
    [InlineData(@"C:\dir\name.")]
    [InlineData(@"C:\dir\notes.final draft")]
    [InlineData(@"C:\dir\..tiff")]
    [InlineData(@"C:\dir\...tiff")] // UNVERIFIED O-42: the stem before the last "." is exactly ".." (ED-140)
    [InlineData("C:\\dir\\a.t\U0000200Ext")] // U+200E LEFT-TO-RIGHT MARK
    public void FileURL_pathExtension_is_empty_where_swift_foundation_rejects_the_extension(string path)
    {
        // UNVERIFIED O-42: macOS URL.pathExtension equals swift-foundation's String.pathExtension on the last component:
        // "" when the component ends in ".", when its last "." is its first character, when the stem is "." or "..",
        // or when the extension holds U+0020, "/", U+061C, U+200E, U+200F, U+202A-U+202E or U+2066-U+2069 (ED-140).
        // Callers: Storage.swift:99, :184, Wallpaper.swift:127, DownloadsPopover.swift:70.
        Assert.Equal("", SwiftPath.FileURLPathExtension(path));
    }

    [Theory]
    [Trait("Decision", "ED-140")]
    [Trait("Oracle", "O-42")]
    [InlineData("C:\\dir\\a.\U0000061Cx")] // U+061C ARABIC LETTER MARK
    [InlineData("C:\\dir\\a.x\U0000200F")] // U+200F RIGHT-TO-LEFT MARK
    [InlineData("C:\\dir\\a.x\U0000202A")] // U+202A LEFT-TO-RIGHT EMBEDDING
    [InlineData("C:\\dir\\a.x\U0000202E")] // U+202E RIGHT-TO-LEFT OVERRIDE
    [InlineData("C:\\dir\\a.x\U00002066")] // U+2066 LEFT-TO-RIGHT ISOLATE
    [InlineData("C:\\dir\\a.x\U00002069")] // U+2069 POP DIRECTIONAL ISOLATE
    [InlineData(@"C:\dir\...")]
    public void FileURL_pathExtension_rejects_every_listed_invalid_scalar(string path)
    {
        // UNVERIFIED O-42: the same swift-foundation rule (ED-140): each end of the two listed ranges and the other
        // listed scalars; "..." ends in ".".
        Assert.Equal("", SwiftPath.FileURLPathExtension(path));
    }

    [Theory]
    [Trait("Decision", "ED-140")]
    [Trait("Oracle", "O-42")]
    [InlineData(@"C:\dir\a..tiff", "tiff")]
    [InlineData(@"C:\dir\Photo.PNG", "PNG")]
    [InlineData("C:\\dir\\a.x\U00002065", "x\U00002065")] // U+2065 (unassigned, outside U+2066-U+2069)
    [InlineData("C:\\dir\\a.x\U0000202F", "x\U0000202F")] // U+202F NARROW NO-BREAK SPACE (outside U+202A-U+202E)
    public void FileURL_pathExtension_keeps_an_extension_outside_the_rejected_forms(string path, string expected)
    {
        // UNVERIFIED O-42: control rows; the text after the last "." with its case kept (ED-140).
        Assert.Equal(expected, SwiftPath.FileURLPathExtension(path));
    }

    [Fact]
    [Trait("Decision", "ED-140")]
    [Trait("Oracle", "O-43")]
    public void FileURL_deletingLastPathComponent_at_a_root_is_the_root_itself()
    {
        // UNVERIFIED O-43: URL.deletingLastPathComponent() at the root returns the root (swift-foundation outside the
        // FOUNDATION_FRAMEWORK compatibility1 branch); no reachable caller depends on it (ED-140).
        Assert.Equal("C:/", SwiftPath.Normalize(SwiftPath.FileURLDeletingLastPathComponentPath(@"C:\")));
        Assert.Equal("//srv/share", SwiftPath.Normalize(SwiftPath.FileURLDeletingLastPathComponentPath(@"\\srv\share")));
    }

    // ---- ED-141: lexical standardization on a real folder ------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-141")]
    public void StandardizingPath_is_lexical_on_a_real_folder()
    {
        // ED-141: purely lexical for existing and missing paths alike. <tmp>\a\b exists; <tmp>\a\c does not.
        var data = Marrow.Testing.TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(data), "MARROW_PORT_DATA_DIR must point at a throwaway folder (R-TEST-9)");
        var tmp = System.IO.Path.GetFullPath(System.IO.Path.Combine(data!, "SwiftPathTests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(System.IO.Path.Combine(tmp, "a", "b"));
        try
        {
            Assert.Equal(SwiftPath.Normalize(tmp) + "/a/c", SwiftPath.Normalize(SwiftPath.StandardizingPath(tmp + @"\a\b\..\c")));
            Assert.Equal(SwiftPath.Normalize(tmp) + "/a/c",
                SwiftPath.Normalize(SwiftPath.StandardizedFileURLPath(@"a\b\..\c", tmp)));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    // ---- ED-142: a path function keeps an absolute input absolute ------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-142")]
    public void Every_path_function_keeps_a_UNC_or_long_form_input_absolute()
    {
        // ED-142: StandardizingPath, StandardizedFileURLPath, the FileURL*Path forms, AppendingPathComponent and
        // DeletingLastPathComponent keep an absolute input's lead-in, so IsAbsolute(result) holds.
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.StandardizingPath(@"\\srv\share\d\..\e")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.StandardizingPath(@"\\srv\share\..\..")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.StandardizingPath(@"\\?\C:\a\..\b")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.StandardizingPath(@"\\?\UNC\srv\share\a\.\b")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.StandardizedFileURLPath(@"\\?\UNC\srv\share\d", null)));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.StandardizedFileURLPath(@"\\srv\share\d\..\..", @"C:\Repo")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.DeletingLastPathComponent(@"\\srv\share\d")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.DeletingLastPathComponent(@"\\?\UNC\srv\share\d")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.FileURLDeletingLastPathComponentPath(@"\\srv\share\d\f")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.AppendingPathComponent(@"\\srv\share", "a")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.FileURLAppendingPathComponentPath(@"\\?\C:\r", "a")));
        Assert.True(SwiftPath.IsAbsolute(SwiftPath.FileURLAppendingPathComponentPath(@"\\srv\share", "a.txt")));
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

    private static string FixedPoint(string start)
    {
        var seen = Walk(start);
        var last = seen[^1];
        Assert.Equal(last, SwiftPath.DeletingLastPathComponent(last)); // the walk ended by reaching a fixed point
        return last;
    }
}
