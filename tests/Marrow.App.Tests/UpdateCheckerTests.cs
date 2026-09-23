using System.Reflection;
using Marrow.App;
using Xunit;

namespace Marrow.App.Tests;

public sealed class UpdateCheckerTests
{
    [Fact]
    [Trait("Req", "PR-UPDATE-001")] // App/UpdateChecker.swift:10-22
    public void AppVersion_parses_tags_trims_whitespace_and_pads_to_three_components()
    {
        Assert.Equal(new long[] { 1, 2, 3 }, AppVersion.Create("v1.2.3")!.Components);
        Assert.Equal(new long[] { 1, 2, 0 }, AppVersion.Create("1.2")!.Components);
        Assert.Equal(new long[] { 7, 0, 0 }, AppVersion.Create("7")!.Components);
        Assert.Equal(new long[] { 1, 2, 3 }, AppVersion.Create("\n V1.2.3 \t")!.Components);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-001")] // App/UpdateChecker.swift:10-22
    public void AppVersion_rejects_empty_parts_extra_parts_and_non_numeric_suffixes()
    {
        Assert.Null(AppVersion.Create(""));
        Assert.Null(AppVersion.Create("1..2"));
        Assert.Null(AppVersion.Create("1."));
        Assert.Null(AppVersion.Create("1.2.3.4"));
        Assert.Null(AppVersion.Create("1.2.3-beta.1"));
        Assert.Null(AppVersion.Create("1.2.3-nightly.4"));
        Assert.Null(AppVersion.Create("1.2.3rc"));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-002")] // App/UpdateChecker.swift:24-26,35-37
    public void AppVersion_compares_padded_components_lexicographically()
    {
        var shortVersion = AppVersion.Create("1.2")!;
        var paddedVersion = AppVersion.Create("1.2.0")!;
        var tenVersion = AppVersion.Create("1.10.0")!;
        var nineVersion = AppVersion.Create("1.9.0")!;

        Assert.Equal("1.2.0", shortVersion.Description);
        Assert.Equal(0, shortVersion.CompareTo(paddedVersion));
        Assert.True(tenVersion.CompareTo(nineVersion) > 0);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-003")] // App/UpdateChecker.swift:28-33
    public void AppVersion_text_of_tag_trims_and_removes_only_one_leading_v()
    {
        Assert.Equal("1.3", AppVersion.Text("v1.3"));
        Assert.Equal("1.3", AppVersion.Text(" \nV1.3\t"));
        Assert.Equal("vv1.3", AppVersion.Text("vv1.3"));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-004")] // App/UpdateChecker.swift:41-51
    public void AvailableUpdate_has_exactly_the_seven_swift_record_fields()
    {
        var properties = typeof(AvailableUpdate).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var names = properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        Assert.True(names.SetEquals(new[]
        {
            "Version",
            "Tag",
            "DownloadURL",
            "Size",
            "Notes",
            "ReleasedAt",
            "PageURL"
        }));
        Assert.Equal(7, properties.Length);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-105")] // App/UpdateChecker.swift:77-81
    public void Update_checker_uses_its_three_dedicated_defaults_keys()
    {
        var keys = typeof(UpdateChecker).GetNestedType("Keys", BindingFlags.NonPublic)!;
        Assert.Equal("updateChecker_release", keys.GetField("Update")!.GetRawConstantValue());
        Assert.Equal("updateChecker_lastChecked", keys.GetField("LastChecked")!.GetRawConstantValue());
        Assert.Equal("updateChecker_notifiedVersion", keys.GetField("NotifiedVersion")!.GetRawConstantValue());
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-014")] // App/UpdateChecker.swift:164-166
    [Trait("Req", "PR-UPDATE-015")] // App/UpdateChecker.swift:199-219
    public void An_empty_release_feed_has_no_newest_release()
    {
        Assert.Null(UpdateChecker.NewestRelease(Array.Empty<GitLabRelease>()));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-014")] // App/UpdateChecker.swift:265-272
    public void No_release_error_has_the_exact_description()
    {
        var error = new UpdateError(new UpdateError.Case.NoRelease());

        Assert.Equal("No release with a disk image was found.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-020")] // App/UpdateChecker.swift:265-273
    public void Bad_response_error_includes_the_literal_status_number()
    {
        var error = new UpdateError(new UpdateError.Case.BadResponse(404));

        Assert.Equal("GitLab answered with HTTP 404.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-020")] // App/UpdateChecker.swift:265-273
    public void Bad_response_error_preserves_the_non_http_status_sentinel()
    {
        var error = new UpdateError(new UpdateError.Case.BadResponse(-1));

        Assert.Equal("GitLab answered with HTTP -1.", error.ErrorDescription);
    }
}
