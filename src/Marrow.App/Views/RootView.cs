using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Marrow.App.Port;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :1-450.
// RootView and the no-thread detail branches are implemented. ThreadColumns and the selected-thread branch
// alone remain MARROW_STUB, pending App.Views.Chat.ChatView (STOP #2 in RootViewTests.cs).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` and
// `@Environment(WindowLiveResize.self)` are read from the environment, not passed in. The `@Entry` environment value of
// `extension EnvironmentValues` (:116-118) is an inherited attached property on the class named after the Swift file
// (R-NAME-4, R-VIEW-6), which is the file's own view RootView. NSViewRepresentable is the Decorator hosting its NSView
// (NSView is Control). Swift `private` top-level types are `internal`.
// B3 delta (B3_PLAN.md section 4): CopyCommandButton (B2 :424-451) is removed from this file outright; grep of the
// pinned B3 tree confirms no CopyCommandButton in RootView.swift and no other DroppyCode file declaring it either
// (SOURCE FACT, verified against E:/Droppy/reference/droppy-code-v1.7.0). Its class and test are deleted here.
//
// This unit implements the 6 types that do not depend on SidebarView.cs or ChatView.cs (DetailView's empty-state
// branch, WelcomeView, ProviderChecklist, ProviderStatusRow, OpenProvidersSettingsButton, NoThreadView), plus
// RootView.HandleDrop/HandleDropCore (needs no visual tree), plus RootView's own top-level body
// (PR-WIN-062, -063, -066, -067): the HStack, both resize-handle
// overlays, the backdrop/chrome-probe background, the window's own rounded clip, the real drag-and-drop wiring onto
// HandleDrop, the model-alert dialog and the genie/row-glide/command-palette overlay stack. SidebarView.cs's
// constructor and CommandPalette.cs's constructor are both real now (0 MARROW_STUB each), which is what unblocked
// this. ThreadColumns and DetailView's "a thread is selected" branch stay MARROW_STUB: ThreadColumns.updateNSView
// calls into ChatView (App.Views.Chat.ChatView.cs, still 23 MARROW_STUB), the one dependency left (see the STOP note
// at the end of RootViewTests.cs, narrowed to name only ChatView).

/// <summary>:4-111 <c>struct RootView: View</c>, with the file's <c>extension EnvironmentValues</c> (:116-118).</summary>
public sealed class RootView : Decorator
{
    private readonly SidebarView sidebarView = new();
    private readonly Border sidebarClip;
    private readonly SidebarResizeHandle trailingHandleConfig;
    private readonly DragHandleView trailingHandle;
    private readonly SidebarResizeHandle leadingHandleConfig;
    private readonly DragHandleView leadingHandle;
    private readonly DetailView detailView = new();
    private readonly Border detailClip;
    private readonly Border detailPadded;
    private readonly Panel detailSlot = new();
    private readonly WindowBackdrop backdrop;
    private readonly WindowChromeProbe chromeProbe = new();
    private readonly WindowChromeConfigurator chromeConfig = new();
    private readonly Border windowClip;
    private readonly Panel outerRoot = new();
    private readonly Panel paletteHost = new();
    private CommandPalette? palette;
    private Control? floatingPanel;
    private Size detailSize;
    private bool? lastFloats;
    private bool lastOnlyFloats;
    private Size lastFloatDetailSize = new(-1, -1);
    private bool alertShowing;
    private Avalonia.Controls.Window? alertDialog;

    // :80-91 R-VIEW-9: record the owned modal request in headless tests, keeping the real dialog's content.
    internal static Ambient<Func<Avalonia.Controls.Window, Avalonia.Controls.Window, System.Threading.Tasks.Task>> PresentAlertDialog { get; } =
        new(() => (dialog, owner) => dialog.ShowDialog(owner));

    /// <summary>Test-only: the window's own rounded clip (PR-WIN-062, :66-68).</summary>
    internal Border WindowClip => windowClip;

    /// <summary>Test-only: the detail column's own clip, before its (inert) padding (PR-WIN-063, :40).</summary>
    internal Border DetailClip => detailClip;

    // :8-92 body
    public RootView()
    {
        // :14-31 the sidebar column: laid out at its own full target width (:18), then clipped to the visible
        // (rendered) width (:19-20), the visible slice aligned trailing so it slides in from the sidebar's own
        // leading edge as the width grows. :21 onGeometryChange reports the clip's own laid-out width back to the
        // model, the same SizeChanged-report convention already used for a laid-out width (SidebarView.cs:2304).
        sidebarView.HorizontalAlignment = HorizontalAlignment.Right; // :19 alignment: .trailing
        sidebarClip = new Border
        {
            Child = sidebarView,
            ClipToBounds = true, // :20 .clipped()
            Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        sidebarClip.SizeChanged += (_, e) => // :21
        {
            AppModel model;
            try { model = AppEnvironment.GetAppModel(this); }
            catch (InvalidOperationException) { return; }
            model.Sidebar.NoteLaidOutWidth(e.NewSize.Width);
        };

        // :22-31 the trailing resize handle, over the sidebar column's own trailing edge, active while visible.
        trailingHandleConfig = new SidebarResizeHandle
        {
            OnBegin = () => AppEnvironment.GetAppModel(this).Sidebar.BeginDrag(),
            OnChange = delta => AppEnvironment.GetAppModel(this).Sidebar.Drag(delta),
            OnEnd = () => AppEnvironment.GetAppModel(this).Sidebar.EndDrag(),
        };
        trailingHandle = trailingHandleConfig.MakeNSView();
        trailingHandle.Width = SidebarResizeHandle.HitWidth; // :29
        trailingHandle.HorizontalAlignment = HorizontalAlignment.Right; // :22 overlay(alignment: .trailing)
        trailingHandle.VerticalAlignment = VerticalAlignment.Stretch;
        trailingHandle.Margin = new Thickness(0, Chrome.TrafficLightTop + Chrome.TrafficLightDiameter, 0, 0); // :30

        var sidebarSlot = new Panel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        sidebarSlot.Children.Add(sidebarClip);
        sidebarSlot.Children.Add(trailingHandle);

        // :33-51 the detail column: stretches, clips its own bounds first (:40), then insets by the sheet inset on
        // every side but the leading one (:41-43; inert at Chrome.SheetInset == 0, PR-WIN-063).
        detailView.HorizontalAlignment = HorizontalAlignment.Stretch;
        detailView.VerticalAlignment = VerticalAlignment.Stretch;
        detailClip = new Border
        {
            Child = detailView,
            ClipToBounds = true, // :40 .clipped()
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        detailPadded = new Border { Child = detailClip, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch }; // :41-43
        detailPadded.SizeChanged += (_, e) => // :44 onGeometryChange(for: CGSize.self, of: { $0.size }) { detailSize = $0 }
        {
            detailSize = e.NewSize;
            RebuildFloatingOverlay(animated: false);
        };
        detailSlot.Children.Add(detailPadded);
        detailSlot.HorizontalAlignment = HorizontalAlignment.Stretch;
        detailSlot.VerticalAlignment = VerticalAlignment.Stretch;

        // :14 HStack(spacing: 0)
        var hstack = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        hstack.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        hstack.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetColumn(sidebarSlot, 0);
        Grid.SetColumn(detailSlot, 1);
        hstack.Children.Add(sidebarSlot);
        hstack.Children.Add(detailSlot);

        // :53-63 the leading-edge strip, over the whole HStack, that pulls the hidden sidebar back out.
        leadingHandleConfig = new SidebarResizeHandle
        {
            OnBegin = () => AppEnvironment.GetAppModel(this).Sidebar.BeginDrag(),
            OnChange = delta => AppEnvironment.GetAppModel(this).Sidebar.Drag(delta),
            OnEnd = () => AppEnvironment.GetAppModel(this).Sidebar.EndDrag(),
        };
        leadingHandle = leadingHandleConfig.MakeNSView();
        leadingHandle.Width = SidebarResizeHandle.HitWidth; // :61
        leadingHandle.HorizontalAlignment = HorizontalAlignment.Left; // :54 overlay(alignment: .leading)
        leadingHandle.VerticalAlignment = VerticalAlignment.Stretch;
        leadingHandle.Margin = new Thickness(0, Chrome.TrafficLightTop + Chrome.TrafficLightDiameter, 0, 0); // :62

        var content = new Panel { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        content.Children.Add(hstack);
        content.Children.Add(leadingHandle);

        // :64-65 the backdrop and the window-chrome probe, both painted behind the content.
        backdrop = new WindowBackdrop(AppSettings.DefaultBackdropOpacity); // :64, placeholder until UpdateBackdrop's first real read below
        var behindContent = new Panel();
        behindContent.Children.Add(chromeProbe); // :65, no visual output (WindowChromeProbe.IsHitTestVisible false)
        behindContent.Children.Add(backdrop); // :64
        behindContent.Children.Add(content);

        // :66-68 clipped to the window's own rounded shape only after the backdrop is painted, so the glass and its
        // edge stop at the window's own curve (PR-WIN-062, "Clip the composited window to the same squircle... so
        // the DWM shadow follows the same curve" - WindowChrome.cs's own Configure/PlaceTrafficLights already own
        // the platform window's frame and region; this Border is the composited content's own clip, the Avalonia
        // half of that windows_path). .ignoresSafeArea() (:68) has nothing to do here: Windows reserves no OS-drawn
        // safe area over this fully custom-chromed window, the identical call WindowChrome.cs's own top-of-file
        // note already makes for the titlebar container it has no counterpart for.
        windowClip = new Border
        {
            Child = behindContent,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        }.RoundedRectangle(Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous);

        // :66 Clip the entire composition, including the backdrop. ED-181's shared curve deferral applies.
        windowClip.SizeChanged += (_, e) => windowClip.Clip = SwiftShapes.RoundedRectangleGeometry(
            new Rect(e.NewSize), Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous);

        // :69 .coordinateSpace(.named(GenieAnimator.coordinateSpace)): nothing to install here. Every consumer reaches
        // "the window" through Avalonia's own ancestor-relative TranslatePoint/TransformToVisual directly
        // (SidebarView.cs's ReportsFrame, :677-694, is the established convention for exactly this), which replaces a
        // named coordinate space outright; there is no separate per-control declaration step the way SwiftUI's
        // modifier needs one.
        // :70-76 the genie, row-glide and command-palette overlays sit OUTSIDE windowClip (added after it below), so
        // none of the three is clipped by the window's own rounded shape (PR-WIN-067).
        var genieLayer = new GenieLayer();
        var rowGlideLayer = new RowGlideLayer();

        outerRoot.Children.Add(windowClip);
        outerRoot.Children.Add(genieLayer);
        outerRoot.Children.Add(rowGlideLayer);
        outerRoot.Children.Add(paletteHost); // :72-76, CommandPalette added/removed by UpdatePalette below

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Child = outerRoot;

        // :77-79 .dropDestination(for: URL.self) { urls, _ in handleDrop(urls) }
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Every model-dependent knob below is deferred past the constructor (DetailView.cs's own identical pattern,
        // just above): AppEnvironment.SetAppModel(this, ...) runs only after WindowManager finishes building this
        // instance (HostedEnvironment.Body, App.Window.WindowManager.cs), well after `new RootView()` returns, so
        // nothing above may read the model, and nothing below may either until each Track's first callback fires.
        // Independent, sibling Track calls (WelcomeView.cs's own identical note): each fires once immediately at
        // attach (Port/ViewLifetime.cs's Tracked&lt;T&gt;) and again on its own change alone.
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.SidebarFloats, _ => Recompute()); // :12-13
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.SidebarOnlyFloats, _ => Recompute()); // :12
        this.Track(() => AppEnvironment.GetAppModel(this).Sidebar.IsVisible, _ => Recompute()); // :13
        this.Track(() => AppEnvironment.GetAppModel(this).Sidebar.Width, _ => Recompute()); // :18
        this.Track(() => AppEnvironment.GetAppModel(this).Sidebar.HoldsTrafficLights, _ => UpdateChrome()); // :65
        this.Track(() => AppEnvironment.GetAppModel(this).Sidebar.IsDragging, _ => UpdateChrome()); // :65
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.BackdropOpacity, _ => UpdateBackdrop()); // :64
        this.Track(() => WallpaperStore.Shared.Current.Image, _ => UpdateBackdrop()); // :64
        this.Track(() => AppEnvironment.GetAppModel(this).IsCommandPalettePresented, _ => UpdatePalette()); // :72-76
        this.Track(() => AppEnvironment.GetAppModel(this).Alert, _ => UpdateAlert()); // :80-91
    }

    // :9,12-13 the sidebar column's target/rendered width, the two resize handles' active states, and the detail
    // column's leading inset: everything the HStack itself recomputes fresh on every relevant model change.
    private void Recompute()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        var sidebar = model.Sidebar; // :9
        var onlyFloats = model.Settings.SidebarFloats && model.Settings.SidebarOnlyFloats; // :12
        var floats = model.Settings.SidebarFloats && (onlyFloats || !sidebar.IsVisible); // :13

        sidebarView.Width = sidebar.Width; // :18
        sidebarClip.Width = onlyFloats ? 0 : sidebar.RenderedWidth; // :19

        trailingHandleConfig.IsActive = !onlyFloats && sidebar.IsVisible; // :24
        trailingHandleConfig.UpdateNSView(trailingHandle);
        leadingHandleConfig.IsActive = !onlyFloats && !sidebar.IsVisible; // :56
        leadingHandleConfig.UpdateNSView(leadingHandle);

        var leadingInset = sidebar.IsVisible && !onlyFloats ? 0 : Chrome.SheetInset; // :41
        detailPadded.Padding = new Thickness(leadingInset, Chrome.SheetInset, Chrome.SheetInset, Chrome.SheetInset); // :41-43

        var animated = lastFloats is { } previous && previous != floats; // :51 .animation(Chrome.panelSlide, value: floats)
        lastFloats = floats;
        RebuildFloatingOverlay(animated);
    }

    // :45-50 the floating panel overlay: rebuilt fresh from the current inputs every time this or detailPadded's own
    // SizeChanged handler calls in, skipping the rebuild when nothing it reads actually changed. FloatingSidebarPanel
    // (Views/Sidebar/FloatingSidebarPanel.cs) carries no Update method of its own (R-VIEW-1's per-render
    // reconstruction is that unit's own design, not this one's to revisit): a resize that lands mid-drag on the
    // panel's own title or resize handle loses that drag's transient @State a real SwiftUI re-render would have
    // kept identity-preserved - ENGINEERING GAP, the same shape as NewThreadPrompt.cs's own noted
    // minimumScaleFactor gap, and unreachable outside that one simultaneous gesture.
    private void RebuildFloatingOverlay(bool animated)
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        var onlyFloats = model.Settings.SidebarFloats && model.Settings.SidebarOnlyFloats; // :12
        var floats = model.Settings.SidebarFloats && (onlyFloats || !model.Sidebar.IsVisible); // :13
        var shouldShow = floats && detailSize != default; // :46 detailSize != .zero

        if (shouldShow == (floatingPanel is not null) && onlyFloats == lastOnlyFloats && detailSize == lastFloatDetailSize) return;
        lastOnlyFloats = onlyFloats;
        lastFloatDetailSize = detailSize;

        void Apply()
        {
            if (floatingPanel is { } old) // :46 the previous instance leaves under the same transition, if any
            {
                SwiftTransition.Remove(detailSlot, old);
                floatingPanel = null;
            }
            if (shouldShow)
            {
                var panel = new FloatingSidebarPanel(detailSize, !onlyFloats, () => model.Sidebar.Toggle()) // :47
                    .Transition(SwiftTransition.Scale(0.94).Combined(SwiftTransition.Opacity)); // :48
                panel.HorizontalAlignment = HorizontalAlignment.Left;
                panel.VerticalAlignment = VerticalAlignment.Top; // :45 overlay(alignment: .topLeading)
                SwiftTransition.Insert(detailSlot, detailSlot.Children.Count, panel);
                floatingPanel = panel;
            }
        }

        if (animated) Transaction.WithAnimation(Chrome.PanelSlide, Apply); else Apply(); // :51
    }

    // :64 the backdrop updates in place (WindowBackdrop.Update's own doc comment: "The parent passing new inputs...
    // built again from RootView.swift:64").
    private void UpdateBackdrop()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }
        backdrop.Update(model.Settings.BackdropOpacity, WallpaperStore.Shared.Current.Image);
    }

    // :65 the window-chrome probe updates in place the same way (WindowChromeConfigurator.Update's identical shape).
    private void UpdateChrome()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }
        chromeConfig.SidebarVisible = model.Sidebar.HoldsTrafficLights; // :65 sidebarVisible: sidebar.holdsTrafficLights
        chromeConfig.SidebarDragging = model.Sidebar.IsDragging; // :65 sidebarDragging: sidebar.isDragging
        chromeConfig.Update(chromeProbe);
    }

    // :72-76 the command palette: built only while shown, torn down entirely when not (PR-WIN-067's "holds no state
    // between openings").
    private void UpdatePalette()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        if (model.IsCommandPalettePresented) // :73
        {
            if (palette is not null) return;
            palette = new CommandPalette(); // :74
            paletteHost.Children.Add(palette);
        }
        else if (palette is { } shown)
        {
            paletteHost.Children.Remove(shown);
            palette = null;
        }
    }

    // :80-91 The model alert belongs to the rendering window. Clearing the presentation binding closes it;
    // closing it clears the model. The production shared presenter's ordinary Window.Show has no owner or
    // reverse binding, so this source site owns its dialog while retaining the existing recording override.
    private void UpdateAlert()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }
        if (model.Alert is not { } alert) { alertDialog?.Close(); return; } // :83-84 false presentation binding
        if (alertShowing) return;
        alertShowing = true;
        _ = ShowAlert(model, alert);
    }

    private async Task ShowAlert(AppModel model, AppAlert alert)
    {
        try
        {
            var presenter = AlertPresentation.Presenter.Current;
            if (presenter is WindowsAlertPresenter)
            {
                var owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window
                    ?? throw new InvalidOperationException("A model alert requires its rendering window.");
                var dialog = BuildAlertDialog(alert);
                alertDialog = dialog;
                await PresentAlertDialog.Current(dialog, owner); // :80-91 owned, window-modal alert
            }
            else
            {
                await presenter.Show(alert.Title, alert.Message, AlertStyle.Warning,
                    [new AlertButton("OK", () => { })]); // existing R-VIEW-9 recorder, :81, :88, :90
            }
        }
        finally
        {
            alertDialog = null;
            alertShowing = false;
            model.Alert = null; // :83-84 dismissal writes the false presentation binding
        }
    }

    // :80-91 The plain Windows alert form already used by Port/AlertPresenter, with the source's one cancel button.
    // Keep the closeable window in this RootView so model.alert controls presentation in both directions.
    private static Avalonia.Controls.Window BuildAlertDialog(AppAlert alert)
    {
        var dialog = new Avalonia.Controls.Window
        {
            Title = alert.Title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var button = new Button { Content = "OK", IsDefault = true, IsCancel = true }; // :88; Enter/Escape
        button.Click += (_, _) => dialog.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(button);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children = { new TextBlock { Text = alert.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 }, buttons },
        };
        return dialog;
    }

    // PORT-ONLY: Avalonia's drag-and-drop pipeline only fires Drop when a DragOver handler has already accepted the
    // drag (Composer/ComposerTextView.cs's identical OnDragOverEvent note; there is no NSDraggingInfo counterpart
    // needing this on macOS).
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy; // :77-79 dropDestination(for: URL.self) accepts a URL drag unconditionally
        e.Handled = true;
    }

    // :77-79 .dropDestination(for: URL.self) { urls, _ in handleDrop(urls) }: every dropped item that has a file (or
    // folder) representation is turned into the same non-"file"-scheme SwiftURL DroppedFileUrl below builds, and
    // HandleDrop (below) does the rest - already implemented and tested through HandleDropCore (PR-WIN-064, -065).
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var urls = new List<SwiftURL>();
        foreach (var item in e.DataTransfer.Items)
            if (item.TryGetFile() is { } file && file.TryGetLocalPath() is { } path)
                urls.Add(DroppedFileUrl(path));
        if (urls.Count == 0) return;
        e.Handled = HandleDrop(urls);
    }

    // Builds the same non-"file"-scheme SwiftURL RootViewTests.cs's own DroppedUrl helper builds for HandleDropCore's
    // tests (both exist so a Windows drive-letter path round-trips through SwiftURL.Path; see the comment above
    // HandleDropCore below for why a "file://" URL corrupts one instead).
    private static SwiftURL DroppedFileUrl(string path)
    {
        var normalized = path.Replace('\\', '/');
        var builder = new System.Text.StringBuilder();
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return SwiftURL.Create("marrow-dropped-path:" + builder) ?? throw new InvalidOperationException("bad dropped path");
    }

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
