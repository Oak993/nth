using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port;

// F.ProcessLauncher, part 1: the pure parts of src/Marrow.Core/Port/ProcessLauncher.cs (PORT-ONLY, the stand-in
// for Foundation Process that Shell.swift, StdioProcess.swift, MCPHub.swift, MCPProbe.swift and HangWatchdog.swift
// use). Argument quoting and the command-line limit (R-PROC-1, R-PROC-3, ED-024), the environment block
// (R-PROC-1, PA-0009) and executable lookup (PA-0010, PA-0016, ED-020, ED-031).
//
// The surface these tests assume, all in namespace Marrow.Core.Port (a proposal: the implementer may not rename
// without changing the tests through review):
//   static string ProcessLauncher.QuoteArgument(string argument)
//       libuv's quote_cmd_arg, the algorithm ED-024 names ("Node/libuv applies the MSVCRT CommandLineToArgvW
//       quoting rules"): "" for an empty argument; the argument unchanged when it holds no space, tab or quote;
//       wrapped in quotes when it holds no quote and no backslash; otherwise quoted with each quote escaped and
//       each run of backslashes before a quote or before the closing quote doubled.
//   static string ProcessLauncher.BuildCommandLine(string executable, IReadOnlyList<string> arguments)
//       argv[0] is the executable path (Foundation passes the launch path as argv[0]); every token goes through
//       QuoteArgument; tokens are joined by one space.
//   static string ProcessLauncher.BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
//       "KEY=VALUE" + NUL per entry, sorted by name as CreateProcessW requires, then one more NUL.
//   static string? ProcessLauncher.Which(string name, string path, string pathExt, string? home = null)   ED-031
//       Shell.swift:415-427 with the Windows rules of ED-031, ED-129, ED-130, ED-131. `home` is the folder `(name as NSString)
//       .expandingTildeInPath` (Shell.swift:418) puts in place of a leading "~"; null means the user's own home
//       folder. The seam exists so a test never touches the user's profile (R-TEST-9).
//   static bool ProcessLauncher.IsWindowsCommandInterpreter(string path)                  PA-0016 steps 3, 2 (ED-127)
//   sealed class CommandLineTooLongException : Exception, with int Length                 ED-024 (3)
// The rest of the surface (the launcher instance, ProcessPipe, IProcessControl, TerminationLadder) is described in
// ProcessLauncherLadderTests.cs and ProcessLauncherSpawnTests.cs.
public sealed class ProcessLauncherTests
{
    private const string ToolWithSpaces = "C:\\Program Files\\Marrow Test\\tool.exe";

    // ---- Quoting: the exact libuv form (ED-024 names Node/libuv's quoting) -----------------------------------

    [Theory]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-024")]
    [InlineData("", "\"\"")]                                           // empty: written as two quotes, never dropped
    [InlineData("plain", "plain")]                                     // nothing to quote
    [InlineData("--max-turns", "--max-turns")]
    [InlineData("C:\\dir\\", "C:\\dir\\")]                             // no space: trailing backslash stays literal
    [InlineData("new\nline", "new\nline")]                             // libuv quotes only for space, tab and quote
    [InlineData("two words", "\"two words\"")]                         // no quote, no backslash: just wrapped
    [InlineData("tab\there", "\"tab\there\"")]
    [InlineData("C:\\Program Files\\", "\"C:\\Program Files\\\\\"")]   // trailing backslash doubled before the closing quote
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]                   // each quote escaped
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]                           // a backslash before a quote is doubled, then the quote escaped
    [InlineData("{\"a\":\"b\"}", "\"{\\\"a\\\":\\\"b\\\"}\"")]         // a JSON token with no space is still quoted (it holds quotes)
    public void An_argument_is_quoted_exactly_as_libuv_quotes_it(string argument, string expected) =>
        Assert.Equal(expected, ProcessLauncher.QuoteArgument(argument));

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-024")]
    public void The_command_line_is_the_quoted_executable_then_each_quoted_argument_joined_by_one_space()
    {
        var line = ProcessLauncher.BuildCommandLine(ToolWithSpaces, ["--agents", "", "two words"]);
        Assert.Equal("\"C:\\Program Files\\Marrow Test\\tool.exe\" --agents \"\" \"two words\"", line);
        Assert.Equal("C:\\bin\\tool.exe", ProcessLauncher.BuildCommandLine("C:\\bin\\tool.exe", []));
    }

    // ---- Quoting: the round trip through the parser the child uses (R-PROC-1, ED-024 (2)) ---------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Rule", "R-PROC-3")]
    [Trait("Decision", "ED-024")]
    public void Every_argument_round_trips_byte_identically_through_CommandLineToArgvW()
    {
        // ClaudeSession.swift:108 passes a whole JSON document as one token after "--agents"; this one carries
        // spaces, quotes, an escaped quote and a backslash-n, the shapes compactString produces.
        const string Agents = "{\"reviewer\":{\"description\":\"Reviews code\",\"prompt\":\"Say \\\"done\\\" when finished.\\n\"}}";
        string[] arguments = [
            "plain", "", "two words", "tab\there", "new\nline", "say \"hi\"", "\"", "\"\"",
            "C:\\Program Files\\", "C:\\dir\\", "a\\\"b", "a\\\\\"b", "trailing\\\\",
            "\\\\server\\share\\with space\\", "& | ^ < > %PATH% !x!", "--flag=value with space", "-",
            "caf\U000000E9 \U0001F600", // U+00E9 LATIN SMALL LETTER E WITH ACUTE, U+1F600 GRINNING FACE
            "--agents", Agents,
        ];
        var line = ProcessLauncher.BuildCommandLine(ToolWithSpaces, arguments);
        var parsed = WindowsCommandLine.Parse(line);
        string[] expected = [ToolWithSpaces, .. arguments];
        Assert.Equal(expected, parsed);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-024")]
    public void An_empty_argument_is_kept_as_its_own_empty_token()
    {
        // parity_matrix.json:40500: "Passing an empty string as an argument value ... needs explicit handling".
        var parsed = WindowsCommandLine.Parse(ProcessLauncher.BuildCommandLine("C:\\bin\\tool.exe", ["-p", "", "--x"]));
        string[] expected = ["C:\\bin\\tool.exe", "-p", "", "--x"];
        Assert.Equal(expected, parsed);
        // Negative control: joining unquoted tokens loses the empty argument.
        string[] lost = ["C:\\bin\\tool.exe", "-p", "--x"];
        Assert.Equal(lost, WindowsCommandLine.Parse(string.Join(' ', "C:\\bin\\tool.exe", "-p", "", "--x")));
    }

    // ---- The 32,767 character limit (ED-024 (3), R-PROC-3) ----------------------------------------------------
    // CreateProcessW documents lpCommandLine as at most 32,767 characters INCLUDING the terminating NUL, so the
    // longest command line it takes is 32,766 characters. Measured on this machine (Windows 11 10.0.26200,
    // 2026-09-23, CreateProcessW from PowerShell Add-Type on where.exe): 32,765 ok, 32,766 ok, 32,767 fails with
    // error 206 (ERROR_FILENAME_EXCED_RANGE), 32,768 fails with 206. Characters are UTF-16 code units.

    [Fact]
    [Trait("Rule", "R-PROC-3")]
    [Trait("Decision", "ED-024")]
    public void A_command_line_of_32767_characters_fails_before_the_spawn_with_an_explicit_error()
    {
        var folder = ProcessTestFolders.Create("limit over");
        var arguments = ArgumentsFilling(SystemTools.Where, 32_767, astralCharacters: 0);
        Assert.Equal(32_767, ProcessLauncher.BuildCommandLine(SystemTools.Where, arguments).Length);
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = arguments, CurrentDirectoryURL = folder };
        var handled = 0;
        launcher.TerminationHandler = _ => Interlocked.Increment(ref handled);
        var error = Assert.Throws<CommandLineTooLongException>(launcher.Run);
        Assert.Equal(32_767, error.Length);
        Assert.False(launcher.IsRunning);
        Assert.Equal(0, handled);
    }

    [Fact]
    [Trait("Rule", "R-PROC-3")]
    [Trait("Decision", "ED-024")]
    public void A_command_line_of_32766_characters_is_spawned_whole()
    {
        var folder = ProcessTestFolders.Create("limit at");
        var arguments = ArgumentsFilling(SystemTools.Where, 32_766, astralCharacters: 0);
        Assert.Equal(32_766, ProcessLauncher.BuildCommandLine(SystemTools.Where, arguments).Length);
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = arguments, CurrentDirectoryURL = folder };
        launcher.Run();
        launcher.WaitUntilExit();
        Assert.False(launcher.IsRunning);
    }

    [Fact]
    [Trait("Rule", "R-PROC-3")]
    [Trait("Decision", "ED-024")]
    public void The_limit_counts_UTF16_code_units_so_a_character_outside_the_BMP_counts_twice()
    {
        // 32,767 UTF-16 units but only 32,766 Unicode scalars: a count of scalars or Swift Characters would let it
        // through to CreateProcessW, which fails with error 206 instead of the launcher's own error.
        var folder = ProcessTestFolders.Create("limit astral");
        var arguments = ArgumentsFilling(SystemTools.Where, 32_767, astralCharacters: 1);
        var line = ProcessLauncher.BuildCommandLine(SystemTools.Where, arguments);
        Assert.Equal(32_767, line.Length);
        Assert.Equal(32_766, line.EnumerateRunes().Count());
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = arguments, CurrentDirectoryURL = folder };
        Assert.Equal(32_767, Assert.Throws<CommandLineTooLongException>(launcher.Run).Length);
    }

    [Fact]
    [Trait("Rule", "R-PROC-3")]
    [Trait("Decision", "ED-024")]
    public void The_limit_counts_UTF16_code_units_not_UTF8_bytes_so_32766_accented_letters_still_spawn()
    {
        // U+00E9 is one UTF-16 unit but two UTF-8 bytes: a port that measures UTF-8 bytes would refuse this line.
        var folder = ProcessTestFolders.Create("limit bmp");
        var arguments = ArgumentsFilling(SystemTools.Where, 32_766, astralCharacters: 0, filler: (char)0x00E9); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        var line = ProcessLauncher.BuildCommandLine(SystemTools.Where, arguments);
        Assert.Equal(32_766, line.Length);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) > 32_767); // the control: bytes would exceed the limit
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = arguments, CurrentDirectoryURL = folder };
        launcher.Run();
        launcher.WaitUntilExit();
        Assert.False(launcher.IsRunning);
    }

    // ED-024 (3) and R-PROC-3: "the ASSEMBLED command line is measured". The two cases below carry a JSON token shaped
    // like the real one (ClaudeSession.swift:108 `arguments += ["--agents", HydraPrompts.claudeAgents(hydra)
    // .compactString]`: compact JSONSerialization output, JSONValue.swift:60, so quotes, spaces, `\"` and `\n`
    // inside strings). Quoting adds characters, so the quoted line reaches the limit while the raw argv, summed with
    // one separator per token, is 16 characters shorter: a port that measures the raw argv lets the first case
    // through to CreateProcessW (error 206) instead of failing with its own error.

    [Fact]
    [Trait("Rule", "R-PROC-3")]
    [Trait("Decision", "ED-024")]
    public void A_JSON_token_whose_quoted_command_line_is_32767_characters_fails_although_its_raw_argv_is_shorter()
    {
        var folder = ProcessTestFolders.Create("limit json over");
        var arguments = AgentsArgumentsFilling(SystemTools.Where, 32_767);
        Assert.Equal(32_767, ProcessLauncher.BuildCommandLine(SystemTools.Where, arguments).Length);
        Assert.Equal(32_751, string.Join(' ', [SystemTools.Where, .. arguments]).Length);
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = arguments, CurrentDirectoryURL = folder };
        var handled = 0;
        launcher.TerminationHandler = _ => Interlocked.Increment(ref handled);
        var error = Assert.Throws<CommandLineTooLongException>(launcher.Run);
        Assert.Equal(32_767, error.Length);
        Assert.False(launcher.IsRunning);
        Assert.Equal(0, Volatile.Read(ref handled));
    }

    [Fact]
    [Trait("Rule", "R-PROC-3")]
    [Trait("Decision", "ED-024")]
    public void A_JSON_token_whose_quoted_command_line_is_32766_characters_is_spawned_whole()
    {
        var folder = ProcessTestFolders.Create("limit json at");
        var arguments = AgentsArgumentsFilling(SystemTools.Where, 32_766);
        var line = ProcessLauncher.BuildCommandLine(SystemTools.Where, arguments);
        Assert.Equal(32_766, line.Length);
        string[] expected = [SystemTools.Where, .. arguments];
        Assert.Equal(expected, WindowsCommandLine.Parse(line)); // and it is the argv, byte for byte
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = arguments, CurrentDirectoryURL = folder };
        launcher.Run();
        launcher.WaitUntilExit();
        Assert.False(launcher.IsRunning);
    }

    // ---- The environment block (R-PROC-1, PA-0009) --------------------------------------------------------------
    // `process.environment = env` replaces the whole environment (Shell.swift:49, StdioProcess.swift:41). The block
    // is sorted by NAME, case-insensitively in ordinal (upper-cased) order, as CreateProcessW documents; each entry is
    // "KEY=VALUE" and NUL, and one more NUL ends the block.

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void The_environment_block_is_sorted_by_name_ignoring_case_in_ordinal_order_and_ends_with_two_NULs()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a_b"] = "1",
            ["AB"] = "2",
            ["zed"] = "",
            ["Path"] = "C:\\t\U000000E9st;C:\\bin", // U+00E9 LATIN SMALL LETTER E WITH ACUTE
            ["A"] = "5",
            ["A0"] = "6=7",
        };
        var block = ProcessLauncher.BuildEnvironmentBlock(environment);
        Assert.Equal("A=5\0A0=6=7\0AB=2\0a_b=1\0Path=C:\\t\U000000E9st;C:\\bin\0zed=\0\0", block);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void Sorting_by_ordinal_by_lowercase_or_by_the_whole_entry_would_each_give_another_order()
    {
        // Negative controls for the test above (R-TEST-4): each tempting .NET sort puts these names elsewhere.
        string[] names = ["a_b", "AB", "zed", "Path", "A", "A0"];
        string[] windows = ["A", "A0", "AB", "a_b", "Path", "zed"];
        Assert.Equal(windows, names.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.NotEqual(windows, names.Order(StringComparer.Ordinal).ToArray());                              // a_b after Path
        Assert.NotEqual(windows, names.OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal).ToArray()); // a_b before AB
        Assert.NotEqual(windows, names.OrderBy(n => n + "=", StringComparer.OrdinalIgnoreCase).ToArray());     // A0= before A=
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-128")]
    public void Names_that_differ_only_in_case_give_one_entry_in_the_block()
    {
        // Windows names are case-insensitive; a case-sensitive dictionary can hold both spellings. ED-128 (2): the
        // block folds them as successive OrdinalIgnoreCase assignments in the input's enumeration order (first
        // spelling, last value). This dictionary has no removals, so its enumeration order is its insertion order.
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["Path"] = "C:\\one", ["PATH"] = "C:\\two", ["X"] = "1" };
        var entries = ProcessLauncher.BuildEnvironmentBlock(environment).Split('\0');
        Assert.Single(entries, e => e.StartsWith("path=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Path=C:\\two", entries);
        Assert.Contains("X=1", entries);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-128")]
    public void A_name_set_again_in_another_case_keeps_the_inherited_spelling_and_takes_the_later_value()
    {
        // ED-128: MCPHub.swift:329-330 `var environment = LoginEnvironment.current; for (key, value) in env
        // { environment[key] = value }`. The rule is (1): environment dictionaries are OrdinalIgnoreCase, so the
        // assignment keeps the inherited spelling and takes the MCP config's value (3), as SetEnvironmentVariableW
        // does; that is the LoginEnvironment/Shell unit's job. What is pinned here is the launcher's defensive fold
        // (2) for a caller that still hands it a case-sensitive dictionary: successive OrdinalIgnoreCase
        // assignments in enumeration order, first spelling and last value, then the sort. The dictionary below has
        // no removals, so its order is defined. Nothing is asserted about two spellings inside one MCP env (4).
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["Path"] = "C:\\inherited", ["X"] = "1" };
        environment["PATH"] = "C:\\override";
        Assert.Equal("Path=C:\\override\0X=1\0\0", ProcessLauncher.BuildEnvironmentBlock(environment));
    }

    // ---- Executable lookup (ED-031, PA-0010) -----------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_splits_PATH_on_semicolons_and_returns_the_first_directory_that_has_the_tool()
    {
        var first = ProcessTestFolders.Create("which first");
        var second = ProcessTestFolders.Create("which second");
        ProcessTestFolders.Touch(first, "tool.exe");
        ProcessTestFolders.Touch(second, "tool.exe");
        Assert.Equal(Path.Join(first, "tool.exe"), ProcessLauncher.Which("tool", first + ";" + second, ".com;.exe;.bat;.cmd"));
        Assert.Equal(Path.Join(second, "tool.exe"), ProcessLauncher.Which("tool", second + ";" + first, ".com;.exe;.bat;.cmd"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_tries_every_PATHEXT_extension_in_PATHEXT_order_before_the_next_directory()
    {
        var first = ProcessTestFolders.Create("which ext first");
        var second = ProcessTestFolders.Create("which ext second");
        ProcessTestFolders.Touch(first, "tool.cmd");
        ProcessTestFolders.Touch(first, "tool.exe");
        ProcessTestFolders.Touch(second, "tool.com");
        var path = first + ";" + second;
        Assert.Equal(Path.Join(first, "tool.exe"), ProcessLauncher.Which("tool", path, ".com;.exe;.bat;.cmd"));
        Assert.Equal(Path.Join(first, "tool.cmd"), ProcessLauncher.Which("tool", path, ".cmd;.exe"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_skips_a_file_without_a_PATHEXT_extension_and_finds_the_npm_shim()
    {
        // A bare `codex` next to `codex.cmd` (how npm installs a CLI): only the .cmd counts as executable.
        var folder = ProcessTestFolders.Create("which shim");
        ProcessTestFolders.Touch(folder, "codex");
        ProcessTestFolders.Touch(folder, "codex.cmd");
        Assert.Equal(Path.Join(folder, "codex.cmd"), ProcessLauncher.Which("codex", folder, ".com;.exe;.bat;.cmd"));
        Assert.Null(ProcessLauncher.Which("codex", folder, ".com;.exe"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_accepts_a_name_that_already_carries_a_PATHEXT_extension_in_any_case()
    {
        var folder = ProcessTestFolders.Create("which named");
        ProcessTestFolders.Touch(folder, "node.exe");
        Assert.Equal(Path.Join(folder, "node.exe"), ProcessLauncher.Which("node.exe", folder, ".COM;.EXE;.BAT;.CMD"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_returns_nil_when_no_directory_has_the_tool_and_ignores_empty_PATH_entries()
    {
        var folder = ProcessTestFolders.Create("which none");
        ProcessTestFolders.Touch(folder, "tool.exe");
        Assert.Null(ProcessLauncher.Which("missing", folder, ".exe"));
        // Shell.swift:422 path.split(separator: ":") drops empty pieces; ED-031 (1) splits on ';' instead.
        Assert.Equal(Path.Join(folder, "tool.exe"), ProcessLauncher.Which("tool", ";;" + folder + ";", ".exe"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_takes_an_absolute_or_separator_bearing_name_as_a_path_and_never_searches_PATH_for_it()
    {
        // Shell.swift:417-420: a name with a separator is tested itself; ED-031 (3) widens "/" to "/", "\" and
        // drive-qualified names.
        var onPath = ProcessTestFolders.Create("which onpath");
        var elsewhere = ProcessTestFolders.Create("which elsewhere");
        ProcessTestFolders.Touch(onPath, "tool.exe");
        var direct = ProcessTestFolders.Touch(elsewhere, "tool.exe");
        Assert.Equal(direct, ProcessLauncher.Which(direct, onPath, ".exe"));
        Assert.Equal(direct, ProcessLauncher.Which(direct.Replace('\\', '/'), onPath, ".exe") is { } found ? Path.GetFullPath(found) : null);
        Assert.Null(ProcessLauncher.Which(Path.Join(elsewhere, "absent.exe"), onPath, ".exe"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-131")]
    public void Which_accepts_a_directory_whose_name_passes_the_PATHEXT_test_and_Run_on_it_throws_without_a_process()
    {
        // ED-131: Shell.swift:424 isExecutableFile is Darwin's access(X_OK), true for a searchable directory, and
        // R-FS-1's existence test is dir-inclusive; so the first entry's tool.exe DIRECTORY is the match, and the
        // file in the later entry is never reached. Run on it fails from CreateProcessW, as posix_spawn fails on
        // macOS: no process, no termination handler.
        var first = ProcessTestFolders.Create("which dir first");
        var second = ProcessTestFolders.Create("which dir second");
        var directory = Path.Join(first, "tool.exe");
        Directory.CreateDirectory(directory);
        ProcessTestFolders.Touch(second, "tool.exe");
        Assert.Equal(directory, ProcessLauncher.Which("tool", first + ";" + second, ".exe"));
        Assert.Equal(directory, ProcessLauncher.Which("tool.exe", first + ";" + second, ".exe"));
        Assert.Equal(directory, ProcessLauncher.Which("tool", first, ".exe"));
        var launcher = new ProcessLauncher { ExecutableURL = directory, CurrentDirectoryURL = second };
        var handled = 0;
        launcher.TerminationHandler = _ => Interlocked.Increment(ref handled);
        Assert.ThrowsAny<Exception>(launcher.Run);
        Assert.False(launcher.IsRunning);
        Assert.Equal(0, Volatile.Read(ref handled));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-129")]
    public void Which_given_a_path_without_an_extension_appends_each_PATHEXT_extension_in_PATHEXT_order()
    {
        // ED-129: the path branch uses the PATH walk's test. `codex` beside `codex.cmd` (npm's shape) gives the
        // .cmd; with codex.exe present too, .exe comes first in PATHEXT and wins. PATH holds a codex.exe of its own
        // and is never searched on this branch (ED-031 (3)).
        var folder = ProcessTestFolders.Create("which path ext");
        var onPath = ProcessTestFolders.Create("which path ext onpath");
        ProcessTestFolders.Touch(onPath, "codex.exe");
        ProcessTestFolders.Touch(folder, "codex");
        var shim = ProcessTestFolders.Touch(folder, "codex.cmd");
        Assert.Equal(shim, ProcessLauncher.Which(Path.Join(folder, "codex"), onPath, ".com;.exe;.bat;.cmd"));
        var exe = ProcessTestFolders.Touch(folder, "codex.exe");
        Assert.Equal(exe, ProcessLauncher.Which(Path.Join(folder, "codex"), onPath, ".com;.exe;.bat;.cmd"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-129")]
    public void Which_given_a_path_whose_extension_is_not_in_PATHEXT_appends_each_PATHEXT_extension()
    {
        // ED-129: "no extension, or one not in PATHEXT, such as '.txt'" (measured: where and cmd resolve foo.txt
        // to foo.txt.cmd).
        var folder = ProcessTestFolders.Create("which path txt");
        var onPath = ProcessTestFolders.Create("which path txt onpath");
        ProcessTestFolders.Touch(folder, "codex.txt");
        var shim = ProcessTestFolders.Touch(folder, "codex.txt.cmd");
        Assert.Equal(shim, ProcessLauncher.Which(Path.Join(folder, "codex.txt"), onPath, ".com;.exe;.bat;.cmd"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-129")]
    public void Which_on_PATH_appends_each_PATHEXT_extension_to_a_name_whose_extension_is_not_in_PATHEXT()
    {
        // ED-129, the PATH-walk twin of the test above.
        var folder = ProcessTestFolders.Create("which walk txt");
        ProcessTestFolders.Touch(folder, "foo.txt");
        var shim = ProcessTestFolders.Touch(folder, "foo.txt.cmd");
        Assert.Equal(shim, ProcessLauncher.Which("foo.txt", folder, ".com;.exe;.bat;.cmd"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-130")]
    public void Which_expands_a_leading_tilde_backslash_exactly_as_it_expands_a_leading_tilde_slash()
    {
        // ED-130: with '\' a separator (ED-031 (3)), '~\' is the leading '~' component expandingTildeInPath
        // (Shell.swift:418) expands, in Windows spelling. The home folder comes through the seam (R-TEST-9).
        var home = ProcessTestFolders.Create("which home back");
        var onPath = ProcessTestFolders.Create("which tilde back path");
        ProcessTestFolders.Touch(onPath, "tool.exe");
        Directory.CreateDirectory(Path.Join(home, "bin"));
        var inHome = ProcessTestFolders.Touch(Path.Join(home, "bin"), "tool.exe");
        var found = ProcessLauncher.Which("~\\bin\\tool.exe", onPath, ".exe", home);
        Assert.NotNull(found);
        Assert.Equal(inHome, Path.GetFullPath(found));
        Assert.Null(ProcessLauncher.Which("~\\bin\\absent.exe", onPath, ".exe", home));
        Assert.Null(ProcessLauncher.Which("~\\tool.exe", onPath, ".exe", home)); // PATH has tool.exe; it is not asked
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-129")]
    [Trait("Decision", "ED-130")]
    public void Which_appends_the_PATHEXT_extensions_after_expanding_the_tilde()
    {
        // ED-129: "After the tilde expansion of ED-130", the name without a PATHEXT extension takes each one.
        var home = ProcessTestFolders.Create("which home ext");
        var onPath = ProcessTestFolders.Create("which tilde ext path");
        Directory.CreateDirectory(Path.Join(home, "bin"));
        var inHome = ProcessTestFolders.Touch(Path.Join(home, "bin"), "tool.exe");
        foreach (var name in (string[])["~\\bin\\tool", "~/bin/tool"])
        {
            var found = ProcessLauncher.Which(name, onPath, ".com;.exe;.bat;.cmd", home);
            Assert.NotNull(found);
            Assert.Equal(inHome, Path.GetFullPath(found));
        }
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-130")]
    public void Which_leaves_a_leading_tilde_followed_by_a_name_unexpanded()
    {
        // ED-130: '~name' (NSString's other-user form) is left unchanged; there is no user database on Windows, and
        // Foundation leaves the text unchanged when that user does not exist, so the path fails the existence test.
        // The files below are where a port that expanded '~' anyway would look: <home><rest> and <home>\<rest>.
        var home = ProcessTestFolders.Create("which home user");
        var user = "marrow" + Guid.NewGuid().ToString("N");
        foreach (var root in (string[])[home + user, Path.Join(home, user)])
        {
            Directory.CreateDirectory(Path.Join(root, "bin"));
            ProcessTestFolders.Touch(Path.Join(root, "bin"), "tool.exe");
        }
        var onPath = ProcessTestFolders.Create("which tilde user path");
        Assert.Null(ProcessLauncher.Which("~" + user + "\\bin\\tool.exe", onPath, ".exe", home));
        Assert.Null(ProcessLauncher.Which("~" + user + "/bin/tool.exe", onPath, ".exe", home));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    [Trait("Decision", "ED-129")]
    public void Which_given_a_path_rejects_a_file_whose_extension_is_not_in_PATHEXT()
    {
        // Shell.swift:419 `isExecutableFile(atPath: path) ? ... : nil`; ED-031 (2) replaces the execute bit with
        // the PATHEXT test on this branch too. Only files without a PATHEXT extension exist here.
        var folder = ProcessTestFolders.Create("which direct ext");
        var bare = ProcessTestFolders.Touch(folder, "tool");
        var text = ProcessTestFolders.Touch(folder, "tool.txt");
        Assert.Null(ProcessLauncher.Which(bare, folder, ".com;.exe;.bat;.cmd"));
        Assert.Null(ProcessLauncher.Which(text, folder, ".com;.exe;.bat;.cmd"));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_expands_a_leading_tilde_to_the_home_folder_and_never_searches_PATH_for_it()
    {
        // Shell.swift:417-419: a name with "/" is `(name as NSString).expandingTildeInPath`, then tested itself.
        // The home folder comes through the seam (R-TEST-9: never the user's profile).
        var home = ProcessTestFolders.Create("which home");
        var onPath = ProcessTestFolders.Create("which tilde path");
        ProcessTestFolders.Touch(onPath, "tool.exe");
        Directory.CreateDirectory(Path.Join(home, "bin"));
        var inHome = ProcessTestFolders.Touch(Path.Join(home, "bin"), "tool.exe");
        var found = ProcessLauncher.Which("~/bin/tool.exe", onPath, ".exe", home);
        Assert.NotNull(found);
        Assert.Equal(inHome, Path.GetFullPath(found));
        Assert.Null(ProcessLauncher.Which("~/bin/absent.exe", onPath, ".exe", home));
        Assert.Null(ProcessLauncher.Which("~/tool.exe", onPath, ".exe", home)); // PATH has tool.exe; it is not asked
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-031")]
    public void Which_takes_a_drive_qualified_or_UNC_name_as_a_path_and_never_searches_PATH_for_it()
    {
        // ED-031 (3): "contains / or \, or is drive-qualified or UNC", so it bypasses PATH. `X:name` is relative to
        // the current folder of drive X; no such file exists there (the name is unique), while PATH has it.
        var onPath = ProcessTestFolders.Create("which drive");
        var name = "marrow-which-" + Guid.NewGuid().ToString("N") + ".exe";
        ProcessTestFolders.Touch(onPath, name);
        Assert.Equal(Path.Join(onPath, name), ProcessLauncher.Which(name, onPath, ".exe")); // the control: PATH has it
        var drive = Path.GetPathRoot(onPath)![..2]; // "C:"
        Assert.Null(ProcessLauncher.Which(drive + name, onPath, ".exe"));
        Assert.Null(ProcessLauncher.Which("\\\\localhost\\marrow-no-share-" + Guid.NewGuid().ToString("N") + "\\" + name, onPath, ".exe"));
    }

    // ---- The Windows command interpreter is never a provider (PA-0016, ED-020) --------------------------------

    [Fact]
    [Trait("Decision", "ED-020")]
    [Trait("Decision", "ED-127")]
    public void The_system_command_interpreter_is_recognised_so_Command_Code_is_not_reported_installed()
    {
        // Provider.swift:54 executableName "cmd"; ProviderRegistry.swift:221 reads the custom binaryPath, :223
        // resolves `LoginEnvironment.which(custom.isEmpty ? provider.executableName : custom)`, and :243 tries
        // `for name in ["command-code", "commandcode", "cmd"]`; on Windows PATH finds %SystemRoot%\System32\cmd.exe.
        // ED-127: IsWindowsCommandInterpreter is PA-0016 steps 3 (the canonical path) and 2 (the version resource).
        // ED-020's %SystemRoot% exclusion and its name order live in the Command Code resolver, not here.
        Assert.True(ProcessLauncher.IsWindowsCommandInterpreter(SystemTools.Interpreter));
    }

    [Fact]
    [Trait("Decision", "ED-020")]
    [Trait("Decision", "ED-127")]
    public void Another_program_in_the_system_folder_is_not_the_interpreter()
    {
        // ED-127: "IsWindowsCommandInterpreter(%SystemRoot%\System32\where.exe) is false". ED-020's directory
        // exclusion would reject it; PA-0016 steps 3 and 2 do not (another path, and no interpreter resource).
        Assert.False(ProcessLauncher.IsWindowsCommandInterpreter(SystemTools.Where));
    }

    [Fact]
    [Trait("Decision", "ED-020")]
    [Trait("Decision", "ED-127")]
    public void The_32_bit_interpreter_in_SysWOW64_is_recognised_too()
    {
        // PA-0016 step 3: "%SystemRoot%\System32\cmd.exe and %SystemRoot%\SysWOW64\cmd.exe outright" (ED-127).
        Assert.True(File.Exists(SystemTools.InterpreterWow64));
        Assert.True(ProcessLauncher.IsWindowsCommandInterpreter(SystemTools.InterpreterWow64));
    }

    [Fact]
    [Trait("Decision", "ED-020")]
    [Trait("Decision", "ED-127")]
    public void The_canonical_path_is_recognised_in_any_letter_case()
    {
        // Windows paths are case-insensitive; PATH entries come in any case (this machine's SystemDirectory is
        // "C:\WINDOWS\system32").
        Assert.True(ProcessLauncher.IsWindowsCommandInterpreter(SystemTools.Interpreter.ToUpperInvariant()));
        Assert.True(ProcessLauncher.IsWindowsCommandInterpreter(SystemTools.Interpreter.ToLowerInvariant()));
    }

    [Fact]
    [Trait("Decision", "ED-020")]
    [Trait("Decision", "ED-127")]
    public void A_byte_copy_of_the_interpreter_placed_outside_the_system_folder_is_still_recognised()
    {
        // PA-0016 step 2 rejects on the version resource: "Rejecting on the version resource rather than on the
        // literal path also covers a copy of cmd.exe placed elsewhere on PATH." Measured on this machine
        // 2026-09-23 (FileVersionInfo): the copy reads CompanyName "Microsoft Corporation", FileDescription
        // "Windows Command Processor", OriginalFilename "Cmd.Exe" (the System32 original reads "Cmd.Exe.MUI").
        // A port that checks only paths fails here.
        var folder = ProcessTestFolders.Create("cmd copy");
        var copy = Path.Join(folder, "cmd.exe");
        File.Copy(SystemTools.Interpreter, copy);
        Assert.True(ProcessLauncher.IsWindowsCommandInterpreter(copy));
    }

    [Fact]
    [Trait("Decision", "ED-020")]
    [Trait("Decision", "ED-127")]
    public void Another_Microsoft_program_named_cmd_outside_the_system_folder_is_accepted()
    {
        // PA-0016 step 2 needs CompanyName "Microsoft Corporation" TOGETHER WITH OriginalFilename "Cmd.Exe" or
        // "Cmd.Exe.MUI", or FileDescription "Windows Command Processor". where.exe renamed cmd.exe has the company
        // but neither of the others (measured 2026-09-23: FileDescription "Where - Lists location of files",
        // OriginalFilename "where.exe"). A port that checks only CompanyName fails here.
        var folder = ProcessTestFolders.Create("where as cmd");
        var impostor = Path.Join(folder, "cmd.exe");
        File.Copy(SystemTools.Where, impostor);
        Assert.False(ProcessLauncher.IsWindowsCommandInterpreter(impostor));
    }

    [Fact]
    [Trait("Decision", "ED-020")]
    [Trait("Decision", "ED-127")]
    public void A_stub_named_cmd_outside_the_system_folder_is_accepted()
    {
        // PA-0016 verification method: "a stub executable whose version resource does not identify the command
        // interpreter IS accepted" (ED-127: steps 3 and 2 both pass it). An empty file has no version
        // resource at all. The .cmd shim case is PA-0016's residual risk: a shim named cmd.cmd must not be rejected.
        var folder = ProcessTestFolders.Create("stub cmd");
        Assert.False(ProcessLauncher.IsWindowsCommandInterpreter(ProcessTestFolders.Touch(folder, "cmd.exe")));
        Assert.False(ProcessLauncher.IsWindowsCommandInterpreter(ProcessTestFolders.Touch(folder, "cmd.cmd")));
    }

    // ---- The job is set at creation, never afterwards (ADR-0002, probe P5 A3) -----------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void The_child_joins_its_job_through_the_attribute_list_at_creation_and_is_never_assigned_afterwards()
    {
        // ADR-0002: "PROC_THREAD_ATTRIBUTE_JOB_LIST sits beside PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE in one attribute
        // list", so the child is in the job from its first instruction (P5 A3: IsProcessInJob true on the first
        // call after CreateProcessW). A port that calls AssignProcessToJobObject after an ordinary CreateProcessW,
        // with or without CREATE_SUSPENDED and ResumeThread, passes every kill test (its grandchildren start much
        // later) and still leaves a window in which the child runs outside the job. No runtime check can see that
        // window reliably, so this is the scan P5's own RESULT used ("No CREATE_SUSPENDED / ResumeThread anywhere
        // in the path"), plus R-PROC-1's "Process.Start and UseShellExecute never appear".
        var root = RepositoryRoot();
        var port = Path.Join(root, "src", "Marrow.Core", "Port");
        var launcherFiles = Directory.GetFiles(port, "ProcessLauncher*.cs");
        Assert.Contains(launcherFiles, f => Path.GetFileName(f) == "ProcessLauncher.cs");
        var launcher = string.Concat(launcherFiles.Select(File.ReadAllText));
        Assert.Contains("PROC_THREAD_ATTRIBUTE_JOB_LIST", launcher, StringComparison.Ordinal);
        Assert.Contains("PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE", launcher, StringComparison.Ordinal);
        foreach (var forbidden in (string[])["Process.Start", "ProcessStartInfo", "UseShellExecute"])
            Assert.DoesNotContain(forbidden, launcher, StringComparison.Ordinal);
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(Path.Join(root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                              && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in (string[])["AssignProcessToJobObject", "CREATE_SUSPENDED", "ResumeThread"])
                Assert.False(text.Contains(forbidden, StringComparison.Ordinal), $"{file} contains {forbidden}");
        }
    }

    /// <summary>The repository root: this file is tests/Marrow.Core.Tests/Port/ProcessLauncherTests.cs.</summary>
    private static string RepositoryRoot([CallerFilePath] string path = "")
    {
        var root = Path.GetFullPath(Path.Join(Path.GetDirectoryName(path)!, "..", "..", ".."));
        Assert.True(File.Exists(Path.Join(root, "Marrow.sln")), root);
        return root;
    }

    /// <summary>
    /// Arguments for <paramref name="executable"/> that make the command line exactly <paramref name="total"/> UTF-16
    /// units long: "/Q" then one token of 'a' (with <paramref name="astralCharacters"/> of them replaced by U+1F600,
    /// two units each). No token needs quoting, so the length is plain arithmetic: executable, space, "/Q", space,
    /// token. /Q makes where.exe quiet. <paramref name="filler"/> replaces 'a' (any character that needs no quoting).
    /// </summary>
    private static IReadOnlyList<string> ArgumentsFilling(string executable, int total, int astralCharacters, char filler = 'a')
    {
        Assert.DoesNotContain(' ', executable);
        var tokenLength = total - executable.Length - 1 - 2 - 1;
        var astral = string.Concat(Enumerable.Repeat("\U0001F600", astralCharacters)); // U+1F600 GRINNING FACE
        return ["/Q", astral + new string(filler, tokenLength - astral.Length)];
    }

    /// <summary>
    /// Arguments "/Q", "--agents", then one compact JSON token, sized so the QUOTED command line is exactly
    /// <paramref name="total"/> UTF-16 units. The token is
    /// <c>{"reviewer":{"description":"Reviews code &amp; more","prompt":"Say \"done\" when finished.\n</c> + filler +
    /// <c>"}}</c>: 12 quotes, two of them preceded by one backslash, no trailing backslash. By the MSVCRT rules
    /// (the libuv quote_cmd_arg form) its quoted form is the token plus 16 characters: 2 enclosing quotes, one
    /// backslash before each of the 12 quotes, and one more for each of the 2 backslashes that precede a quote.
    /// </summary>
    private static IReadOnlyList<string> AgentsArgumentsFilling(string executable, int total)
    {
        Assert.DoesNotContain(' ', executable);
        const string Head = "{\"reviewer\":{\"description\":\"Reviews code & more\",\"prompt\":\"Say \\\"done\\\" when finished.\\n";
        const string Tail = "\"}}";
        Assert.Equal(12, (Head + Tail).Count(c => c == '"'));
        Assert.Equal(2, Regex.Count(Head + Tail, "\\\\\""));
        Assert.Equal(0, Regex.Count(Head + Tail, "\\\\\\\\\""));
        var tokenLength = total - executable.Length - " /Q --agents ".Length - 16;
        return ["/Q", "--agents", Head + new string('x', tokenLength - Head.Length - Tail.Length) + Tail];
    }
}
