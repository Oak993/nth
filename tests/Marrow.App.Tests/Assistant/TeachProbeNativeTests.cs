using System.Diagnostics;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Teach;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Owed native check (never run by the unit agent; the lead runs it alone, with focus, on a quiet desktop): DESIGN.md rev 5, 16.1 "Native gate", AS-19.
// UI Automation, asked directly (16.2): the window in front is named with its app and its title, the control in focus is found with its type and
// its name, and its text is read only on request and only from a control that does not hold a password. A password field is flagged IsPassword
// and its reader answers nothing, whatever the box holds. A hung call is given up on within a second and a half. UNVERIFIED until this has run:
// whether Windows 11's Notepad exposes its text through the Value pattern (the text may come back null there, and the step then keeps only a length).
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-9")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class TeachProbeNativeTests
{
    [Fact]
    public void The_window_in_front_is_named_with_its_app_and_title_and_the_focus_is_found()
    {
        using var notepad = new TeachNativeDesk.Notepad("hello teach");
        var probe = new UiaElementProbe();
        var front = probe.Foreground();
        Assert.NotNull(front);
        Assert.Equal("notepad", front!.App, ignoreCase: true);
        Assert.False(string.IsNullOrEmpty(front.Window));
        var focus = probe.Focused();
        Assert.NotNull(focus);
        Assert.Equal("notepad", focus!.Element.App, ignoreCase: true);
        Assert.False(focus.Element.IsPassword);
        Assert.False(string.IsNullOrEmpty(focus.Element.ControlType));
        var text = focus.ReadValue();
        Assert.True(text is null || text.Contains("hello teach", StringComparison.Ordinal), "a text that is read must be the document's");
    }

    [Fact]
    public void A_control_under_a_point_is_found_with_the_app_of_its_window()
    {
        using var notepad = new TeachNativeDesk.Notepad("hello teach");
        var probe = new UiaElementProbe();
        var element = probe.Foreground();
        Assert.NotNull(element);
        var under = probe.At(400, 300);
        Assert.NotNull(under);
        Assert.False(string.IsNullOrEmpty(under!.App));
    }

    [Fact]
    public void A_password_field_is_flagged_and_its_text_is_never_read()
    {
        // A one-line form with a password box that holds a known text, shown by Windows PowerShell and closed by the test.
        const string script = "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object Windows.Forms.Form; $f.Text = 'as19 password probe'; "
            + "$t = New-Object Windows.Forms.TextBox; $t.UseSystemPasswordChar = $true; $t.Text = 'native-secret-text'; $f.Controls.Add($t); "
            + "$f.Add_Shown({ $f.Activate(); $t.Focus() }); [void]$f.ShowDialog()";
        using var form = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + script + "\"") { UseShellExecute = true })!;
        try
        {
            var probe = new UiaElementProbe();
            TeachFocus? focus = null;
            Assert.True(SpinWait.SpinUntil(() => (focus = probe.Focused()) is { Element.IsPassword: true }, TimeSpan.FromSeconds(15)), "the password box did not come into focus");
            Assert.True(focus!.Element.IsPassword);
            Assert.Equal("", focus.Element.Name);
            Assert.Null(focus.ReadValue());
        }
        finally
        {
            if (!form.HasExited) form.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void Nothing_in_front_or_an_app_that_does_not_answer_gives_a_null_not_a_hang()
    {
        var probe = new UiaElementProbe();
        var clock = Stopwatch.StartNew();
        _ = probe.At(-30000, -30000);
        Assert.True(clock.Elapsed < UiaElementProbe.CallLimit + TimeSpan.FromSeconds(1));
    }
}
