using System.Reflection;
using Marrow.Core.Support;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Text;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Text"; unit U1 (ED-NEW-inst-u1-1). Every string the window shows
// comes from one embedded table, so a translation needs no code change. The words follow OD-0135 (lower-case "nth",
// never "Marrow") and OD-0072 (the up-to-date and newer sentences), with typographic apostrophes and quotes.
public sealed class SetupStringsTests
{
    private static readonly StringTable English = StringTable.Load("en");

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Every_accessor_reads_a_key_the_English_table_has()
    {
        var failures = new List<string>();
        foreach (var property in typeof(SetupStrings).GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string)))
        {
            try { Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(null))); }
            catch (TargetInvocationException error) { failures.Add(property.Name + ": " + error.InnerException?.Message); }
        }
        foreach (var method in typeof(SetupStrings).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName))
        {
            var arguments = method.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object)"x" : p.ParameterType == typeof(int) ? 2 : (object)2L).ToArray();
            try { Assert.False(string.IsNullOrWhiteSpace((string?)method.Invoke(null, arguments))); }
            catch (TargetInvocationException error) { failures.Add(method.Name + ": " + error.InnerException?.Message); }
        }
        Assert.Empty(failures);
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void The_product_is_lower_case_nth_and_never_Marrow()
    {
        foreach (var key in English.Keys)
        {
            var words = English[key];
            Assert.DoesNotContain("Marrow", words, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Nth", words, StringComparison.Ordinal);
        }
        Assert.Equal("nth Setup", SetupStrings.CaptionSetup);
        Assert.Equal("Uninstall nth", SetupStrings.CaptionUninstall);
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Apostrophes_and_quotes_are_typographic()
    {
        foreach (var key in English.Keys)
        {
            Assert.DoesNotContain("'", English[key], StringComparison.Ordinal);
            Assert.DoesNotContain("\"", English[key], StringComparison.Ordinal);
        }
        Assert.Equal("This copy of nth Setup has been changed. Download it again.", SetupStrings.ProblemChanged);
        Assert.Equal("Unsigned test build, not for sharing", SetupStrings.WelcomeUnsigned);
        Assert.Equal("Signed by Ana-María Østergård-Fernández", SetupStrings.WelcomeSigned("Ana-María Østergård-Fernández"));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Up_to_date_and_newer_say_OD_0072s_sentences()
    {
        Assert.Equal(InstallerGate.UpToDateMessage("nth", "1.1.0"), SetupStrings.CurrentSubtitle("1.1.0"));
        Assert.StartsWith(InstallerGate.NewerInstalledMessage("nth", "1.2.0"), SetupStrings.NewerSubtitle("1.2.0"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_close_paused_lines_and_the_shutdown_reason_are_the_plans_words()
    {
        Assert.Equal("You can close this window once nth is safely in place.", SetupStrings.InstallingHold);
        Assert.Equal("Finishing installing nth", SetupStrings.ShutdownInstalling);
        Assert.Equal("You can close this window once nth is removed.", SetupStrings.UninstallHold);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Problem_in_uninstall_mode_says_removing_words_unit_U6()
    {
        // ED-NEW-inst-u6-1: the canvas has no uninstall failure, so Problem carries it, worded for removing
        Assert.Equal("nth couldn’t be removed", SetupStrings.UninstallProblemTitle);
        Assert.Equal("Something went wrong while removing nth.", SetupStrings.UninstallProblemOther);
        Assert.Equal("Some of nth’s files may be left. Retry removes them.", SetupStrings.UninstallProblemLeft);
        Assert.Equal("Nothing was changed on your PC.", SetupStrings.ProblemSafe);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_setup_that_cannot_draw_its_window_says_plain_calm_words_and_keeps_the_reason_for_whoever_helps()
    {
        // ED-NEW-inst-picks-1 (the pick of ED-NEW-inst-fix-5): no error code or path in the words the person reads first
        Assert.Equal("nth Setup can’t start", SetupStrings.StartFailedTitle);
        Assert.Equal("Some of nth Setup’s files are missing or damaged. Download it again from the nth website and run it.", SetupStrings.StartFailedBody);
        const string reason = @"Another nth Setup is using 'C:\Users\Ana\AppData\Local\nth Setup\runtime\0123456789abcdef' and its libraries are not complete yet. (0x80070020)";

        var (caption, text) = Program.StartFailure(reason);

        Assert.Equal(SetupStrings.StartFailedTitle, caption);
        var paragraphs = text.Split("\n\n");
        Assert.Equal(2, paragraphs.Length);
        Assert.Equal(SetupStrings.StartFailedBody, paragraphs[0]);
        Assert.DoesNotContain("0x", paragraphs[0], StringComparison.Ordinal);
        Assert.DoesNotContain("\\", paragraphs[0], StringComparison.Ordinal);
        Assert.Equal("Details: " + reason, paragraphs[1]);
        Assert.Equal(SetupStrings.StartFailedBody, Program.StartFailure("  ").Text); // nothing to add: the plain words alone
        Assert.Equal(SetupStrings.StartFailedBody, Program.StartFailure("").Text);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_program_shows_that_message_and_not_the_exceptions_own_text_when_the_libraries_cannot_be_put_in_place()
    {
        var program = File.ReadAllText(Path.Combine(Fixtures.RepositoryRoot.Path, "src", "Marrow.Setup", "Program.cs"));
        var prepare = program.IndexOf("SetupNatives.Prepare(", StringComparison.Ordinal);
        var failed = program.IndexOf("StartFailure(error.Message)", prepare, StringComparison.Ordinal);
        Assert.True(prepare > 0 && failed > prepare, "the handler of Prepare builds the message with StartFailure");
        Assert.DoesNotContain("ShowMessage(InstallerGate.Caption(LegacyGateHost.Metadata(\"InstallerTitle\")), error.Message", program[prepare..(failed + 200)], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_count_picks_its_form_and_numbers_are_invariant()
    {
        Assert.Equal("Stopping 1 thread and closing nth", SetupStrings.StepStopping(1));
        Assert.Equal("Stopping 3 threads and closing nth", SetupStrings.StepStopping(3));
        Assert.Equal("Waiting for 1 thread to finish", SetupStrings.StepWaiting(1));
        var table = StringTable.Parse("n.one = {0} file of {1}\nn.other = {0} files of {1}\nbig = {0}", "xx");
        Assert.Equal("1 file of 2", table.Count("n", 1, 2));
        Assert.Equal("1234567 files of 2", table.Count("n", 1234567, 2));
        Assert.Equal("1.5", table.Format("big", 1.5));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_table_is_key_value_lines_and_a_bad_one_is_refused()
    {
        var table = StringTable.Parse("# a comment\r\n\r\n a.b = Hello there  \r\nc=d=e\n", "xx");
        Assert.Equal("Hello there", table["a.b"]);
        Assert.Equal("d=e", table["c"]);
        Assert.Equal(2, table.Keys.Count);
        Assert.Throws<KeyNotFoundException>(() => table["missing"]);
        Assert.Throws<FormatException>(() => StringTable.Parse("no equals sign", "xx"));
        Assert.Throws<FormatException>(() => StringTable.Parse(" = value", "xx"));
        Assert.Throws<FormatException>(() => StringTable.Parse("a = 1\na = 2", "xx"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_language_without_a_table_falls_back_to_English()
    {
        var table = StringTable.Load("zz");
        Assert.Equal("en", table.Language);
        Assert.Equal(English.Keys.Count, table.Keys.Count);
        Assert.Equal("Welcome to nth", table["welcome.title"]);
    }
}
