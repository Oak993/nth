using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 7 and 9, unit U7 (ED-NEW-inst-u7-3 to ED-NEW-inst-u7-9). The functions behind
// scripts/package-windows.ps1 run in a fresh PowerShell 7 over fixtures in temp folders: the PE checks, the version resources, the icon, the
// package sizes, the file names. Nothing here starts a build, vpk or an installer, and nothing touches the real Desktop, Start menu or HKCU.
public sealed class NthPackagingLibraryTests
{
    private static string Quote(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    // The smallest PE file the packaging checks read: the DOS header, the PE signature, the machine, and the optional header's magic and subsystem.
    private static string MinimalPe(ushort subsystem, ushort machine = 0x8664, string name = "app.exe")
    {
        var data = new byte[0x200];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(data, 0x3C);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(data, 0x80);
        BitConverter.GetBytes(machine).CopyTo(data, 0x84);
        BitConverter.GetBytes((ushort)240).CopyTo(data, 0x94);
        BitConverter.GetBytes((ushort)0x20B).CopyTo(data, 0x98);
        BitConverter.GetBytes(subsystem).CopyTo(data, 0x98 + 68);
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-pe-").FullName, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string NativeExeCopy(string name)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-ver-").FullName, name);
        File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), path);
        return path;
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_pe_file_reports_its_machine_and_subsystem_and_a_console_or_32_bit_exe_is_refused_as_a_gui_exe()
    {
        var gui = MinimalPe(2);
        var console = MinimalPe(3);
        var x86 = MinimalPe(2, machine: 0x14C);

        var json = PackagingRig.RunJson($$"""
            $errors = @()
            try { Assert-NthGuiExe -Path '{{Quote(console)}}' } catch { $errors += $_.Exception.Message }
            try { Assert-NthGuiExe -Path '{{Quote(x86)}}' } catch { $errors += $_.Exception.Message }
            Assert-NthGuiExe -Path '{{Quote(gui)}}'
            Write-Output '#JSON'
            [pscustomobject]@{ gui = (Get-NthPeInfo -Path '{{Quote(gui)}}'); console = (Get-NthPeInfo -Path '{{Quote(console)}}'); errors = $errors } | ConvertTo-Json -Depth 4 -Compress
            """);

        Assert.True(json.GetProperty("gui").GetProperty("IsGui").GetBoolean());
        Assert.True(json.GetProperty("gui").GetProperty("IsX64").GetBoolean());
        Assert.True(json.GetProperty("gui").GetProperty("Pe32Plus").GetBoolean());
        Assert.True(json.GetProperty("console").GetProperty("IsConsole").GetBoolean());
        var errors = json.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.Contains("console window", errors[0], StringComparison.Ordinal);
        Assert.Contains("not an x64 PE", errors[1], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_file_that_is_not_a_pe_is_refused_by_the_gui_check()
    {
        var plain = Path.Combine(Directory.CreateTempSubdirectory("nth-pe-").FullName, "plain.exe");
        File.WriteAllText(plain, new string('x', 200));

        var (exit, output) = PackagingRig.Run($"Assert-NthGuiExe -Path '{Quote(plain)}'");

        Assert.NotEqual(0, exit);
        Assert.Contains("not a PE file", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_identity_table_is_the_one_in_plan_section_7_for_the_app_the_setup_and_the_updater()
    {
        var json = PackagingRig.RunJson("""
            $i = Get-NthIdentity -Publisher 'Example Publisher' -Version '1.0.0' -Build '1' -AppCopyright 'Copyright C'
            Write-Output '#JSON'
            $i | ConvertTo-Json -Depth 5 -Compress
            """);

        var app = json.GetProperty("App");
        Assert.Equal("nth", app.GetProperty("FileDescription").GetString());
        Assert.Equal("nth", app.GetProperty("ProductName").GetString());
        Assert.Equal("Example Publisher", app.GetProperty("CompanyName").GetString());
        Assert.Equal("1.0.0.1", app.GetProperty("FileVersion").GetString());
        Assert.Equal("1.0.0", app.GetProperty("ProductVersion").GetString());
        Assert.Equal("Copyright C", app.GetProperty("LegalCopyright").GetString());
        Assert.Equal("nth.exe", app.GetProperty("OriginalFilename").GetString());
        Assert.Equal("nth", app.GetProperty("InternalName").GetString());

        var setup = json.GetProperty("Setup");
        Assert.Equal("nth Setup", setup.GetProperty("FileDescription").GetString());
        Assert.Equal("nth", setup.GetProperty("ProductName").GetString());
        Assert.Equal("Example Publisher", setup.GetProperty("CompanyName").GetString());
        Assert.Equal("1.0.0.1", setup.GetProperty("FileVersion").GetString());
        Assert.Equal("1.0.0", setup.GetProperty("ProductVersion").GetString());
        Assert.Equal("© 2026 Example Publisher. Includes Droppy Code by Jordy Spruit (Droppy), GNU AGPL v3.0.", setup.GetProperty("LegalCopyright").GetString());
        Assert.Equal("nth-Setup.exe", setup.GetProperty("OriginalFilename").GetString());
        Assert.Equal("nth Setup", setup.GetProperty("InternalName").GetString());

        var updater = json.GetProperty("Updater");
        Assert.Equal("nth Updater", updater.GetProperty("FileDescription").GetString());
        Assert.Equal("nth", updater.GetProperty("ProductName").GetString());
        Assert.Equal("Example Publisher", updater.GetProperty("CompanyName").GetString());
        Assert.Equal(4, updater.EnumerateObject().Count()); // its own file version and its Velopack copyright line are not touched
        var velopack = json.GetProperty("VelopackSetup");
        Assert.Equal(["CompanyName", "ProductName"], velopack.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_stamped_exe_passes_the_version_check_and_a_wrong_field_is_named()
    {
        var exe = NativeExeCopy("nth.exe");

        var json = PackagingRig.RunJson($$"""
            $identity = Get-NthIdentity -Publisher 'Example Publisher' -Version '1.0.0' -Build '1' -AppCopyright 'Copyright C'
            Set-NthVersionResource -Path '{{Quote(exe)}}' -Fields $identity.App
            Assert-NthVersionResource -Path '{{Quote(exe)}}' -Expected $identity.App -CheckLanguage
            $wrong = [ordered]@{ CompanyName = 'Someone Else'; InternalName = 'nth'; OriginalFilename = 'Marrow.exe' }
            $message = try { Assert-NthVersionResource -Path '{{Quote(exe)}}' -Expected $wrong; '' } catch { $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; info = (Get-NthVersionInfo -Path '{{Quote(exe)}}') } | ConvertTo-Json -Depth 4 -Compress
            """);

        var message = json.GetProperty("message").GetString()!;
        Assert.Contains("CompanyName is 'Example Publisher', not 'Someone Else'", message, StringComparison.Ordinal);
        Assert.Contains("OriginalFilename is 'nth.exe', not 'Marrow.exe'", message, StringComparison.Ordinal);
        Assert.DoesNotContain("InternalName", message, StringComparison.Ordinal);
        var number = json.GetProperty("info").GetProperty("FileVersionNumber");
        Assert.Equal((1, 0, 0, 1), (number.GetProperty("Major").GetInt32(), number.GetProperty("Minor").GetInt32(), number.GetProperty("Build").GetInt32(), number.GetProperty("Revision").GetInt32()));
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_shipped_icon_holds_all_ten_sizes_and_a_nine_size_icon_is_refused()
    {
        var real = Path.Combine(PackagingRig.Repository, "src", "Marrow.App", "Assets", "icon.ico");
        var nine = Path.Combine(Directory.CreateTempSubdirectory("nth-ico-").FullName, "nine.ico");
        var bytes = File.ReadAllBytes(real);
        BitConverter.GetBytes((ushort)9).CopyTo(bytes, 4); // the header now counts one entry less
        File.WriteAllBytes(nine, bytes);

        var json = PackagingRig.RunJson($$"""
            $message = try { Assert-NthIconSizes -Path '{{Quote(nine)}}'; '' } catch { $_.Exception.Message }
            Assert-NthIconSizes -Path '{{Quote(real)}}'
            Write-Output '#JSON'
            [pscustomobject]@{ sizes = @(Get-NthIcoSizes -Path '{{Quote(real)}}'); message = $message } | ConvertTo-Json -Compress
            """);

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 96, 128, 256], json.GetProperty("sizes").EnumerateArray().Select(e => e.GetInt32()).ToArray());
        Assert.Contains("holds sizes 16, 20, 24, 32, 40, 48, 64, 96, 128 instead of", json.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    // ---- package sizes (PLAN.md section 4) ----

    private static string Package(params (string Name, int Size)[] entries)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-nupkg-").FullName, "NthApp-1.0.0-full.nupkg");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, size) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(new byte[size]);
        }
        return path;
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_package_sizes_are_T_pkg_T_app_T_disk_and_Velopacks_own_free_space_threshold()
    {
        var package = Package(("NthApp.nuspec", 400), ("lib/app/nth.exe", 1000), ("lib/app/lib.dll", 5000), ("lib/app/Squirrel.exe", 300),
            ("lib/app/nth_ExecutionStub.exe", 50), ("lib/app/sub/data.bin", 70), ("lib/app/sub/", 0), ("[Content_Types].xml", 80));
        var length = new FileInfo(package).Length;

        var json = PackagingRig.RunJson($$"""
            Write-Output '#JSON'
            Get-NthPackageMetrics -Package '{{Quote(package)}}' | ConvertTo-Json -Compress
            """);

        Assert.Equal(length, json.GetProperty("PackageBytes").GetInt64());
        Assert.Equal(6070, json.GetProperty("AppBytes").GetInt64()); // lib/app without Squirrel.exe and the stub, which Setup.exe moves out of current\
        Assert.Equal(300, json.GetProperty("UpdaterBytes").GetInt64());
        Assert.Equal(50, json.GetProperty("StubBytes").GetInt64());
        Assert.Equal(length + 6070 + 300 + 50, json.GetProperty("DiskBytes").GetInt64());
        Assert.Equal(2 * 6900 + 50_000_000, json.GetProperty("VelopackNeedBytes").GetInt64()); // all entries, compressed plus uncompressed, plus 50,000,000
        Assert.DoesNotContain("lib/app/sub/", json.GetProperty("Entries").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_package_without_the_updater_or_with_two_stubs_is_not_measured()
    {
        var noUpdater = Package(("lib/app/nth.exe", 10), ("lib/app/nth_ExecutionStub.exe", 5));
        var twoStubs = Package(("lib/app/Squirrel.exe", 10), ("lib/app/a_ExecutionStub.exe", 5), ("lib/app/b_ExecutionStub.exe", 5));

        var json = PackagingRig.RunJson($$"""
            $one = try { Get-NthPackageMetrics -Package '{{Quote(noUpdater)}}'; '' } catch { $_.Exception.Message }
            $two = try { Get-NthPackageMetrics -Package '{{Quote(twoStubs)}}'; '' } catch { $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ one = $one; two = $two } | ConvertTo-Json -Compress
            """);

        Assert.Contains("holds no lib/app/Squirrel.exe", json.GetProperty("one").GetString(), StringComparison.Ordinal);
        Assert.Contains("2 lib/app/*_ExecutionStub.exe entries, not one", json.GetProperty("two").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_nuspec_and_single_entries_of_a_package_can_be_read_and_extracted()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-nupkg-").FullName, "NthApp-1.0.0-full.nupkg");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("NthApp.nuspec").Open(), new UTF8Encoding(false)))
                writer.Write("<?xml version=\"1.0\"?><package><metadata><id>NthApp</id><runtimeDependencies></runtimeDependencies><shortcutLocations>StartMenuRoot</shortcutLocations><shortcutAumid>nth.App</shortcutAumid><mainExe>nth.exe</mainExe></metadata></package>");
            using var stream = zip.CreateEntry("lib/app/nth.exe").Open();
            stream.Write("MZ"u8);
        }
        var destination = Path.Combine(Directory.CreateTempSubdirectory("nth-out-").FullName, "out");

        var json = PackagingRig.RunJson($$"""
            $nuspec = (Get-NthNuspec -Package '{{Quote(path)}}').package.metadata
            Expand-NthPackageEntries -Package '{{Quote(path)}}' -Destination '{{Quote(destination)}}' -Names @('lib/app/nth.exe') | Out-Null
            $missing = try { Expand-NthPackageEntries -Package '{{Quote(path)}}' -Destination '{{Quote(destination)}}' -Names @('lib/app/none.exe'); '' } catch { $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ deps = "$($nuspec.runtimeDependencies)"; shortcuts = "$($nuspec.shortcutLocations)"; aumid = "$($nuspec.shortcutAumid)"; main = "$($nuspec.mainExe)"; missing = $missing } | ConvertTo-Json -Compress
            """);

        Assert.Equal("", json.GetProperty("deps").GetString());
        Assert.Equal("StartMenuRoot", json.GetProperty("shortcuts").GetString());
        Assert.Equal("nth.App", json.GetProperty("aumid").GetString());
        Assert.Equal("nth.exe", json.GetProperty("main").GetString());
        Assert.Contains("holds no lib/app/none.exe", json.GetProperty("missing").GetString(), StringComparison.Ordinal);
        Assert.Equal("MZ", File.ReadAllText(Path.Combine(destination, "lib", "app", "nth.exe")));
    }

    // ---- names ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_shared_file_is_named_UNSIGNED_until_it_is_signed_and_says_so()
    {
        var json = PackagingRig.RunJson("""
            Write-Output '#JSON'
            [pscustomobject]@{ unsigned = (Get-NthSetupFileName -Version '1.0.0' -Signed $false); signed = (Get-NthSetupFileName -Version '1.0.0' -Signed $true); message = (Get-NthUnsignedMessage) } | ConvertTo-Json -Compress
            """);

        Assert.Equal("nth-Setup-1.0.0-UNSIGNED.exe", json.GetProperty("unsigned").GetString());
        Assert.Equal("nth-Setup-1.0.0.exe", json.GetProperty("signed").GetString());
        Assert.Equal("UNSIGNED, not for sharing", json.GetProperty("message").GetString());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_word_in_a_binary_is_found_through_the_library_and_a_dotted_namespace_is_not()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nth-scan-").FullName, "setup.bin");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("\0\0Marrow Code\0\0Marrow.Setup.Program\0\0").Concat(Encoding.Unicode.GetBytes("MarrowSetup-1")).ToArray());

        var json = PackagingRig.RunJson($$"""
            Write-Output '#JSON'
            [pscustomobject]@{ found = @(Find-NthWordInBinary -Path '{{Quote(path)}}' -Word 'Marrow'); visible = @(Find-NthWordInBinary -Path '{{Quote(path)}}' -Word 'Marrow' -Encoding Utf16) } | ConvertTo-Json -Compress
            """);

        Assert.Single(json.GetProperty("visible").EnumerateArray()); // only the UTF-16 one is what a user can see
        var found = json.GetProperty("found").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(2, found.Length);
        Assert.Contains(found, f => f.Contains("Marrow Code", StringComparison.Ordinal));
        Assert.Contains(found, f => f.StartsWith("utf16:", StringComparison.Ordinal));
    }
}
