using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
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
// RootView, both detail branches and the independently hosted, cached chat columns are implemented.
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
// this. The selected-thread branch now hosts the real ChatView in ThreadColumns (:130-154,177-297).

/// <summary>:4-111 <c>struct RootView: View</c>, with the file's <c>extension EnvironmentValues</c> (:116-118).</summary>
public sealed class RootView : Decorator
{
    private readonly SidebarView sidebarView = new();
    private readonly Border sidebarClip;
    private readonly SidebarResizeHandle sidebarHandleConfig;
    private readonly Splitter sidebarSplitter;
    private readonly DetailView detailView = new();

    /// <summary>WV-1: the Welcome voice question the detail pane draws on the empty page (see <see cref="DetailView.VoiceQuestion"/>).</summary>
    internal Marrow.App.Port.Voice.VoiceSpeedQuestion? VoiceQuestion
    {
        get => detailView.VoiceQuestion;
        set => detailView.VoiceQuestion = value;
    }
    private readonly Border detailClip;
    private readonly SaturationLayer detailSaturation;
    private readonly Border detailPadded;
    private readonly Panel detailSlot = new OverlayPanel();
    private readonly WindowBackdrop backdrop;
    private readonly WindowChromeProbe chromeProbe = new();
    private readonly WindowChromeConfigurator chromeConfig = new();
    private readonly SwiftShapeBorder windowClip;
    private readonly Panel outerRoot = new();
    private readonly Panel paletteHost = new();
    private CommandPalette? palette;
    private FloatingSidebarPanel? floatingPanel;
    private Size detailSize;
    private bool? lastFloats;
    private bool lastOnlyFloats;
    private Size lastFloatDetailSize = new(-1, -1);
    private bool alertShowing;
    private Avalonia.Controls.Window? alertDialog;
    private TextBlock? alertTitle;
    private TextBlock? alertMessage;
    private AppAlert? displayedAlert;

    // :80-91 R-VIEW-9: record the owned modal request in headless tests, keeping the real dialog's content.
    internal static Ambient<Func<Avalonia.Controls.Window, Avalonia.Controls.Window, System.Threading.Tasks.Task>> PresentAlertDialog { get; } =
        new(() => (dialog, owner) => dialog.ShowDialog(owner));

    /// <summary>Test-only: the window's own rounded clip (PR-WIN-062, :66-68).</summary>
    internal SwiftShapeBorder WindowClip => windowClip;

    /// <summary>Test-only: the detail column's own clip, before its (inert) padding (PR-WIN-063, :40).</summary>
    internal Border DetailClip => detailClip;

    /// <summary>GR-2: the Getting ready sheet hanging over the detail pane, null when there is none (OD-0096).</summary>
    internal Marrow.App.Port.FirstRun.GettingReadySheet? GettingReady { get; private set; }

    /// <summary>Test-only: the detail column the sheet blurs and dims (:41-43).</summary>
    internal Border DetailPane => detailPadded;

    /// <summary>
    /// GR-2 (OD-0096, look I "Sheet"): hangs the Getting ready sheet over the detail pane, centred on it and from the window's top edge,
    /// with the pane behind blurred, dimmed and not pressable. The sheet brings the pane back as it lifts (<see cref="GettingReadySheet.HandChanged"/>)
    /// and is taken out of the tree when it is gone. Called when the window's content is made (the launch's reveal of the window is the
    /// sheet's entrance), never later.
    /// ENGINEERING DECISION (ED-306): the pane is blurred with Gaussian sigma 9 returning to 0 and dimmed to 0.45 returning to 1
    /// (the page's filter: blur(9px) saturate(.6) and opacity .45). A live Skia layer supplies the saturation. Below a
    /// sigma of 0.5 the effect is taken off, which is the blur Skia gives a radius of zero.
    /// </summary>
    internal void ShowGettingReady(Marrow.App.Port.FirstRun.GettingReadySheet sheet)
    {
        if (GettingReady is not null) throw new InvalidOperationException("RootView already shows a Getting ready sheet.");
        GettingReady = sheet;
        sheet.HorizontalAlignment = HorizontalAlignment.Center;
        sheet.VerticalAlignment = VerticalAlignment.Top;
        sheet.ZIndex = 1;
        var blur = CometPainter.GaussianBlur(Marrow.App.Port.FirstRun.GettingReadyLook.BlurSigma);
        void Apply(double hand)
        {
            var sigma = Marrow.App.Port.FirstRun.GettingReadyLook.PaneBlurSigma(hand);
            if (sigma >= 0.5)
            {
                blur.Radius = CometPainter.GaussianBlur(sigma).Radius;
                detailPadded.Effect = blur;
            }
            else detailPadded.Effect = null;
            detailPadded.Opacity = Marrow.App.Port.FirstRun.GettingReadyLook.PaneOpacity(hand);
            detailSaturation.Amount = .6 + .4 * Marrow.App.Port.FirstRun.GettingReadyLook.Hi(hand);
        }
        sheet.HandChanged += Apply;
        sheet.Lifted += () =>
        {
            sheet.HandChanged -= Apply;
            detailPadded.Effect = null;
            detailPadded.Opacity = 1;
            detailSaturation.Amount = 1;
            detailPadded.IsHitTestVisible = true;
            detailSlot.Children.Remove(sheet);
            GettingReady = null;
        };
        detailPadded.IsHitTestVisible = false;
        Apply(sheet.Hand);
        detailSlot.Children.Add(sheet);
    }

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

        var sidebarSlot = new OverlayPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        sidebarSlot.Children.Add(sidebarClip);

        // :33-51 the detail column: stretches, clips its own bounds first (:40), then insets by the sheet inset on
        // every side but the leading one (:41-43; inert at Chrome.SheetInset == 0, PR-WIN-063).
        detailView.HorizontalAlignment = HorizontalAlignment.Stretch;
        detailView.VerticalAlignment = VerticalAlignment.Stretch;
        detailSaturation = new SaturationLayer(detailView);
        detailClip = new Border
        {
            Child = detailSaturation,
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

        // :22-31 and :53-63 the two resize strips, the visible column's trailing edge and the leading edge that pulls the hidden
        // column back out, are one shared splitter over the whole HStack (nth web surfaces W1, DESIGN.md 4.3, ED-NEW-web-w1-3):
        // centred on the column's edge while it shows, at the window's leading edge while it is hidden (Recompute places it),
        // starting under the traffic lights (:30, :62).
        sidebarHandleConfig = new SidebarResizeHandle(() =>
        {
            try { return AppEnvironment.GetAppModel(this).Sidebar; }
            catch (InvalidOperationException) { return null; }
        });
        sidebarSplitter = sidebarHandleConfig.MakeSplitter();
        sidebarSplitter.HorizontalAlignment = HorizontalAlignment.Left;
        sidebarSplitter.VerticalAlignment = VerticalAlignment.Stretch;
        sidebarSplitter.Margin = new Thickness(0, Chrome.TrafficLightTop + Chrome.TrafficLightDiameter, 0, 0); // :30, :62

        var content = new Panel { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        content.Children.Add(hstack);
        content.Children.Add(sidebarSplitter);

        // :64-65 the backdrop and the window-chrome probe, both painted behind the content.
        backdrop = new WindowBackdrop(AppSettings.DefaultBackdropOpacity); // :64, placeholder until UpdateBackdrop's first real read below
        var behindContent = new Panel();
        behindContent.Children.Add(chromeProbe); // :65, no visual output (WindowChromeProbe.IsHitTestVisible false)
        behindContent.Children.Add(backdrop); // :64
        behindContent.Children.Add(content);

        // :66-68 clipped to the window's own shape only after the backdrop is painted (PR-WIN-062, "Clip the composited window to the same squircle... so
        // the DWM shadow follows the same curve" - the native region/DWM preference and four-corner capture remain STOP #4 in RootViewTests.cs; this
        // Border is only the composited-content part of that contract). .ignoresSafeArea() (:68) has nothing to do here: Windows reserves no OS-drawn
        // safe area over this fully custom-chromed window, the identical call WindowChrome.cs's own top-of-file
        // note already makes for the titlebar container it has no counterpart for.
        // :66 Clip the entire composition, including the backdrop: `.clipShape(RoundedRectangle(cornerRadius: 26, style: .continuous))`.
        // PORT-ONLY (OD-0075; the owner's intent of 2026-10-05; ED-338): the clip is the window's RECTANGLE, not the 26 pt shape. Windows rounds a
        // window's corner by 8 px at most (DWM, ED-236 UNRESOLVED (a)), so a 26 pt clip left a wedge of the system's Acrylic with nothing on it, or, filled
        // with a second paint of the scrim (the first version of OD-0075), a corner that was not the colour of the glass beside it: the body also wears the
        // legibility tint and the detail sheet's wash. With the rectangle every layer of the body (the backdrop's scrim and tint, the sheet) reaches the
        // window's edge and DWM rounds it: the corner IS the body. The backdrop's hairline is the rectangle's inner stroke too (WindowBackdrop).
        windowClip = new SwiftShapeBorder
        {
            Child = behindContent,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

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
        this.Track(() => Marrow.UI.Theme.Wallpapers.WallpaperLibraryStore.Shared.Current.Selection, value => backdrop.UpdateLibrary(value));
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

        // :24 the trailing strip is active while the column shows, :56 the leading one while it is hidden: one splitter, active
        // unless the sidebar only floats, centred on the column's edge or at the leading edge.
        sidebarHandleConfig.IsActive = !onlyFloats;
        var edge = !onlyFloats && sidebar.IsVisible ? sidebar.RenderedWidth : 0;
        sidebarSplitter.Margin = new Thickness(
            edge > 0 ? edge - Splitter.DividerThickness / 2 : 0, Chrome.TrafficLightTop + Chrome.TrafficLightDiameter, 0, 0);
        sidebarHandleConfig.Update(sidebarSplitter);

        var leadingInset = sidebar.IsVisible && !onlyFloats ? 0 : Chrome.SheetInset; // :41
        detailPadded.Padding = new Thickness(leadingInset, Chrome.SheetInset, Chrome.SheetInset, Chrome.SheetInset); // :41-43

        var animated = lastFloats is { } previous && previous != floats; // :51 .animation(Chrome.panelSlide, value: floats)
        lastFloats = floats;
        RebuildFloatingOverlay(animated);
    }

    // :44-51 A size update changes only the panel area; its SidebarView and transient state keep identity.
    private void RebuildFloatingOverlay(bool animated)
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        var onlyFloats = model.Settings.SidebarFloats && model.Settings.SidebarOnlyFloats; // :12
        var floats = model.Settings.SidebarFloats && (onlyFloats || !model.Sidebar.IsVisible); // :13
        var shouldShow = floats && detailSize != default; // :46 detailSize != .zero

        if (shouldShow == (floatingPanel is not null) && onlyFloats == lastOnlyFloats)
        {
            if (detailSize != lastFloatDetailSize) floatingPanel?.UpdateArea(detailSize);
            lastFloatDetailSize = detailSize;
            return;
        }
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

    // :80-91 The shared ED-216 request/window/session owns the modal UI. This source's binding additionally
    // updates an already presented alert and closes it when model.alert becomes nil.
    private void UpdateAlert()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }
        if (model.Alert is not { } alert) { alertDialog?.Close(); return; } // :83-84 false presentation binding
        if (alertShowing)
        {
            // :81,90 A replacement non-nil alert updates the presentation that is already shown.
            if (alertDialog is { IsVisible: true } shown)
            {
                shown.Title = alert.Title;
                alertTitle!.Text = alert.Title; // :81; ED-216 window title and bold first line stay in sync
                alertMessage!.Text = alert.Message;
                displayedAlert = alert;
            }
            return;
        }
        displayedAlert = alert;
        alertShowing = true;
        _ = ShowAlert(model, alert);
    }

    private async Task ShowAlert(AppModel model, AppAlert alert)
    {
        try
        {
            var presenter = AlertPresentation.Presenter.Current;
            var owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window
                ?? throw new InvalidOperationException("A model alert requires its rendering window.");
            var request = new AlertRequest(alert.Title, alert.Message,
                [new AlertButton("OK", () => { }) { Role = AlertButtonRole.Cancel }], Owner: owner); // :81,88,90
            if (presenter is WindowsAlertPresenter)
            {
                Task showing;
                // Keep the source's mutable presentation binding and the existing owned-modal test seam,
                // using ED-216's shared request preparation, real window builder and action/close session.
                using (AlertPresentation.Presenter.Override(new ModelAlertPresenter(this)))
                    showing = AlertPresentation.Show(request);
                await showing;
            }
            else
            {
                await AlertPresentation.Show(request); // extended seam; old recorder overloads still bridge
            }
        }
        finally
        {
            var dismissed = displayedAlert;
            alertDialog = null;
            alertTitle = null;
            alertMessage = null;
            displayedAlert = null;
            alertShowing = false;
            if (ReferenceEquals(model.Alert, dismissed))
                model.Alert = null; // :83-84 dismissal clears only the alert actually displayed
            else if (model.Alert is not null)
                UpdateAlert(); // a newer alert arriving after close must not be cleared unseen
        }
    }

    // :80-91 PORT-ONLY binding adapter: no private dialog styling or keyboard/action implementation.
    // The shared ED-216 builder supplies those; RootView keeps the window only for the source's live binding.
    private sealed class ModelAlertPresenter(RootView root) : IAlertPresenter
    {
        public Task Show(string title, string informativeText, AlertStyle style, IReadOnlyList<AlertButton> buttons) =>
            Show(new AlertRequest(title, informativeText, buttons));

        public Task Show(AlertRequest request)
        {
            Avalonia.Controls.Window? dialog = null;
            var session = new AlertSession(request, () => dialog?.Close());
            session.BindLifetime(SwiftTask.CancellationToken);
            dialog = WindowsAlertPresenter.BuildWindow(request, session);
            root.alertDialog = dialog;
            var content = (StackPanel)dialog.Content!;
            root.alertTitle = (TextBlock)content.Children[0];
            root.alertMessage = (TextBlock)content.Children[1];
            _ = Present(dialog, request.Owner!, session);
            return session.Completion;
        }

        private static async Task Present(Avalonia.Controls.Window dialog, Avalonia.Controls.Window owner, AlertSession session)
        {
            try { await PresentAlertDialog.Current(dialog, owner); session.Closed(); }
            catch (Exception error) { session.Failed(error); }
        }
    }

    // :22-31,44-51 SwiftUI overlays never contribute to the underlying view's measured size.
    private sealed class OverlayPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children.ToArray()) child.Measure(availableSize);
            return Children.Count == 0 ? default : Children[0].DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // Arranging the base reports its area and may insert/remove the floating overlay.
            foreach (var child in Children.ToArray()) child.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }

    // PORT-ONLY: Avalonia's drag-and-drop pipeline only fires Drop when a DragOver handler has already accepted the
    // drag (Composer/ComposerTextView.cs's identical OnDragOverEvent note; there is no NSDraggingInfo counterpart
    // needing this on macOS).
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Items.Any(item => item.TryGetFile()?.TryGetLocalPath() is not null)
            ? DragDropEffects.Copy : DragDropEffects.None; // :77-79 only a file URL can reach HandleDrop
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
        if (urls.Count == 0)
        {
            // Nothing the drag offered can be opened any more (a path removed after DragOver accepted it): refused, and said so
            // to the drag source (see below).
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var accepted = HandleDrop(urls);
        // :79,109 return handleDrop's acceptance to the drag source. A local path can disappear after
        // DragOver accepted Copy; leaving that effect in place would report a successful rejected drop.
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        // The drop is consumed either way (dropDestination takes it and answers false); only a handled event carries its effect to
        // the drag source through OLE, which otherwise reports the offered effects (measured natively: 7) for a refused drop too
        // (ED-332 (2)).
        e.Handled = true;
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
    // control, so it is directly testable independently of RootView's own visual tree.
    // ENGINEERING DECISION: this split is pure testability scaffolding, not an observable behaviour change; the
    // Swift function's own body is reproduced verbatim in HandleDropCore, cited line for line below.
    private bool HandleDrop(IReadOnlyList<SwiftURL> urls) =>
        HandleDropCore(AppEnvironment.GetAppModel(this), urls);

    // :94-110 the whole handleDrop body. internal (not private, R-NAME-6's testability exception noted above): tests
    // reach it through Marrow.App's existing [assembly: InternalsVisibleTo("Marrow.App.Tests")]
    // (Port/AttachmentFlightPlatform.cs:10), the same visibility every other testable internal member already uses.
    // The real drop handler uses DroppedFileUrl above: do not build
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
        // selectedThreadID = thread.id), as PR-WIN-065 now reads. newThread(in:) selects the thread it creates BEFORE this line runs, so a mixed drop of a
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
    private readonly List<Guid> prebuilt = [];
    private bool hasPrebuiltTopRows;
    private Guid? shown;
    private ThreadColumns? columns;
    private AppModel? columnModel;
    private ObservationTracker? runningObservation;
    // PORT-ONLY: web surfaces W3a (DESIGN 3.11; ED-NEW-web-w3a-1). The thread's pane workspace around the chat columns,
    // bound to the shown thread's runtime: its state is drawn, and what the user changes is written back.
    private Marrow.App.Views.Panes.PaneWorkspace? workspace;
    private ThreadRuntime? workspaceRuntime;
    private ObservationTracker? workspaceObservation;

    /// <summary>The pane workspace around the chat columns while a thread is shown.</summary>
    internal Marrow.App.Views.Panes.PaneWorkspace? Workspace => workspace;

    /// <summary>What each pane kind draws (the pane units register theirs); tests replace it.</summary>
    internal Marrow.App.Views.Panes.PaneContentFactory PaneFactory { get; set; } = Marrow.App.Views.Panes.PaneContents.Create;
    private Marrow.App.Port.Voice.VoiceSpeedQuestion? voiceQuestion;

    /// <summary>
    /// WV-1 (OD-0098 welcome-voice pick): the Welcome voice question the empty page draws (a "Voice typing" section above Activity). When the page gives way to a thread or the no-thread page the question is told
    /// Welcome has gone, so an open slow-PC question is asked at the next orb click (the orb's wiring is VT-12's).
    /// </summary>
    internal Marrow.App.Port.Voice.VoiceSpeedQuestion? VoiceQuestion
    {
        get => voiceQuestion;
        set
        {
            if (ReferenceEquals(voiceQuestion, value)) return;
            voiceQuestion = value;
            Rebuild();
        }
    }

    // :147 combined-session input idle. Windows' session-wide last-input tick is the Quartz counterpart;
    // the ambient seam lets headless tests exercise the one-second gate without the user's real pointer.
    internal static Ambient<Func<double>> SecondsSinceLastInput { get; } = new(InputIdleSeconds);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Tick;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static double InputIdleSeconds()
    {
        if (!OperatingSystem.IsWindows()) return double.PositiveInfinity;
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Tick) / 1000.0 : 0;
    }

    // :129-166 body. Each Track call is an independent, sibling top-level statement in the constructor (not nested
    // inside another Track's apply callback): a tracker registered from inside AttachedToVisualTree's own dispatch
    // does not fire for that same attachment (standard .NET event-handler-added-during-dispatch semantics).
    public DetailView()
    {
        this.Track(() => AppEnvironment.GetAppModel(this), _ => Rebuild());
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            // :130-133 thread existence and every existing runtime's running state are body inputs.
            return (model.SelectedThreadID, model.Threads, model.Projects.Count);
        }, _ => Rebuild());
        this.Task(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            return model.SelectedThreadID is { } id && model.Thread(id) is not null ? (Guid?)id : null;
        }, Prebuild); // :134 .task(id: threadID), cancelled on selection change or detach.
        DetachedFromVisualTree += (_, _) =>
        {
            runningObservation?.Cancel();
            runningObservation = null;
            workspaceObservation?.Cancel();
            workspaceObservation = null;
        };
    }

    // PORT-ONLY: web surfaces W3a. Shows the thread's workspace: a new thread's panes are drawn at once, with that
    // thread's contents; a change of its state afterwards springs.
    private void BindWorkspace(ThreadRuntime runtime)
    {
        if (workspace is null) return;
        var factory = PaneFactory;
        if (!ReferenceEquals(workspaceRuntime, runtime))
        {
            workspaceRuntime = runtime;
            workspace.Reload(kind => factory(kind, runtime), runtime.Workspace);
        }
        else workspace.SetState(runtime.Workspace);
        workspace.IsChatWorking = runtime.IsRunning;
        ObserveWorkspace(runtime);
    }

    private void ObserveWorkspace(ThreadRuntime runtime)
    {
        workspaceObservation?.Cancel();
        workspaceObservation = ObservationTracking.Track(() => (runtime.Workspace, runtime.IsRunning), () => MainActor.Post(() =>
        {
            if (!ReferenceEquals(workspaceRuntime, runtime) || workspace is null || TopLevel.GetTopLevel(this) is null) return;
            workspace.SetState(runtime.Workspace);
            workspace.IsChatWorking = runtime.IsRunning;
            ObserveWorkspace(runtime);
        })).Tracker;
    }

    // :131-133 evaluate the running input again after columns create their runtimes. A state-driven Swift body
    // reads those new runtimes on its next evaluation; an observation captured only on attach misses them.
    private void ObserveRunning(AppModel model)
    {
        runningObservation?.Cancel();
        runningObservation = ObservationTracking.Track(() =>
        {
            foreach (var thread in model.Threads) _ = model.ExistingRuntime(thread.Id)?.IsRunning;
            return true;
        }, () => MainActor.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) is not null) Rebuild();
        })).Tracker;
    }

    // :134-153 neighbours, then the first six sidebar rows once per DetailView lifetime.
    private async System.Threading.Tasks.Task Prebuild(Guid? threadID)
    {
        if (threadID is not { } selected) return;
        await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(1500));
        if (SwiftTask.IsCancelled) return;
        var model = AppEnvironment.GetAppModel(this);
        var candidates = model.Neighbors(selected).ToList();
        if (!hasPrebuiltTopRows)
        {
            hasPrebuiltTopRows = true;
            candidates.AddRange(model.SidebarThreads.Take((int)ThreadColumns.TopRowsBuiltAtLaunch).Select(thread => thread.Id));
        }
        foreach (var id in candidates)
        {
            if (SwiftTask.IsCancelled) break;
            while (!SwiftTask.IsCancelled && SecondsSinceLastInput.Current() < 1)
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(300));
            if (SwiftTask.IsCancelled || model.ExistingRuntime(selected)?.IsRunning == true) return;
            if (id != selected && !prebuilt.Contains(id))
            {
                prebuilt.Add(id);
                Rebuild();
            }
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(400));
        }
    }

    private void Rebuild()
    {
        AppModel model;
        try { model = AppEnvironment.GetAppModel(this); }
        catch (InvalidOperationException) { return; }

        voiceQuestion ??= Marrow.App.Port.Voice.VoiceFirstRun.Find(model.Settings)?.Question; // WV-1: the same question every composer reads

        // :130 `if let threadID = model.selectedThreadID, model.thread(threadID) != nil`
        if (model.SelectedThreadID is { } threadID && model.Thread(threadID) is not null)
        {
            voiceQuestion?.CloseWelcome();
            if (shown != threadID) prebuilt.Clear(); // :133 onChange(threadID)
            shown = threadID;
            var running = model.Threads.Where(thread => model.ExistingRuntime(thread.Id)?.IsRunning == true).Select(thread => thread.Id).ToHashSet();
            var liveResize = AppEnvironment.GetWindowLiveResize(this);
            if (columns is null || !ReferenceEquals(columnModel, model))
            {
                columnModel = model;
                columns = new ThreadColumns(threadID, prebuilt, running, model, liveResize);
                workspace = new Marrow.App.Views.Panes.PaneWorkspace(columns, _ => null);
                var settings = model.Settings;
                workspace.DrawerHeight = () => settings.TerminalHeight; // web W4: where a terminal dragged back to the drawer is outlined
                workspace.StateChanged += state =>
                {
                    if (workspaceRuntime is { } owner) owner.Workspace = state;
                };
                workspaceRuntime = null;
                Child = workspace;
            }
            else columns.Update(threadID, prebuilt, running, model, liveResize);
            BindWorkspace(model.Runtime(threadID));
            ObserveRunning(model);
            return;
        }

        shown = null;
        columns = null;
        columnModel = null;
        workspace = null;
        workspaceRuntime = null;
        workspaceObservation?.Cancel();
        workspaceObservation = null;
        prebuilt.Clear();
        runningObservation?.Cancel();
        runningObservation = null;

        // :156-164 the empty states, frame(maxWidth:.infinity,maxHeight:.infinity).detailSheet()
        if (model.Projects.Count != 0) voiceQuestion?.CloseWelcome();
        Control content = model.Projects.Count == 0 ? new WelcomeView(voiceQuestion) : new NoThreadView();
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
    private readonly Panel pane = new();
    private readonly Coordinator coordinator = new();

    public ThreadColumns(Guid threadID, IReadOnlyList<Guid> prebuilt, IReadOnlySet<Guid> running, AppModel model, WindowLiveResize liveResize)
    {
        Child = pane; // :189-190 empty pane; each independently hosted column fills it.
        Update(threadID, prebuilt, running, model, liveResize);
    }

    // :193-198 updateNSView. The coordinator survives input changes, just as NSViewRepresentable's does.
    public void Update(Guid threadID, IReadOnlyList<Guid> prebuilt, IReadOnlySet<Guid> running, AppModel model, WindowLiveResize liveResize)
    {
        coordinator.Show(threadID, pane, model, liveResize);
        coordinator.DropHidden(id => running.Contains(id) || model.Thread(id) is null || model.ExistingRuntime(id) is null);
        coordinator.Prebuild(prebuilt.Where(id => model.Thread(id) is not null && !running.Contains(id)).ToArray(), pane, model, liveResize);
    }

    /// <summary>:204-296 <c>@MainActor final class Coordinator</c>.</summary>
    public sealed class Coordinator
    {
        // :206 private static let keptLimit = 8
        private const long KeptLimit = 8;
        private readonly Dictionary<Guid, FrameHostingView> columns = [];
        private readonly List<Guid> order = [];
        private Guid? shown;
        private HashSet<Guid> requested = [];
        private readonly List<FrameHostingView> retired = [];
        private TaskHandle? retirement;

        // :224-243 func show(_:in:model:liveResize:)
        public void Show(Guid id, Control @in, AppModel model, WindowLiveResize liveResize)
        {
            if (id == shown) return;
            if (shown is { } previousID && columns.TryGetValue(previousID, out var previous))
            {
                previous.Width = previous.Bounds.Width;
                previous.Height = previous.Bounds.Height; // :207-209 freeze hidden layout at the size built.
                RootView.SetChatColumnIsShown(previous, false);
                previous.IsVisible = false;
            }
            if (columns.TryGetValue(id, out var kept))
            {
                RootView.SetChatColumnIsShown(kept, true);
                kept.Width = double.NaN;
                kept.Height = double.NaN;
                kept.IsVisible = true;
                kept.Measure(@in.Bounds.Size);
                kept.Arrange(new Rect(@in.Bounds.Size));
            }
            else Make(id, true, @in, model, liveResize);
            shown = id;
            order.Remove(id);
            order.Add(id);
            while (order.Count > KeptLimit + 1) Drop(order[0]);
        }

        // :247-249 func dropHidden(where:)
        public void DropHidden(Func<Guid, bool> where)
        {
            foreach (var id in columns.Keys.ToArray())
                if (id != shown && where(id)) Drop(id);
        }

        // :254-263 func prebuild(_:in:model:liveResize:)
        public void Prebuild(IReadOnlyList<Guid> ids, Control @in, AppModel model, WindowLiveResize liveResize)
        {
            foreach (var id in ids)
            {
                if (requested.Contains(id) || columns.ContainsKey(id)) continue;
                Make(id, false, @in, model, liveResize);
                // :255-257 SwitchLatency's development-only instrumentation is a no-op under ED-005.
                order.Insert(0, id);
                while (order.Count > KeptLimit + 1) Drop(order[0]);
            }
            requested = ids.ToHashSet();
        }

        // :266-283 make/root. Keep the ChatView tree when its visibility input changes, preserving its @State.
        private FrameHostingView Make(Guid id, bool isShown, Control pane, AppModel model, WindowLiveResize liveResize)
        {
            var column = new FrameHostingView(new HostedEnvironment(model, liveResize, Zooms: false).Body(new ChatView(model.Runtime(id)))); // the window root zooms it (HostedEnvironment)
            RootView.SetChatColumnIsShown(column, isShown);
            ((Panel)pane).Children.Add(column);
            column.Measure(pane.Bounds.Size);
            column.Arrange(new Rect(pane.Bounds.Size));
            if (!isShown)
            {
                column.Width = pane.Bounds.Width;
                column.Height = pane.Bounds.Height;
                column.IsVisible = false;
            }
            columns[id] = column;
            return column;
        }

        // :284-294 release tree teardown after the switch frame, coalescing further retirements.
        private void Drop(Guid id)
        {
            order.Remove(id);
            if (!columns.Remove(id, out var column)) return;
            retired.Add(column);
            retirement?.Cancel();
            var weak = new WeakReference<Coordinator>(this);
            retirement = MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(500));
                if (SwiftTask.IsCancelled || !weak.TryGetTarget(out var owner)) return;
                foreach (var view in owner.retired)
                    if (view.Parent is Panel parent) parent.Children.Remove(view);
                owner.retired.Clear();
            });
        }
    }
}

/// <summary>:299-350 <c>struct WelcomeView: View</c>: the first-run screen.</summary>
public sealed class WelcomeView : Decorator
{
    // :302-349 body. Two independent, sibling Track calls (not one nested inside the other's apply callback, per
    // the Track-nesting bug this port keeps re-finding): the whole page rebuilds on either the model attaching or
    // the activity style changing, since :331 `ChromeRow(detail: settings.activityThreadStyle.detail)` reads a
    // plain string computed once, not a reactive binding.
    private StackPanel? column;
    private ActivityThreadStyle? builtStyle;
    private readonly Marrow.App.Port.Voice.VoiceSpeedQuestion? voice;
    private int activityIndex = 2;

    /// <param name="voice">WV-1 (OD-0098): the voice question; with it a "Voice typing" section stands above Activity (variant A), without it the page is the source's.</param>
    public WelcomeView(Marrow.App.Port.Voice.VoiceSpeedQuestion? voice = null)
    {
        this.voice = voice;
        this.Track(() => AppEnvironment.GetAppModel(this), Build);
        // :331 ChromeRow(detail: settings.activityThreadStyle.detail): only that section is evaluated again; the ScrollView
        // (its offset, its focus) keeps its identity as SwiftUI keeps it.
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.ActivityThreadStyle, style =>
        {
            if (column is null || builtStyle == style) return;
            builtStyle = style;
            column.Children[activityIndex] = MakeActivitySection(AppEnvironment.GetAppModel(this));
        });
    }

    // :329-338 "Activity list" section, capped at 480
    private static ChromeSection MakeActivitySection(AppModel model)
    {
        var settings = model.Settings;
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
        return activitySection;
    }

    private void Build(AppModel model)
    {
        var settings = model.Settings;

        // :306-315 the icon, product name and tagline, spacing 14. DEVIATION (OD-0002; OD-0012 F2): "Marrow" replaces
        // the literal "Droppy Code"; the tagline reads "A calm, native home for your coding agents."
        var icon = AppIconImage.Make(104); // :307-309 NSApp.applicationIconImage; OD-0015: the packaged Marrow app icon, as the About pill draws it

        var title = new TextBlock { Text = "nth" }; // :310-311, OD-0002
        SwiftFont.System(34, SwiftFont.Weight.Semibold).Apply(title);

        var tagline = new TextBlock { Text = "A calm, native home for your coding agents." }; // :312-313, OD-0012 F2
        SwiftFont.System(SwiftFont.TextStyle.Title3).Apply(tagline);
        tagline.Foreground = Chrome.SecondaryText.Brush(tagline);

        // :306 a VStack centres each child at its own width; an Avalonia StackPanel stretches them, which left the name flush
        // left under the tagline.
        title.HorizontalAlignment = HorizontalAlignment.Center;
        tagline.HorizontalAlignment = HorizontalAlignment.Center;
        var identity = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
        identity.Children.Add(icon);
        identity.Children.Add(title);
        identity.Children.Add(tagline);

        // :317-327 "Add a project" (glassProminent, extraLarge) over the drop hint, spacing 10
        // :321 `.padding(.horizontal, 6)` on the label, inside the button style's own padding (DEFECT, UI-WIN-059: it was missing).
        var addProjectLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        addProjectLabel.Children.Add(SwiftImage.SystemName("folder.badge.plus").Font(SwiftFont.System(SwiftFont.TextStyle.Body)));
        addProjectLabel.Children.Add(new TextBlock { Text = "Add a project", VerticalAlignment = VerticalAlignment.Center });
        var addProjectButton = GlassProminentButton(addProjectLabel, ExtraLargeControlHeight, model.ChooseProjectFolder); // :318 model.chooseProjectFolder()

        var dropHint = new TextBlock { Text = "Or drop a folder anywhere in this window." }; // :325-327
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(dropHint);
        dropHint.Foreground = Chrome.SecondaryText.Brush(dropHint);

        // :316 a VStack centres each child at its own width; the stack would stretch the capsule to the hint's width.
        addProjectButton.HorizontalAlignment = HorizontalAlignment.Center;
        dropHint.HorizontalAlignment = HorizontalAlignment.Center;
        var addProjectStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        addProjectStack.Children.Add(addProjectButton);
        addProjectStack.Children.Add(dropHint);

        var activitySection = MakeActivitySection(model);
        builtStyle = settings.ActivityThreadStyle;

        // :339-342 "Providers" section, capped at 480
        var providersSection = new ChromeSection("Providers", () => new ProviderChecklist());
        providersSection.MaxWidth = 480;

        // :304-343 the outer VStack(spacing: 30), padded 48
        // :345 `.frame(maxWidth: .infinity)`: the column fills the page, so a section's `.frame(maxWidth: 480)` takes 480 (or what the
        // page offers) rather than the width of its widest row (DEFECT, UI-WIN-061/063: Center sized the column, and the sections, to content).
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        column.Children.Add(identity);
        column.Children.Add(addProjectStack);
        // WV-1 (OD-0098 "Place A"): the Voice typing section above Activity, in Welcome's own section pattern, capped at 480 as its siblings are.
        activityIndex = 2;
        if (voice is not null)
        {
            var voiceSection = new Marrow.App.Port.FirstRun.WelcomeVoiceSection(voice) { HorizontalAlignment = HorizontalAlignment.Stretch };
            column.Children.Add(voiceSection);
            activityIndex = 3;
        }
        column.Children.Add(activitySection);
        column.Children.Add(providersSection);
        this.column = column;

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
    private int trailingBranch = -1;
    // PORT-ONLY (OD-0133, OD-0076): the status line and the trailing control each change by a crossfade, the one they replace
    // laid over them in the row's grid fading out while they fade in; the providers' checks now finish after the window shows
    private Grid? rowGrid;
    private readonly Grid trailingCell = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>OD-0133 / OD-0076: how long a status change crossfades.</summary>
    internal const double ChangeSeconds = 0.25;

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
        trailingCell.Children.Add(trailing);
        Grid.SetColumn(trailingCell, 2);
        grid.Children.Add(leading);
        grid.Children.Add(trailingCell);
        rowGrid = grid;

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
        var text = status.IsChecking ? "Checking…" : status.Summary; // :379, U+2026
        // OD-0133 / OD-0076: a change of an attached row crossfades; its first build, and reduce motion, are at once
        var soft = statusText.Text is not null && TopLevel.GetTopLevel(this) is not null && !EnvironmentValues.GetAccessibilityReduceMotion(this);
        if (soft && statusText.Text != text && rowGrid is { } over && statusText.TranslatePoint(default, over) is { } at)
        {
            var copy = CopyOf(statusText);
            copy.HorizontalAlignment = HorizontalAlignment.Left; copy.VerticalAlignment = VerticalAlignment.Top;
            copy.Margin = new Thickness(at.X, at.Y, 0, 0); // where the line it replaces was
            FadeOut(over, copy, statusText);
        }
        statusText.Text = text;

        // :383-405 the trailing control keeps its structural identity while the row stays in the same branch: another
        // provider's status change must not replace a focused Login button or restart a spinner.
        var branch = status.IsChecking ? 0
            : !status.IsInstalled ? 1
            : status.ProviderStatusAuth is ProviderStatus.Auth.SignedOut ? 2
            : 3;
        if (branch == trailingBranch && trailing.Content is not null) return;
        trailingBranch = branch;

        Control? content = null;
        if (status.IsChecking) // :383-386
        {
            content = new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 };
        }
        else if (!status.IsInstalled) // :386-394
        {
            content = provider.IsAPIKeyBased
                ? new OpenProvidersSettingsButton()
                : GlassButton("Install", () => Marrow.Services.Port.Workspace.Open.Current(provider.InstallURL)); // :392 Link(destination:) hands the address to the default browser, through the seam tests replace (it started the shell directly)
        }
        else if (status.ProviderStatusAuth is ProviderStatus.Auth.SignedOut) // :395-401
        {
            content = provider.IsAPIKeyBased
                ? new OpenProvidersSettingsButton()
                : GlassButton("Login", () => Marrow.App.Port.ProviderLoginWatch.Open(AppEnvironment.GetAppModel(this).Providers, provider)); // owner finding F2: re-checks on its own
        }
        else // :402-405
        {
            content = SwiftImage.SystemName("checkmark.circle.fill").ForegroundStyle(Chrome.Success);
        }
        if (soft && trailing.Content is Control old)
        {
            trailing.Content = null;
            FadeOut(trailingCell, new ContentControl { Content = old, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }, content);
        }
        trailing.Content = content;
    }

    // the old status line, laid over the new one where it was
    private static TextBlock CopyOf(TextBlock text)
    {
        var copy = new TextBlock { Text = text.Text, Foreground = text.Foreground, IsHitTestVisible = false };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(copy);
        return copy;
    }

    // OD-0076: the one leaving fades out over the one arriving, which fades in; the leaving one then goes
    private static void FadeOut(Grid cell, Control leaving, Control? arriving)
    {
        cell.Children.Add(leaving);
        var curve = Animation.EaseInOut(ChangeSeconds);
        Marrow.App.Views.Composer.EffortView.Animate(leaving, curve, progress =>
        {
            leaving.Opacity = 1 - Math.Clamp(progress, 0, 1);
            if (progress >= 1) cell.Children.Remove(leaving);
        });
        if (arriving is not null) Marrow.App.Views.Composer.EffortView.Animate(arriving, curve, progress => arriving.Opacity = Math.Clamp(progress, 0, 1));
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
        // :428 a VStack centres each child at its own width; the stack would stretch the button to the heading's width and
        // leave the hint flush left.
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(symbol);
        stack.Children.Add(heading);
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            var button = WelcomeView.GlassProminentButton(newThreadLabel, LargeControlHeight, () => model.NewThread()); // :434-440
            button.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Insert(2, button);
        });
        // :441-446 the chord hint, omitted entirely when the shortcut has been cleared: the body reads the store's label, so a
        // remap shows at once.
        TextBlock? chordText = null;
        this.Track(() => ShortcutStore.Label(AppShortcut.NewThread), chord =>
        {
            if (chord is null)
            {
                if (chordText is not null) stack.Children.Remove(chordText);
                chordText = null;
                return;
            }
            if (chordText is null)
            {
                chordText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
                SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(chordText);
                chordText.Foreground = Chrome.SecondaryText.Brush(chordText);
                stack.Children.Add(chordText);
            }
            chordText.Text = chord;
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
