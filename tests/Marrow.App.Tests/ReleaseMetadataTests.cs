using System.Reflection;
using System.Text.RegularExpressions;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 2 (override 7) and 7, unit U7 (ED-NEW-inst-u7-2; amends RR-43). Explorer shows InformationalVersion
// as "Product version", and it must read "1.0.0", not "1.0.0+build.1": the RR-43 check in Directory.Release.props now requires InformationalVersion to
// equal the version, while the build number stays in FileVersion (1.0.0.1) and in the CFBundleVersion metadata the app reads. The publisher is one
// value (decision A, open): the Company of the app and of the setup.
public sealed class ReleaseMetadataTests
{
    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_built_app_reads_the_plain_version_as_its_product_version_and_keeps_the_build_in_the_file_version()
    {
        var assembly = typeof(Program).Assembly;
        string Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;
        var version = Metadata("CFBundleShortVersionString");
        var build = Metadata("CFBundleVersion");

        Assert.Equal(version, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.DoesNotContain('+', assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.Equal($"{version}.{build}", assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);
        Assert.Equal($"{version}.0", assembly.GetName().Version!.ToString());
        Assert.Equal("nth", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal("nth", assembly.GetCustomAttribute<AssemblyTitleAttribute>()!.Title);
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_app_company_is_the_one_publisher_value_in_Directory_Release_props()
    {
        var props = File.ReadAllText(Path.Combine(Repository(), "Directory.Release.props"));
        var publisher = Regex.Match(props, "<NthPublisher>([^<]+)</NthPublisher>", RegexOptions.CultureInvariant).Groups[1].Value;

        Assert.False(string.IsNullOrWhiteSpace(publisher));
        Assert.Equal(publisher, typeof(Program).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_RR_43_check_requires_the_product_version_to_equal_the_version_and_no_longer_asks_for_the_build_suffix()
    {
        var props = File.ReadAllText(Path.Combine(Repository(), "Directory.Release.props"));

        Assert.Contains("<InformationalVersion>$(MarrowReleaseVersion)</InformationalVersion>", props, StringComparison.Ordinal);
        Assert.Contains("'$(InformationalVersion)' != '$(MarrowReleaseVersion)'", props, StringComparison.Ordinal);
        Assert.DoesNotContain("+build.", props.Replace("1.0.0+build.1", "", StringComparison.Ordinal), StringComparison.Ordinal); // only the comment names the old form
        Assert.Contains("<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>", props, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>$(MarrowReleaseVersion).$(MarrowReleaseBuild)</FileVersion>", props, StringComparison.Ordinal);
    }
}
