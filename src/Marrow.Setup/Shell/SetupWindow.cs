using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Setup.Motion;
using Marrow.Setup.Platform;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Shell;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Backdrop"; the canvas's "Window" row; unit U1, ED-NEW-inst-u1-1). Mica on Windows 11
/// (Avalonia's Mica level), Acrylic where Mica is not given, and a solid tint on Windows 10, in a contrast theme and in
/// headless tests. Over Mica or Acrylic the window lays the canvas's tint (74 % at the top to 82 % at the bottom); without
/// them the bottom tint over Windows' own base, opaque. Nothing inside the window blurs.
/// </summary>
public static class SetupBackdrop
{
    public static IReadOnlyList<WindowTransparencyLevel> Levels(SystemLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        return look.HighContrast || !look.IsWindows11
            ? [WindowTransparencyLevel.None]
            : [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
    }

    public static bool IsGlass(WindowTransparencyLevel actual) => actual == WindowTransparencyLevel.Mica || actual == WindowTransparencyLevel.AcrylicBlur;

    public static IBrush Fill(SetupPalette palette, WindowTransparencyLevel actual)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (!palette.Glass || !IsGlass(actual)) return new SolidColorBrush(palette.Solid);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(palette.TintTop, 0), new GradientStop(palette.TintBottom, 1) },
        };
    }

    /// <summary>Windows 11 rounds the window (8 DIP); Windows 10 does not.</summary>
    public static double CornerRadius(SystemLook look) => look is { IsWindows11: true } ? 8 : 0;
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Window details"; unit U1, ED-NEW-inst-u1-1). The setup window's shell: 600 × 468 DIP at
/// 100 % text size, growing with Windows' text size to fit its tallest screen, up to the work area (only past that does the
/// body scroll); not resizable, no maximise; Windows 11 corners; the backdrop above; its own caption with hand hit-testing;
/// Close pausing; and every Windows setting it follows (theme, contrast, Animation effects, text size) applied live while
/// it is open. Screens are shown through <see cref="Body"/>. Per-monitor DPI v2 comes from the manifest; Avalonia rescales.
/// </summary>
public sealed class SetupWindow : Window, ICaptionSurface
{
    public const double BaseWidth = 600, BaseHeight = 468;

    private readonly Border backdrop = new() { IsHitTestVisible = false };
    private readonly Border rim = new() { IsHitTestVisible = false };
    private readonly Border topLight = new() { IsHitTestVisible = false, BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly ScrollViewer scroller;
    private readonly Panel root;
    private readonly WindowShutdownBlocker? windowBlocker;
    private readonly SetupWindowHooks? hooks;
    private RegistryWatcher? textSizeWatcher;
    private SystemLook? applied;
    private bool closeApproved;

    public SetupWindow(SystemSettings settings, IFrameSource? frames = null, IShutdownBlocker? blocker = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Animator = new Animator(frames ?? new TopLevelFrameSource(this));
        if (blocker is null) blocker = windowBlocker = new WindowShutdownBlocker();
        Guard = new CloseGuard(blocker);
        Caption = new CaptionBar(Animator, SetupIcon.Frame(16));
        Body = new ScreenHost(Animator, () => Settings.Current.Calm);

        Width = BaseWidth;
        MinHeight = BaseHeight;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMaximize = false;
        CanMinimize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = CaptionBar.BandHeight;
        WindowDecorations = WindowDecorations.Full;
        Background = Brushes.Transparent;
        Icon = SetupIcon.WindowIcon();
        Win32Properties.SetWindowCornerPreference(this, Win32Properties.WindowCornerPreference.Round);

        scroller = new ScrollViewer
        {
            Content = Body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("34,*") };
        Grid.SetRow(Caption, 0);
        Grid.SetRow(scroller, 1);
        layout.Children.Add(Caption);
        layout.Children.Add(scroller);
        // The window's limits are also the content's, so its size comes out of layout the same on every platform
        // (Windows also enforces them through WM_GETMINMAXINFO; the headless platform does not).
        root = new Panel { MinHeight = BaseHeight, Children = { backdrop, layout, topLight, rim } };
        Content = root;
        Title = SetupStrings.CaptionSetup;
        CaptionTitle = SetupStrings.CaptionSetup;

        Guard.PausedChanged += paused => Caption.Close.IsPaused = paused;
        Closing += OnClosing;
        Opened += OnOpened;
        Closed += OnClosed;
        ScalingChanged += (_, _) => FitToWorkArea(resetHeight: true);
        PositionChanged += (_, _) => FitToWorkArea(resetHeight: false);
        Settings.Changed += OnSettingsChanged;

        if (OperatingSystem.IsWindows())
        {
            hooks = new SetupWindowHooks(this, new WindowsCaptionCalls());
            Win32Properties.AddWndProcHookCallback(this, hooks.WndProc);
        }
        ApplyLook(Settings.Current);
    }

    public SystemSettings Settings { get; }

    /// <summary>The one per-frame loop every motion in this window runs on.</summary>
    public Animator Animator { get; }

    public CloseGuard Guard { get; }

    public CaptionBar Caption { get; }

    /// <summary>The screens.</summary>
    public ScreenHost Body { get; }

    /// <summary>The palette in use.</summary>
    public SetupPalette Palette { get; private set; } = SetupPalette.Dark;

    /// <summary>The look last applied.</summary>
    public SystemLook Look => applied ?? Settings.Current;

    /// <summary>The window's edge, for tests: its thickness and brush.</summary>
    public Border Rim => rim;

    /// <summary>The backdrop fill, for tests.</summary>
    public IBrush? Fill => backdrop.Background;

    /// <summary>The message hook (null off Windows).</summary>
    public SetupWindowHooks? Hooks => hooks;

    /// <summary>"nth Setup", or "Uninstall nth" in uninstall mode: the caption and the taskbar.</summary>
    public string CaptionTitle
    {
        get => Caption.Title;
        set
        {
            Caption.Title = value;
            Title = value;
        }
    }

    /// <summary>Raised once the window has a native handle (the single-instance lock publishes it).</summary>
    public event Action<nint>? HandleReady;

    /// <summary>Closes the window whatever the guard says (the flow is done: after the install, or a smoke run).</summary>
    public void CloseNow()
    {
        closeApproved = true;
        Close();
    }

    /// <summary>Re-reads Windows' settings now (also done on every settings message and when Text size changes).</summary>
    public void RefreshSettings() => Settings.Refresh();

    private void OnSettingsChanged(SystemLook look)
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyLook(look);
        else Dispatcher.UIThread.Post(() => ApplyLook(Settings.Current));
    }

    private void ApplyLook(SystemLook look)
    {
        var previous = applied;
        applied = look;
        Palette = SetupPalette.For(look);
        SetupTheme.Apply(Resources, Palette, look.TextScale);
        RequestedThemeVariant = look.HighContrast ? (Luminance(Palette.Solid) < 0.5 ? ThemeVariant.Dark : ThemeVariant.Light) : look.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        TransparencyLevelHint = SetupBackdrop.Levels(look);
        var radius = new CornerRadius(SetupBackdrop.CornerRadius(look));
        backdrop.CornerRadius = radius;
        rim.CornerRadius = radius;
        topLight.CornerRadius = radius;
        rim.BorderThickness = new Thickness(Palette.EdgeThickness);
        rim.BorderBrush = new SolidColorBrush(Palette.Edge);
        topLight.BorderBrush = new SolidColorBrush(Palette.TopLight);
        Caption.ApplyPalette(Palette);
        DrawBackdrop();
        if (previous is not null && previous.TextScale != look.TextScale) Body.ResetHeight();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty) DrawBackdrop();
    }

    private void DrawBackdrop() => backdrop.Background = SetupBackdrop.Fill(Palette, ActualTransparencyLevel);

    private void OnOpened(object? sender, EventArgs e)
    {
        FitToWorkArea(resetHeight: false);
        var mark = SetupIcon.Frame((int)Math.Ceiling(16 * RenderScaling));
        if (mark is not null) Caption.SetMark(mark);
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == 0) return;
        if (windowBlocker is not null) windowBlocker.Window = handle;
        textSizeWatcher = new RegistryWatcher(WindowsSystemLookSource.AccessibilityKey, () => Dispatcher.UIThread.Post(Settings.Refresh));
        HandleReady?.Invoke(handle);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closeApproved) return;
        if (!Guard.TryClose()) e.Cancel = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Settings.Changed -= OnSettingsChanged;
        textSizeWatcher?.Dispose();
        textSizeWatcher = null;
    }

    /// <summary>The work area caps the height (the body scrolls past it) and, on a very small screen, the width.</summary>
    private void FitToWorkArea(bool resetHeight)
    {
        if (resetHeight) Body.ResetHeight();
        var screen = Screens?.ScreenFromWindow(this);
        if (screen is null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var fit = WindowFit.Limits(new Size(area.Width / scale, area.Height / scale));
        MaxHeight = fit.MaxHeight;
        Width = fit.Width;
        MinHeight = Math.Min(BaseHeight, fit.MaxHeight);
        root.MinHeight = MinHeight;
        root.MaxHeight = MaxHeight;
    }

    private static double Luminance(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;

    // ICaptionSurface
    Point ICaptionSurface.ScreenToClient(PixelPoint screen) => this.PointToClient(screen);

    Point ICaptionSurface.ClientPixelsToClient(PixelPoint client) => new(client.X / RenderScaling, client.Y / RenderScaling);

    double ICaptionSurface.ClientWidth => ClientSize.Width;

    bool ICaptionSurface.ClosePaused => Guard.IsPaused;

    void ICaptionSurface.SetCaptionHover(int hit) => Caption.SetHover(hit);

    void ICaptionSurface.SetCaptionPressed(int hit) => Caption.SetPressed(hit);

    void ICaptionSurface.CloseRefused() => Guard.TryClose();

    void ICaptionSurface.SettingsMayHaveChanged() => Settings.Refresh();
}

/// <summary>The window's size against the work area (in DIP).</summary>
public readonly record struct WindowFit(double Width, double MaxHeight)
{
    /// <summary>600 wide unless the work area is narrower; as tall as its content needs, up to the work area's height.</summary>
    public static WindowFit Limits(Size workArea) =>
        new(Math.Min(SetupWindow.BaseWidth, Math.Max(1, workArea.Width)), Math.Max(1, workArea.Height));

    /// <summary>The window's height for content that needs <paramref name="contentHeight"/>: at least 468, at most the work area; it scrolls when the content needs more.</summary>
    public static (double Height, bool Scrolls) Height(double contentHeight, double workAreaHeight)
    {
        var cap = Math.Max(1, workAreaHeight);
        var height = Math.Min(Math.Max(SetupWindow.BaseHeight, contentHeight), cap);
        return (height, contentHeight > cap);
    }
}

/// <summary>The app's icon, embedded once: the 16 px caption mark (the frame nearest the DPI) and the taskbar icon.</summary>
public static class SetupIcon
{
    private const string Resource = "SetupIcon.ico";

    public static WindowIcon? WindowIcon()
    {
        using var stream = typeof(SetupIcon).Assembly.GetManifestResourceStream(Resource);
        return stream is null ? null : new WindowIcon(stream);
    }

    /// <summary>The icon's PNG frame of at least <paramref name="pixels"/> (the largest if none is), as a bitmap.</summary>
    public static Bitmap? Frame(int pixels)
    {
        using var stream = typeof(SetupIcon).Assembly.GetManifestResourceStream(Resource);
        if (stream is null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var data = memory.GetBuffer().AsSpan(0, (int)memory.Length);
        if (data.Length < 6) return null;
        var count = BitConverter.ToUInt16(data[4..6]);
        (int Size, int Offset, int Length) best = (0, 0, 0);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            if (entry + 16 > data.Length) break;
            var size = data[entry] == 0 ? 256 : data[entry];
            var length = (int)BitConverter.ToUInt32(data[(entry + 8)..(entry + 12)]);
            var offset = (int)BitConverter.ToUInt32(data[(entry + 12)..(entry + 16)]);
            var fits = size >= pixels;
            var better = best.Size == 0 || (fits && (best.Size < pixels || size < best.Size)) || (!fits && best.Size < pixels && size > best.Size);
            if (better && offset + length <= data.Length) best = (size, offset, length);
        }
        if (best.Size == 0) return null;
        using var png = new MemoryStream(data.Slice(best.Offset, best.Length).ToArray());
        return new Bitmap(png);
    }
}
