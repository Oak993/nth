using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.Core.Localisation;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W8b, the cards a Visus tab asks in besides app links (E:/Droppy/work/design/web-surfaces/DESIGN.md
// 5.4 "nth shows a glass prompt under the toolbar: file name, size, type, source site. Buttons: Save (to the Downloads
// folder), Save as…, Cancel. Esc cancels", "Risky types ... add 'This file can run code on your PC.' and Save needs a second
// click within 3 s", 5.5 "'localhost:5173 wants to use your camera.' Allow this time / Always allow on this site / Block. No
// default focus; Enter does nothing; Esc blocks", 8.4 "HTTP auth ... nth shows its own card under the toolbar: 'example.com
// asks you to sign in' with user name and password fields, the realm in small text, Sign in / Cancel; values go to the
// response and are not stored by nth", "Script dialogs (alert, confirm, prompt): nth draws them as cards under the toolbar,
// labelled with the site", pointer lock "asks"; the canvas's #perm2 and #dl2 (.pop .pt2 .warn .row .pbtn.pri .foot);
// ED-NEW-web-w8b-1, -2, -4). No Swift counterpart.

namespace Marrow.App.Views.Browser;

/// <summary>5.5 and 8.4: "localhost:5173 wants to use your camera" with Allow this time, Always on this site and Block.</summary>
public sealed class VisusPermissionPrompt : VisusPromptCard
{
    public VisusPermissionPrompt(VisusPermissionAsk ask, string? pageTitle)
        : base(ask, SitePermissions.Title(ask.Source, ask.Permission), DetailFor(ask, pageTitle), GlyphFor(ask.Permission))
    {
        Permission = ask;
        AllowOnceButton = AddButton(SitePermissions.AllowOnceLabel, SitePermissions.AllowOnceName(ask.Source, ask.Permission), primary: false,
            () => ask.Answer(SitePermissionAnswer.AllowOnce));
        if (ask.OffersAlways)
        {
            AlwaysButton = AddButton(SitePermissions.AllowAlwaysLabel, SitePermissions.AllowAlwaysName(ask.Source, ask.Permission), primary: false,
                () => ask.Answer(SitePermissionAnswer.AllowAlways));
        }
        BlockButton = AddButton(SitePermissions.BlockLabel, SitePermissions.BlockName(ask.Source, ask.Permission), primary: false,
            () => ask.Answer(SitePermissionAnswer.Block));
        Finish(SitePermissions.Foot);
    }

    public VisusPermissionAsk Permission { get; }

    public Button AllowOnceButton { get; }

    /// <summary>"Always on this site" (null in a private tab, which keeps no answer).</summary>
    public Button? AlwaysButton { get; }

    public Button BlockButton { get; }

    private static string DetailFor(VisusPermissionAsk ask, string? pageTitle)
    {
        var title = pageTitle?.ReplaceLineEndings(" ").Trim() ?? "";
        if (title.Length > 0) return UiText.Format("visus.card.permission_for_page", Shorten(title, 60));
        return ask.Tab.Kind == VisusTabKind.Private ? UiText.Get("visus.card.permission_private_tab") : UiText.Get("visus.card.permission_this_page");
    }

    internal static PaneGlyphKind GlyphFor(SitePermissionKind kind) => kind switch
    {
        SitePermissionKind.Camera => PaneGlyphKind.Camera,
        SitePermissionKind.Microphone => PaneGlyphKind.Microphone,
        SitePermissionKind.Location => PaneGlyphKind.Location,
        SitePermissionKind.Notifications => PaneGlyphKind.Bell,
        SitePermissionKind.ClipboardRead => PaneGlyphKind.Copy,
        SitePermissionKind.FileSystemAccess => PaneGlyphKind.Folder,
        SitePermissionKind.MultipleDownloads => PaneGlyphKind.Download,
        SitePermissionKind.PointerLock => PaneGlyphKind.Pointer,
        _ => PaneGlyphKind.Shield,
    };
}

/// <summary>5.4: the file's name, "12 KB · CSV · from localhost:5173", Save, Save as… and Cancel.</summary>
public sealed class VisusDownloadPrompt : VisusPromptCard
{
    private readonly DownloadSaveArm arm;
    private readonly TextBlock saveFace;
    private readonly Func<VisusDownloadAsk, Task<string?>> pickPath;
    private DispatcherTimer? disarm;
    private bool picking;

    public VisusDownloadPrompt(VisusDownloadAsk ask, Func<VisusDownloadAsk, Task<string?>> pickPath)
        : base(ask, ask.FileName, ask.Detail, PaneGlyphKind.Download)
    {
        Download = ask;
        this.pickPath = pickPath;
        arm = new DownloadSaveArm(ask.IsRisky, VisusHost.Time);
        if (ask.IsRisky)
        {
            var mark = new PaneGlyph(PaneGlyphKind.Warning, 13) { VerticalAlignment = VerticalAlignment.Center };
            Warning = Text(DownloadGate.RiskyWarning, 12, VisusTokens.NotSecureText, wrap: true);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(30, 8, 0, 0), Children = { mark, Warning } };
            void Look() => mark.Foreground = VisusTokens.NotSecureText.Brush(mark);
            mark.ActualThemeVariantChanged += (_, _) => Look();
            Look();
            Body.Children.Add(row);
        }
        SaveButton = AddButton(DownloadGate.SaveLabel, DownloadGate.SaveName(ask.FileName), primary: true, PressSave, out saveFace);
        SaveAsButton = AddButton(DownloadGate.SaveAsLabel, DownloadGate.SaveAsName(ask.FileName), primary: false, () => _ = SaveAsAsync());
        CancelButton = AddButton(DownloadGate.CancelLabel, DownloadGate.CancelName(ask.FileName), primary: false, () => ask.Answer(DownloadChoice.Cancel));
        Finish(DownloadGate.Foot);
        Done += _ => StopDisarm();
    }

    public VisusDownloadAsk Download { get; }

    public Button SaveButton { get; }

    public Button SaveAsButton { get; }

    public Button CancelButton { get; }

    /// <summary>"This file can run code on your PC." (risky types only).</summary>
    public TextBlock? Warning { get; }

    /// <summary>Save's words now ("Save", or "Click again to save" for 3 s after a first click on a risky file).</summary>
    public string SaveLabel => arm.IsArmed ? DownloadGate.ArmedLabel : DownloadGate.SaveLabel;

    private void PressSave()
    {
        if (picking) return;
        if (VisusHost.AskWhereToSave())
        {
            // Settings › Visus "Ask where to save": Save opens the system dialog too (the risky second click still holds).
            if (!arm.Press())
            {
                Arm();
                return;
            }
            _ = SaveAsAsync();
            return;
        }
        if (!arm.Press())
        {
            Arm();
            return;
        }
        StopDisarm();
        Download.Answer(DownloadChoice.Save);
    }

    private void Arm()
    {
        saveFace.Text = DownloadGate.ArmedLabel;
        AutomationProperties.SetName(SaveButton, DownloadGate.ArmedLabel + ": " + DownloadGate.SaveName(Download.FileName));
        StopDisarm();
        if (!Dispatcher.UIThread.CheckAccess()) return;
        disarm = new DispatcherTimer(DownloadGate.SecondClickWindow, DispatcherPriority.Background, (_, _) =>
        {
            StopDisarm();
            arm.Disarm();
            saveFace.Text = DownloadGate.SaveLabel;
            AutomationProperties.SetName(SaveButton, DownloadGate.SaveName(Download.FileName));
        });
        disarm.Start();
    }

    private void StopDisarm()
    {
        disarm?.Stop();
        disarm = null;
    }

    private async Task SaveAsAsync()
    {
        if (picking || IsClosed) return;
        picking = true;
        SetButtonsEnabled(false);
        string? path;
        try
        {
            path = await pickPath(Download);
        }
        catch (Exception)
        {
            path = null;
        }
        picking = false;
        SetButtonsEnabled(true);
        // The system dialog was closed without a place: the prompt stays, nothing is saved.
        if (!string.IsNullOrEmpty(path)) Download.Answer(DownloadChoice.SaveAs, path);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (var button in Buttons) button.IsEnabled = enabled;
    }
}

/// <summary>8.4: "example.com asks you to sign in", user name and password, the realm in small text, Sign in and Cancel.</summary>
public sealed class VisusSignInCard : VisusPromptCard
{
    /// <summary>The card's foot: where the values go (resource key visus.card.sign_in_foot).</summary>
    public static string Foot => UiText.Get("visus.card.sign_in_foot");

    /// <summary>The warning on a page without encryption (not a local server; resource key visus.card.sign_in_plain_warning).</summary>
    public static string PlainWarning => UiText.Get("visus.card.sign_in_plain_warning");

    public VisusSignInCard(VisusSignInAsk ask)
        : base(ask, UiText.Format("visus.card.sign_in_title", ask.Source), ask.Realm ?? "", PaneGlyphKind.Lock)
    {
        SignIn = ask;
        UserName = Field(UiText.Get("visus.card.user_name"), secret: false);
        Password = Field(UiText.Get("visus.card.password"), secret: true);
        AutomationProperties.SetName(UserName, UiText.Format("visus.card.user_name_for", ask.Source));
        AutomationProperties.SetName(Password, UiText.Format("visus.card.password_for", ask.Source));
        Body.Children.Add(Frame(UserName, new Thickness(30, 10, 0, 0)));
        Body.Children.Add(Frame(Password, new Thickness(30, 6, 0, 0)));
        if (IsPlain(ask.Uri))
        {
            Warning = Text(PlainWarning, 12, VisusTokens.NotSecureText, wrap: true, margin: new Thickness(30, 8, 0, 0));
            Body.Children.Add(Warning);
        }
        SignInButton = AddButton(UiText.Get("visus.card.sign_in"), UiText.Format("visus.card.sign_in_name", ask.Source), primary: true, Send);
        CancelButton = AddButton(UiText.Get("visus.card.cancel"), UiText.Format("visus.card.sign_in_cancel_name", ask.Source), primary: false, ask.Cancel);
        Finish(Foot);
    }

    public VisusSignInAsk SignIn { get; }

    public TextBox UserName { get; }

    public TextBox Password { get; }

    public Button SignInButton { get; }

    public Button CancelButton { get; }

    /// <summary>The plain-http warning (null on https and local servers).</summary>
    public TextBlock? Warning { get; }

    protected override bool OnFieldEnter(TextBox field)
    {
        Send();
        return true;
    }

    private void Send()
    {
        var user = UserName.Text ?? "";
        var password = Password.Text ?? "";
        // nth keeps none of it: the fields are cleared as the values go to the response.
        UserName.Text = "";
        Password.Text = "";
        SignIn.SignIn(user, password);
    }

    /// <summary>Plain http to anything but this PC (a password over the network in the clear).</summary>
    internal static bool IsPlain(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttp) return false;
        var host = parsed.Host.Trim('[', ']').ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal)) return false;
        return !(System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
    }

    internal static TextBox Field(string placeholder, bool secret)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = 12.5,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };
        if (secret) box.PasswordChar = '\u2022';
        ChatDialogControls.TextEditor(box);
        return box;
    }

    internal static Border Frame(TextBox field, Thickness margin)
    {
        var frame = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(10, 0),
            Margin = margin,
            Child = field,
        };
        void Look()
        {
            var focused = field.IsKeyboardFocusWithin;
            frame.Background = (focused ? VisusTokens.AddressFocusFill : VisusTokens.AddressFill).Brush(frame);
            frame.BorderBrush = (focused ? VisusTokens.AddressFocusRim : VisusTokens.AddressRim).Brush(frame);
            field.Foreground = PaneTokens.Text1.Brush(frame);
        }
        field.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty) Look();
        };
        frame.ActualThemeVariantChanged += (_, _) => Look();
        Look();
        return frame;
    }
}

/// <summary>8.4: a page's alert, confirm, prompt or leave-page dialog, as nth's card labelled with the site.</summary>
public sealed class VisusDialogCard : VisusPromptCard
{
    /// <summary>"Don't let this page show more dialogs" (from the page's second dialog; resource key visus.card.dialog_stop).</summary>
    public static string StopLabel => UiText.Get("visus.card.dialog_stop");

    /// <summary>The longest page message a card shows (the rest is cut with an ellipsis).</summary>
    public const int MessageLimit = 600;

    public VisusDialogCard(VisusDialogAsk ask)
        : base(ask, TitleFor(ask), DetailFor(ask), PaneGlyphKind.Transcript)
    {
        Dialog = ask;
        if (ask.DialogKind == WebScriptDialogKind.Prompt)
        {
            Answer = VisusSignInCard.Field("", secret: false);
            Answer.Text = ask.DefaultText;
            AutomationProperties.SetName(Answer, UiText.Format("visus.card.dialog_answer_name", ask.Source));
            Body.Children.Add(VisusSignInCard.Frame(Answer, new Thickness(30, 10, 0, 0)));
        }
        if (ask.OffersStop)
        {
            Stop = new CheckBox { Content = Text(StopLabel, 12.5, PaneTokens.Text1, wrap: true), Margin = new Thickness(30, 10, 0, 0) };
            ChatDialogControls.Checkbox(Stop);
            AutomationProperties.SetName(Stop, UiText.Format("visus.card.dialog_stop_name", ask.Source));
            Body.Children.Add(Stop);
        }
        switch (ask.DialogKind)
        {
            case WebScriptDialogKind.Alert:
                AcceptButton = AddButton(UiText.Get("visus.card.ok"), UiText.Format("visus.card.dialog_alert_ok_name", ask.Source), primary: true, Accept);
                break;
            case WebScriptDialogKind.BeforeUnload:
                AcceptButton = AddButton(UiText.Get("visus.card.dialog_leave"), UiText.Format("visus.card.dialog_leave_name", ask.Source), primary: false, Accept);
                DismissButton = AddButton(UiText.Get("visus.card.dialog_stay"), UiText.Format("visus.card.dialog_stay_name", ask.Source), primary: true, Refuse);
                break;
            case WebScriptDialogKind.Prompt:
                AcceptButton = AddButton(UiText.Get("visus.card.ok"), UiText.Format("visus.card.dialog_prompt_ok_name", ask.Source), primary: true, Accept);
                DismissButton = AddButton(UiText.Get("visus.card.cancel"), UiText.Format("visus.card.dialog_prompt_cancel_name", ask.Source), primary: false, Refuse);
                break;
            default:
                AcceptButton = AddButton(UiText.Get("visus.card.ok"), UiText.Format("visus.card.dialog_confirm_ok_name", ask.Source), primary: true, Accept);
                DismissButton = AddButton(UiText.Get("visus.card.cancel"), UiText.Format("visus.card.dialog_confirm_cancel_name", ask.Source), primary: false, Refuse);
                break;
        }
        Finish(ask.DialogKind == WebScriptDialogKind.BeforeUnload ? UiText.Get("visus.card.dialog_foot_leave") : UiText.Get("visus.card.dialog_foot"));
    }

    public VisusDialogAsk Dialog { get; }

    /// <summary>OK, or Leave on a leave-page dialog.</summary>
    public Button AcceptButton { get; }

    /// <summary>Cancel, or Stay (null on an alert, which has only OK).</summary>
    public Button? DismissButton { get; }

    /// <summary>A prompt dialog's text field.</summary>
    public TextBox? Answer { get; }

    public CheckBox? Stop { get; }

    protected override bool OnFieldEnter(TextBox field)
    {
        if (!ReferenceEquals(field, Answer)) return false;
        Accept();
        return true;
    }

    private void Accept() => Dialog.Accept(Answer?.Text, Stop?.IsChecked == true);

    private void Refuse() => Dialog.Dismiss(Stop?.IsChecked == true);

    private static string TitleFor(VisusDialogAsk ask) => ask.DialogKind switch
    {
        WebScriptDialogKind.BeforeUnload => UiText.Get("visus.card.dialog_leave_title"),
        WebScriptDialogKind.Alert => UiText.Format("visus.card.dialog_says", ask.Source),
        _ => UiText.Format("visus.card.dialog_asks", ask.Source),
    };

    private static string DetailFor(VisusDialogAsk ask)
    {
        if (ask.DialogKind == WebScriptDialogKind.BeforeUnload) return UiText.Format("visus.card.dialog_unsaved", ask.Source);
        var message = new string(ask.Message.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
        return Shorten(message, MessageLimit);
    }
}

/// <summary>8.4 and 5.10: the guarded file upload card of an agent tab: only the user's Choose gives the page a file.</summary>
public sealed class VisusFileCard : VisusPromptCard
{
    /// <summary>The card's detail: who chooses, and what the model sees (resource key visus.card.file_explanation).</summary>
    public static string Explanation => UiText.Get("visus.card.file_explanation");

    private readonly Func<VisusFileAsk, Task<IReadOnlyList<string>?>> pick;
    private bool picking;

    public VisusFileCard(VisusFileAsk ask, Func<VisusFileAsk, Task<IReadOnlyList<string>?>> pick)
        : base(ask, ask.Multiple ? UiText.Format("visus.card.file_title_many", ask.Source) : UiText.Format("visus.card.file_title_one", ask.Source), Explanation, PaneGlyphKind.Folder)
    {
        File = ask;
        this.pick = pick;
        ChooseButton = AddButton(ask.Multiple ? UiText.Get("visus.card.file_choose_many") : UiText.Get("visus.card.file_choose_one"),
            ask.Multiple ? UiText.Format("visus.card.file_choose_many_name", ask.Source) : UiText.Format("visus.card.file_choose_one_name", ask.Source), primary: true, () => _ = ChooseAsync());
        CancelButton = AddButton(UiText.Get("visus.card.cancel"), UiText.Format("visus.card.file_cancel_name", ask.Source), primary: false, ask.Cancel);
        Finish(VisusPrompt.Foot);
    }

    public VisusFileAsk File { get; }

    public Button ChooseButton { get; }

    public Button CancelButton { get; }

    private async Task ChooseAsync()
    {
        if (picking || IsClosed) return;
        picking = true;
        foreach (var button in Buttons) button.IsEnabled = false;
        IReadOnlyList<string>? paths;
        try
        {
            paths = await pick(File);
        }
        catch (Exception)
        {
            paths = null;
        }
        picking = false;
        foreach (var button in Buttons) button.IsEnabled = true;
        // The system dialog was closed without a file: the card stays, the page gets nothing.
        if (paths is { Count: > 0 }) await File.GiveAsync(paths);
    }
}
