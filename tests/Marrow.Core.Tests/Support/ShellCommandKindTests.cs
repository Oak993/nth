using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// ShellCommandKind (Shell.swift:136-216): whether a command a model produced may write files. Every expectation below
// is read off the Swift (the substring guard at :169, the splitter at :177-183, the tool rules at :185-214 and the two
// tables at :144-159), with the OD-0008 A8 corrections exactly as ED-060 (4)-(6) words them for git config, git tag
// and git remote (PR-TERM-098 deviation). The find, fd, sort, xcodebuild, interpreter and env rules are part 2.
public sealed class ShellCommandKindTests
{
    // Shell.swift:144-151, all 52 entries, in source order.
    private static readonly string[] ReaderNames =
    [
        "ls", "cat", "bat", "head", "tail", "wc", "grep", "egrep", "fgrep", "rg", "tree",
        "pwd", "echo", "printf", "which", "command", "type", "stat", "file", "du", "df",
        "ps", "printenv", "date", "whoami", "hostname", "uname", "basename", "dirname",
        "realpath", "readlink", "column", "uniq", "cut", "tr", "nl", "diff", "cmp", "jq",
        "yq", "xxd", "od", "md5", "shasum", "cksum", "man", "true", "false", "sleep",
        "cd", "pushd", "popd",
    ];

    public static TheoryData<string> Readers => new(ReaderNames);

    // Shell.swift:154-159, all 25 entries, in source order.
    private static readonly string[] GitReaderNames =
    [
        "log", "status", "diff", "show", "branch", "rev-parse", "rev-list", "ls-files",
        "ls-tree", "blame", "remote", "describe", "shortlog", "cat-file", "tag", "config",
        "grep", "whatchanged", "reflog", "for-each-ref", "symbolic-ref", "count-objects",
        "var", "version", "help",
    ];

    public static TheoryData<string> GitReaders => new(GitReaderNames);

    [Fact]
    [Trait("Req", "PR-TERM-092")] // Core/Support/Shell.swift:164-166
    public void An_empty_or_whitespace_only_command_reads()
    {
        Assert.False(ShellCommandKind.MayWriteFiles(""));
        Assert.False(ShellCommandKind.MayWriteFiles("   "));
        // text.trimmed trims whitespace AND newlines (Text.swift:132).
        Assert.False(ShellCommandKind.MayWriteFiles(" \n\t\r\n "));
    }

    [Fact]
    [Trait("Req", "PR-TERM-092")] // Core/Support/Shell.swift:136-143,164-172
    public void A_command_reads_only_when_every_part_reads_and_one_writing_part_makes_it_a_writer()
    {
        Assert.False(ShellCommandKind.MayWriteFiles("ls -la"));
        Assert.False(ShellCommandKind.MayWriteFiles("ls -la && grep foo bar.txt | wc -l"));
        Assert.True(ShellCommandKind.MayWriteFiles("rm -rf build"));
        Assert.True(ShellCommandKind.MayWriteFiles("ls && rm x"));
        Assert.True(ShellCommandKind.MayWriteFiles("rm x; ls"));
        Assert.True(ShellCommandKind.MayWriteFiles("touch a"));
        Assert.True(ShellCommandKind.MayWriteFiles("npm install"));
        // Surrounding whitespace and newlines are trimmed before anything else.
        Assert.False(ShellCommandKind.MayWriteFiles("\n  pwd  \n"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-093")] // Core/Support/Shell.swift:167-169
    public void A_greater_than_sign_a_dollar_parenthesis_or_a_backtick_anywhere_makes_a_writer()
    {
        Assert.True(ShellCommandKind.MayWriteFiles("ls > out.txt"));
        Assert.True(ShellCommandKind.MayWriteFiles("cat a >> b"));
        Assert.True(ShellCommandKind.MayWriteFiles("ls 2>&1"));
        Assert.True(ShellCommandKind.MayWriteFiles("test 1 > 2"));
        Assert.True(ShellCommandKind.MayWriteFiles("grep '>' file"));
        Assert.True(ShellCommandKind.MayWriteFiles("echo \"a>b\""));
        Assert.True(ShellCommandKind.MayWriteFiles("echo $(pwd)"));
        Assert.True(ShellCommandKind.MayWriteFiles("cat \"$(ls)\""));
        Assert.True(ShellCommandKind.MayWriteFiles("echo `pwd`"));
        // Only "$(" is tested: a ${VAR} expansion and a bare $ do not match.
        Assert.False(ShellCommandKind.MayWriteFiles("echo ${HOME}"));
        Assert.False(ShellCommandKind.MayWriteFiles("echo $HOME"));
        Assert.False(ShellCommandKind.MayWriteFiles("echo (x)"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-094")] // Core/Support/Shell.swift:174-183
    public void The_line_splits_on_the_six_separators_and_trims_and_drops_empty_parts()
    {
        Assert.False(ShellCommandKind.MayWriteFiles("ls;pwd"));
        Assert.False(ShellCommandKind.MayWriteFiles("ls || pwd"));
        Assert.False(ShellCommandKind.MayWriteFiles("ls|cat"));
        Assert.False(ShellCommandKind.MayWriteFiles("ls & pwd"));
        Assert.False(ShellCommandKind.MayWriteFiles("ls\npwd"));
        Assert.False(ShellCommandKind.MayWriteFiles("ls &&& pwd"));
        Assert.False(ShellCommandKind.MayWriteFiles("ls ;; ; pwd ;"));
        Assert.False(ShellCommandKind.MayWriteFiles("&& ls"));
        Assert.True(ShellCommandKind.MayWriteFiles("ls & rm x"));
        Assert.True(ShellCommandKind.MayWriteFiles("ls\nrm x"));
        Assert.True(ShellCommandKind.MayWriteFiles("ls || rm x"));
        Assert.True(ShellCommandKind.MayWriteFiles("cat x | tee y"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-094")] // Core/Support/Shell.swift:174-176
    public void A_separator_inside_quotes_still_splits_so_the_quoted_command_is_a_writer()
    {
        // "grep 'a" and "b' file": the second part's tool is "b'", which is not a reader.
        Assert.True(ShellCommandKind.MayWriteFiles("grep 'a;b' file"));
        Assert.True(ShellCommandKind.MayWriteFiles("grep \"x|y\" file"));
        Assert.True(ShellCommandKind.MayWriteFiles("echo 'a && b'"));
    }

    [Theory]
    [MemberData(nameof(Readers))]
    [Trait("Req", "PR-TERM-095")] // Core/Support/Shell.swift:144-151,212-214
    public void Each_of_the_52_readers_reads_with_or_without_arguments(string tool)
    {
        Assert.False(ShellCommandKind.MayWriteFiles(tool));
        Assert.False(ShellCommandKind.MayWriteFiles(tool + " -x some/path"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-095")] // Core/Support/Shell.swift:144-151
    public void The_reader_table_has_exactly_52_entries()
    {
        Assert.Equal(52, ReaderNames.Length);
        Assert.Equal(52, ReaderNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Req", "PR-TERM-095")] // Core/Support/Shell.swift:185-190,212-214
    public void The_tool_is_the_last_path_component_of_the_first_token_compared_exactly()
    {
        Assert.False(ShellCommandKind.MayWriteFiles("/usr/bin/ls -la"));
        Assert.False(ShellCommandKind.MayWriteFiles("./bin/cat file"));
        // PR-TERM-095 windows_path: both Windows separators mark a path component.
        Assert.False(ShellCommandKind.MayWriteFiles("C:\\tools\\cat file"));
        Assert.False(ShellCommandKind.MayWriteFiles("\t  head   -n 5 file  "));
        // Exact and case-sensitive: no extension is stripped and no case is folded.
        Assert.True(ShellCommandKind.MayWriteFiles("LS"));
        Assert.True(ShellCommandKind.MayWriteFiles("Cat file"));
        Assert.True(ShellCommandKind.MayWriteFiles("cat.exe file"));
        Assert.True(ShellCommandKind.MayWriteFiles("/usr/bin/rm x"));
        // An environment assignment prefix is taken as the tool.
        Assert.True(ShellCommandKind.MayWriteFiles("FOO=bar ls"));
        // nawk is not one of the awk aliases and not a reader.
        Assert.True(ShellCommandKind.MayWriteFiles("nawk '{print}' f"));
        Assert.True(ShellCommandKind.MayWriteFiles("mkdir x"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-096")] // Core/Support/Shell.swift:191-194
    public void Sed_reads_only_with_a_short_flag_holding_n_and_no_in_place_flag()
    {
        Assert.False(ShellCommandKind.MayWriteFiles("sed -n '1,5p' file"));
        Assert.False(ShellCommandKind.MayWriteFiles("sed -ne 's/a/b/p' file"));
        Assert.False(ShellCommandKind.MayWriteFiles("/usr/bin/sed -n p file"));
        Assert.True(ShellCommandKind.MayWriteFiles("sed '1,5p' file"));
        Assert.True(ShellCommandKind.MayWriteFiles("sed -e p file"));
        Assert.True(ShellCommandKind.MayWriteFiles("sed -i -n p file"));
        Assert.True(ShellCommandKind.MayWriteFiles("sed -n -i.bak p file"));
        Assert.True(ShellCommandKind.MayWriteFiles("sed -n --in-place p file"));
        // A long option holding an n does not count: the double-dash case is excluded.
        Assert.True(ShellCommandKind.MayWriteFiles("sed --quiet p file"));
        Assert.True(ShellCommandKind.MayWriteFiles("sed"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-097")] // Core/Support/Shell.swift:185-189,195-197
    public void Awk_gawk_and_mawk_read_unless_the_part_holds_system_open_parenthesis()
    {
        Assert.False(ShellCommandKind.MayWriteFiles("awk '{print $1}' file"));
        Assert.False(ShellCommandKind.MayWriteFiles("gawk -F, '{print $2}' file"));
        Assert.False(ShellCommandKind.MayWriteFiles("mawk '{print}' file"));
        Assert.False(ShellCommandKind.MayWriteFiles("/usr/bin/awk '{print}' file"));
        Assert.False(ShellCommandKind.MayWriteFiles("awk '{ print \"system\" }' file"));
        Assert.False(ShellCommandKind.MayWriteFiles("awk 'BEGIN { system (1) }'"));
        Assert.True(ShellCommandKind.MayWriteFiles("awk 'BEGIN { system(\"rm x\") }'"));
        // Anywhere in the part, a quoted string it merely prints included.
        Assert.True(ShellCommandKind.MayWriteFiles("awk '{ print \"system(\" }' file"));
        Assert.True(ShellCommandKind.MayWriteFiles("gawk 'system(1)'"));
        // Only the awk part is searched: system( in another part does not matter to it, and grep reads.
        Assert.False(ShellCommandKind.MayWriteFiles("awk '{print}' f | grep 'system('"));
    }

    [Theory]
    [MemberData(nameof(GitReaders))]
    [Trait("Req", "PR-TERM-098")] // Core/Support/Shell.swift:153-159,202-204
    public void Each_of_the_25_git_subcommands_reads_on_its_own(string subcommand)
    {
        Assert.False(ShellCommandKind.MayWriteFiles("git " + subcommand));
    }

    [Fact]
    [Trait("Req", "PR-TERM-098")] // Core/Support/Shell.swift:153-159
    public void The_git_reader_table_has_exactly_25_entries()
    {
        Assert.Equal(25, GitReaderNames.Length);
        Assert.Equal(25, GitReaderNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Req", "PR-TERM-098")] // Core/Support/Shell.swift:202-204
    public void The_git_subcommand_is_the_first_argument_that_does_not_start_with_a_dash()
    {
        Assert.False(ShellCommandKind.MayWriteFiles("git --no-pager log --oneline -5"));
        Assert.False(ShellCommandKind.MayWriteFiles("git diff --stat HEAD~1"));
        Assert.False(ShellCommandKind.MayWriteFiles("/usr/bin/git status"));
        // Bare git and a git with only dash arguments read.
        Assert.False(ShellCommandKind.MayWriteFiles("git"));
        Assert.False(ShellCommandKind.MayWriteFiles("git --version"));
        // The value an option takes is not skipped: "/tmp" is the subcommand here, and it is not in the table.
        Assert.True(ShellCommandKind.MayWriteFiles("git -C /tmp status"));
        Assert.True(ShellCommandKind.MayWriteFiles("git commit -m x"));
        Assert.True(ShellCommandKind.MayWriteFiles("git add -A"));
        Assert.True(ShellCommandKind.MayWriteFiles("git checkout main"));
        Assert.True(ShellCommandKind.MayWriteFiles("git push"));
        Assert.True(ShellCommandKind.MayWriteFiles("git status && git stash"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-098")] // Core/Support/Shell.swift:153-159,202-204
    [Trait("Decision", "ED-060")]
    public void Git_config_writes_unless_every_argument_is_a_read_form()
    {
        // The writing forms OD-0008 A8 names (upstream classified them as readers, BLK-0008).
        Assert.True(ShellCommandKind.MayWriteFiles("git config user.name x"));
        Assert.True(ShellCommandKind.MayWriteFiles("git config user.email x"));
        Assert.True(ShellCommandKind.MayWriteFiles("git config --global user.name x"));
        Assert.True(ShellCommandKind.MayWriteFiles("git config --unset user.name"));
        Assert.True(ShellCommandKind.MayWriteFiles("git config --add remote.origin.fetch x"));
        // Read forms: the key or pattern a read form takes belongs to that form.
        Assert.False(ShellCommandKind.MayWriteFiles("git config --get user.email"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --get-all remote.origin.fetch"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --get-regexp '^user'"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --get-urlmatch http.proxy https://example.com"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --list"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config -l"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --list --show-origin --show-scope"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --name-only --list"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config -z --list"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --null --get user.name"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --global --get user.name"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --system --list"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --local --get core.bare"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --worktree --list"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config --file .gitmodules --get-regexp path"));
        Assert.False(ShellCommandKind.MayWriteFiles("git config -f .gitmodules --list"));
        // --file with a writing form is still a writer.
        Assert.True(ShellCommandKind.MayWriteFiles("git config --file .gitmodules submodule.x.url y"));
        Assert.True(ShellCommandKind.MayWriteFiles("git config -f cfg --unset a.b"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-098")] // Core/Support/Shell.swift:153-159,202-204
    [Trait("Decision", "ED-060")]
    public void Git_tag_writes_unless_it_only_lists()
    {
        Assert.True(ShellCommandKind.MayWriteFiles("git tag v1"));
        Assert.True(ShellCommandKind.MayWriteFiles("git tag -d v1"));
        Assert.True(ShellCommandKind.MayWriteFiles("git tag -a v1 -m release"));
        Assert.True(ShellCommandKind.MayWriteFiles("git tag -f v1"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag -l"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag --list"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag -l 'v1.*'"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag -n"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag --contains HEAD"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag --points-at HEAD"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag --merged main"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag --no-merged main"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag -l --sort=-v:refname"));
        Assert.False(ShellCommandKind.MayWriteFiles("git tag --list --format=%(refname)"));
    }

    [Fact]
    [Trait("Req", "PR-TERM-098")] // Core/Support/Shell.swift:153-159,202-204
    [Trait("Decision", "ED-060")]
    public void Git_remote_writes_for_its_nine_writing_subcommands_and_reads_otherwise()
    {
        foreach (var write in (string[])["add origin https://example.com/r.git", "remove origin", "rm origin",
                     "rename origin upstream", "set-url origin https://example.com/r.git", "set-head origin -a",
                     "set-branches origin main", "prune origin", "update"])
            Assert.True(ShellCommandKind.MayWriteFiles("git remote " + write), write);
        Assert.True(ShellCommandKind.MayWriteFiles("git remote -v add origin url"));
        Assert.False(ShellCommandKind.MayWriteFiles("git remote"));
        Assert.False(ShellCommandKind.MayWriteFiles("git remote -v"));
        Assert.False(ShellCommandKind.MayWriteFiles("git remote show origin"));
        Assert.False(ShellCommandKind.MayWriteFiles("git remote get-url origin"));
        Assert.False(ShellCommandKind.MayWriteFiles("git --no-pager remote -v"));
    }
}
