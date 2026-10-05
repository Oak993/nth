using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for UI-COMPOSER-029 (the attach dialog: DroppyCode/App/Views/Composer/ComposerView.swift:526-533, PromptEditor.swift:117-124),
// STOP App.Views.Composer.ComposerView #11. The real composer and the real prompt editor on a real window, their attach buttons pressed by the
// system's pointer (SendInput), the dialog the production one (OpenPanel through WindowsOpenPanelRunner: IFileOpenDialog, the options the sites
// set), driven by window messages the way a user's typing and clicks reach it (CommonFileDialogDriver):
//   - the OK button reads the sites' prompt, "Attach" (SetOkButtonLabel);
//   - the caption an empty message gives (SetTitle(""), OpenPanel.cs:74) is recorded;
//   - a folder cannot be chosen: its name and OK open it, the dialog stays and nothing is attached;
//   - Cancel leaves the draft as it was;
//   - two files multi-select (canChooseDirectories false, allowsMultipleSelection true) and both reach the draft.
// The folders are throwaway ones under the run's temp directory. Needs a free interactive desktop; the runner takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class ComposerAttachOpenPanelNativeTests(WindowManagerNativeSession native)
{
    private static void Click(PixelPoint point)
    {
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
        Thread.Sleep(80);
        N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
    }

    private static nint AwaitDialog(string what)
    {
        nint dialog = 0;
        N.Until(() => (dialog = CommonFileDialogDriver.Find()) != 0, what + ": the common Open dialog never appeared", 10000);
        Thread.Sleep(700); // the shell fills the dialog in
        return dialog;
    }

    private static (string Folder, string Inner, string[] Files) Folders(string label)
    {
        var folder = Directory.CreateTempSubdirectory("rec-composer-" + label + "-").FullName;
        var inner = Directory.CreateDirectory(Path.Combine(folder, "inner folder")).FullName;
        var files = new[] { Path.Combine(folder, "first.txt"), Path.Combine(folder, "second.txt") };
        File.WriteAllText(files[0], "one");
        File.WriteAllText(files[1], "two");
        return (folder, inner, files);
    }

    private AvaloniaWindow Show(WindowManagerNativeSubject subject, Control content, Marrow.App.AppModel model)
    {
        var host = new Border { Child = content, Background = Brushes.DimGray, Padding = new Thickness(20) };
        AppEnvironment.SetAppModel(host, model);
        var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
        var window = new AvaloniaWindow
        {
            Width = 700, Height = 360, ShowInTaskbar = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(screen.WorkingArea.X + 120, screen.WorkingArea.Y + 80), Content = host,
        };
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(window);
        window.Show();
        window.Activate();
        shown = window;
        return window;
    }

    // The process's composer chooser shows the real dialog; recent downloads come from a folder of the run's own.
    private void Prepare(WindowManagerNativeSubject subject) => native.OnUi(() =>
    {
        subject.Main.Hide();
        subject.Settings.Hide();
        FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
        ComposerFileDialog.Current.SetProcessDefault(new SystemOpenPanelComposerFileDialog());
        var downloads = Directory.CreateTempSubdirectory("rec-composer-downloads-").FullName;
        RecentDownloads.DirectoryProvider.SetProcessDefault(() => downloads);
    });

    private AvaloniaWindow? shown;

    // The test window in front and active before a press (another program on the desktop may hold the foreground), so the system's
    // pointer press lands on its button.
    private void Front()
    {
        if (shown is not { } window) return;
        native.OnUi(() => WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()!.Handle, native.ThreadId));
        N.Until(() => native.OnUi(() => window.IsActive), "the test window is not the active window");
    }

    // Opens the dialog with the press, then: the label and caption; a folder opened, not chosen; Cancel; again, and two files.
    private (string OkLabel, string Caption) Exercise(Func<PixelPoint> open, Func<int> attached, (string Folder, string Inner, string[] Files) folders, string site)
    {
        Front();
        Click(open());
        var dialog = AwaitDialog(site);
        var label = CommonFileDialogDriver.OkLabel(dialog);
        var caption = CommonFileDialogDriver.Text(dialog);
        Console.WriteLine($"ComposerAttachOpenPanelNativeTests ({site}): OK reads '{label}', the caption reads '{caption}'");

        // the folder the files are in, then the folder inside it: OK opens each, the dialog stays and nothing is attached
        CommonFileDialogDriver.TypeFileName(dialog, folders.Folder);
        CommonFileDialogDriver.Ok(dialog);
        Thread.Sleep(900);
        Assert.True(CommonFileDialogDriver.IsOpen(dialog), $"{site}: OK on a folder's path closed the dialog");
        CommonFileDialogDriver.TypeFileName(dialog, Path.GetFileName(folders.Inner));
        CommonFileDialogDriver.Ok(dialog);
        Thread.Sleep(900);
        Assert.True(CommonFileDialogDriver.IsOpen(dialog), $"{site}: OK on a folder chose it: the dialog closed");
        Assert.Equal(0, attached());
        CommonFileDialogDriver.Cancel(dialog);
        N.Until(() => !CommonFileDialogDriver.IsOpen(dialog), site + ": Cancel did not close the dialog", 5000);
        Thread.Sleep(600);
        Assert.Equal(0, attached()); // Cancel leaves the draft as it was

        Front();
        Click(open());
        dialog = AwaitDialog(site + ", again");
        CommonFileDialogDriver.TypeFileName(dialog, folders.Folder);
        CommonFileDialogDriver.Ok(dialog);
        Thread.Sleep(900);
        Assert.True(CommonFileDialogDriver.IsOpen(dialog), $"{site}: OK on the folder's path closed the dialog");
        CommonFileDialogDriver.TypeFileName(dialog, "\"first.txt\" \"second.txt\""); // two files, as the dialog's own multi-selection writes them
        CommonFileDialogDriver.Ok(dialog);
        N.Until(() => !CommonFileDialogDriver.IsOpen(dialog), site + ": OK with two files did not close the dialog", 5000);
        N.Until(() => attached() == 2, $"{site}: the two files never reached the draft ({attached()} attached)", 8000);
        return (label, caption);
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-029")] // ComposerView.swift:526-533 (allowsMultipleSelection true, canChooseDirectories false, prompt "Attach")
    public void The_attach_dialog_reads_Attach_takes_two_files_refuses_a_folder_and_cancel_leaves_the_draft() => native.Run(subject =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        try
        {
            Prepare(subject);
            var folders = Folders("attach");
            var (runtime, view) = native.OnUi(() =>
            {
                var model = new Marrow.App.AppModel();
                // R-TEST-10, as AppModelHarness.PrepareBootstrap: every CLI provider points at a binary that does not exist
                var missing = Path.Combine(folders.Folder, "no-such-cli", "missing.exe");
                foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
                    if (!kind.IsAPIKeyBased) model.Settings.SetBinaryPath(missing, kind);
                model.Settings.RecentDownloadsPicker = false; // the button opens the chooser itself (:628-631)
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", folders.Folder).Url))!.Id);
                var view = new ComposerView(runtime, folders.Folder);
                Show(subject, view, model);
                return (runtime, view);
            });
            Thread.Sleep(800);
            var attach = native.OnUi(() => view.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Attach files"));
            var (label, _) = Exercise(
                () => native.OnUi(() => attach.PointToScreen(new Point(attach.Bounds.Width / 2, attach.Bounds.Height / 2))),
                () => native.OnUi(() => runtime.Draft.Attachments.Count), folders, "composer");
            Assert.Equal("Attach", label.Replace("&", "", StringComparison.Ordinal));
            var names = native.OnUi(() => runtime.Draft.Attachments.Select(attachment => attachment.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            Assert.Equal(new[] { "first.txt", "second.txt" }, names);
        }
        finally
        {
            N.SetCursorPos(original.X, original.Y);
        }
    }, factType: typeof(ComposerAttachOpenPanelNativeTests));

    [Fact]
    [Trait("Req", "UI-COMPOSER-029")] // PromptEditor.swift:117-124 (the same panel, prompt "Attach"), reached through Choose Files… (:133-135 of DownloadsPopover)
    public void The_prompt_editors_attach_dialog_reads_Attach_takes_two_files_refuses_a_folder_and_cancel_leaves_its_attachments() => native.Run(subject =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        try
        {
            Prepare(subject);
            var folders = Folders("prompt");
            var attachments = ValueList<Attachment>.Empty;
            var editor = native.OnUi(() =>
            {
                var model = new Marrow.App.AppModel();
                var text = "";
                var editor = new PromptEditor("Edit prompt", new Binding<string>(() => text, value => text = value),
                    new Binding<ValueList<Attachment>>(() => attachments, value => attachments = value),
                    onSubmit: () => { }, onCancel: () => { }, extra: () => new Border(), actions: () => new Border());
                Show(subject, editor, model);
                return editor;
            });
            Thread.Sleep(800);
            var add = native.OnUi(() => editor.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Add files"));
            PixelPoint Open()
            {
                // the add button opens the recent downloads popover; its Choose Files… row opens the panel
                Front();
                Click(native.OnUi(() => add.PointToScreen(new Point(add.Bounds.Width / 2, add.Bounds.Height / 2))));
                Control? choose = null;
                N.Until(() => (choose = native.OnUi(() => FindChoose(editor))) is not null, "the Choose Files… row never appeared", 5000);
                Thread.Sleep(400);
                var row = choose!;
                return native.OnUi(() => row.PointToScreen(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2)));
            }
            var (label, _) = Exercise(Open, () => native.OnUi(() => attachments.Count), folders, "prompt editor");
            Assert.Equal("Attach", label.Replace("&", "", StringComparison.Ordinal));
            var names = native.OnUi(() => attachments.Select(attachment => attachment.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            Assert.Equal(new[] { "first.txt", "second.txt" }, names);
        }
        finally
        {
            N.SetCursorPos(original.X, original.Y);
        }
    }, factType: typeof(ComposerAttachOpenPanelNativeTests));

    // The Choose Files… row (DownloadsPopover.swift:133-135) of the editor's open recent downloads popover.
    private static Control? FindChoose(PromptEditor editor)
    {
        var popup = (Avalonia.Controls.Primitives.Popup?)typeof(PromptEditor).GetField("filesPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor);
        if (popup is not { IsOpen: true, Child: { } child }) return null;
        return child.GetSelfAndVisualDescendants().OfType<PopoverItem>()
            .FirstOrDefault(item => item.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Choose Files…"));
    }
}
