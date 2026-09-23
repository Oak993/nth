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
        Assert.True(string.Equals(micro, Path.Join(home, ".local", "bin"), StringComparison.OrdinalIgnoreCase));
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
            path => LoginEnvironment.AugmentedPath(path, Home));
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
        Assert.Equal(Path.Join(System.Environment.SystemDirectory, "cmd.exe"), LoginEnvironment.Which("cmd", environment, Home));
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
