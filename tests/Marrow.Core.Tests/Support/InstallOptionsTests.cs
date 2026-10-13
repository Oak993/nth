using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 and 8a, unit U0 (ED-NEW-inst-u0-1, amending ED-247). nth Setup tells
// the app's --veloapp-install hook what to do through install-options.json, and the hook answers in install-result.json
// beside it. These are the two file contracts every later installer unit builds on: their exact bytes, their round trip,
// and that anything unexpected (an unknown version, malformed JSON, a missing file) counts as no file, which for the
// result means "failed", so Ready never claims a desktop shortcut the hook did not report. Files live in a temporary folder.
public sealed class InstallOptionsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "nth-tests", "install-options-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_options_file_is_exactly_the_planned_json()
    {
        Assert.Equal("""{"version":1,"kind":"install","desktopShortcut":true}""", InstallOptions.ForInstall(desktopShortcut: true).ToJson());
        Assert.Equal("""{"version":1,"kind":"install","desktopShortcut":false}""", InstallOptions.ForInstall(desktopShortcut: false).ToJson());
        Assert.Equal("""{"version":1,"kind":"update"}""", InstallOptions.ForUpdate.ToJson());
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("install", true)]
    [InlineData("install", false)]
    [InlineData("update", false)]
    public void Options_round_trip_through_a_file(string kind, bool desktopShortcut)
    {
        var options = kind == "update" ? InstallOptions.ForUpdate : InstallOptions.ForInstall(desktopShortcut);
        var path = Path.Combine(folder, InstallOptions.OptionsFileName);

        options.Write(path);

        Assert.Equal(options, InstallOptions.Read(path));
        Assert.Equal(options.ToJson(), File.ReadAllText(path, Encoding.UTF8));
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith("\uFEFF"u8), "written as UTF-8 without a byte-order mark");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_update_never_asks_for_a_shortcut_whatever_its_file_says()
    {
        var options = InstallOptions.Parse("""{"version":1,"kind":"update","desktopShortcut":true}""");

        Assert.Equal(InstallOptions.ForUpdate, options);
        Assert.False(options!.DesktopShortcut);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("""{"version":2,"kind":"install","desktopShortcut":true}""")]
    [InlineData("""{"version":0,"kind":"install","desktopShortcut":true}""")]
    [InlineData("""{"version":-1,"kind":"update"}""")]
    [InlineData("""{"kind":"install","desktopShortcut":true}""")]
    [InlineData("""{"version":"1","kind":"install","desktopShortcut":true}""")]
    [InlineData("""{"version":1.5,"kind":"update"}""")]
    [InlineData("""{"version":null,"kind":"update"}""")]
    public void Options_of_an_unknown_version_read_as_no_file(string json) => Assert.Null(InstallOptions.Parse(json));

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"kind":"Install","desktopShortcut":true}""")]
    [InlineData("""{"version":1,"kind":"repair","desktopShortcut":true}""")]
    [InlineData("""{"version":1,"kind":"install"}""")]
    [InlineData("""{"version":1,"kind":"install","desktopShortcut":"true"}""")]
    [InlineData("""{"version":1,"kind":"install","desktopShortcut":1}""")]
    [InlineData("""{"version":1,"kind":"install","desktopShortcut":null}""")]
    [InlineData("""{"version":1,"kind":"update",}""")]
    [InlineData("""{"version":1,"kind":"update"} // the setup""")]
    [InlineData("""{"version":1,"kind":"update"}{"version":1,"kind":"update"}""")]
    public void Malformed_options_read_as_no_file(string json) => Assert.Null(InstallOptions.Parse(json));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_byte_order_mark_and_fields_a_later_setup_adds_are_accepted()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""{"note":"from a later setup","version":1,"kind":"install","desktopShortcut":false}""")];

        Assert.Equal(InstallOptions.ForInstall(desktopShortcut: false), InstallOptions.Parse(bytes));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_missing_unreadable_or_oversized_options_file_reads_as_no_file()
    {
        Directory.CreateDirectory(folder);
        var oversized = Path.Combine(folder, "big.json");
        File.WriteAllText(oversized, """{"version":1,"kind":"update"}""" + new string(' ', 70 * 1024));
        var locked = Path.Combine(folder, "locked.json");
        InstallOptions.ForUpdate.Write(locked);

        Assert.Null(InstallOptions.Read(Path.Combine(folder, "missing.json")));
        Assert.Null(InstallOptions.Read(folder)); // a folder, not a file
        Assert.Null(InstallOptions.Read(oversized));
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert.Null(InstallOptions.Read(locked));
        Assert.Equal(InstallOptions.ForUpdate, InstallOptions.Read(locked));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Writing_creates_the_folder_and_replaces_the_file_whole_leaving_nothing_else()
    {
        var path = Path.Combine(folder, "nth Setup", "1.0.0", InstallOptions.OptionsFileName);

        InstallOptions.ForInstall(desktopShortcut: true).Write(path);
        InstallOptions.ForUpdate.Write(path);

        Assert.Equal(InstallOptions.ForUpdate, InstallOptions.Read(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_result_file_is_exactly_the_planned_json_in_the_planned_field_order()
    {
        Assert.Equal(
            """{"version":1,"desktopShortcut":"created","iconsHidden":false,"scanCapped":false,"startAppIdRestamped":false}""",
            new InstallResult(DesktopShortcutOutcome.Created).ToJson());
        Assert.Equal(
            """{"version":1,"desktopShortcut":"failed","hresult":"0x80070005","iconsHidden":true,"scanCapped":true,"startAppIdRestamped":true}""",
            (InstallResult.FailedWith(unchecked((int)0x80070005)) with { IconsHidden = true, ScanCapped = true, StartAppIdRestamped = true }).ToJson());
        Assert.Equal(
            """{"version":1,"desktopShortcut":"name-taken","iconsHidden":false,"scanCapped":false,"startAppIdRestamped":false}""",
            new InstallResult(DesktopShortcutOutcome.NameTaken).ToJson());
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(DesktopShortcutOutcome.Created, null, false, false, false, "created")]
    [InlineData(DesktopShortcutOutcome.Created, null, true, false, true, "created")]
    [InlineData(DesktopShortcutOutcome.Kept, null, false, true, false, "kept")]
    [InlineData(DesktopShortcutOutcome.Off, null, false, false, true, "off")]
    [InlineData(DesktopShortcutOutcome.NameTaken, null, false, false, false, "name-taken")]
    [InlineData(DesktopShortcutOutcome.Failed, -2147024891, false, false, false, "failed")]
    [InlineData(DesktopShortcutOutcome.Failed, -2147467259, true, true, true, "failed")]
    [InlineData(DesktopShortcutOutcome.Failed, null, false, false, false, "failed")]
    public void Every_result_round_trips_through_a_file(DesktopShortcutOutcome outcome, int? hresult, bool iconsHidden, bool scanCapped, bool restamped, string word)
    {
        var result = new InstallResult(outcome, hresult, iconsHidden, scanCapped, restamped);
        var path = InstallOptions.ResultPathFor(Path.Combine(folder, InstallOptions.OptionsFileName));

        result.Write(path);

        Assert.Equal(result, InstallResult.Read(path));
        Assert.Equal(result, InstallResult.ReadForReady(path));
        Assert.Contains($"\"desktopShortcut\":\"{word}\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(hresult is not null, File.ReadAllText(path).Contains("\"hresult\"", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_missing_result_file_reads_as_failed_with_no_HRESULT_so_Ready_claims_nothing()
    {
        var path = Path.Combine(folder, InstallOptions.ResultFileName);

        Assert.Null(InstallResult.Read(path));
        var ready = InstallResult.ReadForReady(path);

        Assert.Equal(DesktopShortcutOutcome.Failed, ready.DesktopShortcut);
        Assert.Null(ready.HResult);
        Assert.False(ready.IconsHidden);
        Assert.Same(InstallResult.Missing, ready);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("""{"version":2,"desktopShortcut":"created"}""")]
    [InlineData("""{"desktopShortcut":"created"}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"desktopShortcut":"Created"}""")]
    [InlineData("""{"version":1,"desktopShortcut":"made"}""")]
    [InlineData("""{"version":1,"desktopShortcut":"failed:0x80070005"}""")]
    [InlineData("""{"version":1,"desktopShortcut":"failed","hresult":"80070005"}""")]
    [InlineData("""{"version":1,"desktopShortcut":"failed","hresult":"0x180070005"}""")]
    [InlineData("""{"version":1,"desktopShortcut":"failed","hresult":"0xZZ"}""")]
    [InlineData("""{"version":1,"desktopShortcut":"failed","hresult":-2147024891}""")]
    [InlineData("""{"version":1,"desktopShortcut":"created","iconsHidden":"yes"}""")]
    [InlineData("""{"version":1,"desktopShortcut":"created",""")]
    [InlineData("")]
    public void A_result_of_an_unknown_version_or_malformed_reads_as_failed(string json)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, InstallOptions.ResultFileName);
        File.WriteAllText(path, json);

        Assert.Null(InstallResult.Parse(json));
        Assert.Null(InstallResult.Read(path));
        Assert.Same(InstallResult.Missing, InstallResult.ReadForReady(path));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Flags_the_hook_did_not_write_read_as_false()
    {
        Assert.Equal(new InstallResult(DesktopShortcutOutcome.Kept), InstallResult.Parse("""{"version":1,"desktopShortcut":"kept"}"""));
        Assert.Equal(
            new InstallResult(DesktopShortcutOutcome.Created, IconsHidden: true),
            InstallResult.Parse("""{"version":1,"desktopShortcut":"created","iconsHidden":true}"""));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void HRESULTs_are_written_as_eight_upper_case_hex_digits_and_read_back_in_either_case()
    {
        Assert.Equal("0x80070005", InstallResult.FormatHResult(unchecked((int)0x80070005)));
        Assert.Equal("0x00000000", InstallResult.FormatHResult(0));
        Assert.Equal("0x8007000E", InstallResult.FormatHResult(unchecked((int)0x8007000E)));

        Assert.True(InstallResult.TryParseHResult("0x8007000e", out var lower));
        Assert.Equal(unchecked((int)0x8007000E), lower);
        Assert.True(InstallResult.TryParseHResult("0X1", out var one));
        Assert.Equal(1, one);
        foreach (var bad in new[] { null, "", "0x", "80070005", "0x123456789", "0xGG", "0x-1", " 0x1" })
            Assert.False(InstallResult.TryParseHResult(bad, out _), bad ?? "(null)");
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Every_outcome_has_its_one_word_and_only_those_words_name_one()
    {
        foreach (var outcome in Enum.GetValues<DesktopShortcutOutcome>())
            Assert.Equal(outcome, InstallResult.ParseOutcome(InstallResult.OutcomeName(outcome)));
        Assert.Equal(
            new[] { "created", "kept", "off", "name-taken", "failed" },
            Enum.GetValues<DesktopShortcutOutcome>().Select(InstallResult.OutcomeName));
        Assert.Null(InstallResult.ParseOutcome("NAME-TAKEN"));
        Assert.Null(InstallResult.ParseOutcome("name_taken"));
        Assert.Null(InstallResult.ParseOutcome(null));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_hook_finds_the_options_through_the_variable_or_the_fixed_folder_and_answers_beside_them()
    {
        const string local = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local";
        const string fixedPath = local + @"\nth Setup\1.0.0\install-options.json";
        const string staged = @"D:\Staging\nth Setup\1.0.0\install-options.json";

        Assert.Equal("NTH_SETUP_OPTIONS", InstallOptions.EnvironmentVariable);
        Assert.Equal(local + @"\nth Setup\1.0.0", InstallOptions.StagingFolder(local, "1.0.0"));
        Assert.Equal(fixedPath, InstallOptions.DefaultOptionsPath(local, "1.0.0"));
        Assert.Equal(fixedPath, InstallOptions.OptionsPathFor(null, local, "1.0.0"));
        Assert.Equal(fixedPath, InstallOptions.OptionsPathFor("  ", local, "1.0.0"));
        Assert.Equal(staged, InstallOptions.OptionsPathFor(staged, local, "1.0.0"));
        Assert.Equal(fixedPath, InstallOptions.OptionsPathFor(@"nth Setup\1.0.0\install-options.json", local, "1.0.0")); // not a full path
        Assert.Equal(fixedPath, InstallOptions.OptionsPathFor(@"D:\Staging\settings.json", local, "1.0.0")); // not the options file
        Assert.Equal(local + @"\nth Setup\1.0.0\install-result.json", InstallOptions.ResultPathFor(fixedPath));
        Assert.Equal(@"D:\Staging\nth Setup\1.0.0\install-result.json", InstallOptions.ResultPathFor(staged));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"1.0.0\..")]
    [InlineData("1.0.0/x")]
    [InlineData("1.0 0")]
    [InlineData("1.0.0:x")]
    public void A_version_that_cannot_name_one_folder_is_refused(string version) =>
        Assert.Throws<ArgumentException>(() => InstallOptions.StagingFolder(@"C:\Users\Ana\AppData\Local", version));

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("1.0.0+build.7")]
    [InlineData("10.20.30_rc")]
    public void A_SemVer_version_names_its_staging_folder(string version) =>
        Assert.Equal(@"C:\Users\Ana\AppData\Local\nth Setup\" + version, InstallOptions.StagingFolder(@"C:\Users\Ana\AppData\Local", version));
}
