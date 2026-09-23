using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// Marrow.Testing.TestDataDirectory normalizes MARROW_PORT_DATA_DIR (R-TEST-9) so a value an agent sets Unix-style
// from Git Bash works exactly like its Windows form. ENGINEERING DECISION: this is test-harness plumbing, not a
// port of app behavior, so there is no Swift source line to trace it to; every test below carries R-TEST-9 instead.
public sealed class TestDataDirectoryTests
{
    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void An_MSYS_drive_path_becomes_its_Windows_form()
    {
        Assert.Equal(@"C:\Users\x\Temp\y", TestDataDirectory.Normalize("/c/Users/x/Temp/y"));
    }

    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void An_MSYS_drive_root_alone_becomes_the_drive_root()
    {
        Assert.Equal(@"C:\", TestDataDirectory.Normalize("/c"));
    }

    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void An_uppercase_MSYS_drive_letter_is_accepted_the_same_way()
    {
        Assert.Equal(@"C:\Users\x", TestDataDirectory.Normalize("/C/Users/x"));
    }

    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void An_already_Windows_style_path_is_unchanged()
    {
        Assert.Equal(@"C:\a\b", TestDataDirectory.Normalize(@"C:\a\b"));
    }

    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void A_relative_path_is_made_absolute()
    {
        var normalized = TestDataDirectory.Normalize(@"relative\sub");
        Assert.True(Path.IsPathRooted(normalized));
        Assert.Equal(Path.GetFullPath(@"relative\sub"), normalized);
    }

    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void Null_and_empty_pass_through_unchanged()
    {
        Assert.Null(TestDataDirectory.Normalize(null));
        Assert.Equal("", TestDataDirectory.Normalize(""));
    }

    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void A_path_that_merely_starts_with_a_slash_and_a_longer_segment_is_not_mistaken_for_a_drive()
    {
        // "/cargo/bin" is not "/c/argo/bin": the letter after the slash must be its own path component.
        Assert.Equal(Path.GetFullPath(@"\cargo\bin"), TestDataDirectory.Normalize("/cargo/bin"));
    }

    [Trait("Rule", "R-TEST-9")]
    [Fact]
    public void Read_normalizes_whatever_is_currently_set_or_returns_null_when_unset()
    {
        // No mutation here: MARROW_PORT_DATA_DIR is process-wide and other tests run in parallel against it
        // (R-TEST-5), so this only checks Read() agrees with Normalize() on the value already in place.
        Assert.Equal(TestDataDirectory.Normalize(Environment.GetEnvironmentVariable(TestDataDirectory.VariableName)),
            TestDataDirectory.Read());
    }
}
