using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Panes;
using Marrow.UI.Web;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.7-5.8, 2.4 and canvas .vp/.device; ED-NEW-web-w9-3. Live responsive region, shared grips, snapshot during ordinary drawer geometry changes.
public sealed class ProjectPreviewBody : Grid, IDisposable
{
    private readonly WebViewHost host;
    private readonly Grid viewport = new() { ClipToBounds = true, Margin = new Thickness(6, 0, 6, 0), Background = new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)) };
    private readonly Canvas chrome = new();
    private readonly Border frame = new() { BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), CornerRadius = new CornerRadius(6), BoxShadow = BoxShadows.Parse("0 20 50 -10 #b3000000") };
    private readonly TextBlock scaled = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 8), IsHitTestVisible = false };
    private readonly Splitter drawerGrip;
    private readonly AnimatedScalar drawerHeight;
    private readonly DispatcherTimer emulationTimer;
    private readonly List<Splitter> deviceSplitters = [];
    private bool resizePending;
    private bool drawing;
    private int startWidth;
    private int startHeight;
    private double startDrawer;
    private double startScale = 1;
    private IDisposable? snapshot;
    private bool snapshotWanted;
    private Task? acquiringSnapshot;
    private bool drawerDragging;
    private double drawerGoal;
    private bool disposed;
    public ProjectPreviewBody(VisusTab tab, WebViewHost host)
    {
        Tab = tab; this.host = host; Runtime = tab.Preview!;
        scaled.Foreground = PaneTokens.Text1.Brush(this);
        ActualThemeVariantChanged += (_, _) => scaled.Foreground = PaneTokens.Text1.Brush(this);
        RowDefinitions = new RowDefinitions("38,*,Auto");
        Bar = new DevicePresetBar(this); Children.Add(Bar);
        viewport.Children.Add(host); viewport.Children.Add(chrome); viewport.Children.Add(scaled); chrome.Children.Add(frame);
        Grid.SetRow(viewport, 1); Children.Add(viewport);
        Drawer = new PreviewConsoleDrawer(this); Grid.SetRow(Drawer, 2); Children.Add(Drawer);
        foreach (var orientation in new[] { SplitterOrientation.Vertical, SplitterOrientation.Horizontal, SplitterOrientation.Both })
        {
            var axis = orientation;
            var splitter = new Splitter(axis, new SplitterTarget
            {
                OnReport = () => new SplitterReport(axis == SplitterOrientation.Horizontal ? Tab.PreviewState.Height : Tab.PreviewState.Width, 240, 4000, false, 0, "", Bar.SizeText),
                OnBegin = BeginResponsive,
                OnMove = travel => ResizeResponsive(startWidth + (axis == SplitterOrientation.Horizontal ? 0 : (int)Math.Round(travel.X * 2 / startScale)), startHeight + (axis == SplitterOrientation.Vertical ? 0 : (int)Math.Round(travel.Y * 2 / startScale))),
                OnEnd = _ => EndResponsive(),
                OnReset = () => { ResizeResponsive(390, 844); EndResponsive(); },
                OnStepTo = maximum => { ResizeResponsive(maximum ? 4000 : 240, maximum ? 4000 : 240); EndResponsive(); },
            }) { Label = "Resize preview device", FineInset = 0, MirrorsInRightToLeft = false };
            deviceSplitters.Add(splitter); chrome.Children.Add(splitter);
        }
        drawerGrip = new Splitter(SplitterOrientation.Horizontal, new SplitterTarget
        {
            OnReport = () => new SplitterReport(Drawer.State.Height, 90, Math.Max(90, Bounds.Height * .6), Drawer.State.Collapsed, 24, "", "Console height"),
            OnSettle = () => drawerHeight?.Finish(),
            OnBegin = () => { drawerDragging = true; startDrawer = Drawer.ShownHeight; Drawer.State.Begin(); snapshotWanted = true; _ = Snapshot(); },
            OnMove = travel => { Drawer.State.DragTo(startDrawer - travel.Y, Bounds.Height); Drawer.Refresh(); },
            OnEnd = moved => { drawerDragging = false; Drawer.State.End(); if (snapshot is not null || TopLevel.GetTopLevel(this) is null) ApplyDrawerHeight(); else _ = Snapshot(); },
            OnReset = () => { Drawer.State.Reset(); Drawer.Refresh(); },
            OnCollapse = collapse => { if (collapse) Drawer.State.Collapse(); else Drawer.State.Restore(); Drawer.Refresh(); },
            OnStepTo = maximum => { Drawer.State.DragTo(maximum ? Bounds.Height * .6 : 90, Bounds.Height); Drawer.Refresh(); },
            OnSetValue = value => { Drawer.State.DragTo(value, Bounds.Height); Drawer.Refresh(); },
        }) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, -6, 6, 0), CollapseSide = SplitterSide.Trailing, Label = "Resize console" };
        Grid.SetRow(drawerGrip, 2); Children.Add(drawerGrip);
        drawerGoal = Drawer.ShownHeight;
        drawerHeight = new AnimatedScalar(this, Drawer.ShownHeight, value =>
        {
            Drawer.Height = Math.Max(0, value); Drawer.IsVisible = Drawer.Height > 0;
            var gap = Math.Min(6, Drawer.Height / 2);
            Drawer.Margin = Drawer.Height > 0 ? new Thickness(6, gap, 6, gap) : new Thickness(0);
            drawerGrip.IsVisible = Drawer.Height > 0; drawerGrip.IsHitTestVisible = Drawer.State.Open;
            Place(); if (!drawerDragging && Math.Abs(value - drawerGoal) < .001) FinishSnapshot();
        });
        emulationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Flush());
        AttachedToVisualTree += (_, _) => { emulationTimer.Start(); Refresh(); };
        DetachedFromVisualTree += (_, _) => { emulationTimer.Stop(); FinishSnapshot(); };
        LayoutUpdated += (_, _) => Place(); Runtime.Changed += Refresh;
        Refresh();
    }
    public VisusTab Tab { get; }
    public ProjectPreviewRuntime Runtime { get; }
    public DevicePresetBar Bar { get; }
    public PreviewConsoleDrawer Drawer { get; }
    public IReadOnlyList<Splitter> DeviceSplitters => deviceSplitters;
    public DeviceMetrics Metrics { get; private set; } = new(390, 844, 3, true, true, 1);
    public Task WhenIdle { get { Flush(); return Runtime.Emulation?.WhenIdle ?? Task.CompletedTask; } }
    public void SetDevice(DevicePreset preset) { Tab.SetPreviewState(Tab.PreviewState with { Preset = preset, Rotated = false }); Refresh(); Flush(); }
    public void Rotate()
    {
        var state = Tab.PreviewState;
        Tab.SetPreviewState(state.Preset == DevicePreset.Responsive ? state with { Width = state.Height, Height = state.Width, Rotated = false } : state with { Rotated = !state.Rotated }); Refresh(); Flush();
    }
    public void BeginResponsive() { startWidth = Tab.PreviewState.Width; startHeight = Tab.PreviewState.Height; startScale = Metrics.Scale; Place(); }
    public void ResizeResponsive(int width, int height) { Tab.SetPreviewState(Tab.PreviewState with { Preset = DevicePreset.Responsive, Width = Math.Clamp(width, 240, 4000), Height = Math.Clamp(height, 240, 4000), Rotated = false }); Refresh(); }
    public void EndResponsive() => Flush();
    public void ToggleConsole() { Drawer.State.Open = !Drawer.State.Open; Drawer.Refresh(); }
    public void Refresh()
    {
        if (disposed) return;
        Runtime.Reload.Enabled = Tab.PreviewState.ReloadOnSave;
        resizePending = true; Place(); Bar.Refresh(Metrics);
    }
    internal bool DrawerHeightChanged()
    {
        if (drawerGrip is null || drawerHeight is null) return false;
        drawerGrip.IsVisible = Drawer.Height > 0; drawerGrip.IsHitTestVisible = Drawer.State.Open;
        if (Drawer.State.Open) Drawer.IsVisible = true;
        drawerGrip.Margin = new Thickness(6, -6, 6, 0);
        drawerGoal = Drawer.ShownHeight;
        if (TopLevel.GetTopLevel(this) is null) { ApplyDrawerHeight(); return true; }
        snapshotWanted = true;
        if (snapshot is null) _ = Snapshot(); else ApplyDrawerHeight();
        return true;
    }
    private void ApplyDrawerHeight() => drawerHeight?.Set(drawerGoal, drawerDragging ? null : Marrow.UI.Port.Animation.Spring(duration: .32, bounce: 0));
    private void Place()
    {
        if (disposed || drawing) return;
        drawing = true;
        try
        {
            var width = Math.Max(1, viewport.Bounds.Width); var height = Math.Max(1, viewport.Bounds.Height);
            var metrics = Tab.PreviewState.Metrics(width, height);
            if (Metrics != metrics) { Metrics = metrics; resizePending = true; Bar.Refresh(metrics); }
            var w = metrics.Width * metrics.Scale; var h = metrics.Height * metrics.Scale;
            var x = Math.Max(0, (width - w) / 2); var y = Math.Max(0, (height - h) / 2);
            Canvas.SetLeft(frame, x); Canvas.SetTop(frame, y); frame.Width = w; frame.Height = h;
            scaled.Text = metrics.Scale < .999 ? "Scaled " + Math.Round(metrics.Scale * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %" : "";
            foreach (var splitter in deviceSplitters) splitter.IsVisible = Tab.PreviewState.Preset == DevicePreset.Responsive;
            if (deviceSplitters.Count == 3)
            {
                var right = deviceSplitters[0]; right.Width = 12; right.Height = Math.Max(16, h - 16); Canvas.SetLeft(right, x + w); Canvas.SetTop(right, y);
                var bottom = deviceSplitters[1]; bottom.Width = Math.Max(16, w - 16); bottom.Height = 12; Canvas.SetLeft(bottom, x); Canvas.SetTop(bottom, y + h);
                var corner = deviceSplitters[2]; corner.Width = 16; corner.Height = 16; Canvas.SetLeft(corner, x + w); Canvas.SetTop(corner, y + h);
            }
            if (Tab.View?.Engine is IPreviewViewport native && Tab.View.Engine.IsAttached && TopLevel.GetTopLevel(this) is { } top) native.PlacePreview(width, height, top.RenderScaling, x, y, w, h);
        }
        finally { drawing = false; }
    }
    private void Flush()
    {
        if (disposed || !resizePending || Runtime.Emulation is null || Tab.View?.Engine?.IsAttached != true) return;
        resizePending = false; Runtime.Emulation.Update(Tab.PreviewState, Math.Max(1, viewport.Bounds.Width), Math.Max(1, viewport.Bounds.Height));
    }
    private Task Snapshot()
    {
        if (acquiringSnapshot is { IsCompleted: false }) return acquiringSnapshot;
        return acquiringSnapshot = Acquire();
        async Task Acquire()
        {
            var lease = await host.BeginGestureAsync();
            acquiringSnapshot = null;
            if (disposed || !snapshotWanted) lease.Dispose(); else { snapshot?.Dispose(); snapshot = lease; ApplyDrawerHeight(); }
        }
    }
    private void FinishSnapshot() { snapshotWanted = false; snapshot?.Dispose(); snapshot = null; }
    public void Dispose()
    {
        disposed = true; emulationTimer.Stop(); Runtime.Changed -= Refresh; Drawer.Dispose(); FinishSnapshot();
        if (Tab.View?.Engine is IPreviewViewport native) native.ClearPreview();
        viewport.Children.Remove(host);
    }
}
