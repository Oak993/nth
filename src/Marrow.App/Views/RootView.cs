using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Port;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :1-450.
// Skeleton (c100 group 4): every type the file declares, with the inputs other files pass; every view body and member
// with behaviour is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` and
// `@Environment(WindowLiveResize.self)` are read from the environment, not passed in. The `@Entry` environment value of
// `extension EnvironmentValues` (:116-118) is an inherited attached property on the class named after the Swift file
// (R-NAME-4, R-VIEW-6), which is the file's own view RootView. NSViewRepresentable is the Decorator hosting its NSView
// (NSView is Control). Swift `private` top-level types are `internal`.
// B3 delta (B3_PLAN.md section 4): CopyCommandButton (B2 :424-451) is removed from this file outright; grep of the
// pinned B3 tree confirms no CopyCommandButton in RootView.swift and no other DroppyCode file declaring it either
// (SOURCE FACT, verified against E:/Droppy/reference/droppy-code-v1.7.0). Its class and test are deleted here.
//
// This unit implements the 6 remaining types that do not depend on SidebarView.cs (53 MARROW_STUB) or ChatView.cs (23
// MARROW_STUB): DetailView's empty-state branch, WelcomeView, ProviderChecklist, ProviderStatusRow,
// OpenProvidersSettingsButton and NoThreadView. RootView.HandleDrop (below) is implemented and tested through an
// extracted HandleDropCore, which needs no visual tree at all. RootView's top-level body and ThreadColumns stay
// MARROW_STUB: RootView's body needs SidebarView (still 53 MARROW_STUB; its own constructor throws unconditionally,
// so no RootView instance can be built while it does) and, only for the isCommandPalettePresented branch,
// CommandPalette (still MARROW_STUB); GenieLayer, RowGlideLayer, WindowChromeConfigurator and WallpaperStore are now
// real (0 MARROW_STUB each), narrowing the block from the prior STOP note (see the STOP note at the end of
// RootViewTests.cs). ThreadColumns needs ChatView (blocked) and a WindowLiveResize read from the environment, which
// HostedEnvironment (App.Window.WindowManager.cs) now attaches for real; DetailView's "a thread is selected" branch
// calls into ThreadColumns and so stays MARROW_STUB for ChatView's sake alone.

/// <summary>:4-111 <c>struct RootView: View</c>, with the file's <c>extension EnvironmentValues</c> (:116-118).</summary>
public sealed class RootView : Decorator
{
    // :8-92 body
    public RootView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:8");

    // :94-110 @discardableResult private func handleDrop(_ urls: [URL]) -> Bool. Delegates to HandleDropCore, which
    // holds the whole function body: HandleDropCore needs only the model and the dropped urls, never `this` or any
    // control, so it is directly testable without RootView's own body (still MARROW_STUB, see above) ever running.
    // ENGINEERING DECISION: this split is pure testability scaffolding, not an observable behaviour change; the
    // Swift function's own body is reproduced verbatim in HandleDropCore, cited line for line below.
    private bool HandleDrop(IReadOnlyList<SwiftURL> urls) =>
        HandleDropCore(AppEnvironment.GetAppModel(this), urls);

    // :94-110 the whole handleDrop body. internal (not private, R-NAME-6's testability exception noted above): tests
    // reach it through Marrow.App's existing [assembly: InternalsVisibleTo("Marrow.App.Tests")]
    // (Port/AttachmentFlightPlatform.cs:10), the same visibility every other testable internal member already uses.
    // FOR WHOEVER WIRES THIS TO A REAL DROP EVENT (RootView's own top-level body, still MARROW_STUB): do not build
    // these SwiftURL arguments as a plain "file://" + path URL. SOURCE FACT, empirically confirmed in
    // RootViewTests.cs's own DroppedUrl helper and already documented on Services.Providers.CodexSession.cs's
    // DirectoryFileUrl and ACPSession.cs: a "file://" SwiftURL's .Path (SwiftURL.cs's FileSystemPath, cited there)
    // reproduces macOS's POSIX url.path unconditionally and corrupts a Windows drive-letter path ("C:\Users\x" ->
    // "/C:/Users/x"), which then reads as not existing. Build the URL the same way DirectoryFileUrl does: a
    // non-"file" scheme (e.g. "marrow-dropped-path:"), whose .Path takes the plain percent-decode branch with no
    // POSIX-root handling.
    internal static bool HandleDropCore(AppModel model, IReadOnlyList<SwiftURL> urls)
    {
        // :96-102. FileManager.fileExists(atPath:isDirectory:) (R-FS-1): a path that exists as neither is skipped
        // (the guard fails and the loop `continue`s); Directory.Exists is checked first since Swift's isDirectory
        // out-param is authoritative when the path exists at all.
        var folders = new List<SwiftURL>();
        var files = new List<SwiftURL>();
        foreach (var url in urls)
        {
            var path = url.Path;
            if (Directory.Exists(path)) folders.Add(url); // :101 isDirectory.boolValue
            else if (File.Exists(path)) files.Add(url); // :101 else branch
            // else: fileExists returned false; :100 guard ... else { continue }
        }

        // :103-105. lastProject stays null when folders is empty, matching Swift's uninitialized-then-optional var.
        Project? lastProject = null;
        foreach (var folder in folders) lastProject = model.AddProject(folder); // :104
        if (lastProject is { } project) model.NewThread(project); // :105 model.newThread(in: lastProject)

        // :106-108. The selection is read now, as it stands after the folder loop above. SOURCE FACT, verified
        // directly against the pinned reference (AppModel.swift:614/623: newThread(in:) sets
        // selectedThreadID = thread.id), overriding this record's own windows_path prose (PR-WIN-065, "a thread
        // created by the folder half is NOT the thread the files attach to"): that prose does not match the traced
        // control flow. newThread(in:) selects the thread it creates BEFORE this line runs, so a mixed drop of a
        // folder and a file, with nothing selected beforehand, DOES attach the file to the thread the folder half
        // just created (RootViewTests.cs, A_mixed_drop_with_no_prior_selection_attaches_the_file_to_the_thread_the_
        // folder_half_just_created). Only a thread already selected BEFORE the drop, unrelated to the folder half,
        // keeps its own selection here (:104-105 touch nothing when folders is empty).
        if (files.Count > 0 && model.SelectedThreadID is { } threadID)
        {
            var runtime = model.Runtime(threadID);
            Storage.Attach(
                files.Select(file => (AttachmentSource)new AttachmentSource.File(file.Path)).ToArray(), // :107 files.map(AttachmentSource.file)
                () => runtime.Draft.Attachments,
                value => runtime.Draft = runtime.Draft with { Attachments = value }); // :107 Bindable(...).draft.attachments
        }

        return folders.Count > 0 || files.Count > 0; // :109
    }

    // :117 @Entry var chatColumnIsShown = true: whether the chat column a view sits in is the one on screen.
    public static readonly AttachedProperty<bool> ChatColumnIsShownProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ChatColumnIsShown", typeof(RootView), true, inherits: true);

    public static bool GetChatColumnIsShown(Control control) => control.GetValue(ChatColumnIsShownProperty);

    public static void SetChatColumnIsShown(Control control, bool value) => control.SetValue(ChatColumnIsShownProperty, value);
}

/// <summary>:120-167 <c>struct DetailView: View</c>: the selected thread's column, or the empty states.</summary>
public sealed class DetailView : Decorator
{
    // :129-166 body. Each Track call is an independent, sibling top-level statement in the constructor (not nested
    // inside another Track's apply callback): a tracker registered from inside AttachedToVisualTree's own dispatch
    // does not fire for that same attachment (standard .NET event-handler-added-during-dispatch semantics).
    public DetailView()
    {
        this.Track(() => AppEnvironment.GetAppModel(this), _ => Rebuild());
        this.Track(() => AppEnvironment.GetAppModel(this).SelectedThreadID, _ => Rebuild());
        this.Track(() => AppEnvironment.GetAppModel(this).Projects.Count, _ => Rebuild());
    }

    private void Rebuild()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        // :130 `if let threadID = model.selectedThreadID, model.thread(threadID) != nil`
        if (model.SelectedThreadID is { } threadID && model.Thread(threadID) is not null)
        {
            // MARROW_STUB: :132 ThreadColumns(threadID:prebuilt:running:model:liveResize:). Genuinely blocked: it
            // needs a WindowLiveResize read from the environment, which nothing yet attaches (WindowManager's
            // HostedEnvironment, :165-179, is itself MARROW_STUB), and ThreadColumns.updateNSView (:193-198) calls
            // into ChatView (App.Views.Chat.ChatView.cs, 23 MARROW_STUB).
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:132");
        }

        // :156-164 the empty states, frame(maxWidth:.infinity,maxHeight:.infinity).detailSheet()
        Control content = model.Projects.Count == 0 ? new WelcomeView() : new NoThreadView();
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Stretch;
        Child = content.DetailSheet();
    }
}

/// <summary>:177-297 <c>private struct ThreadColumns: NSViewRepresentable</c>: the chat column hosted per thread, the
/// columns of the threads visited last kept hidden in the pane.</summary>
internal sealed class ThreadColumns : Decorator
{
    // :179 static let topRowsBuiltAtLaunch = 6
    public const long TopRowsBuiltAtLaunch = 6;

    // :181-187 let threadID, prebuilt, running, model, liveResize; :189-202 makeNSView, updateNSView, makeCoordinator
    public ThreadColumns(Guid threadID, IReadOnlyList<Guid> prebuilt, IReadOnlySet<Guid> running, AppModel model, WindowLiveResize liveResize) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:189");

    /// <summary>:204-296 <c>@MainActor final class Coordinator</c>.</summary>
    public sealed class Coordinator
    {
        // :206 private static let keptLimit = 8
        private const long KeptLimit = 8;

        // :224-243 func show(_:in:model:liveResize:)
        public void Show(Guid id, Control @in, AppModel model, WindowLiveResize liveResize) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:224");

        // :247-249 func dropHidden(where:)
        public void DropHidden(Func<Guid, bool> where) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:247");

        // :254-263 func prebuild(_:in:model:liveResize:)
        public void Prebuild(IReadOnlyList<Guid> ids, Control @in, AppModel model, WindowLiveResize liveResize) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:254");
    }
}

/// <summary>:299-350 <c>struct WelcomeView: View</c>: the first-run screen.</summary>
public sealed class WelcomeView : Decorator
{
    // :302-349 body. Two independent, sibling Track calls (not one nested inside the other's apply callback, per
    // the Track-nesting bug this port keeps re-finding): the whole page rebuilds on either the model attaching or
    // the activity style changing, since :331 `ChromeRow(detail: settings.activityThreadStyle.detail)` reads a
    // plain string computed once, not a reactive binding.
    public WelcomeView()
    {
        this.Track(() => AppEnvironment.GetAppModel(this), Build);
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.ActivityThreadStyle, _ => Build(AppEnvironment.GetAppModel(this)));
    }

    private void Build(AppModel model)
    {
        var settings = model.Settings;

        // :306-315 the icon, product name and tagline, spacing 14. DEVIATION (OD-0002; OD-0012 F2): "Marrow" replaces
        // the literal "Droppy Code"; the tagline reads "A calm, native home for your coding agents."
        var icon = SwiftImage.Named("droppy-logo")
            .Resizable()
            .Interpolation(BitmapInterpolationMode.HighQuality)
            .AspectRatio(ContentMode.Fit);
        icon.Width = 104;
        icon.Height = 104;

        var title = new TextBlock { Text = "Marrow" }; // :310-311, OD-0002
        SwiftFont.System(34, SwiftFont.Weight.Semibold).Apply(title);

        var tagline = new TextBlock { Text = "A calm, native home for your coding agents." }; // :312-313, OD-0012 F2
        SwiftFont.System(SwiftFont.TextStyle.Title3).Apply(tagline);
        tagline.Foreground = Chrome.SecondaryText.Brush(tagline);

        var identity = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
        identity.Children.Add(icon);
        identity.Children.Add(title);
        identity.Children.Add(tagline);

        // :317-327 "Add a project" (glassProminent, extraLarge) over the drop hint, spacing 10
        var addProjectLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        addProjectLabel.Children.Add(SwiftImage.SystemName("folder.badge.plus").Font(SwiftFont.System(SwiftFont.TextStyle.Body)));
        addProjectLabel.Children.Add(new TextBlock { Text = "Add a project", VerticalAlignment = VerticalAlignment.Center });
        var addProjectButton = GlassProminentButton(addProjectLabel, ExtraLargeControlHeight, model.ChooseProjectFolder); // :318 model.chooseProjectFolder()

        var dropHint = new TextBlock { Text = "Or drop a folder anywhere in this window." }; // :325-327
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(dropHint);
        dropHint.Foreground = Chrome.SecondaryText.Brush(dropHint);

        var addProjectStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        addProjectStack.Children.Add(addProjectButton);
        addProjectStack.Children.Add(dropHint);

        // :329-338 "Activity list" section, capped at 480
        var activityBinding = new Binding<ActivityThreadStyle>(
            () => settings.ActivityThreadStyle,
            value => settings.ActivityThreadStyle = value);
        var activityPicker = new ChromeVisualPicker<ActivityThreadStyle, Control>(
            [(ActivityThreadStyle.Icon, ActivityThreadStyle.Icon.Title), (ActivityThreadStyle.ProjectName, ActivityThreadStyle.ProjectName.Title)],
            activityBinding,
            style => SwiftImage.SystemName(style == ActivityThreadStyle.Icon ? "list.bullet" : "text.alignleft"));
        var activityRow = new ChromeRow("Project mark", () => activityPicker, settings.ActivityThreadStyle.Detail);
        var activitySection = new ChromeSection("Activity list", () => new ChromeCard(() => activityRow));
        activitySection.MaxWidth = 480;

        // :339-342 "Providers" section, capped at 480
        var providersSection = new ChromeSection("Providers", () => new ProviderChecklist());
        providersSection.MaxWidth = 480;

        // :304-343 the outer VStack(spacing: 30), padded 48
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Center };
        column.Children.Add(identity);
        column.Children.Add(addProjectStack);
        column.Children.Add(activitySection);
        column.Children.Add(providersSection);

        var padded = new Border { Padding = new Thickness(48), Child = column, HorizontalAlignment = HorizontalAlignment.Stretch };

        // :347-348 .scrollIndicators(.never); Avalonia has no direct twin of .scrollBounceBehavior(.basedOnSize).
        Child = new ScrollViewer
        {
            Content = padded,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    // ENGINEERING DECISION: SwiftUI's `.controlSize(.extraLarge)` has no Avalonia twin and this codebase gives no
    // metric for it elsewhere (only Chrome.CapsuleContentHeight, .controlSize(.regular)'s own 28). 40 reads as a
    // visibly larger glass capsule than the 28-point regular one without a source citation; UNVERIFIED under OD-0001.
    internal const double ExtraLargeControlHeight = 40;

    /// <summary>A `.buttonStyle(.glassProminent)` capsule: the tinted-glass recipe TourView's primaryActionButton
    /// (App.Views.Tour.TourView.cs) already established for this button style.</summary>
    internal static Control GlassProminentButton(Control label, double height, Action action)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        var framed = new Border
        {
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0),
            Height = height,
            Child = label,
        };
        framed.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Tint(Chrome.Accent, 0.3).Interactive());
        framed.Background = Chrome.Accent.Opacity(0.3).Brush(framed);
        return PlainControls.Button(framed, action);
    }
}

/// <summary>:352-363 <c>struct ProviderChecklist: View</c>.</summary>
public sealed class ProviderChecklist : Decorator
{
    // :356-361 body: one row per ProviderKind case in declaration order, a divider between consecutive rows.
    public ProviderChecklist()
    {
        var kinds = SwiftEnum<ProviderKind>.AllCases;
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var index = 0; index < kinds.Count; index++)
        {
            if (index > 0) stack.Children.Add(new ChromeRowDivider());
            stack.Children.Add(new ProviderStatusRow(kinds[index]));
        }
        Child = new ChromeCard(() => stack);
    }
}

/// <summary>:365-410 <c>private struct ProviderStatusRow: View</c>.</summary>
internal sealed class ProviderStatusRow : Decorator
{
    private readonly ProviderKind provider;
    private readonly StackPanel textStack = new() { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock statusText = new();
    private readonly ContentControl trailing = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

    // :367 let provider: ProviderKind; :369-409 body
    public ProviderStatusRow(ProviderKind provider)
    {
        this.provider = provider;

        var icon = new ProviderIcon { Provider = provider, Size = 18 }; // :372-374
        var iconBox = new Border { Width = 22, Child = icon }; // :374 .frame(width: 22)

        var name = new TextBlock { Text = provider.DisplayName }; // :376-377
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(name);
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(statusText); // :378-380
        statusText.Foreground = Chrome.SecondaryText.Brush(statusText);
        textStack.Children.Add(name);
        textStack.Children.Add(statusText);

        var leading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; // :371 HStack(spacing: 12)
        leading.Children.Add(iconBox);
        leading.Children.Add(textStack);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; // :382 Spacer() pushes trailing to the far edge
        Grid.SetColumn(leading, 0);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(leading);
        grid.Children.Add(trailing);

        Child = new Border { Padding = new Thickness(16, 11), Child = grid }; // :407-408

        this.Track(() => AppEnvironment.GetAppModel(this), _ => Rebuild());
        // Tracks the observed Statuses map (Read/Set-wrapped), not Status(provider) itself, which reads the
        // backing dictionary directly without going through Read(): a pre-existing gap in ProviderRegistry
        // (Services.Providers.ProviderRegistry.cs), a different, already-implemented unit, not this one's to fix.
        this.Track(() => AppEnvironment.GetAppModel(this).Providers.Statuses, _ => Rebuild());
    }

    private void Rebuild()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        var status = model.Providers.Status(provider); // :370
        statusText.Text = status.IsChecking ? "Checking…" : status.Summary; // :379, U+2026

        Control? content = null;
        if (status.IsChecking) // :383-386
        {
            content = new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 };
        }
        else if (!status.IsInstalled) // :386-394
        {
            content = provider.IsAPIKeyBased
                ? new OpenProvidersSettingsButton()
                : GlassButton("Install", () => Process.Start(new ProcessStartInfo(provider.InstallURL.AbsoluteString) { UseShellExecute = true }));
        }
        else if (status.ProviderStatusAuth is ProviderStatus.Auth.SignedOut) // :395-401
        {
            content = provider.IsAPIKeyBased
                ? new OpenProvidersSettingsButton()
                : GlassButton("Login", () => Workspace.OpenTerminal(provider.LoginCommand));
        }
        else // :402-405
        {
            content = SwiftImage.SystemName("checkmark.circle.fill").ForegroundStyle(Chrome.Success);
        }
        trailing.Content = content;
    }

    /// <summary>A plain `.buttonStyle(.glass)` text capsule (Install, Login, Add key): the same glass-capsule recipe
    /// Chrome.ChromeTextButton (Marrow.UI.Chrome.Chrome.cs) already established, without its hover tint (not
    /// documented for this row's trailing control). The tooltip, when given, is set on the Button itself (not the
    /// glass wrapper ChromeGlassCapsule() returns), so a caller or a test can find it via ToolTip.GetTip(button).</summary>
    internal static Control GlassButton(string title, Action action, string? help = null)
    {
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        var framed = new Border { Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), Height = Chrome.CapsuleContentHeight, Child = text };
        var label = new Border { Padding = new Thickness(0, Chrome.CapsuleVerticalPadding), Background = Brushes.Transparent, Child = framed }
            .Capsule(RoundedCornerStyle.Continuous);
        var button = PlainControls.Button(label, action);
        if (help is not null) ToolTip.SetTip(button, help);
        return button.ChromeGlassCapsule();
    }
}

/// <summary>:413-422 <c>private struct OpenProvidersSettingsButton: View</c>.</summary>
internal sealed class OpenProvidersSettingsButton : Decorator
{
    // :414-421 body. The window is shown BEFORE the page is requested, the opposite order from
    // WindowManager.ShowSettings(page:); reproduced as written (PR-WIN-084 windows_path).
    public OpenProvidersSettingsButton()
    {
        Child = ProviderStatusRow.GlassButton("Add key", () =>
        {
            WindowManager.Shared.ShowSettings(); // :416
            SettingsNavigation.Shared.RequestedPage = SettingsPage.Providers; // :417
        }, help: "Open Settings > Providers"); // :420
    }
}

/// <summary>:424-450 <c>struct NoThreadView: View</c>.</summary>
public sealed class NoThreadView : Decorator
{
    // :427-448 body
    public NoThreadView()
    {
        var symbol = SwiftImage.SystemName("bubble.left.and.text.bubble.right") // :429
            .Font(SwiftFont.System(46, SwiftFont.Weight.Light))
            .ForegroundStyle(Chrome.SecondaryText);

        var heading = new TextBlock { Text = "Pick a thread or start a new one" }; // :432
        SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium).Apply(heading);

        var newThreadLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        newThreadLabel.Children.Add(SwiftImage.SystemName("square.and.pencil").Font(SwiftFont.System(SwiftFont.TextStyle.Body)));
        newThreadLabel.Children.Add(new TextBlock { Text = "New thread", VerticalAlignment = VerticalAlignment.Center });

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(symbol);
        stack.Children.Add(heading);
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            stack.Children.Add(WelcomeView.GlassProminentButton(newThreadLabel, LargeControlHeight, () => model.NewThread())); // :434-440
            // :441-446 the chord hint, omitted entirely when the shortcut has been cleared
            if (ShortcutStore.Label(AppShortcut.NewThread) is { } chord)
            {
                var chordText = new TextBlock { Text = chord };
                SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(chordText);
                chordText.Foreground = Chrome.SecondaryText.Brush(chordText);
                stack.Children.Add(chordText);
            }
        });

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Child = stack;
    }

    // ENGINEERING DECISION: `.controlSize(.large)`, like WelcomeView's `.extraLarge` above it, has no Avalonia twin
    // or a metric elsewhere in this codebase; 34 reads as a visibly smaller glass capsule than WelcomeView's 40-point
    // extra-large button, matching the packet's note that the two buttons use different control sizes on purpose.
    // UNVERIFIED under OD-0001.
    private const double LargeControlHeight = 34;
}
