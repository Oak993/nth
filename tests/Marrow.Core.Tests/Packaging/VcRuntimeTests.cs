using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Core.Tests.Fixtures;
using Nth.Build;

namespace Marrow.Core.Tests.Packaging;

// PORT-ONLY (no Swift twin): nth installer PLAN.md override 4 and section 9 item 2, unit U7 (ED-NEW-inst-u7-3). The four Visual C++ runtime DLLs the
// voice natives import are copied next to nth.exe from Visual Studio's redistributable folder, pinned by version and SHA-256 in
// scripts/release-toolchain.json; they get no Windows servicing, so every release build compares the pin with the newest redistributable installed
// and stops when the pin is older than the newest Microsoft security update the file notes. The fixtures are fake DLLs in temp folders.
public sealed class VcRuntimeTests
{
    private static readonly string[] Crt = ["msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"];

    // <root>\<version>\x64\Microsoft.<toolset>.CRT and Microsoft.<toolset>.OpenMP, each DLL a copy of a managed dll stamped with this file version and its own comment.
    private static void Redist(string root, string version, string toolset, string fileVersion)
    {
        var crt = Directory.CreateDirectory(Path.Combine(root, version, "x64", $"Microsoft.{toolset}.CRT")).FullName;
        var openMp = Directory.CreateDirectory(Path.Combine(root, version, "x64", $"Microsoft.{toolset}.OpenMP")).FullName;
        foreach (var (folder, name) in Crt.Select(n => (crt, n)).Append((openMp, "vcomp140.dll")))
        {
            var path = Path.Combine(folder, name);
            File.Copy(typeof(VcRuntimeTests).Assembly.Location, path);
            Win32VersionStamp.Stamp(path, new VersionFields { FileVersion = fileVersion, ProductVersion = fileVersion, Comments = name + " " + version });
        }
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // The vcRuntime section of release-toolchain.json for the fixture's <version>.
    private static string PinFile(string root, string version, string fileVersion, string floor)
    {
        var folder = Path.Combine(root, version, "x64");
        string Where(string name) => Directory.GetFiles(folder, name, SearchOption.AllDirectories).Single();
        var files = Crt.Select(name => new { name, group = "CRT", sha256 = Sha(Where(name)) })
            .Append(new { name = "vcomp140.dll", group = "OpenMP", sha256 = Sha(Where("vcomp140.dll")) }).ToArray();
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-vcpin-").FullName, "release-toolchain.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { vcRuntime = new { redistVersion = version, fileVersion, architecture = "x64", securityFloor = new { fileVersion = floor }, files } }));
        return path;
    }

    private static string Quote(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    [Fact]
    [Trait("Decision", "ED-267")]
    public void The_pinned_redistributable_is_found_whatever_toolset_the_folders_name_and_every_file_is_proved_by_its_hash()
    {
        var root = Directory.CreateTempSubdirectory("nth-redist-").FullName;
        Redist(root, "14.40.33807", "VC143", "14.40.33810.0");
        Redist(root, "14.50.35710", "VC145", "14.50.35719.0");
        var current = PinFile(root, "14.50.35710", "14.50.35719.0", "14.50.35719.0");
        var older = PinFile(root, "14.40.33807", "14.40.33810.0", "14.40.33810.0");

        var json = PackagingRig.RunJson($$"""
            $now = Resolve-NthVcRuntime -Pin (Get-NthVcRuntimePin -ToolchainPath '{{Quote(current)}}') -Roots @('{{Quote(root)}}')
            $then = Resolve-NthVcRuntime -Pin (Get-NthVcRuntimePin -ToolchainPath '{{Quote(older)}}') -Roots @('{{Quote(root)}}')
            Write-Output '#JSON'
            [pscustomobject]@{ now = $now; then = $then } | ConvertTo-Json -Depth 5 -Compress
            """);

        var now = json.GetProperty("now");
        Assert.Equal("14.50.35710", now.GetProperty("Version").GetString());
        Assert.Equal(["msvcp140.dll", "vcomp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"], now.GetProperty("Files").EnumerateArray().Select(f => f.GetProperty("Name").GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.All(now.GetProperty("Files").EnumerateArray(), f => Assert.Contains("VC145", f.GetProperty("Source").GetString(), StringComparison.Ordinal));
        Assert.All(json.GetProperty("then").GetProperty("Files").EnumerateArray(), f => Assert.Contains("VC143", f.GetProperty("Source").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-267")]
    public void A_file_that_is_not_the_pinned_one_or_a_version_that_is_not_installed_stops_the_build_and_is_named()
    {
        var root = Directory.CreateTempSubdirectory("nth-redist-").FullName;
        Redist(root, "14.50.35710", "VC145", "14.50.35719.0");
        var pin = PinFile(root, "14.50.35710", "14.50.35719.0", "14.50.35719.0");
        var missing = PinFile(root, "14.50.35710", "14.50.35719.0", "14.50.35719.0");
        File.WriteAllText(missing, File.ReadAllText(missing).Replace("14.50.35710", "14.99.99999", StringComparison.Ordinal));
        var tampered = Path.Combine(root, "14.50.35710", "x64", "Microsoft.VC145.OpenMP", "vcomp140.dll");
        File.AppendAllText(tampered, "x");

        var json = PackagingRig.RunJson($$"""
            $one = try { Resolve-NthVcRuntime -Pin (Get-NthVcRuntimePin -ToolchainPath '{{Quote(pin)}}') -Roots @('{{Quote(root)}}'); '' } catch { $_.Exception.Message }
            $two = try { Resolve-NthVcRuntime -Pin (Get-NthVcRuntimePin -ToolchainPath '{{Quote(missing)}}') -Roots @('{{Quote(root)}}'); '' } catch { $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ one = $one; two = $two } | ConvertTo-Json -Compress
            """);

        Assert.Contains("vcomp140.dll in", json.GetProperty("one").GetString(), StringComparison.Ordinal);
        Assert.Contains("is not the pinned file", json.GetProperty("one").GetString(), StringComparison.Ordinal);
        Assert.Contains("14.99.99999", json.GetProperty("two").GetString(), StringComparison.Ordinal);
        Assert.Contains("was not found under", json.GetProperty("two").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-267")]
    public void The_refresh_check_reports_a_newer_redistributable_and_stops_only_below_the_security_floor()
    {
        var root = Directory.CreateTempSubdirectory("nth-redist-").FullName;
        Redist(root, "14.50.35710", "VC145", "14.50.35719.0");
        var alone = Directory.CreateTempSubdirectory("nth-redist-").FullName;
        Redist(alone, "14.50.35710", "VC145", "14.50.35719.0");
        Redist(root, "14.60.36000", "VC146", "14.60.36001.0");
        var atFloor = PinFile(root, "14.50.35710", "14.50.35719.0", "14.50.35719.0");
        var belowFloor = PinFile(root, "14.50.35710", "14.50.35719.0", "14.50.35800.0");

        var json = PackagingRig.RunJson($$"""
            $pin = Get-NthVcRuntimePin -ToolchainPath '{{Quote(atFloor)}}'
            $low = Get-NthVcRuntimePin -ToolchainPath '{{Quote(belowFloor)}}'
            Write-Output '#JSON'
            [pscustomobject]@{
                newer = (Test-NthVcRuntimeRefresh -Pin $pin -Roots @('{{Quote(root)}}'))
                alone = (Test-NthVcRuntimeRefresh -Pin $pin -Roots @('{{Quote(alone)}}'))
                low = (Test-NthVcRuntimeRefresh -Pin $low -Roots @('{{Quote(root)}}'))
                none = (Test-NthVcRuntimeRefresh -Pin $pin -Roots @())
            } | ConvertTo-Json -Depth 4 -Compress
            """);

        var newer = json.GetProperty("newer");
        Assert.True(newer.GetProperty("NewerInstalled").GetBoolean());
        Assert.Equal("14.60.36000", newer.GetProperty("NewestFolder").GetString());
        Assert.False(newer.GetProperty("BelowSecurityFloor").GetBoolean());
        Assert.False(json.GetProperty("alone").GetProperty("NewerInstalled").GetBoolean());
        Assert.True(json.GetProperty("low").GetProperty("BelowSecurityFloor").GetBoolean());
        Assert.False(json.GetProperty("none").GetProperty("NewerInstalled").GetBoolean());
    }

    [Fact]
    [Trait("Decision", "ED-267")]
    public void The_four_dlls_are_copied_next_to_the_app_intact_and_a_missing_or_changed_one_is_caught()
    {
        var root = Directory.CreateTempSubdirectory("nth-redist-").FullName;
        Redist(root, "14.50.35710", "VC145", "14.50.35719.0");
        var pin = PinFile(root, "14.50.35710", "14.50.35719.0", "14.50.35719.0");
        var app = Directory.CreateTempSubdirectory("nth-app-").FullName;

        var json = PackagingRig.RunJson($$"""
            $pin = Get-NthVcRuntimePin -ToolchainPath '{{Quote(pin)}}'
            Copy-NthVcRuntime -Resolved (Resolve-NthVcRuntime -Pin $pin -Roots @('{{Quote(root)}}')) -Destination '{{Quote(app)}}'
            Assert-NthVcRuntimeBeside -Pin $pin -Folder '{{Quote(app)}}'
            Remove-Item -LiteralPath (Join-Path '{{Quote(app)}}' 'vcruntime140_1.dll')
            $missing = try { Assert-NthVcRuntimeBeside -Pin $pin -Folder '{{Quote(app)}}'; '' } catch { $_.Exception.Message }
            Copy-Item -LiteralPath (Join-Path '{{Quote(app)}}' 'msvcp140.dll') -Destination (Join-Path '{{Quote(app)}}' 'vcruntime140_1.dll')
            $wrong = try { Assert-NthVcRuntimeBeside -Pin $pin -Folder '{{Quote(app)}}'; '' } catch { $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ missing = $missing; wrong = $wrong } | ConvertTo-Json -Compress
            """);

        Assert.Contains("vcruntime140_1.dll is not next to the app", json.GetProperty("missing").GetString(), StringComparison.Ordinal);
        Assert.Contains("is not the pinned vcruntime140_1.dll", json.GetProperty("wrong").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-267")]
    public void The_redistributable_folders_come_from_the_roots_given_then_from_the_environment()
    {
        var root = Directory.CreateTempSubdirectory("nth-redist-").FullName;
        var gone = Path.Combine(root, "gone");

        var json = PackagingRig.RunJson($$"""
            $env:NTH_VC_REDIST_ROOT = '{{Quote(root)}}'
            $fromEnvironment = @(Find-NthVcRedistRoots)
            $explicit = @(Find-NthVcRedistRoots -Roots @('{{Quote(gone)}}', '{{Quote(root)}}'))
            Write-Output '#JSON'
            [pscustomobject]@{ fromEnvironment = $fromEnvironment; explicit = $explicit } | ConvertTo-Json -Compress
            """);

        Assert.Equal([root], json.GetProperty("fromEnvironment").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal([root], json.GetProperty("explicit").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-267")]
    public void The_real_pin_names_the_four_imported_dlls_with_hashes_and_is_not_below_its_own_security_floor()
    {
        var toolchain = JsonDocument.Parse(File.ReadAllText(Path.Combine(PackagingRig.Scripts, "release-toolchain.json"))).RootElement.GetProperty("vcRuntime");
        var voice = JsonDocument.Parse(File.ReadAllText(Path.Combine(PackagingRig.Repository, "src", "Marrow.Services", "Port", "Voice", "Assets", "voice-pins.json")))
            .RootElement.GetProperty("vcRuntime").GetProperty("importedByNatives").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        var files = toolchain.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(voice, files.Select(f => f.GetProperty("name").GetString()!).Order(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase); // the DLLs the whisper natives import
        Assert.All(files, f => Assert.Matches("^[0-9A-F]{64}$", f.GetProperty("sha256").GetString()!));
        Assert.Equal(["CRT", "CRT", "CRT", "OpenMP"], files.Select(f => f.GetProperty("group").GetString()!).Order(StringComparer.Ordinal).ToArray());
        Assert.Matches(@"^14\.\d+\.\d+$", toolchain.GetProperty("redistVersion").GetString()!);
        Assert.Equal("x64", toolchain.GetProperty("architecture").GetString());
        Assert.True(Version.Parse(toolchain.GetProperty("fileVersion").GetString()!) >= Version.Parse(toolchain.GetProperty("securityFloor").GetProperty("fileVersion").GetString()!));
        Assert.False(string.IsNullOrWhiteSpace(toolchain.GetProperty("securityFloor").GetProperty("source").GetString()));
    }

    [Fact]
    [Trait("Decision", "ED-267")]
    public void Where_the_pinned_redistributable_is_installed_its_four_files_are_the_pinned_ones()
    {
        var json = PackagingRig.RunJson($$"""
            $pin = Get-NthVcRuntimePin -ToolchainPath '{{Quote(Path.Combine(PackagingRig.Scripts, "release-toolchain.json"))}}'
            $roots = @(Find-NthVcRedistRoots)
            $found = @(Get-NthVcRuntimeCandidates -Roots $roots -Architecture $pin.architecture | Where-Object { $_.Version -eq $pin.redistVersion })
            $message = ''
            if ($found.Count -gt 0) { try { Resolve-NthVcRuntime -Pin $pin -Roots $roots | Out-Null } catch { $message = $_.Exception.Message } }
            Write-Output '#JSON'
            [pscustomobject]@{ installed = ($found.Count -gt 0); message = $message } | ConvertTo-Json -Compress
            """);

        // A PC without this Visual Studio redistributable has nothing to prove here (the packaging script then stops at its own check); where it is installed, it must match the pin.
        Assert.Equal("", json.GetProperty("message").GetString());
    }
}
