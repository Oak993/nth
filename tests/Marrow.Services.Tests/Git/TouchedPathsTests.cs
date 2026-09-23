using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.Services.Tests.Git;

// Source: DroppyCode/Services/Git/TouchedPaths.swift (B2 1fbf81e), :1-61.
public sealed class TouchedPathsTests
{
    // ---- Normalize (PR-GIT-093) ----------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-093")] // Services/Git/TouchedPaths.swift:7
    public void Normalize_of_empty_path_returns_empty_string()
    {
        Assert.Equal("", TouchedPaths.Normalize("", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-093")] // Services/Git/TouchedPaths.swift:6-13
    public void Normalize_of_relative_path_under_root_drops_the_root_prefix()
    {
        Assert.Equal("src/App.cs", TouchedPaths.Normalize(@"src\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-093")] // Services/Git/TouchedPaths.swift:6-13
    public void Normalize_of_absolute_path_under_root_drops_the_root_prefix()
    {
        Assert.Equal("src/App.cs", TouchedPaths.Normalize(@"C:\repo\src\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-093")] // Services/Git/TouchedPaths.swift:9-12
    public void Normalize_of_path_outside_root_is_returned_unchanged_and_absolute()
    {
        // ED-019(3): forward-slashed like every TouchedPaths result, since consumers split/concatenate on "/".
        Assert.Equal("C:/other/App.cs", TouchedPaths.Normalize(@"C:\other\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Decision", "ED-045")] // TouchedPaths.swift:6-13 is one of the four sites sharing case-insensitive containment
    public void Normalize_root_containment_is_case_insensitive()
    {
        Assert.Equal("App.cs", TouchedPaths.Normalize(@"C:\REPO\App.cs", @"C:\repo"));
    }

    // ---- Relative (PR-GIT-094) ------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:20-21
    public void Relative_of_empty_or_whitespace_path_is_null()
    {
        Assert.Null(TouchedPaths.Relative("", @"C:\repo"));
        Assert.Null(TouchedPaths.Relative("   ", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:21
    public void Relative_of_path_containing_url_scheme_is_null()
    {
        Assert.Null(TouchedPaths.Relative(@"https://example.com/src/App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:22
    public void Relative_expands_a_leading_tilde()
    {
        var home = Environment.GetEnvironmentVariable("USERPROFILE")!;
        var root = SwiftPath.AppendingPathComponent(home, "repo");
        Assert.Equal("App.cs", TouchedPaths.Relative(@"~\repo\App.cs", root));
    }

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:24-28
    public void Relative_of_relative_path_under_root_returns_the_tail()
    {
        Assert.Equal("src/App.cs", TouchedPaths.Relative(@"src\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:24-28
    public void Relative_of_absolute_path_under_root_returns_the_tail()
    {
        Assert.Equal("src/App.cs", TouchedPaths.Relative(@"C:\repo\src\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:26-27
    public void Relative_of_path_outside_the_checkout_is_null()
    {
        Assert.Null(TouchedPaths.Relative(@"C:\other\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:24, 26-28 - a climb out of the root is resolved and rejected
    public void Relative_of_a_climb_out_of_the_root_is_null()
    {
        Assert.Null(TouchedPaths.Relative(@"..\other\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-094")] // Services/Git/TouchedPaths.swift:28 - String(full.dropFirst(prefix.count)).nilIfEmpty
    public void Relative_of_the_root_itself_is_null_because_the_tail_is_empty()
    {
        Assert.Null(TouchedPaths.Relative(@"C:\repo", @"C:\repo"));
    }

    [Fact]
    [Trait("Decision", "ED-045")] // TouchedPaths.swift:23 is one of the four sites sharing case-insensitive containment
    public void Relative_root_containment_is_case_insensitive()
    {
        Assert.Equal("App.cs", TouchedPaths.Relative(@"C:\REPO\App.cs", @"C:\repo"));
    }

    [Fact]
    [Trait("Decision", "ED-138")] // the "/" absoluteness test at :24 is SwiftPath.IsAbsolute, UNC form
    public void Relative_treats_a_UNC_path_as_absolute()
    {
        Assert.Null(TouchedPaths.Relative(@"\\server\share\App.cs", @"C:\repo"));
    }

    // ---- IsBuildOutput (PR-GIT-095) -------------------------------------------------------------------------------

    [Theory]
    [Trait("Req", "PR-GIT-095")] // Services/Git/TouchedPaths.swift:48-52
    [InlineData("-Xcc")]
    [InlineData("DerivedData")]
    [InlineData(".build")]
    [InlineData("node_modules")]
    [InlineData("__pycache__")]
    [InlineData(".pytest_cache")]
    [InlineData(".mypy_cache")]
    [InlineData(".ruff_cache")]
    [InlineData("xcuserdata")]
    [InlineData(".swiftpm")]
    [InlineData("ModuleCache")]
    [InlineData(".gradle")]
    [InlineData(".turbo")]
    [InlineData(".parcel-cache")]
    [InlineData(".commandcode")]
    public void IsBuildOutput_matches_each_of_the_fifteen_exact_names(string name)
    {
        Assert.True(TouchedPaths.IsBuildOutput($"src/{name}/file.txt"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-095")] // Services/Git/TouchedPaths.swift:43
    public void IsBuildOutput_matches_DS_Store()
    {
        Assert.True(TouchedPaths.IsBuildOutput("src/.DS_Store"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-095")] // Services/Git/TouchedPaths.swift:43
    public void IsBuildOutput_matches_any_component_ending_noindex()
    {
        Assert.True(TouchedPaths.IsBuildOutput("Library/Spotlight-V100.noindex/data"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-095")] // Services/Git/TouchedPaths.swift:39-44
    public void IsBuildOutput_of_a_source_folder_named_build_or_dist_is_false()
    {
        Assert.False(TouchedPaths.IsBuildOutput("build/output.txt"));
        Assert.False(TouchedPaths.IsBuildOutput("dist/bundle.js"));
    }

    [Fact]
    [Trait("Req", "PR-GIT-095")] // Services/Git/TouchedPaths.swift:39-44 - matching is case-sensitive
    public void IsBuildOutput_matching_is_case_sensitive()
    {
        Assert.False(TouchedPaths.IsBuildOutput("src/derivedData/file.txt"));
        Assert.False(TouchedPaths.IsBuildOutput("src/NODE_MODULES/file.txt"));
    }

    // ---- Matches (PR-GIT-096) -------------------------------------------------------------------------------------

    private static DiffFile MakeFile(string path, string? oldPath = null) =>
        new(path, DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty, oldPath);

    [Fact]
    [Trait("Req", "PR-GIT-096")] // Services/Git/TouchedPaths.swift:57 - exact set membership
    public void Matches_true_when_the_path_is_an_exact_set_member()
    {
        var touched = new HashSet<string> { "src/App.cs" };
        Assert.True(TouchedPaths.Matches(MakeFile("src/App.cs"), touched));
    }

    [Fact]
    [Trait("Req", "PR-GIT-096")] // Services/Git/TouchedPaths.swift:57 - touched entry ends with "/" + path
    public void Matches_true_when_a_touched_entry_ends_with_slash_plus_the_file_path()
    {
        var touched = new HashSet<string> { "sub/src/App.cs" };
        Assert.True(TouchedPaths.Matches(MakeFile("src/App.cs"), touched));
    }

    [Fact]
    [Trait("Req", "PR-GIT-096")] // Services/Git/TouchedPaths.swift:57 - file path ends with "/" + touched entry
    public void Matches_true_when_the_file_path_ends_with_slash_plus_a_touched_entry()
    {
        var touched = new HashSet<string> { "App.cs" };
        Assert.True(TouchedPaths.Matches(MakeFile("sub/src/App.cs"), touched));
    }

    [Fact]
    [Trait("Req", "PR-GIT-096")] // Services/Git/TouchedPaths.swift:56 - the oldPath arm
    public void Matches_true_when_the_old_path_matches_even_if_the_path_does_not()
    {
        var touched = new HashSet<string> { "src/Old.cs" };
        Assert.True(TouchedPaths.Matches(MakeFile("src/New.cs", "src/Old.cs"), touched));
    }

    [Fact]
    [Trait("Req", "PR-GIT-096")] // Services/Git/TouchedPaths.swift:54-59
    public void Matches_false_when_neither_path_nor_old_path_match()
    {
        var touched = new HashSet<string> { "src/Other.cs" };
        Assert.False(TouchedPaths.Matches(MakeFile("src/New.cs", "src/Old.cs"), touched));
    }

    [Fact]
    [Trait("Req", "PR-GIT-096")] // Services/Git/TouchedPaths.swift:54-56 - no old path present
    public void Matches_false_when_old_path_is_absent_and_path_does_not_match()
    {
        var touched = new HashSet<string> { "src/Other.cs" };
        Assert.False(TouchedPaths.Matches(MakeFile("src/New.cs"), touched));
    }
}
