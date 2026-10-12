using System.Text.RegularExpressions;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.SelfUpdate;

// PORT-ONLY (no Swift twin): nth installer PLAN.md sections 2, 7 and 9 and VELOPACK_CHECK.md checks 3, 12 and 13, unit U7 (ED-NEW-inst-u7-1 to
// ED-NEW-inst-u7-9). scripts/package-windows.ps1 is read as text, never run here (the proving run is the lead's gate): the order of its steps, what
// it hands to dotnet publish and vpk, what it checks, and that signing and the UI smoke test stay what the plan says.
public sealed class PackageWindowsNthTests
{
    private static readonly string Script = File.ReadAllText(Path.Combine(PackagingRig.Scripts, "package-windows.ps1"));

    // the script without its help block and without comment lines
    private static string Code() =>
        string.Join('\n', Regex.Replace(Script, @"<#.*?#>", "", RegexOptions.Singleline | RegexOptions.CultureInvariant).Split('\n')
            .Where(l => !l.TrimStart().StartsWith('#')));

    private static int At(string code, string text)
    {
        var index = code.IndexOf(text, StringComparison.Ordinal);
        Assert.True(index >= 0, "the script has no: " + text);
        return index;
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_main_exe_is_nth_exe_and_the_apphost_is_renamed_stamped_and_given_the_runtime_before_anything_is_packed()
    {
        var code = Code();
        Assert.Contains("[string]$MainExe = 'nth.exe',", code, StringComparison.Ordinal);
        Assert.Contains("--mainExe $MainExe", code, StringComparison.Ordinal);
        Assert.Contains("$builtAppHost = [System.IO.Path]::GetFileNameWithoutExtension($project) + '.exe'", code, StringComparison.Ordinal); // named after the assembly, never written by hand

        var publish = At(code, "dotnet publish $project");
        var rename = At(code, "Move-Item -LiteralPath (Join-Path $publishDir $builtAppHost)");
        var stamp = At(code, "Set-NthVersionResource -Path (Join-Path $publishDir $MainExe) -Fields $identity.App");
        var runtime = At(code, "Copy-NthVcRuntime -Resolved $vcRuntime -Destination $publishDir");
        var setup = At(code, "dotnet publish $setupProject -c $Configuration -r $Runtime -o $setupFreeDir");
        var pack = At(code, "vpk pack");
        Assert.True(publish < rename && rename < stamp && stamp < runtime && runtime < setup && setup < pack);
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_visual_cpp_runtime_is_resolved_and_checked_against_its_pin_before_anything_is_built_and_replaces_the_vcredist_prerequisite()
    {
        var code = Code();
        var resolve = At(code, "$vcRuntime = Resolve-NthVcRuntime -Pin $vcPin -Roots $vcRoots");
        var refresh = At(code, "$vcRefresh = Test-NthVcRuntimeRefresh -Pin $vcPin -Roots $vcRoots");
        var floor = At(code, "if ($vcRefresh.BelowSecurityFloor) {");
        Assert.True(resolve < refresh && refresh < floor && floor < At(code, "dotnet publish $project"));
        Assert.Contains("throw \"The pinned Visual C++ runtime", code, StringComparison.Ordinal);
        Assert.Contains("Write-Warning \"A newer Visual C++ runtime", code, StringComparison.Ordinal); // newer installed is reported, not an error
        Assert.DoesNotContain("--framework", code, StringComparison.Ordinal);
        Assert.DoesNotContain("vcredist143", code, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Assert-NthVcRuntimeBeside -Pin $vcPin -Folder $publishDir", code, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void The_pack_puts_Start_at_Programs_nth_lnk_with_the_apps_own_AppUserModelID_and_no_prerequisite()
    {
        var code = Code();
        Assert.Contains("--shortcuts StartMenuRoot `", code, StringComparison.Ordinal);
        Assert.DoesNotContain("--shortcuts StartMenu `", code, StringComparison.Ordinal);
        Assert.Contains("--aumid $aumid `", code, StringComparison.Ordinal);
        Assert.Contains("$aumid = 'nth.App'", code, StringComparison.Ordinal);
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, "nth.App"); // the ID nth sets on its own process (ToastBuildIdentity.ForBuild(false, false).AppId)
        Assert.Contains("if (\"$($nuspec.shortcutLocations)\" -ne 'StartMenuRoot')", code, StringComparison.Ordinal);
        Assert.Contains("if (\"$($nuspec.shortcutAumid)\" -ne $aumid)", code, StringComparison.Ordinal);
        Assert.Contains("runtimeDependencies is", code, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void Signing_is_off_by_default_and_goes_only_through_the_template_vpk_runs()
    {
        var code = Code();
        Assert.Contains("[ValidateSet('None', 'SignTool', 'Azure')][string]$Sign = 'None',", code, StringComparison.Ordinal);
        Assert.Contains("--signTemplate", code, StringComparison.Ordinal);
        Assert.Contains("{{file...}}", code, StringComparison.Ordinal);
        Assert.DoesNotContain("--signParams", code, StringComparison.Ordinal);
        Assert.DoesNotContain("--azureTrustedSignFile", code, StringComparison.Ordinal); // vpk takes only one of signTemplate, signParams and azureTrustedSignFile
        Assert.DoesNotContain("signtool.exe", code, StringComparison.OrdinalIgnoreCase);    // only the template script and the library start it
        // with signing off vpk is kept from calling the template for any file but the updater; with it on, it signs everything it packs
        Assert.Contains(@"if ($Sign -eq 'None') { $signArgs += @('--signExclude', '^(?!.*[\\/]Squirrel\.exe$)') }", code, StringComparison.Ordinal);
        Assert.Contains("mode = $Sign", code, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "OD-0058")]
    public void An_unsigned_build_is_named_UNSIGNED_and_the_last_lines_say_so()
    {
        var code = Code();
        Assert.Contains("Get-NthSetupFileName -Version $version -Signed $signed", code, StringComparison.Ordinal);
        Assert.Contains("$signed = $false", code, StringComparison.Ordinal);
        Assert.Contains("if ($Sign -ne 'None') { $signed = (Get-NthSignatureState -Path $setupShared).Signed }", code, StringComparison.Ordinal);
        var tail = string.Join('\n', Code().Split('\n').TakeLast(8));
        Assert.Contains("if ($Sign -eq 'None') {", tail, StringComparison.Ordinal);
        Assert.Contains("Write-Host (Get-NthUnsignedMessage)", tail, StringComparison.Ordinal);
        Assert.EndsWith("}", code.TrimEnd(), StringComparison.Ordinal); // nothing is printed after the verdict
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_shared_setup_carries_the_measured_sizes_the_default_root_the_publisher_and_the_notes_as_metadata()
    {
        var code = Code();
        foreach (var property in new[] { "InstallerTitle", "InstallerAppId", "InstallerVersion", "NthPublisher", "InstallerMainExe", "InstallerAumid", "InstallerDefaultRoot",
                     "InstallerPackageBytes", "InstallerAppBytes", "InstallerDiskBytes", "InstallerVelopackNeedBytes", "InstallerReleaseNotesFile", "VelopackSetupExe" })
            Assert.Contains($"\"-p:{property}=", code, StringComparison.Ordinal);
        Assert.Contains("\"-p:InstallerPackageBytes=$($metrics.PackageBytes)\"", code, StringComparison.Ordinal);
        Assert.Contains("\"-p:InstallerDiskBytes=$($metrics.DiskBytes)\"", code, StringComparison.Ordinal);
        Assert.Contains("\"-p:InstallerDefaultRoot=$defaultRootMetadata\"", code, StringComparison.Ordinal);
        Assert.Contains("$defaultRootMetadata = '%25LOCALAPPDATA%25\\Programs\\nth'", code, StringComparison.Ordinal);
        Assert.Contains("$defaultRootText = '%LOCALAPPDATA%\\Programs\\nth'", code, StringComparison.Ordinal);

        // every metadata key the script sets is added to the setup by Directory.Build.targets, and the other way round
        var targets = File.ReadAllText(Path.Combine(PackagingRig.Repository, "Directory.Build.targets"));
        var stamped = Regex.Matches(targets, @"<AssemblyMetadata Include=""(Installer\w+)""", RegexOptions.CultureInvariant).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var passed = Regex.Matches(code, @"""-p:(Installer\w+)=", RegexOptions.CultureInvariant).Select(m => m.Groups[1].Value)
            .Where(p => p is not ("InstallerTitle" or "InstallerAppId" or "InstallerVersion" or "InstallerReleaseNotesFile")).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(stamped.Except(["InstallerPublisher"]).Order(StringComparer.Ordinal).ToArray(), passed.Order(StringComparer.Ordinal).ToArray());
        Assert.Contains("<EmbeddedResource Include=\"$(InstallerReleaseNotesFile)\" LogicalName=\"ReleaseNotes.md\"", targets, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_setup_is_published_with_native_AOT_without_its_payload_first_and_with_it_after_the_pack()
    {
        var code = Code();
        var free = At(code, "dotnet publish $setupProject -c $Configuration -r $Runtime -o $setupFreeDir");
        var pack = At(code, "vpk pack");
        var metrics = At(code, "$metrics = Get-NthPackageMetrics -Package $fullPackage.FullName");
        var shared = At(code, "dotnet publish $setupProject -c $Configuration -r $Runtime -o $installerBuildDir");
        Assert.True(free < pack && pack < metrics && metrics < shared);
        // the linker lookup of Native AOT needs vswhere.exe on PATH (found when the first AOT publish broke without it)
        Assert.True(At(code, "$env:PATH = \"$vsInstaller;$env:PATH\"") < free);
        Assert.Equal(2, Regex.Matches(code, "-p:PublishAot=true", RegexOptions.CultureInvariant).Count);
        Assert.DoesNotContain("PublishSingleFile", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishTrimmed", code, StringComparison.Ordinal);
        // only the payload-free build is started by the UI smoke check, and only the second carries Velopack's Setup.exe
        Assert.Single(Regex.Matches(code, "\"-p:VelopackSetupExe=", RegexOptions.CultureInvariant));
        Assert.Contains("Copy-Item -LiteralPath $setupFree -Destination (Join-Path $publishDir 'nth-Setup.exe') -Force", code, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_build_checks_of_plan_section_9_item_8_run_before_anything_is_reported_as_built()
    {
        var code = Code();
        var firstCheck = At(code, "Invoke-NthCheck ");
        var built = At(code, "Write-Host \"Built (not published, not installed) in $releasesDir :\"");
        Assert.True(firstCheck < built);
        foreach (var check in new[]
                 {
                     "the payload-free setup passes --ui-smoke", "every exe is a GUI-subsystem exe", "version resources match PLAN.md section 7", "the icon holds all ten sizes",
                     "no user-visible \"Marrow\" in the setup exes", "no reference to the old exe name is left", "the Visual C++ runtime sits next to nth.exe",
                     "the package asks for no framework prerequisite", "the shared file adds at most 30 MiB",
                 })
            Assert.Contains(check, code, StringComparison.Ordinal);
        Assert.Contains("$setupBudgetBytes = 30MB", code, StringComparison.Ordinal);
        Assert.Contains("Find-NthOldExeNameReferences -Root $root", code, StringComparison.Ordinal);
        Assert.Contains("Assert-NthIconSizes -Path $icon", code, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_UI_smoke_test_runs_by_default_and_is_skipped_only_by_naming_it_and_the_build_then_says_so()
    {
        var code = Code();
        Assert.Contains("[switch]$SkipUiSmoke,", code, StringComparison.Ordinal);
        Assert.Contains("Invoke-NthUiSmoke -StartProcess { Start-NthUiSmokeProcess -FilePath $setupFree -ArgumentList @('--ui-smoke') }", code, StringComparison.Ordinal);
        Assert.Contains("if ($SkipUiSmoke) {", code, StringComparison.Ordinal);
        var smoke = At(code, "Invoke-NthUiSmoke -StartProcess");
        Assert.True(At(code, "} else {", At(code, "if ($SkipUiSmoke) {")) < smoke); // the smoke test is the else branch
        Assert.Contains("NOT FULLY VERIFIED: the payload-free setup was not started", code, StringComparison.Ordinal);
    }

    private static int At(string code, string text, int from)
    {
        var index = code.IndexOf(text, from, StringComparison.Ordinal);
        Assert.True(index >= 0, "the script has no: " + text);
        return index;
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_artifacts_can_be_built_elsewhere_and_the_default_is_the_repositorys_artifacts_folder()
    {
        var code = Code();
        Assert.Contains("[string]$ArtifactsDir = '',", code, StringComparison.Ordinal);
        Assert.Contains("$artifacts = if ($ArtifactsDir) { [System.IO.Path]::GetFullPath($ArtifactsDir) } else { Join-Path $root 'artifacts' }", code, StringComparison.Ordinal);
        Assert.Contains("$publishDir = Join-Path $artifacts 'publish'", code, StringComparison.Ordinal);
        Assert.Contains("$releasesDir = Join-Path $artifacts 'releases'", code, StringComparison.Ordinal);
        Assert.Contains("$installerDir = Join-Path $artifacts 'installer'", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Join-Path $root 'artifacts/", code, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_publisher_default_is_the_one_in_Directory_Release_props()
    {
        var props = File.ReadAllText(Path.Combine(PackagingRig.Repository, "Directory.Release.props"));
        var publisher = Regex.Match(props, "<NthPublisher>([^<]+)</NthPublisher>", RegexOptions.CultureInvariant).Groups[1].Value;

        Assert.Equal("oak993", publisher); // decision A (owner, 2026-10-11): lower case, everywhere
        Assert.Contains($"[string]$PackAuthors = '{publisher}',", Script, StringComparison.Ordinal);
        Assert.Contains("\"-p:NthPublisher=$PackAuthors\"", Script, StringComparison.Ordinal);
        Assert.Contains("<Company>$(NthPublisher)</Company>", props, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Every_script_the_packaging_uses_parses_as_PowerShell()
    {
        var files = new[] { "package-windows.ps1", "sign-template.ps1", "inspect-release-metadata.ps1", "restore-dependencies.ps1", Path.Combine("lib", "NthPackaging.ps1") }
            .Select(name => Path.Combine(PackagingRig.Scripts, name)).ToArray();

        var (exit, output) = PackagingRig.Run($$"""
            foreach ($file in @({{string.Join(", ", files.Select(f => "'" + f.Replace("'", "''", StringComparison.Ordinal) + "'"))}})) {
                $errors = $null; $tokens = $null
                [void][System.Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
                foreach ($e in $errors) { Write-Output "$file $($e.Extent.StartLineNumber): $($e.Message)" }
            }
            Write-Output 'parsed'
            """, library: false);

        Assert.Equal(0, exit);
        Assert.Equal("parsed", output.Trim());
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_inspection_script_reads_nth_exe_from_a_published_folder_and_expects_the_plain_product_version()
    {
        var inspect = File.ReadAllText(Path.Combine(PackagingRig.Scripts, "inspect-release-metadata.ps1"));

        Assert.Contains("product_version=$version;", inspect, StringComparison.Ordinal);
        Assert.DoesNotContain("+build.$build", inspect, StringComparison.Ordinal);
        Assert.Contains("(Test-Path (Join-Path $AppDirectory 'nth.exe')) { 'nth.exe' }", inspect, StringComparison.Ordinal);
        Assert.Contains("company=$publisher", inspect, StringComparison.Ordinal);
    }
}
