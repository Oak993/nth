using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

// LoginEnvironment (Shell.swift:327-462) on Windows. No login shell is spawned: read() gives what fallback() gives, the
// process environment with the fifteen excluded keys removed, PATH through augmentedPath and LANG=en_US.UTF-8 when
// absent (ED-028 (3), ED-170 (2)). augmentedPath appends only the five entries under the home folder, split and joined on
// ';', each unless an entry already equals it ignoring letter case after one trailing separator is trimmed (ED-170 (1)).
// which searches the given environment's PATH, else current's, else augmentedPath(nil) (ED-170 (3)), with the PATHEXT
// of ED-171 and the path rules of ED-031, ED-129 and ED-130. Environments and home folders are injected through the
// internal overloads and a Loader of the test's own, so nothing here reads or changes the process's own login
// environment; the process-wide members are in ShellLoginEnvironmentProcessTests.
public sealed class ShellLoginEnvironmentTests
{
    private const string Home = @"C:\Users\Tester";

    private static readonly string[] HomeEntries = LoginEnvironmentSamples.HomeEntries(Home);

    private static string Joined(params string[] entries) => string.Join(";", entries);

    // For the Loader tests that do not look at git: a fixed answer, so no lookup touches a real folder.
    private static readonly Func<IReadOnlyDictionary<string, string>, string> UnusedGit = _ => @"X:\not-looked-at\git.exe";

    // ---- augmentedPath (:441-453) ----

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:441-453
    [Trait("Decision", "ED-170")]
    public void AugmentedPath_of_nil_or_empty_is_the_five_home_entries_in_the_Swift_order_joined_with_semicolons()
    {
        var expected = @"C:\Users\Tester\.local\bin;C:\Users\Tester\.bun\bin;C:\Users\Tester\.cargo\bin;" +
            @"C:\Users\Tester\.npm-global\bin;C:\Users\Tester\.volta\bin";
        Assert.Equal(expected, LoginEnvironment.AugmentedPath(null, Home));
        Assert.Equal(expected, LoginEnvironment.AugmentedPath("", Home));
        Assert.Equal(expected, LoginEnvironment.AugmentedPath(";;", Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:444-448
    [Trait("Decision", "ED-170")]
    public void AugmentedPath_appends_none_of_the_seven_absolute_macOS_locations()
    {
        var augmented = LoginEnvironment.AugmentedPath(@"C:\Windows\system32", Home);
        var entries = augmented.Split(';');
        Assert.Equal(6, entries.Length);
        foreach (var macOS in LoginEnvironmentSamples.MacOSEntries)
        {
            Assert.DoesNotContain(macOS, entries);
            Assert.DoesNotContain(macOS.Replace('/', '\\'), entries);
        }
    }

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:443,449-452
    [Trait("Decision", "ED-170")]
    public void AugmentedPath_keeps_existing_entries_first_in_their_order_and_appends_only_the_missing_home_entries()
    {
        var path = Joined(@"C:\z", HomeEntries[2], @"C:\a", HomeEntries[4]);
        Assert.Equal(Joined(@"C:\z", HomeEntries[2], @"C:\a", HomeEntries[4], HomeEntries[0], HomeEntries[1], HomeEntries[3]),
            LoginEnvironment.AugmentedPath(path, Home));
        // Every home entry already present: nothing is appended, so a second pass changes nothing.
        var full = LoginEnvironment.AugmentedPath(@"C:\z", Home);
        Assert.Equal(full, LoginEnvironment.AugmentedPath(full, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:443,452
    [Trait("Decision", "ED-170")]
    public void AugmentedPath_drops_empty_PATH_entries_as_split_does()
    {
        Assert.Equal(Joined([@"C:\a", @"C:\b", .. HomeEntries]), LoginEnvironment.AugmentedPath(@";;C:\a;;C:\b;", Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:449-451
    [Trait("Decision", "ED-170")]
    public void An_entry_that_differs_only_in_letter_case_or_one_trailing_separator_counts_as_present()
    {
        var path = Joined(@"c:\users\TESTER\.LOCAL\BIN\", @"C:\Users\Tester\.bun\bin/", @"C:\Users\Tester\.cargo\bin\\",
            "C:/Users/Tester/.npm-global/bin");
        // .local and .bun are present; .cargo (two trailing separators), .npm-global (forward slashes) and .volta are not.
        Assert.Equal(Joined(@"c:\users\TESTER\.LOCAL\BIN\", @"C:\Users\Tester\.bun\bin/", @"C:\Users\Tester\.cargo\bin\\",
                "C:/Users/Tester/.npm-global/bin", HomeEntries[2], HomeEntries[3], HomeEntries[4]),
            LoginEnvironment.AugmentedPath(path, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:449-451
    [Trait("Decision", "ED-170")]
    [Trait("Decision", "ED-137")]
    public void Letter_case_is_compared_by_the_Windows_ordinal_table_not_by_OrdinalIgnoreCase()
    {
        // .NET's OrdinalIgnoreCase takes U+00B5 MICRO SIGN for U+039C GREEK CAPITAL LETTER MU; the file system and the
        // Windows ordinal table (ED-137) do not, so the micro-sign folder's .local\bin is another folder and does not count.
        var micro = "C:\\Users\\\U000000B5\\.local\\bin"; // U+00B5 MICRO SIGN
        var home = "C:\\Users\\\U0000039C"; // U+039C GREEK CAPITAL LETTER MU
        Assert.Equal(0, string.Compare(micro, Path.Join(home, ".local", "bin"), StringComparison.OrdinalIgnoreCase));
        var augmented = LoginEnvironment.AugmentedPath(micro, home).Split(';');
        Assert.Equal(new[] { micro }.Concat(LoginEnvironmentSamples.HomeEntries(home)), augmented);
    }

    // ---- fallback (:455-461) and read (:367-391) ----

    [Fact]
    [Trait("Req", "PR-TERM-102")] // Core/Support/Shell.swift:335-339,455-457
    [Trait("Decision", "ED-170")]
    [Trait("Decision", "ED-128")]
    public void Fallback_removes_the_15_excluded_keys_in_any_letter_case_and_keeps_every_other_variable()
    {
        string[] spellings =
        [
            "_", "shlvl", "PWD", "oldpwd", "TERM", "term_program", "TERM_PROGRAM_VERSION", "term_session_id", "COLORTERM",
            "iterm_session_id", "XPC_SERVICE_NAME", "__cfbundleidentifier", "CLAUDECODE", "claude_code_entrypoint",
            "Claude_Code_SSE_Port",
        ];
        Assert.Equal(15, spellings.Length);
        Assert.Equal(LoginEnvironmentSamples.ExcludedKeys, spellings, StringComparer.OrdinalIgnoreCase);
        var process = spellings.Select((name, i) => new KeyValuePair<string, string>(name, "excluded " + i)).ToList();
        process.AddRange(LoginEnvironmentSamples.Pairs(("TERMINAL", "1"), ("_X", "2"), ("CLAUDECODE_X", "3"), ("PWD2", "4"),
            ("Keep", "5"), ("Path", @"C:\a"), ("LANG", "C")));
        var environment = LoginEnvironment.Fallback(process, Home);
        Assert.Equal(new[] { "CLAUDECODE_X", "Keep", "LANG", "PWD2", "Path", "TERMINAL", "_X" },
            environment.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal("1", environment["TERMINAL"]);
        Assert.Equal("2", environment["_X"]);
        Assert.Equal("3", environment["CLAUDECODE_X"]);
        Assert.Equal("4", environment["PWD2"]);
        Assert.Equal("5", environment["Keep"]);
        Assert.Equal("C", environment["LANG"]);
        foreach (var excluded in LoginEnvironmentSamples.ExcludedKeys) Assert.False(environment.ContainsKey(excluded), excluded);
    }

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:458
    [Trait("Decision", "ED-170")]
    [Trait("Decision", "ED-128")]
    public void Fallback_sets_PATH_through_augmentedPath_under_the_spelling_the_process_gave_it()
    {
        var inherited = LoginEnvironment.Fallback(LoginEnvironmentSamples.Pairs(("Path", @"C:\a;;C:\b\"), ("LANG", "C")), Home);
        // environment["PATH"] = ... on a case-insensitive dictionary keeps the first spelling (ED-128 (1)).
        Assert.Equal(new[] { "LANG", "Path" }, inherited.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(Joined([@"C:\a", @"C:\b\", .. HomeEntries]), inherited["PATH"]);
        Assert.Equal(inherited["PATH"], inherited["path"]);

        var absent = LoginEnvironment.Fallback(LoginEnvironmentSamples.Pairs(("LANG", "C")), Home);
        Assert.Equal(new[] { "LANG", "PATH" }, absent.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(Joined(HomeEntries), absent["PATH"]);
    }

    [Fact]
    [Trait("Req", "PR-TERM-101")] // Core/Support/Shell.swift:367-388
    [Trait("Decision", "ED-170")]
    [Trait("Decision", "ED-028")]
    public async Task A_loaded_environment_is_the_fallback_with_no_login_shell_read()
    {
        var process = LoginEnvironmentSamples.Pairs(("Path", @"C:\a"), ("TERM", "xterm-256color"), ("SHLVL", "2"),
            ("_", "/usr/bin/env"), ("HOME", @"C:\Users\Tester"), ("Keep", "value=with=equals"));
        var loader = new LoginEnvironment.Loader(() => LoginEnvironment.Fallback(process, Home),
            path => LoginEnvironment.AugmentedPath(path, Home), UnusedGit);
        await loader.Load().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(loader.IsLoaded);
        var current = loader.Current;
        Assert.Equal(new[] { "HOME", "Keep", "LANG", "Path" }, current.Keys.OrderBy(key => key, StringComparer.Ordinal));
        // augmentedPath runs in fallback() and again in read() (:386); the second pass finds every entry present.
        Assert.Equal(Joined([@"C:\a", .. HomeEntries]), current["PATH"]);
        Assert.Equal("en_US.UTF-8", current["LANG"]);
        Assert.Equal(@"C:\Users\Tester", current["HOME"]);
        Assert.Equal("value=with=equals", current["Keep"]);
    }

    [Fact]
    [Trait("Req", "PR-TERM-101")] // Core/Support/Shell.swift:341-343,455-461
    [Trait("Decision", "ED-170")]
    [Trait("Decision", "ED-128")]
    public void Current_holds_the_process_environment_without_the_excluded_keys_with_the_augmented_PATH()
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var host = ShellScripts.HostEnvironment();
        var current = LoginEnvironment.Current;
        foreach (var excluded in LoginEnvironmentSamples.ExcludedKeys) Assert.False(current.ContainsKey(excluded), excluded);
        Assert.Equal(LoginEnvironmentSamples.ExpectedAugmentedPath(host.GetValueOrDefault("PATH"), home), current["PATH"]);
        Assert.Equal(host.GetValueOrDefault("LANG") ?? "en_US.UTF-8", current["LANG"]);
        foreach (var (name, value) in host)
        {
            if (LoginEnvironmentSamples.ExcludedKeys.Contains(name, StringComparer.OrdinalIgnoreCase) || name.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                continue;
            Assert.Equal(value, current[name]);
        }
        // Copies keep the case-insensitive comparer (ED-128 (1)).
        var copy = current.Mutating(environment => environment["path"] = "x");
        Assert.Equal("x", copy["PATH"]);
        Assert.Equal(current.Count, copy.Count);
    }

    // ---- load (:357-365), current and isLoaded (:341-351) ----

    [Fact]
    [Trait("Req", "PR-TERM-104")] // Core/Support/Shell.swift:331-334,357-365
    public async Task Concurrent_loads_share_one_task_and_one_read_and_all_return_once_the_environment_is_cached()
    {
        var reads = 0;
        var process = LoginEnvironmentSamples.Pairs(("Path", @"C:\a"));
        // The loader's augmentedPath runs once per read (:386), so it counts the reads.
        var loader = new LoginEnvironment.Loader(() => LoginEnvironment.Fallback(process, Home), path =>
        {
            Interlocked.Increment(ref reads);
            return LoginEnvironment.AugmentedPath(path, Home);
        }, UnusedGit);
        Assert.False(loader.IsLoaded);
        using var start = new ManualResetEventSlim();
        var racers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return loader.Loading();
        })).ToArray();
        start.Set();
        var handles = await Task.WhenAll(racers).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(handles, handle => Assert.Same(handles[0], handle));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(loader.Load))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(loader.IsLoaded);
        // A later caller gets the same, finished task: the read is never repeated.
        Assert.Same(handles[0], loader.Loading());
        await loader.Load().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, reads);
        Assert.Equal(Joined([@"C:\a", .. HomeEntries]), loader.Current["PATH"]);
    }

    [Fact]
    [Trait("Req", "PR-TERM-105")] // Core/Support/Shell.swift:341-347,371
    public async Task The_fallback_is_built_once_and_is_current_until_the_load_lands()
    {
        var builds = 0;
        var process = LoginEnvironmentSamples.Pairs(("Path", @"C:\a"), ("TERM", "xterm"));
        var loader = new LoginEnvironment.Loader(() =>
        {
            Interlocked.Increment(ref builds);
            return LoginEnvironment.Fallback(process, Home);
        }, path => LoginEnvironment.AugmentedPath(path, Home), UnusedGit);
        var before = loader.Current;
        Assert.Same(before, loader.Current);
        Assert.False(loader.IsLoaded);
        Assert.Equal(1, builds);
        await loader.Load().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(loader.IsLoaded);
        // read() starts from the same fallback (:371) and never builds another.
        Assert.Equal(1, builds);
        Assert.Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal), loader.Current.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-TERM-105")] // Core/Support/Shell.swift:455-461
    [Trait("Decision", "ED-170")]
    [Trait("Decision", "ED-128")]
    public void Fallback_sets_LANG_to_en_US_UTF8_only_when_the_process_has_no_LANG()
    {
        var absent = LoginEnvironment.Fallback(LoginEnvironmentSamples.Pairs(("PATH", @"C:\a")), Home);
        Assert.Equal("en_US.UTF-8", absent["LANG"]);
        Assert.Contains("LANG", absent.Keys);
        // An existing LANG is left alone, under its own spelling (ED-128 (1)).
        var other = LoginEnvironment.Fallback(LoginEnvironmentSamples.Pairs(("lang", "de_DE.UTF-8")), Home);
        Assert.Equal("de_DE.UTF-8", other["LANG"]);
        Assert.Equal(new[] { "PATH", "lang" }, other.Keys.OrderBy(key => key, StringComparer.Ordinal));
        // `environment["LANG"] == nil` (:459): an empty LANG is present, so it stays empty.
        Assert.Equal("", LoginEnvironment.Fallback(LoginEnvironmentSamples.Pairs(("LANG", "")), Home)["LANG"]);
    }

    // ---- git (:389-390, :393-413) with ED-174 ----

    [Fact]
    [Trait("Req", "PR-TERM-106")] // Core/Support/Shell.swift:404-405
    [Trait("Decision", "ED-174")]
    public void Git_found_on_the_environments_PATH_wins_over_every_install_folder()
    {
        var onPath = LoginEnvironmentSamples.Folder("git on path", "git.exe");
        var environment = LoginEnvironmentSamples.GitEnvironment(onPath,
            LoginEnvironmentSamples.Folder("git program files", @"Git\cmd\git.exe"),
            LoginEnvironmentSamples.Folder("git program files x86", @"Git\cmd\git.exe"),
            LoginEnvironmentSamples.Folder("git local app data", @"Programs\Git\cmd\git.exe"));
        Assert.Equal(Path.Join(onPath, "git.exe"), LoginEnvironment.DeveloperGit(environment, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-106")] // Core/Support/Shell.swift:405
    [Trait("Decision", "ED-174")]
    public void Without_git_on_PATH_the_first_install_folder_that_holds_git_exe_as_a_file_is_taken_in_order()
    {
        var programFiles = LoginEnvironmentSamples.Folder("git program files");
        var x86 = LoginEnvironmentSamples.Folder("git program files x86");
        var local = LoginEnvironmentSamples.Folder("git local app data");
        var environment = LoginEnvironmentSamples.GitEnvironment(LoginEnvironmentSamples.Folder("git empty path"), programFiles, x86, local);
        var programFilesGit = Path.Join(programFiles, "Git", "cmd", "git.exe");
        var x86Git = Path.Join(x86, "Git", "cmd", "git.exe");
        var localGit = Path.Join(local, "Programs", "Git", "cmd", "git.exe");
        LoginEnvironmentSamples.CreateFile(localGit);
        Assert.Equal(localGit, LoginEnvironment.DeveloperGit(environment, Home));
        LoginEnvironmentSamples.CreateFile(x86Git);
        Assert.Equal(x86Git, LoginEnvironment.DeveloperGit(environment, Home));
        // A folder named git.exe is not a file, so it does not count.
        Directory.CreateDirectory(programFilesGit);
        Assert.Equal(x86Git, LoginEnvironment.DeveloperGit(environment, Home));
        Directory.Delete(programFilesGit);
        LoginEnvironmentSamples.CreateFile(programFilesGit);
        Assert.Equal(programFilesGit, LoginEnvironment.DeveloperGit(environment, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-106")] // Core/Support/Shell.swift:398-400,405
    [Trait("Decision", "ED-174")]
    public void With_no_git_anywhere_the_value_is_Program_Files_Git_cmd_git_exe_all_the_same()
    {
        var programFiles = LoginEnvironmentSamples.Folder("git program files");
        var environment = LoginEnvironmentSamples.GitEnvironment(LoginEnvironmentSamples.Folder("git empty path"), programFiles,
            LoginEnvironmentSamples.Folder("git program files x86"), LoginEnvironmentSamples.Folder("git local app data"));
        var git = LoginEnvironment.DeveloperGit(environment, Home);
        Assert.Equal(Path.Join(programFiles, "Git", "cmd", "git.exe"), git);
        Assert.False(File.Exists(git));
    }

    [Fact]
    [Trait("Req", "PR-TERM-106")] // Core/Support/Shell.swift:389-390,398-402
    [Trait("Decision", "ED-174")]
    public async Task Git_resolves_on_every_read_before_the_load_and_once_for_good_at_the_load()
    {
        var onPath = LoginEnvironmentSamples.Folder("git memo path", "git.exe");
        var programFiles = LoginEnvironmentSamples.Folder("git memo program files");
        var process = LoginEnvironmentSamples.GitEnvironment(onPath, programFiles,
            LoginEnvironmentSamples.Folder("git memo program files x86"), LoginEnvironmentSamples.Folder("git memo local app data")).ToList();
        var resolutions = 0;
        var loader = new LoginEnvironment.Loader(() => LoginEnvironment.Fallback(process, Home),
            path => LoginEnvironment.AugmentedPath(path, Home), environment =>
            {
                Interlocked.Increment(ref resolutions);
                return LoginEnvironment.DeveloperGit(environment, Home);
            });
        var pathGit = Path.Join(onPath, "git.exe");
        var fallbackGit = Path.Join(programFiles, "Git", "cmd", "git.exe");
        // Before the load nothing is stored, so git looks again on each read, over current (:399).
        Assert.Equal(pathGit, loader.Git);
        File.Delete(pathGit);
        Assert.Equal(fallbackGit, loader.Git);
        LoginEnvironmentSamples.CreateFile(pathGit);
        Assert.Equal(2, resolutions);
        // The load resolves once over the loaded environment and stores the answer (:389-390).
        await loader.Load().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(3, resolutions);
        Assert.Equal(pathGit, loader.Git);
        File.Delete(pathGit);
        Assert.Equal(pathGit, loader.Git);
        await loader.Load().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(pathGit, loader.Git);
        Assert.Equal(3, resolutions);
    }

    // ---- which (:415-427) ----

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:421-426
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-128")]
    public void Which_searches_the_given_environments_PATH_left_to_right_whatever_the_letter_case_of_its_name()
    {
        var first = ProcessTestFolders.Create("login which first");
        var second = ProcessTestFolders.Create("login which second");
        ProcessTestFolders.Touch(first, "tool.cmd");
        ProcessTestFolders.Touch(second, "tool.exe");
        var environment = LoginEnvironmentSamples.Environment(("Path", first + ";" + second), ("PATHEXT", ".exe;.cmd"));
        // First directory first, then PATHEXT order inside it (ED-031 (2), (4)).
        Assert.Equal(Path.Join(first, "tool.cmd"), LoginEnvironment.Which("tool", environment, Home));
        environment["PATH"] = second + ";" + first;
        Assert.Equal(Path.Join(second, "tool.exe"), LoginEnvironment.Which("tool", environment, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:421,441-453
    [Trait("Decision", "ED-170")]
    public void Which_without_PATH_in_the_environment_searches_augmentedPath_of_nil()
    {
        var home = LoginEnvironmentSamples.Home(@".cargo\bin\tool.exe", @".volta\bin\tool.exe", @"elsewhere\tool.exe");
        var environment = LoginEnvironmentSamples.Environment(("PATHEXT", ".exe"));
        Assert.Equal(Path.Join(home, ".cargo", "bin", "tool.exe"), LoginEnvironment.Which("tool", environment, home));
        // .local\bin comes first among the five.
        File.WriteAllBytes(Path.Join(Directory.CreateDirectory(Path.Join(home, ".local", "bin")).FullName, "tool.exe"), []);
        Assert.Equal(Path.Join(home, ".local", "bin", "tool.exe"), LoginEnvironment.Which("tool", environment, home));
        Assert.Null(LoginEnvironment.Which("absent", environment, home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:415-427
    [Trait("Decision", "ED-171")]
    public void Which_takes_PATHEXT_from_the_environment_it_searches_looked_up_case_insensitively()
    {
        var folder = ProcessTestFolders.Create("login which pathext");
        ProcessTestFolders.Touch(folder, "tool.exe");
        ProcessTestFolders.Touch(folder, "tool.cmd");
        Assert.Equal(Path.Join(folder, "tool.cmd"),
            LoginEnvironment.Which("tool", LoginEnvironmentSamples.Environment(("PATH", folder), ("pathext", ".cmd;.exe")), Home));
        Assert.Equal(Path.Join(folder, "tool.exe"),
            LoginEnvironment.Which("tool", LoginEnvironmentSamples.Environment(("PATH", folder), ("PATHEXT", ".exe;.cmd")), Home));
        // A caller's dictionary that compares names exactly still has its PathExt found.
        var exact = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = folder, ["PathExt"] = ".cmd" };
        Assert.Equal(Path.Join(folder, "tool.cmd"), LoginEnvironment.Which("tool", exact, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:417-420
    [Trait("Decision", "ED-171")]
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-129")]
    public void Which_given_a_path_tests_that_path_with_the_environments_PATHEXT_and_never_searches_PATH()
    {
        var folder = ProcessTestFolders.Create("login which path");
        var onPath = ProcessTestFolders.Create("login which on path");
        ProcessTestFolders.Touch(folder, "codex.cmd");
        ProcessTestFolders.Touch(folder, "codex.exe");
        ProcessTestFolders.Touch(onPath, "codex.exe");
        var environment = LoginEnvironmentSamples.Environment(("PATH", onPath), ("PATHEXT", ".cmd;.exe"));
        Assert.Equal(Path.Join(folder, "codex.cmd"), LoginEnvironment.Which(Path.Join(folder, "codex"), environment, Home));
        Assert.Null(LoginEnvironment.Which(Path.Join(folder, "other"), environment, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:417-419
    [Trait("Decision", "ED-130")]
    public void Which_expands_a_leading_tilde_before_either_separator_to_the_home_folder()
    {
        var home = LoginEnvironmentSamples.Home(@"bin\tool.exe");
        var environment = LoginEnvironmentSamples.Environment(("PATH", ""), ("PATHEXT", ".exe"));
        Assert.Equal(Path.Join(home, "bin", "tool.exe"), LoginEnvironment.Which(@"~\bin\tool", environment, home));
        Assert.Equal(Path.Join(home, "bin", "tool.exe"),
            LoginEnvironment.Which("~/bin/tool", environment, home) is { } found ? Path.GetFullPath(found) : null);
    }

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:421-426
    [Trait("Decision", "ED-031")]
    public void Which_returns_nil_when_no_directory_has_the_tool()
    {
        var folder = ProcessTestFolders.Create("login which empty");
        Assert.Null(LoginEnvironment.Which("tool", LoginEnvironmentSamples.Environment(("PATH", folder), ("PATHEXT", ".exe")), Home));
        Assert.Null(LoginEnvironment.Which("tool", LoginEnvironmentSamples.Environment(("PATH", ""), ("PATHEXT", ".exe")), Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:421-426
    [Trait("Decision", "ED-127")]
    public void Which_does_not_itself_reject_the_Windows_command_interpreter()
    {
        // ED-127 (2): the %SystemRoot% exclusion is the Command Code resolver's, not which's.
        var environment = LoginEnvironmentSamples.Environment(("PATH", System.Environment.SystemDirectory), ("PATHEXT", ".COM;.EXE;.BAT;.CMD"));
        // The extension is appended as PATHEXT spells it (ED-129).
        Assert.Equal(Path.Join(System.Environment.SystemDirectory, "cmd.EXE"), LoginEnvironment.Which("cmd", environment, Home));
    }

    [Fact]
    [Trait("Req", "PR-TERM-100")] // Core/Support/Shell.swift:415,421
    public void Without_an_environment_which_searches_the_PATH_of_current()
    {
        // The test host's PATH, and so current's (fallback keeps the process PATH first), holds System32.
        var found = LoginEnvironment.Which("cmd");
        Assert.NotNull(found);
        Assert.Equal(Path.Join(System.Environment.SystemDirectory, "cmd.exe"), found, ignoreCase: true);
        Assert.Equal(found, LoginEnvironment.Which("cmd", null));
    }

    [Fact]
    [Trait("Req", "PR-TERM-103")] // Core/Support/Shell.swift:353-355,441-442
    [Trait("Decision", "ED-170")]
    public void The_home_folder_is_the_users_profile_folder_and_augmentedPath_appends_under_it()
    {
        var profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        Assert.Equal(profile, LoginEnvironment.HomeDirectory);
        Assert.Equal(string.Join(";", LoginEnvironmentSamples.HomeEntries(profile)), LoginEnvironment.AugmentedPath(null));
        Assert.Equal(@"C:\x;" + string.Join(";", LoginEnvironmentSamples.HomeEntries(profile)), LoginEnvironment.AugmentedPath(@"C:\x"));
    }
}
