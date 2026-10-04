using System.Runtime.InteropServices;
using System.Text;
using Marrow.App.Port;
using Marrow.Core.Port;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for UI-SIDEBAR-044 (PR-SIDEBAR-012): the folder chooser of "Add project", SidebarView.swift:500-512
// (canChooseDirectories true, canChooseFiles false, allowsMultipleSelection true, prompt "Add project", message "Choose a folder
// for your agents to work in."), as the Windows common Open dialog WindowsOpenPanelRunner shows it (OpenPanel.cs). Headless the
// runner is always replaced (the seam), so what the system dialog is titled, what its OK button says and what it returns was
// unproven. The dialog is shown on a thread of its own (RunModal blocks its caller) and driven from the test's thread by window
// messages sent to that thread's own windows only; no other window of the desktop is touched.
[Collection("WindowManagerNative")]
public sealed class OpenPanelNativeTests(WindowManagerNativeSession native)
{
    private const string Prompt = "Add project";
    private const string Message = "Choose a folder for your agents to work in.";
    private const uint WM_SETTEXT = 0x000C;
    private const uint BM_CLICK = 0x00F5;
    private const int IDOK = 1;
    private const int IDCANCEL = 2;

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, NativeWindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint dialog, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint wParam, string lParam);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private static OpenPanel SidebarPanel() => new()
    {
        CanChooseDirectories = true, // :501-503
        CanChooseFiles = false,
        AllowsMultipleSelection = true,
        Prompt = Prompt, // :505
        Message = Message, // :506
    };

    private static string TextOf(nint window) => OpenPanelNativeRun.TitleOf(window);

    // The dialog window: the one titled dialog (class #32770) of the dialog thread, and it carries the panel's message as its title. A second
    // titled dialog (a system error box such as "Location is not available") fails here, naming it.
    private static nint Dialog(OpenPanelNativeRun shown)
    {
        N.Until(() => shown.VisibleWindows().Any(window => window.Class == "#32770" && window.Title.Length > 0), "The folder chooser never appeared.", 15000);
        Thread.Sleep(1200); // the dialog's own contents are built after its frame shows
        var titled = shown.VisibleWindows().Where(window => window.Class == "#32770" && window.Title.Length > 0).ToList();
        var chooser = titled.Where(window => window.Title == Message).ToList();
        if (titled.Count != 1 || chooser.Count != 1)
        {
            var description = string.Join("; ", titled.Select(window => $"\"{window.Title}\": {OpenPanelNativeRun.ChildTexts(window.Handle)}"));
            foreach (var window in titled.Where(window => window.Title != Message)) N.Post(window.Handle, 0x0010, 0, 0); // WM_CLOSE: nothing is left on screen
            Assert.Fail($"Expected exactly one titled dialog, titled \"{Message}\"; the dialog thread shows {titled.Count}: {description}");
        }
        shown.RequireNoOtherWindow(chooser[0].Handle, "while the chooser is open");
        return chooser[0].Handle;
    }

    // After OK or Cancel: until the panel has answered, nothing but the chooser may appear (an error box would hold the panel open).
    private static void Close(OpenPanelNativeRun shown, nint dialog, int button)
    {
        _ = SendMessage(GetDlgItem(dialog, button), BM_CLICK, 0, 0);
        var until = Environment.TickCount64 + 15000;
        while (!shown.Done && Environment.TickCount64 < until)
        {
            shown.RequireNoOtherWindow(dialog, "after " + (button == IDOK ? "OK" : "Cancel"));
            Thread.Sleep(50);
        }
        shown.Finish();
    }

    // The file name edit control of the dialog (the Edit inside its ComboBoxEx32).
    private static nint NameBox(nint dialog)
    {
        nint edit = 0;
        NativeWindowEnumerator callback = (window, _) =>
        {
            if (N.ClassName(window) == "Edit" && N.IsWindowVisible(window)) edit = window;
            return true;
        };
        _ = EnumChildWindows(dialog, callback, 0);
        GC.KeepAlive(callback);
        return edit;
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-044")] // SidebarView.swift:500-512 `panel.message` is the dialog's title, `panel.prompt` its OK button, folders only, several at once
    [Trait("Req", "PR-SIDEBAR-012")]
    public void The_windows_folder_chooser_is_titled_with_the_message_labels_OK_Add_project_picks_several_folders_only_and_cancel_returns_nothing() => native.Run(subject =>
    {
        // The option set handed to the dialog: folders (FOS_PICKFOLDERS), several at once (FOS_ALLOWMULTISELECT); the common Open dialog's
        // own file-system flags besides (OpenPanel.cs; the panel's own words for them).
        var options = WindowsOpenPanelRunner.Options(default, SidebarPanel());
        Assert.True(options.HasFlag(WindowsOpenPanelRunner.PickFoldersOption), "folders only");
        Assert.True(options.HasFlag(WindowsOpenPanelRunner.AllowMultiSelectOption), "several at once");
        Assert.Equal((uint)0x20, (uint)WindowsOpenPanelRunner.PickFoldersOption); // FOS_PICKFOLDERS
        Assert.Equal((uint)0x200, (uint)WindowsOpenPanelRunner.AllowMultiSelectOption); // FOS_ALLOWMULTISELECT

        // The chooser opens at the profile's Desktop when it has no last folder to return to: the child's profile must have it, or the shell
        // answers with a "Location is not available" box of its own on top of the chooser.
        foreach (var known in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
            Assert.True(Directory.Exists(Environment.GetFolderPath(known)), $"The child's {known} folder does not exist: {Environment.GetFolderPath(known)}");

        var root = Path.Combine(Path.GetTempPath(), "openpanel-native-" + Guid.NewGuid().ToString("N")[..8]);
        var first = Path.Combine(root, "first project");
        var second = Path.Combine(root, "second project");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            // 1. The real dialog: its title, its OK button, and two folders typed into the name box come back as two URLs.
            var shown = OpenPanelNativeRun.Show(SidebarPanel());
            var dialog = Dialog(shown);
            Assert.Equal(Message, TextOf(dialog)); // the message is the dialog's title
            var ok = GetDlgItem(dialog, IDOK);
            Assert.NotEqual((nint)0, ok);
            Assert.Equal(Prompt, TextOf(ok)); // the prompt is the OK button's label
            var box = NameBox(dialog);
            Assert.NotEqual((nint)0, box);
            _ = SendMessage(box, WM_SETTEXT, 0, "\"" + first + "\" \"" + second + "\"");
            Thread.Sleep(300);
            Close(shown, dialog, IDOK);
            Assert.NotNull(shown.Urls);
            static string Url(string path) => "/" + path.Replace('\\', '/');// the file URL's path of a Windows path (OpenPanel.cs FileURL)
            Assert.Equal(
                [Url(first), Url(second)],
                shown.Urls!.Select(url => url.Path.TrimEnd('/')).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

            // 2. Cancel: nothing comes back, and the OpenPanel answers .cancel (the sidebar adds nothing, SidebarView.swift:507 guard).
            shown = OpenPanelNativeRun.Show(SidebarPanel());
            dialog = Dialog(shown);
            Assert.Equal(Message, TextOf(dialog));
            Close(shown, dialog, IDCANCEL);
            Assert.Null(shown.Urls);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }, factType: typeof(OpenPanelNativeTests));
}
