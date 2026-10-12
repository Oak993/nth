using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Marrow.Services.Tests.Web;

// nth web surfaces, unit W23 (DESIGN 2.1 and 14, packaging): WebView2Loader.dll for x64 and arm64 follows the runtime the app is built
// for, the package script proves the loader inside the full package, the weekly Canary workflow and the clean-VM scripts exist in
// the shape the design asks for. Everything is read as text or run on synthetic files with pwsh; the package script, the installer,
// the VM matrix and the workflow are never run here (decision C: written, not run).
[Trait("Decision", "ED-NEW-web-w23-1")]
[Trait("Feature", "W-23")]
public sealed class WebPackagingTests : IDisposable
{
    private const ushort MachineAmd64 = 0x8664;
    private const ushort MachineArm64 = 0xAA64;
    private const string PackageVersion = "1.0.4258.31";

    private readonly string temp = Directory.CreateTempSubdirectory("nth-w23-pack ").FullName;

    public void Dispose() => Directory.Delete(temp, recursive: true);

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Marrow.sln was not found above " + AppContext.BaseDirectory);
    }

    private static string Text(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]), Encoding.UTF8);

    private static string PackageFolder()
    {
        using var assets = JsonDocument.Parse(Text("src", "Marrow.Services", "obj", "project.assets.json"));
        var folder = assets.RootElement.GetProperty("packageFolders").EnumerateObject().First().Name;
        return Path.Combine(folder, "microsoft.web.webview2", PackageVersion);
    }

    private static byte[] Loader(string rid) => File.ReadAllBytes(Path.Combine(PackageFolder(), "runtimes", rid, "native", "WebView2Loader.dll"));

    private static ushort Machine(byte[] pe)
    {
        var header = BitConverter.ToInt32(pe, 0x3C);
        Assert.Equal(0x00004550u, BitConverter.ToUInt32(pe, header));
        return BitConverter.ToUInt16(pe, header + 4);
    }

    private static (int Exit, string Output) Pwsh(string script, string folder)
    {
        var file = Path.Combine(folder, "step-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(file, script, new UTF8Encoding(false));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("MARROW_TEST_PWSH") ?? "pwsh")
        {
            WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", file }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("pwsh did not finish.");
        }

        return (process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
    }

    private static string Lib() => Path.Combine(Root(), "scripts", "lib", "NthWebView2.ps1");

    // ---- the loader follows the runtime ----

    [Fact]
    public void The_loader_is_chosen_by_runtime_identifier_arm64_for_win_arm64_and_x64_for_everything_else()
    {
        var project = Text("src", "Marrow.Services", "Marrow.Services.csproj");
        Assert.Contains("""<WebView2LoaderRid Condition="'$(RuntimeIdentifier)' == 'win-arm64'">win-arm64</WebView2LoaderRid>""", project, StringComparison.Ordinal);
        Assert.Contains("""<WebView2LoaderRid Condition="'$(WebView2LoaderRid)' == ''">win-x64</WebView2LoaderRid>""", project, StringComparison.Ordinal);
        Assert.Contains(@"runtimes\$(WebView2LoaderRid)\native\WebView2Loader.dll", project, StringComparison.Ordinal);
        // One loader item, copied to the build and publish roots; no loader for any other platform is named.
        Assert.Single(project.Split('\n'), line => line.Contains("<None Include=", StringComparison.Ordinal) && line.Contains("WebView2Loader.dll", StringComparison.Ordinal));
        Assert.DoesNotContain(@"runtimes\win-x64\native", project, StringComparison.Ordinal);
        Assert.DoesNotContain(@"win-x86\native", project, StringComparison.Ordinal);
        Assert.DoesNotContain(@"win-arm64\native", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pinned_package_carries_a_loader_for_each_of_the_two_architectures_and_they_are_what_they_say()
    {
        Assert.Equal(MachineAmd64, Machine(Loader("win-x64")));
        Assert.Equal(MachineArm64, Machine(Loader("win-arm64")));
    }

    [Fact]
    public void The_build_beside_the_tests_holds_the_x64_loader_because_no_runtime_identifier_is_set()
    {
        Assert.Equal(Loader("win-x64"), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll")));
    }

    // ---- the script library ----

    [Fact]
    public void The_library_reads_the_machine_of_a_PE_image_and_nothing_else()
    {
        var x64 = Path.Combine(temp, "x64.dll");
        var arm64 = Path.Combine(temp, "arm64.dll");
        File.WriteAllBytes(x64, Loader("win-x64"));
        File.WriteAllBytes(arm64, Loader("win-arm64"));
        var (exit, output) = Pwsh($$"""
            $ErrorActionPreference = 'Stop'
            . '{{Lib()}}'
            $a = Get-NthPeMachine -Bytes ([IO.File]::ReadAllBytes('{{x64}}'))
            $b = Get-NthPeMachine -Bytes ([IO.File]::ReadAllBytes('{{arm64}}'))
            $junk = Get-NthPeMachine -Bytes ([byte[]](1..200))
            $mzOnly = Get-NthPeMachine -Bytes ([byte[]](@(0x4D,0x5A) + (0..100 | ForEach-Object { 0 })))
            $short = Get-NthPeMachine -Bytes ([byte[]](0x4D,0x5A))
            "x64=$a arm64=$b junk=[$junk] mzOnly=[$mzOnly] short=[$short]"
            "x64ok=$(Test-NthLoaderBytes -Bytes ([IO.File]::ReadAllBytes('{{x64}}')) -Runtime win-x64) x64asArm=$(Test-NthLoaderBytes -Bytes ([IO.File]::ReadAllBytes('{{x64}}')) -Runtime win-arm64)"
            "armok=$(Test-NthLoaderBytes -Bytes ([IO.File]::ReadAllBytes('{{arm64}}')) -Runtime win-arm64) armAsX64=$(Test-NthLoaderBytes -Bytes ([IO.File]::ReadAllBytes('{{arm64}}')) -Runtime win-x64)"
            """, temp);
        Assert.Equal(0, exit);
        Assert.Contains("x64=34404 arm64=43620 junk=[] mzOnly=[] short=[]", output, StringComparison.Ordinal);
        Assert.Contains("x64ok=True x64asArm=False", output, StringComparison.Ordinal);
        Assert.Contains("armok=True armAsX64=False", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("win-x86")]
    [InlineData("linux-x64")]
    [InlineData("")]
    public void A_runtime_nth_does_not_package_has_no_expected_loader(string runtime)
    {
        var (exit, output) = Pwsh($$"""
            . '{{Lib()}}'
            try { Get-NthExpectedLoaderMachine -Runtime '{{runtime}}'; 'no error' } catch { 'threw: ' + $_.Exception.Message }
            """, temp);
        Assert.Equal(0, exit);
        Assert.Contains("threw:", output, StringComparison.Ordinal);
    }

    private string MakePublishFolder(byte[] loader)
    {
        var publish = Path.Combine(temp, "publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(publish);
        File.WriteAllBytes(Path.Combine(publish, "WebView2Loader.dll"), loader);
        return publish;
    }

    private string MakePackage(byte[] loader, Action<ZipArchive>? change = null, bool rootLoader = true, bool core = true, byte[]? workerLoader = null)
    {
        var path = Path.Combine(temp, "NthApp-" + Guid.NewGuid().ToString("N") + "-full.nupkg");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            void Add(string name, byte[] bytes)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }

            Add("lib/app/nth.exe", [1, 2, 3]);
            if (rootLoader) Add("lib/app/WebView2Loader.dll", loader);
            Add("lib/app/pipeline-worker/WebView2Loader.dll", workerLoader ?? loader);
            if (core) Add("lib/app/Microsoft.Web.WebView2.Core.dll", [4, 5, 6]);
            change?.Invoke(zip);
        }

        return path;
    }

    private (int Exit, string Output) RunAssert(string package, string runtime, string publish) => Pwsh($$"""
        . '{{Lib()}}'
        try { Assert-NthWebView2Package -Package '{{package}}' -Runtime '{{runtime}}' -PublishDir '{{publish}}' } catch { 'threw: ' + $_.Exception.Message }
        """, temp);

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    public void A_package_with_the_right_loader_at_the_root_and_in_the_worker_passes(string rid)
    {
        var loader = Loader(rid);
        var (exit, output) = RunAssert(MakePackage(loader), rid, MakePublishFolder(loader));
        Assert.Equal(0, exit);
        Assert.DoesNotContain("threw:", output, StringComparison.Ordinal);
        Assert.Contains("holds WebView2Loader.dll for " + rid, output, StringComparison.Ordinal);
        Assert.Contains("2 copy/copies", output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_arm64_package_with_the_x64_loader_is_refused_and_so_is_the_reverse()
    {
        var x64 = Loader("win-x64");
        var arm64 = Loader("win-arm64");
        var (_, wrongPublish) = RunAssert(MakePackage(x64), "win-arm64", MakePublishFolder(x64));
        Assert.Contains("threw: The published WebView2Loader.dll is not a win-arm64 image", wrongPublish, StringComparison.Ordinal);
        var (_, wrongPack) = RunAssert(MakePackage(x64), "win-arm64", MakePublishFolder(arm64));
        Assert.Contains("is not a win-arm64 image", wrongPack, StringComparison.Ordinal);
        var (_, reverse) = RunAssert(MakePackage(arm64), "win-x64", MakePublishFolder(x64));
        Assert.Contains("threw:", reverse, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loader_in_the_package_that_is_not_the_published_one_is_refused()
    {
        var loader = Loader("win-x64");
        var padded = loader.Concat(new byte[] { 0 }).ToArray();
        var (_, output) = RunAssert(MakePackage(padded), "win-x64", MakePublishFolder(loader));
        Assert.Contains("threw: WebView2Loader.dll in", output, StringComparison.Ordinal);
        Assert.Contains("is not the published file", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_without_a_root_loader_or_without_the_Core_assembly_is_refused()
    {
        var loader = Loader("win-x64");
        var publish = MakePublishFolder(loader);
        var (_, noLoader) = RunAssert(MakePackage(loader, rootLoader: false), "win-x64", publish);
        Assert.Contains("exactly one WebView2Loader.dll at the app root", noLoader, StringComparison.Ordinal);
        var (_, noCore) = RunAssert(MakePackage(loader, core: false), "win-x64", publish);
        Assert.Contains("does not hold Microsoft.Web.WebView2.Core.dll", noCore, StringComparison.Ordinal);
        var (_, noPublish) = RunAssert(MakePackage(loader), "win-x64", Path.Combine(temp, "no-such-folder"));
        Assert.Contains("was not published", noPublish, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Microsoft.Web.WebView2.Wpf.dll")]
    [InlineData("Microsoft.Web.WebView2.WinForms.dll")]
    public void A_package_that_carries_the_SDKs_WPF_or_WinForms_assembly_is_refused(string assembly)
    {
        var loader = Loader("win-x64");
        var package = MakePackage(loader, zip => zip.CreateEntry("lib/app/" + assembly).Open().Dispose());
        var (_, output) = RunAssert(package, "win-x64", MakePublishFolder(loader));
        Assert.Contains("threw:", output, StringComparison.Ordinal);
        Assert.Contains(assembly, output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loader_of_the_wrong_architecture_in_the_pipeline_worker_folder_is_refused_too()
    {
        var x64 = Loader("win-x64");
        var package = MakePackage(x64, workerLoader: Loader("win-arm64"));
        var (_, output) = RunAssert(package, "win-x64", MakePublishFolder(x64));
        Assert.Contains("lib/app/pipeline-worker/WebView2Loader.dll", output, StringComparison.Ordinal);
        Assert.Contains("is not a win-x64 image", output, StringComparison.Ordinal);
    }

    private string MakeRepo(string runtimeIdentifiers, string lockTargets)
    {
        var repo = Path.Combine(temp, "repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(repo, "src", "Marrow.App"));
        File.WriteAllText(Path.Combine(repo, "Directory.Build.props"), $"<Project><PropertyGroup><RuntimeIdentifiers>{runtimeIdentifiers}</RuntimeIdentifiers></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(repo, "src", "Marrow.App", "packages.lock.json"), lockTargets);
        return repo;
    }

    private (int Exit, string Output) Restorable(string repo, string runtime) => Pwsh($$"""
        . '{{Lib()}}'
        try { Assert-NthRuntimeRestorable -Root '{{repo}}' -Runtime '{{runtime}}'; 'ok' } catch { 'threw: ' + $_.Exception.Message }
        """, temp);

    [Fact]
    public void Packaging_win_x64_needs_nothing_and_win_arm64_says_plainly_what_is_missing_before_anything_is_built()
    {
        const string x64Only = """{ "dependencies": { "net10.0-windows10.0.19041": {}, "net10.0-windows10.0.19041/win-x64": {} } }""";
        const string both = """{ "dependencies": { "net10.0-windows10.0.19041": {}, "net10.0-windows10.0.19041/win-x64": {}, "net10.0-windows10.0.19041/win-arm64": {} } }""";

        Assert.Contains("ok", Restorable(MakeRepo("win-x64", x64Only), "win-x64").Output, StringComparison.Ordinal);

        var noProps = Restorable(MakeRepo("win-x64", x64Only), "win-arm64").Output;
        Assert.Contains("threw:", noProps, StringComparison.Ordinal);
        Assert.Contains("Directory.Build.props", noProps, StringComparison.Ordinal);
        Assert.Contains("voice natives", noProps, StringComparison.Ordinal);

        var noLock = Restorable(MakeRepo("win-x64;win-arm64", x64Only), "win-arm64").Output;
        Assert.Contains("packages.lock.json has no win-arm64 target", noLock, StringComparison.Ordinal);

        Assert.Contains("ok", Restorable(MakeRepo("win-x64;win-arm64", both), "win-arm64").Output, StringComparison.Ordinal);
        Assert.Contains("threw:", Restorable(MakeRepo("win-x64;win-arm64", both), "win-x86").Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_real_repo_ships_win_x64_only_today_so_an_arm64_package_stops_up_front_with_the_reason()
    {
        var (_, win64) = Restorable(Root(), "win-x64");
        Assert.Contains("ok", win64, StringComparison.Ordinal);
        var (_, arm64) = Restorable(Root(), "win-arm64");
        // Until the orchestrator adds win-arm64 to Directory.Build.props and regenerates the lock files (ED-NEW-web-w23-1 item 4).
        if (Text("Directory.Build.props").Contains("win-arm64", StringComparison.Ordinal)) Assert.Contains("ok", arm64, StringComparison.Ordinal);
        else Assert.Contains("Directory.Build.props", arm64, StringComparison.Ordinal);
    }

    // ---- the package script ----

    [Fact]
    public void The_package_script_checks_the_loader_up_front_after_the_notes_check_and_in_the_package_after_the_licence_check()
    {
        var lines = Text("scripts", "package-windows.ps1").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        int At(string needle) => Array.FindIndex(lines, l => !l.TrimStart().StartsWith('#') && l.Contains(needle, StringComparison.Ordinal));
        var notes = At("$releaseNotes = Get-ReleaseNotes");
        var validateOnly = At("if ($ValidateReleaseNotesOnly)");
        var dotSource = At("lib/NthWebView2.ps1");
        var restorable = At("Assert-NthRuntimeRestorable -Root $root -Runtime $Runtime");
        var publish = At("dotnet publish $project");
        var licence = At("holds LICENSE, THIRD_PARTY_NOTICES.md and TRADEMARK.md beside");
        var package = At("Assert-NthWebView2Package -Package $fullPackage.FullName -Runtime $Runtime -PublishDir $publishDir");
        var installer = At("dotnet publish $setupProject");
        Assert.All(new[] { notes, validateOnly, dotSource, restorable, publish, licence, package, installer }, index => Assert.True(index >= 0));
        // The bare-repository notes check (ValidateReleaseNotesOnly) comes first and needs no library; the library is read after it.
        Assert.True(notes < validateOnly && validateOnly < dotSource && dotSource < restorable);
        Assert.True(restorable < publish, "the restore graph is checked before anything is built");
        Assert.True(publish < licence && licence < package && package < installer, "the loader is proven in the package before any installer is built from it");
    }

    [Fact]
    public void The_package_script_still_parses_and_so_does_every_script_this_unit_adds()
    {
        foreach (var script in new[]
        {
            Path.Combine("scripts", "package-windows.ps1"),
            Path.Combine("scripts", "lib", "NthWebView2.ps1"),
            Path.Combine("scripts", "ci", "Install-EdgeCanary.ps1"),
            Path.Combine("scripts", "vm", "Test-NthWebGuest.ps1"),
            Path.Combine("scripts", "vm", "Invoke-NthCleanVmMatrix.ps1"),
        })
        {
            var (exit, output) = Pwsh($$"""
                $errors = $null; $tokens = $null
                [void][System.Management.Automation.Language.Parser]::ParseFile('{{Path.Combine(Root(), script)}}', [ref]$tokens, [ref]$errors)
                "errors=$($errors.Count)"
                $errors | ForEach-Object { $_.Message + ' @' + $_.Extent.StartLineNumber }
                """, temp);
            Assert.Equal(0, exit);
            Assert.Contains("errors=0", output, StringComparison.Ordinal);
        }
    }

    // ---- the weekly Canary job ----

    private static string Workflow() => Text(".github", "workflows", "web-canary.yml");

    [Fact]
    public void The_Canary_job_runs_weekly_and_on_demand_and_selects_the_Canary_channel_by_the_documented_variables()
    {
        var workflow = Workflow();
        var cron = Regex.Match(workflow, @"- cron: '(?<expr>[^']+)'");
        Assert.True(cron.Success, "a schedule trigger");
        var fields = cron.Groups["expr"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, fields.Length);
        Assert.Matches(@"^[0-6]$", fields[4]);          // one weekday: weekly
        Assert.Matches(@"^[0-9]+$", fields[0]);
        Assert.Matches(@"^[0-9]+$", fields[1]);
        Assert.Equal(["*", "*"], fields[2..4]);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("WEBVIEW2_CHANNEL_SEARCH_KIND: '1'", workflow, StringComparison.Ordinal);
        Assert.Contains("WEBVIEW2_RELEASE_CHANNELS: '3'", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/ci/Install-EdgeCanary.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Canary_job_runs_the_engine_tests_against_Canary_and_the_baseline_without_it()
    {
        var workflow = Workflow();
        var baseline = workflow.IndexOf("FullyQualifiedName~Web&FullyQualifiedName!~Native", StringComparison.Ordinal);
        var install = workflow.IndexOf("Install-EdgeCanary.ps1", StringComparison.Ordinal);
        var canary = workflow.IndexOf("FullyQualifiedName~Web&FullyQualifiedName~Native", StringComparison.Ordinal);
        Assert.True(baseline >= 0 && install > baseline && canary > install, "baseline, then Canary installed, then the engine tests");
        // The channel variables belong to the Canary step only: the baseline must run on the stable runtime.
        Assert.True(workflow.IndexOf("WEBVIEW2_CHANNEL_SEARCH_KIND: '1'", StringComparison.Ordinal) > install);
        Assert.True(workflow.IndexOf("WEBVIEW2_RELEASE_CHANNELS: '3'", StringComparison.Ordinal) > install);
    }

    [Fact]
    public void The_Canary_job_publishes_signs_releases_and_pushes_nothing_and_holds_no_secret()
    {
        var workflow = Workflow();
        Assert.Matches(@"(?m)^permissions:\s*\n  contents: read\s*\n  issues: write\s*\n", workflow);
        foreach (var forbidden in new[] { "vpk ", "dotnet publish", "git push", "gh release", "signtool", "secrets.", "pull_request", "push:", "upload github", "actions/create-release" })
            Assert.DoesNotContain(forbidden, workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GH_TOKEN: ${{ github.token }}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Canary_installer_script_downloads_only_the_documented_link_and_runs_nothing_that_is_not_signed_by_Microsoft()
    {
        var script = Text("scripts", "ci", "Install-EdgeCanary.ps1");
        Assert.Contains("https://go.microsoft.com/fwlink/?linkid=2084649&Channel=Canary&language=en", script, StringComparison.Ordinal);
        var signature = script.IndexOf("Get-AuthenticodeSignature", StringComparison.Ordinal);
        var start = script.IndexOf("Start-Process", StringComparison.Ordinal);
        Assert.True(signature > 0 && start > signature, "the signature is checked before anything is started");
        Assert.Contains("CN=Microsoft Corporation", script, StringComparison.Ordinal);
    }

    // ---- the clean-VM scripts ----

    [Fact]
    public void The_VM_matrix_has_the_four_cells_and_never_runs_the_installer_on_the_host()
    {
        var script = Text("scripts", "vm", "Invoke-NthCleanVmMatrix.ps1");
        foreach (var cell in new[] { "x64-runtime", "x64-noruntime", "arm64-runtime", "arm64-noruntime" })
            Assert.Contains("'" + cell + "'", script, StringComparison.Ordinal);
        foreach (var phase in new[] { "PreInstall", "Installed", "SeedWebData", "Uninstalled" })
            Assert.Contains("'" + phase + "'", script, StringComparison.Ordinal);
        Assert.Contains("Restore-VMCheckpoint", script, StringComparison.Ordinal);
        Assert.Contains("PowerShell Direct", script, StringComparison.Ordinal);
        // Both Start-Process calls (install, uninstall) sit inside an Invoke-Command script block, that is, in the guest; the host never starts the installer.
        Assert.Equal(2, Regex.Matches(script, "Start-Process").Count);
        foreach (var line in script.Split('\n').Where(l => l.Contains("Start-Process", StringComparison.Ordinal)))
            Assert.Contains("$p = Start-Process -FilePath ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("& $Installer", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Start-Process -FilePath $Installer", script, StringComparison.Ordinal);
        Assert.Contains(@"Start-Process -FilePath 'C:\nth-w23\nth-Setup.exe'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guest_checks_cover_the_loader_the_runtime_state_and_the_web_data_folder_after_uninstall()
    {
        var script = Text("scripts", "vm", "Test-NthWebGuest.ps1");
        Assert.Contains("ValidateSet('PreInstall', 'Installed', 'SeedWebData', 'Uninstalled')", script, StringComparison.Ordinal);
        Assert.Contains("F3017226-FE2A-4295-8BDF-00C3A9A7E4C5", script, StringComparison.Ordinal);
        Assert.Contains("Test-NthLoaderBytes", script, StringComparison.Ordinal);
        Assert.Contains("the web data folder is gone", script, StringComparison.Ordinal);
        Assert.Contains("the rest of nth''s data folder was left alone", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.Ordinal);   // it only looks, and seeds in one phase
        Assert.DoesNotContain("Start-Process", script, StringComparison.Ordinal);
    }
}
