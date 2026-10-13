using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 9 items 6 and 9 and section 11, VELOPACK_CHECK.md check 12, unit U7 (ED-NEW-inst-u7-7). The
// signing hook is OFF (decision B, the signing route, is the owner's and open; OD-0058): the template vpk runs while it packs only stamps Update.exe
// as nth's, and signs nothing unless the build asked for it and the owner's certificate settings are in his environment. Every file here is a
// copy in a temp folder; signtool is never started.
public sealed class SigningHookTests
{
    private static string Quote(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private static string Exe(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), path);
        return path;
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Config(string folder, string mode)
    {
        var path = Path.Combine(folder, "sign-config.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            stamps = new Dictionary<string, object> { ["Squirrel.exe"] = new { FileDescription = "nth Updater", ProductName = "nth", CompanyName = "Example Publisher", ProductVersion = "1.0.0" } },
            sign = new { mode, description = "nth" },
        }));
        return path;
    }

    // ---- signtool's arguments ----

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Signtool_gets_sha256_an_rfc_3161_timestamp_and_the_owners_certificate_arguments_unquoted()
    {
        var json = PackagingRig.RunJson("""
            $settings = @{ CertificateArguments = '/f "C:\my certs\nth.pfx" /sha1 ABCDEF'; TimestampUrl = $null; AzureDlib = $null; AzureMetadata = $null; SignToolPath = $null }
            $custom = @{ CertificateArguments = '/n Example'; TimestampUrl = 'http://time.example/ts'; AzureDlib = $null; AzureMetadata = $null; SignToolPath = $null }
            Write-Output '#JSON'
            [pscustomobject]@{
                default = @(Get-NthSignToolArguments -Mode SignTool -Settings $settings)
                custom = @(Get-NthSignToolArguments -Mode SignTool -Settings $custom -Description 'nth Setup')
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(["sign", "/d", "nth", "/fd", "SHA256", "/tr", "http://timestamp.digicert.com", "/td", "SHA256", "/f", @"C:\my certs\nth.pfx", "/sha1", "ABCDEF"],
            json.GetProperty("default").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(["sign", "/d", "nth Setup", "/fd", "SHA256", "/tr", "http://time.example/ts", "/td", "SHA256", "/n", "Example"],
            json.GetProperty("custom").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Azure_signing_adds_the_dlib_and_its_metadata_and_uses_Microsofts_timestamp_by_default()
    {
        var json = PackagingRig.RunJson("""
            $settings = @{ CertificateArguments = $null; TimestampUrl = $null; AzureDlib = 'C:\tools\Azure.CodeSigning.Dlib.dll'; AzureMetadata = 'C:\tools\metadata.json'; SignToolPath = $null }
            Write-Output '#JSON'
            @(Get-NthSignToolArguments -Mode Azure -Settings $settings) | ConvertTo-Json -Compress
            """);

        Assert.Equal(["sign", "/d", "nth", "/fd", "SHA256", "/tr", "http://timestamp.acs.microsoft.com", "/td", "SHA256", "/dlib", @"C:\tools\Azure.CodeSigning.Dlib.dll", "/dmdf", @"C:\tools\metadata.json"],
            json.EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Signing_that_is_on_without_the_owners_settings_stops_with_a_message_that_names_them()
    {
        var json = PackagingRig.RunJson("""
            $empty = @{ CertificateArguments = $null; TimestampUrl = $null; AzureDlib = $null; AzureMetadata = $null; SignToolPath = $null }
            $half = @{ CertificateArguments = $null; TimestampUrl = $null; AzureDlib = 'C:\x.dll'; AzureMetadata = $null; SignToolPath = $null }
            $missingTool = @{ SignToolPath = 'C:\no\such\signtool.exe' }
            $signtool = try { Get-NthSignToolArguments -Mode SignTool -Settings $empty; '' } catch { $_.Exception.Message }
            $azure = try { Get-NthSignToolArguments -Mode Azure -Settings $half; '' } catch { $_.Exception.Message }
            $tool = try { Resolve-NthSignTool -Settings $missingTool; '' } catch { $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ signtool = $signtool; azure = $azure; tool = $tool } | ConvertTo-Json -Compress
            """);

        Assert.Contains("NTH_SIGN_SIGNTOOL_ARGS", json.GetProperty("signtool").GetString(), StringComparison.Ordinal);
        Assert.Contains("NTH_SIGN_AZURE_DLIB and NTH_SIGN_AZURE_METADATA", json.GetProperty("azure").GetString(), StringComparison.Ordinal);
        Assert.Contains("does not exist", json.GetProperty("tool").GetString(), StringComparison.Ordinal);
    }

    // ---- the template vpk runs ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void With_signing_off_the_template_stamps_only_the_updater_by_name_and_leaves_every_other_file_alone()
    {
        var folder = Directory.CreateTempSubdirectory("nth-template-").FullName;
        var updater = Exe(folder, "Squirrel.exe");
        var other = Exe(folder, "other.exe");
        var before = Sha(other);

        var (exit, output) = PackagingRig.RunScript("sign-template.ps1", "-Config", Config(folder, "None"), updater, other);

        Assert.True(exit == 0, output);
        Assert.Contains("stamped Squirrel.exe", output, StringComparison.Ordinal);
        Assert.DoesNotContain("other.exe", output, StringComparison.Ordinal);
        Assert.DoesNotContain("signed", output, StringComparison.Ordinal);
        var info = FileVersionInfo.GetVersionInfo(updater);
        Assert.Equal(("nth Updater", "nth", "Example Publisher", "1.0.0"), (info.FileDescription, info.ProductName, info.CompanyName, info.ProductVersion));
        Assert.Equal(before, Sha(other));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_updaters_name_matches_in_any_case_and_a_file_the_config_does_not_list_is_never_touched()
    {
        var folder = Directory.CreateTempSubdirectory("nth-template-").FullName;
        var updater = Exe(folder, "SQUIRREL.EXE");

        var (exit, output) = PackagingRig.RunScript("sign-template.ps1", "-Config", Config(folder, "None"), updater);

        Assert.True(exit == 0, output);
        Assert.Equal("nth Updater", FileVersionInfo.GetVersionInfo(updater).FileDescription);
    }

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Signing_turned_on_without_settings_stops_before_any_file_is_changed()
    {
        var folder = Directory.CreateTempSubdirectory("nth-template-").FullName;
        var updater = Exe(folder, "Squirrel.exe");
        var before = Sha(updater);

        var (exit, output) = PackagingRig.RunScript("sign-template.ps1", "-Config", Config(folder, "SignTool"), updater);

        Assert.NotEqual(0, exit);
        Assert.Contains("NTH_SIGN_SIGNTOOL_ARGS", output, StringComparison.Ordinal);
        Assert.Equal(before, Sha(updater));
    }

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void An_unknown_signing_mode_and_a_missing_file_are_refused()
    {
        var folder = Directory.CreateTempSubdirectory("nth-template-").FullName;
        var updater = Exe(folder, "Squirrel.exe");

        var (modeExit, modeOutput) = PackagingRig.RunScript("sign-template.ps1", "-Config", Config(folder, "Everything"), updater);
        var (fileExit, fileOutput) = PackagingRig.RunScript("sign-template.ps1", "-Config", Config(folder, "None"), Path.Combine(folder, "gone.exe"));

        Assert.NotEqual(0, modeExit);
        Assert.Contains("sign.mode must be None, SignTool or Azure", modeOutput, StringComparison.Ordinal);
        Assert.NotEqual(0, fileExit);
        Assert.Contains("gone.exe does not exist", fileOutput, StringComparison.Ordinal);
    }
}
