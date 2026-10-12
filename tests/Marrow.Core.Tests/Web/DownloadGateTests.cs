using Marrow.Core.Web;
using Marrow.Testing;

namespace Marrow.Core.Tests.Web;

// DESIGN 5.4: every download asks; the prompt names the file, its size, type and source ("12 KB · CSV · from localhost:5173");
// risky types warn "This file can run code on your PC." and Save needs a second click within 3 s; files land under a safe
// name in the Downloads folder and are never opened. ED-NEW-web-w8b-2 (the stricter risky list and the safe names).
[Trait("Decision", "ED-NEW-web-w8b-2")]
[Trait("Feature", "W-8b")]
public sealed class DownloadGateTests
{
    [Theory]
    [InlineData("orbit-setup.exe")]
    [InlineData("installer.MSI")]
    [InlineData("tool.msix")]
    [InlineData("app.appx")]
    [InlineData("run.bat")]
    [InlineData("run.cmd")]
    [InlineData("fix.ps1")]
    [InlineData("macro.vbs")]
    [InlineData("payload.js")]
    [InlineData("app.jar")]
    [InlineData("saver.scr")]
    [InlineData("link.lnk")]
    [InlineData("tweak.reg")]
    [InlineData("page.hta")]
    [InlineData("library.dll")]
    public void The_design_list_is_risky(string name) => Assert.True(DownloadGate.IsRisky(name, "application/octet-stream"));

    [Theory]
    [InlineData("old.com")]
    [InlineData("old.pif")]
    [InlineData("panel.cpl")]
    [InlineData("console.msc")]
    [InlineData("script.wsf")]
    [InlineData("module.psm1")]
    [InlineData("shortcut.url")]
    [InlineData("help.chm")]
    [InlineData("disk.iso")]
    [InlineData("disk.vhdx")]
    [InlineData("addin.xll")]
    [InlineData("deploy.application")]
    public void Other_windows_types_that_run_code_are_risky_too(string name) => Assert.True(DownloadGate.IsRisky(name, null));

    [Theory]
    [InlineData("plans-2026-10.csv", "text/csv")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("notes.zip", "application/zip")]
    [InlineData("exe.txt", "text/plain")]
    public void Ordinary_files_are_not(string name, string mime) => Assert.False(DownloadGate.IsRisky(name, mime));

    [Fact]
    public void An_archive_that_names_a_program_or_that_the_server_calls_a_program_is_risky()
    {
        Assert.True(DownloadGate.IsRisky("setup.exe.zip", "application/zip"));
        Assert.True(DownloadGate.IsRisky("tools.scr.7z", null));
        Assert.True(DownloadGate.IsRisky("bundle.zip", "application/x-msdownload"));
        Assert.True(DownloadGate.IsRisky("data.bin", "application/x-msdownload; charset=binary"));
        Assert.False(DownloadGate.IsRisky("archive.2026.zip", "application/zip"));
    }

    [Fact]
    public void A_bidi_override_or_a_trailing_dot_cannot_hide_a_program()
    {
        // "invoice\u202Etxt.exe" reads as "invoiceexe.txt"; Windows drops a trailing dot, so "a.exe." is "a.exe".
        Assert.True(DownloadGate.IsRisky("invoice\u202Etxt.exe", "text/plain"));
        Assert.Equal("invoicetxt.exe", DownloadGate.SafeFileName("invoice\u202Etxt.exe"));
        Assert.True(DownloadGate.IsRisky("a.exe.", null));
        Assert.True(DownloadGate.IsRisky("a.exe   ", null));
        Assert.Equal("a.exe", DownloadGate.SafeFileName("a.exe. . "));
    }

    [Theory]
    [InlineData(@"..\..\Windows\evil.dll", "evil.dll")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("re:port?.csv", "re_port_.csv")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("com1", "_com1")]
    [InlineData("", "download")]
    [InlineData("   ", "download")]
    [InlineData("...", "download")]
    [InlineData(null, "download")]
    [InlineData("tab\there.txt", "tab_here.txt")]
    public void Names_are_made_safe_for_a_folder_of_nths_choosing(string? suggested, string expected) =>
        Assert.Equal(expected, DownloadGate.SafeFileName(suggested));

    [Fact]
    public void A_long_name_keeps_its_type()
    {
        var name = DownloadGate.SafeFileName(new string('a', 400) + ".csv");
        Assert.Equal(DownloadGate.MaxNameLength, name.Length);
        Assert.EndsWith(".csv", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_picks_the_first_free_name_as_windows_names_copies()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Users\d\Downloads\plans.csv", @"C:\Users\d\Downloads\plans (1).csv" };
        Assert.Equal(@"C:\Users\d\Downloads\plans (2).csv", DownloadGate.UniquePath(@"C:\Users\d\Downloads", "plans.csv", taken.Contains));
        Assert.Equal(@"C:\Users\d\Downloads\other.csv", DownloadGate.UniquePath(@"C:\Users\d\Downloads", "other.csv", taken.Contains));
        Assert.Equal(@"C:\Users\d\Downloads\passwd", DownloadGate.UniquePath(@"C:\Users\d\Downloads", "../../passwd", taken.Contains));
    }

    [Fact]
    public void The_detail_line_reads_as_the_canvas_does()
    {
        Assert.Equal("12 KB · CSV · from localhost:5173", DownloadGate.Detail(12 * 1024, "plans-2026-10.csv", "text/csv", "localhost:5173"));
        Assert.Equal("84 MB · Application · from downloads.example.com", DownloadGate.Detail(84L * 1024 * 1024, "orbit-setup.exe", "application/x-msdownload", "downloads.example.com"));
        Assert.Equal("ZIP archive · from a.test", DownloadGate.Detail(null, "f.zip", null, "a.test"));
        Assert.Equal("Image · from a.test", DownloadGate.Detail(null, "picture", "image/png", "a.test"));
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(12L * 1024, "12 KB")]
    [InlineData(84L * 1024 * 1024, "84 MB")]
    [InlineData(3328599654L, "3.1 GB")]
    public void Sizes_count_1024_to_a_unit(long bytes, string expected) => Assert.Equal(expected, DownloadGate.SizeText(bytes));

    [Fact]
    public void The_prompt_words_are_the_canvas_words()
    {
        Assert.Equal("This file can run code on your PC.", DownloadGate.RiskyWarning);
        Assert.Equal("Click again to save", DownloadGate.ArmedLabel);
        Assert.Equal(("Save", "Save as…", "Cancel"), (DownloadGate.SaveLabel, DownloadGate.SaveAsLabel, DownloadGate.CancelLabel));
        Assert.Equal("Saved to Downloads. Never opened by itself.", DownloadGate.Foot);
        Assert.Equal("Saved to Downloads · plans.csv", DownloadGate.Confirmation(DownloadChoice.Save, "plans.csv"));
        Assert.Equal("Download cancelled", DownloadGate.Confirmation(DownloadChoice.Cancel, "plans.csv"));
        // 10.1: buttons are named in full for a screen reader.
        Assert.Equal("Save plans.csv to Downloads", DownloadGate.SaveName("plans.csv"));
        Assert.Equal("Cancel the download of plans.csv", DownloadGate.CancelName("plans.csv"));
    }

    [Fact]
    public void A_risky_file_saves_on_a_second_click_within_3_s_and_a_late_one_arms_again()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        var arm = new DownloadSaveArm(risky: true, time);
        Assert.Equal("Save", arm.Label);
        Assert.False(arm.Press());
        Assert.Equal("Click again to save", arm.Label);
        time.Advance(TimeSpan.FromSeconds(2.9));
        Assert.True(arm.Press());

        var late = new DownloadSaveArm(risky: true, time);
        Assert.False(late.Press());
        time.Advance(DownloadGate.SecondClickWindow);
        Assert.False(late.IsArmed);
        Assert.Equal("Save", late.Label);
        Assert.False(late.Press()); // the window passed: the click arms again, it does not save
        late.Disarm();
        Assert.False(late.IsArmed);

        Assert.True(new DownloadSaveArm(risky: false, time).Press()); // an ordinary file saves on the first click
    }
}
