// PORT-ONLY: Tactus native smoke (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 5.4 items 1, 2, 3, 5, 6, 8 and 9). NATIVE: drives
// real Notepad, Calculator, File Explorer and the smoke's own PowerShell windows through the real composition and engine,
// so it runs only when the owner schedules native tests (every regular run excludes FullyQualifiedName~Native, which the
// class name carries), alone, with focus, from scripts/pc-control-smoke.ps1. Only the named test apps are approved; every
// file it writes is under %TEMP%\tactus-smoke. Every item keeps real input (R4) off, as the product ships, so each
// shows what Tactus does without the owner's mouse and keyboard.
using System.ComponentModel;
using System.Diagnostics;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcSmokeAppsNativeTests
{
    private static string Scratch(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tactus-smoke", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static async Task<string> NewDialog(HashSet<nint> before, string className = "#32770")
    {
        var deadline = Environment.TickCount64 + 15_000;
        while (Environment.TickCount64 < deadline)
        {
            var dialog = PcSmokeRig.Windows().FirstOrDefault(w => !before.Contains(w.Handle) && w.Class == className);
            if (dialog.Handle != 0) return PcWindowRef.Format(dialog.Handle);
            await Task.Delay(200);
        }
        throw new TimeoutException("no " + className + " window opened in 15 s");
    }

    [Fact]
    public async Task S1_Notepad_types_two_lines_and_saves_them_to_a_temp_file()
    {
        using var rig = new PcSmokeRig("s1-notepad");
        var path = Path.Combine(Scratch("s1"), "tactus-s1.txt");
        var handle = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        var window = PcWindowRef.Format(handle);

        var look = await rig.Call("pc_observe", new { window, screenshot = "no" });
        Assert.True(look.Ok, look.Render());
        var typed = await rig.Call("pc_type", new { window, target = new { name = "Text editor" }, text = "Tactus line one\nTactus line two" });
        Assert.True(typed.Ok, typed.Render());

        var before = PcSmokeRig.Windows().Select(w => w.Handle).ToHashSet();
        var save = await rig.Call("pc_key", new { window, keys = "ctrl+shift+s" });
        if (save.Error == PcErrorCode.NeedsRealInput)
        {
            // Not mapped to an element without real input: the menu, as a model would do next.
            rig.Note("ctrl+shift+s needs real input; using the File menu");
            Assert.True((await rig.Call("pc_click", new { window, target = new { name = "File" } })).Ok);
            Assert.True((await rig.Call("pc_click", new { window, target = new { name = "Save as" } })).Ok);
        }
        else
        {
            Assert.True(save.Ok, save.Render());
        }
        var dialog = await NewDialog(before);
        Assert.True((await rig.Call("pc_set_value", new { window = dialog, target = new { name = "File name:", type = "edit" }, value = path })).Ok);
        Assert.True((await rig.Call("pc_click", new { window = dialog, target = new { name = "Save", type = "button" } })).Ok);

        var deadline = Environment.TickCount64 + 10_000;
        while (!File.Exists(path) && Environment.TickCount64 < deadline) await Task.Delay(200);
        var text = File.ReadAllText(path);
        rig.Result("saved " + new FileInfo(path).Length + " bytes: " + (text.Contains("Tactus line one", StringComparison.Ordinal) && text.Contains("Tactus line two", StringComparison.Ordinal) ? "both lines" : "MISSING LINES"));
        Assert.Contains("Tactus line one", text, StringComparison.Ordinal);
        Assert.Contains("Tactus line two", text, StringComparison.Ordinal);
        PcSmokeRig.Close(handle);
    }

    [Fact]
    public async Task S2_Calculator_adds_12_and_30_by_element_clicks_and_reads_42()
    {
        using var rig = new PcSmokeRig("s2-calculator");
        var handle = await rig.Launch("calc.exe", null, (_, title) => title == "Calculator");
        var window = PcWindowRef.Format(handle);
        Assert.True((await rig.Call("pc_observe", new { window, screenshot = "no" })).Ok);

        foreach (var name in new[] { "Clear", "One", "Two", "Plus", "Three", "Zero", "Equals" })
        {
            var click = await rig.Call("pc_click", new { window, target = new { name, type = "button" } });
            // "Clear" only resets a display left from an earlier run; the digits, Plus and Equals must each land.
            Assert.True(click.Ok || name == "Clear", name + ": " + click.Render());
        }
        var read = await rig.Call("pc_observe", new { window, screenshot = "no", query = "Display is", full = true });

        rig.Result("display: " + (read.Body ?? "").Split('\n').FirstOrDefault(l => l.Contains("Display is", StringComparison.Ordinal))?.Trim());
        Assert.Contains("Display is 42", read.Body ?? "", StringComparison.Ordinal);
        PcSmokeRig.Close(handle);
    }

    [Fact]
    public async Task S3_File_Explorer_renames_a_file_through_its_own_UI()
    {
        using var rig = new PcSmokeRig("s3-explorer");
        var folder = Scratch("s3");
        File.WriteAllText(Path.Combine(folder, "tactus-before.txt"), "rename me");
        var handle = await rig.Launch("explorer.exe", "\"" + folder + "\"", (cls, title) => cls == "CabinetWClass" && title.Contains(Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase));
        var window = PcWindowRef.Format(handle);
        Assert.True((await rig.Call("pc_observe", new { window, screenshot = "no", query = "tactus-before" })).Ok);

        Assert.True((await rig.Call("pc_click", new { window, target = new { name = "tactus-before.txt" } })).Ok);
        var rename = await rig.Call("pc_key", new { window, keys = "f2" });
        if (!rename.Ok)
        {
            // F2 has no element way here: the command bar's Rename button, as a model would do next.
            rig.Note("f2 gave " + (rename.Error?.ToString() ?? rename.Status.ToString()) + "; using the Rename button");
            Assert.True((await rig.Call("pc_click", new { window, target = new { name = "Rename", type = "button" } })).Ok);
        }
        var typed = await rig.Call("pc_type", new { window, text = "tactus-after.txt", mode = "replace", submit = true });
        Assert.True(typed.Ok, typed.Render());

        var deadline = Environment.TickCount64 + 10_000;
        while (!File.Exists(Path.Combine(folder, "tactus-after.txt")) && Environment.TickCount64 < deadline) await Task.Delay(200);
        rig.Result("renamed: " + File.Exists(Path.Combine(folder, "tactus-after.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "tactus-after.txt")));
        Assert.False(File.Exists(Path.Combine(folder, "tactus-before.txt")));
        PcSmokeRig.Close(handle);
    }

    [Fact]
    public async Task S5_A_covered_window_is_still_captured()
    {
        using var rig = new PcSmokeRig("s5-covered");
        var handle = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        var window = PcWindowRef.Format(handle);
        Assert.True((await rig.Call("pc_set_value", new { window, target = new { name = "Text editor" }, value = "TACTUS COVERED WINDOW MARKER" })).Ok);
        var cover = await rig.Form("Tactus smoke cover");
        PcSmokeRig.Cover(handle, cover);
        await Task.Delay(500);

        var shot = await rig.Call("pc_observe", new { window, screenshot = "yes" });

        rig.Result("covered capture: " + (shot.Image is { } image ? image.Width + "x" + image.Height + ", " + image.Jpeg.Length + " bytes" : "NONE") + "; eye check the saved JPEG");
        Assert.True(shot.Ok, shot.Render());
        Assert.NotNull(shot.Image);
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "tactus-smoke", "s5-covered.jpg"), shot.Image.Jpeg);
        PcSmokeRig.Close(cover);
        PcSmokeRig.Close(handle);
    }

    [Fact]
    public async Task S6_Point_clicks_land_on_a_100_and_a_150_percent_monitor()
    {
        // Each window is one button that counts its clicks; the owner drags window B to the other monitor first.
        using var rig = new PcSmokeRig("s6-dpi");
        const string button = "$b = New-Object System.Windows.Forms.Button; $b.Text = 'Hit me'; $b.Dock = 'Fill'; $script:n = 0; $b.Add_Click({ $script:n++; $b.Text = 'hit ' + $script:n }); $f.Controls.Add($b);";
        var first = await rig.Form("Tactus smoke dpi A", button);
        var second = await rig.Form("Tactus smoke dpi B", button);
        rig.Result("drag \"Tactus smoke dpi B\" to the other monitor now (15 s)");
        await Task.Delay(15_000);

        foreach (var (name, handle) in new[] { ("A", first), ("B", second) })
        {
            var window = PcWindowRef.Format(handle);
            var look = await rig.Call("pc_observe", new { window, screenshot = "yes" });
            Assert.True(look.Ok, look.Render());
            Assert.NotNull(look.Image);
            var click = await rig.Call("pc_click", new { window, obs = look.Image.ObsId, point = new[] { look.Image.Width / 2, look.Image.Height / 2 } });
            var after = await rig.Call("pc_observe", new { window, screenshot = "no", query = "hit", full = true });
            var landed = (after.Body ?? "").Contains("hit 1", StringComparison.Ordinal);

            rig.Result("window " + name + " at " + GetDpiForWindow(handle) + " dpi: click " + (click.Error?.ToString() ?? click.Status.ToString()) + ", " + (landed ? "landed" : "NOT landed"));
            Assert.True(landed, click.Render());
            PcSmokeRig.Close(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [Fact]
    public async Task S8_Typing_into_a_password_field_is_refused()
    {
        using var rig = new PcSmokeRig("s8-password");
        var handle = await rig.Form("Tactus smoke password", "$t = New-Object System.Windows.Forms.TextBox; $t.UseSystemPasswordChar = $true; $t.Name = 'secret'; $f.Controls.Add($t);");
        var window = PcWindowRef.Format(handle);
        Assert.True((await rig.Call("pc_observe", new { window, screenshot = "no" })).Ok);

        var typed = await rig.Call("pc_type", new { window, target = new { type = "edit" }, text = "hunter2" });
        var set = await rig.Call("pc_set_value", new { window, target = new { type = "edit" }, value = "hunter2" });

        rig.Result("pc_type " + typed.Error + ", pc_set_value " + set.Error);
        Assert.Equal(PcErrorCode.PasswordField, typed.Error);
        Assert.Equal(PcErrorCode.PasswordField, set.Error);
        PcSmokeRig.Close(handle);
    }

    [Fact]
    public async Task S9_A_UAC_prompt_is_reported_as_the_secure_desktop()
    {
        // The owner answers "No" on the prompt once the RESULT line is written.
        using var rig = new PcSmokeRig("s9-uac");
        var handle = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        var window = PcWindowRef.Format(handle);
        var elevation = Task.Run(() =>
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { UseShellExecute = true, Verb = "runas" });
                process?.WaitForExit();
            }
            catch (Win32Exception) { }
        });
        await Task.Delay(3_000);

        var look = await rig.Call("pc_observe", new { window, screenshot = "no" });
        var apps = await rig.Call("pc_list_apps", new { });

        rig.Result("observe " + look.Error + ", list_apps " + apps.Error + " (answer No on the UAC prompt now)");
        Assert.Equal(PcErrorCode.SecureDesktop, look.Error);
        await elevation.WaitAsync(TimeSpan.FromMinutes(2));
        PcSmokeRig.Close(handle);
    }
}
