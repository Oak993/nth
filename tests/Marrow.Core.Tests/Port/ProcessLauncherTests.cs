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
//   static string? ProcessLauncher.Which(string name, string path, string pathExt)       ED-031
//   static bool ProcessLauncher.IsWindowsCommandInterpreter(string path)                  PA-0016, ED-020
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
    public void Names_that_differ_only_in_case_give_one_entry_in_the_block()
    {
        // Windows names are case-insensitive; a Swift [String: String] can hold both spellings. R-PROC-1: keys are
        // compared OrdinalIgnoreCase. Which spelling and value survive is not asserted here (reported as open).
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["Path"] = "C:\\one", ["PATH"] = "C:\\two", ["X"] = "1" };
        var entries = ProcessLauncher.BuildEnvironmentBlock(environment).Split('\0');
        Assert.Single(entries, e => e.StartsWith("path=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("X=1", entries);
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

    // ---- The Windows command interpreter is never a provider (PA-0016, ED-020) --------------------------------

    [Fact]
    [Trait("Decision", "ED-020")]
    public void The_system_command_interpreter_is_recognised_so_Command_Code_is_not_reported_installed()
    {
        // Provider.swift:51 executableName "cmd"; on Windows PATH finds %SystemRoot%\System32\cmd.exe first. Both
        // PA-0016 (step 3, the canonical path) and ED-020 (a directory under %SystemRoot%) reject it.
        Assert.True(ProcessLauncher.IsWindowsCommandInterpreter(SystemTools.Interpreter));
    }

    [Fact]
    [Trait("Decision", "ED-020")]
    public void A_stub_named_cmd_outside_the_system_folder_is_accepted()
    {
        // PA-0016 verification method: "a stub executable whose version resource does not identify the command
        // interpreter IS accepted"; ED-020 accepts it too (not under %SystemRoot%). An empty file has no version
        // resource at all. The .cmd shim case is PA-0016's residual risk: a shim named cmd.cmd must not be rejected.
        var folder = ProcessTestFolders.Create("stub cmd");
        Assert.False(ProcessLauncher.IsWindowsCommandInterpreter(ProcessTestFolders.Touch(folder, "cmd.exe")));
        Assert.False(ProcessLauncher.IsWindowsCommandInterpreter(ProcessTestFolders.Touch(folder, "cmd.cmd")));
    }

    /// <summary>
    /// Arguments for <paramref name="executable"/> that make the command line exactly <paramref name="total"/> UTF-16
    /// units long: "/Q" then one token of 'a' (with <paramref name="astralCharacters"/> of them replaced by U+1F600,
    /// two units each). No token needs quoting, so the length is plain arithmetic: executable, space, "/Q", space,
    /// token. /Q makes where.exe quiet.
    /// </summary>
    private static IReadOnlyList<string> ArgumentsFilling(string executable, int total, int astralCharacters)
    {
        Assert.DoesNotContain(' ', executable);
        var tokenLength = total - executable.Length - 1 - 2 - 1;
        var astral = string.Concat(Enumerable.Repeat("\U0001F600", astralCharacters)); // U+1F600 GRINNING FACE
        return ["/Q", astral + new string('a', tokenLength - astral.Length)];
    }
}
