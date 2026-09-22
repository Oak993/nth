// P7, task packet T-M0-006: chrome and windowing, four things in one run.
//
// QUESTION (verbatim capability, ADR-0002 / stack_decision.json P7): with ExtendClientAreaToDecorationsHint,
// ExtendClientAreaTitleBarHeightHint and WindowDecorations, app-drawn caption buttons on the LEADING edge at the
// derived traffic-light constants, declared through Win32Properties.SetNonClientHitTestResult as Caption / Close /
// MinButton / MaxButton: do drag, double-click-to-maximise and the system menu work from the app-drawn region, do the
// app-drawn buttons minimise, maximise and close; and can the user type into a WS_EX_NOACTIVATE panel's field (style
// through Win32Properties.AddWindowStylesCallback, WM_MOUSEACTIVATE -> MA_NOACTIVATE through AddWndProcHookCallback).
// Recorded per WindowDecorations value: Snap Layouts hover flyout over the leading-edge maximise button, and whether
// DWM minimise / maximise / restore animations run (AvaloniaUI/Avalonia#21212).
//
// Constants (SOURCE FACT, pinned 1.6.2, E:\Droppy\reference\droppy-code-v1.6.2):
//   Chrome.swift:11  sheetInset = 0              Chrome.swift:15 trafficLightDiameter = 14
//   Chrome.swift:16  trafficLightSpacing = 9     Chrome.swift:17 trafficLightPitch = diameter + spacing = 23
//   Chrome.swift:18  trafficLightLeading = 18    Chrome.swift:19 trafficLightTop = 16
//   Chrome.swift:31  chromeHorizontalPadding=14  Chrome.swift:32 chromeTopPadding = 12
//   Chrome.swift:33-38 capsuleHeight = 28 + 2*2 = 32
//   WindowChrome.swift:148-150 titlebarHeight = sheetInset + chromeTopPadding + capsuleHeight = 44
//   WindowChrome.swift:136-138 collapsedTrafficLightTop = 0 + 12 + (32 - 14) / 2 = 21
//   WindowChrome.swift:178-179 leading = sidebarVisible ? trafficLightLeading (18) : sheetInset + chromeHorizontalPadding (14)
//                              top     = sidebarVisible ? trafficLightTop (16)     : collapsedTrafficLightTop (21)
//   WindowChrome.swift:183-187 order close, miniaturize, zoom; x = leading + index * pitch
//   WindowChrome.swift:197-198 width = height = trafficLightDiameter
//
// Rules for the shared desktop (OD-0007): every mouse or key event is aimed at a window this process created. Before
// every click, WindowFromPoint's root must be the intended HWND of ours; before every key, the foreground window must
// belong to this process. Captures are BitBlt / StretchBlt of the screen DC (DWM-composed). The cursor is restored.
//
// All input is driven from a background thread so the modal move, menu and caption-button loops that DefWindowProc
// runs on the UI thread cannot deadlock the driver.
//
// Usage: p7 --out <dir>.  Exit 0 = PASS, 1 = FAIL, 2 = INCONCLUSIVE, 4 = crash.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using SkiaSharp;

static class N
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
    public const long WS_BORDER = 0x800000, WS_DLGFRAME = 0x400000, WS_SYSMENU = 0x80000, WS_THICKFRAME = 0x40000,
        WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000, WS_POPUP = 0x80000000L;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2), HWND_TOP = IntPtr.Zero;
    public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000, COLORONCOLOR = 3;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public const uint GA_ROOT = 2;
    public const uint WM_NCHITTEST = 0x84;
    public const int SW_RESTORE = 9;
    public const int SPI_GETWORKAREA = 0x30, SPI_GETANIMATION = 0x48, SPI_GETCLIENTAREAANIMATION = 0x1042;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int L, T, R, B;
        public int W => R - L; public int H => B - T;
        public override string ToString() => $"[{L},{T}]-[{R},{B}] {W}x{H}";
        public bool Contains(POINT p) => p.X >= L && p.X < R && p.Y >= T && p.Y < B;
        public bool Intersects(RECT o) => L < o.R && o.L < R && T < o.B && o.T < B;
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public override string ToString() => $"({X},{Y})"; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; [FieldOffset(8)] public KEYBDINPUT ki; }
    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10;
    public const uint KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

    public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
    public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr mod, string name);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(int a, int b, out RECT r, int c);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SpiAnim(int a, int b, ref ANIMATIONINFO r, int c);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SpiBool(int a, int b, out int r, int c);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetSystemMenu(IntPtr h, bool revert);
    [DllImport("user32.dll")] public static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr h, IntPtr rect);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] public static extern IntPtr GetFocus();
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int sw, int sh, int rop);
    [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] public static extern int DwmGetInt(IntPtr h, int a, out int v, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    public static long Style(IntPtr h) => GetWindowLongPtr(h, GWL_STYLE).ToInt64();
    public static long ExStyle(IntPtr h) => GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();

    public static RECT Frame(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0) return r;
        GetWindowRect(h, out r);
        return r;
    }

    public static MONITORINFO Monitor(IntPtr h)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromWindow(h, 2), ref mi);
        return mi;
    }

    /// Screen DC blit, optionally downscaled; top-down BGRA with alpha forced to 255.
    public static byte[] Capture(RECT r, int dw, int dh)
    {
        IntPtr scr = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(scr), bmp = CreateCompatibleBitmap(scr, dw, dh);
        IntPtr old = SelectObject(mem, bmp);
        bool ok;
        if (dw == r.W && dh == r.H) ok = BitBlt(mem, 0, 0, dw, dh, scr, r.L, r.T, SRCCOPY | CAPTUREBLT);
        else { SetStretchBltMode(mem, COLORONCOLOR); ok = StretchBlt(mem, 0, 0, dw, dh, scr, r.L, r.T, r.W, r.H, SRCCOPY | CAPTUREBLT); }
        SelectObject(mem, old);
        var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = dw, biHeight = -dh, biPlanes = 1, biBitCount = 32 };
        var buf = new byte[dw * dh * 4];
        int lines = GetDIBits(mem, bmp, 0, (uint)dh, buf, ref bi, 0);
        DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, scr);
        if (!ok || lines != dh) throw new InvalidOperationException($"capture failed blt={ok} lines={lines}");
        for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;
        return buf;
    }

    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassNameW(h, sb, 256); return sb.ToString(); }
    public static uint PidOf(IntPtr h) { GetWindowThreadProcessId(h, out var p); return p; }
    public static bool Cloaked(IntPtr h) => DwmGetInt(h, DWMWA_CLOAKED, out var v, 4) == 0 && v != 0;
    public static IntPtr Root(POINT p) => GetAncestor(WindowFromPoint(p), GA_ROOT);

    public static int NcHit(IntPtr h, POINT p)
    {
        long lp = ((long)(ushort)(short)p.Y << 16) | (ushort)(short)p.X;
        return (int)SendMessageW(h, WM_NCHITTEST, IntPtr.Zero, new IntPtr(lp)).ToInt64();
    }

    public static string HitName(int v) => v switch
    {
        -1 => "HTTRANSPARENT", 0 => "HTNOWHERE", 1 => "HTCLIENT", 2 => "HTCAPTION", 3 => "HTSYSMENU", 8 => "HTMINBUTTON", 9 => "HTMAXBUTTON",
        10 => "HTLEFT", 11 => "HTRIGHT", 12 => "HTTOP", 13 => "HTTOPLEFT", 14 => "HTTOPRIGHT", 15 => "HTBOTTOM", 16 => "HTBOTTOMLEFT",
        17 => "HTBOTTOMRIGHT", 18 => "HTBORDER", 20 => "HTCLOSE", _ => "HT" + v,
    };
}

/// The typed field. A plain Border subclass so it works with no theme loaded (no TextBox template exists without one).
/// It masks what it receives, as the source's secure field does; the probe reads Text back to prove arrival.
/// Mode Focus: a press gives it Avalonia keyboard focus (what a field normally does).
/// Mode NoFocus: not focusable; a press does nothing to focus (control for Forward).
/// Mode Forward: a press marks it as the app-level text target without focusing it; the active host window diverts its
/// own TextInput to it (the remedy measured when the panel must stay inactive).
enum FieldMode { Focus, NoFocus, Forward }

sealed class Field : Border
{
    static readonly IBrush Dot = new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
    public static volatile Field? ForwardTarget;
    public string Text = "";
    public int KeyDowns, TextInputs, Presses;
    public readonly FieldMode Mode;
    public Field(FieldMode mode = FieldMode.Focus)
    {
        Mode = mode;
        Focusable = mode == FieldMode.Focus;
        Width = 240; Height = 36;
        Background = new ImmutableSolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
        BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x40, 0x80, 0xFF));
        BorderThickness = new Thickness(2);
        Child = new Dots { Owner = this };
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Presses++;
        if (Mode == FieldMode.Focus) Focus();
        if (Mode == FieldMode.Forward) ForwardTarget = this;
        e.Handled = true;
        base.OnPointerPressed(e);
    }
    protected override void OnKeyDown(KeyEventArgs e) { KeyDowns++; base.OnKeyDown(e); }
    protected override void OnTextInput(TextInputEventArgs e)
    {
        Append(e.Text ?? "");
        e.Handled = true;
        base.OnTextInput(e);
    }
    public void Append(string s)
    {
        TextInputs++;
        Text += s;
        Child?.InvalidateVisual();
    }
    public void Reset() { Text = ""; Child?.InvalidateVisual(); }
    sealed class Dots : Control
    {
        public Field? Owner;
        public override void Render(DrawingContext ctx)
        {
            int n = Owner?.Text.Length ?? 0;
            for (int i = 0; i < n; i++) ctx.FillRectangle(Dot, new Rect(8 + i * 14, 11, 10, 10));
        }
    }
}

/// Hooked = declared hit tests PLUS app handling of the non-client buttons and caption context menu in the WndProc hook
/// (the remedy variant, measured separately from the verbatim configuration).
sealed record Variant(string Name, WindowDecorations Deco, bool Extend, bool Declare, bool Hooked = false);

sealed class ChromeWindow : Window
{
    public static readonly IBrush Magenta = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x00, 0xFF));
    public readonly Grid RootGrid = new() { Background = Magenta };
    public readonly Border TitleBar = new();
    public readonly Canvas Lights = new();
    public readonly Ellipse CloseB = new(), MinB = new(), MaxB = new();
    public readonly Field? Sink;
    public volatile bool ClosedFlag;
    public readonly List<string> Msgs = new();
    public readonly Variant V;

    // SOURCE FACT constants, see file header.
    public const double TitlebarHeight = 0 + 12 + (28 + 2 * 2);                 // 44
    public const double Diameter = 14, Pitch = 14 + 9;                          // 14, 23
    public const double LeadingSidebar = 18, TopSidebar = 16;                   // trafficLightLeading, trafficLightTop
    public const double LeadingCollapsed = 0 + 14, TopCollapsed = 0 + 12 + (32 - 14) / 2.0; // 14, 21

    public ChromeWindow(Variant v, bool withSink)
    {
        V = v;
        Title = "P7 " + v.Name;
        WindowDecorations = v.Deco;
        ExtendClientAreaToDecorationsHint = v.Extend;
        if (v.Extend) ExtendClientAreaTitleBarHeightHint = TitlebarHeight;
        if (v.Extend)
        {
            TitleBar.Height = TitlebarHeight;
            TitleBar.VerticalAlignment = VerticalAlignment.Top;
            TitleBar.Background = new ImmutableSolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
            Lights.Height = TitlebarHeight;
            Lights.VerticalAlignment = VerticalAlignment.Top;
            foreach (var (e, c) in new[] { (CloseB, Color.FromRgb(0xFF, 0x5F, 0x57)), (MinB, Color.FromRgb(0xFE, 0xBC, 0x2E)), (MaxB, Color.FromRgb(0x28, 0xC8, 0x40)) })
            {
                e.Width = Diameter; e.Height = Diameter; e.Fill = new ImmutableSolidColorBrush(c);
                Lights.Children.Add(e);
            }
            Place(false);
            RootGrid.Children.Add(TitleBar);
            RootGrid.Children.Add(Lights);
            if (v.Declare)
            {
                Win32Properties.SetNonClientHitTestResult(TitleBar, Win32Properties.Win32HitTestValue.Caption);
                Win32Properties.SetNonClientHitTestResult(CloseB, Win32Properties.Win32HitTestValue.Close);
                Win32Properties.SetNonClientHitTestResult(MinB, Win32Properties.Win32HitTestValue.MinButton);
                Win32Properties.SetNonClientHitTestResult(MaxB, Win32Properties.Win32HitTestValue.MaxButton);
            }
        }
        if (withSink)
        {
            Sink = new Field { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40, 80, 0, 0) };
            RootGrid.Children.Add(Sink);
        }
        Template = new FuncControlTemplate<Window>((_, _) => RootGrid);
        Win32Properties.AddWndProcHookCallback(this, Hook);
        Closed += (_, _) => ClosedFlag = true;
    }

    public void Place(bool collapsed)
    {
        double leading = collapsed ? LeadingCollapsed : LeadingSidebar, top = collapsed ? TopCollapsed : TopSidebar;
        var bs = new[] { CloseB, MinB, MaxB };                                   // close, miniaturize, zoom
        for (int i = 0; i < 3; i++) { Canvas.SetLeft(bs[i], leading + i * Pitch); Canvas.SetTop(bs[i], top); }
    }

    int _pendingNc;

    // Records what DefWindowProc and the system did. Only a Hooked variant handles anything (see Variant).
    IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        int wp = (int)wParam.ToInt64();
        string? s = msg switch
        {
            0x0112 => $"WM_SYSCOMMAND 0x{wParam.ToInt64() & 0xFFF0:X4}",
            0x0117 => (lParam.ToInt64() >> 16 & 0xFFFF) != 0 ? "WM_INITMENUPOPUP (window menu)" : "WM_INITMENUPOPUP",
            0x0211 => "WM_ENTERMENULOOP",
            0x0231 => "WM_ENTERSIZEMOVE",
            0x0232 => "WM_EXITSIZEMOVE",
            0x00A1 => $"WM_NCLBUTTONDOWN {N.HitName(wp)}",
            0x00A2 => $"WM_NCLBUTTONUP {N.HitName(wp)}",
            0x00A3 => $"WM_NCLBUTTONDBLCLK {N.HitName(wp)}",
            0x00A4 => $"WM_NCRBUTTONDOWN {N.HitName(wp)}",
            0x00A5 => $"WM_NCRBUTTONUP {N.HitName(wp)}",
            0x007B => "WM_CONTEXTMENU",
            0x0201 => "WM_LBUTTONDOWN (client)",
            0x0202 => "WM_LBUTTONUP (client)",
            0x0203 => "WM_LBUTTONDBLCLK (client)",
            0x0204 => "WM_RBUTTONDOWN (client)",
            0x0104 => $"WM_SYSKEYDOWN vk=0x{wp:X2}",
            0x0106 => $"WM_SYSCHAR 0x{wp:X2}",
            0x0100 => $"WM_KEYDOWN vk=0x{wp:X2}",
            _ => null,
        };
        if (s != null) Add(s);
        if (!V.Hooked) return IntPtr.Zero;

        if (msg == 0x00A1 && (wp == 8 || wp == 9 || wp == 20))
        {
            // Swallow the press so neither DefWindowProc's classic button tracking nor Avalonia's capture runs.
            _pendingNc = wp; handled = true; Add($"HOOK swallowed WM_NCLBUTTONDOWN {N.HitName(wp)}");
            return IntPtr.Zero;
        }
        if (msg == 0x00A2)
        {
            int pending = _pendingNc; _pendingNc = 0;
            if (wp == pending && (wp == 8 || wp == 9 || wp == 20))
            {
                int sc = wp == 8 ? 0xF020 : wp == 20 ? 0xF060 : (N.IsZoomed(hWnd) ? 0xF120 : 0xF030);
                N.PostMessageW(hWnd, 0x0112, new IntPtr(sc), IntPtr.Zero);
                handled = true; Add($"HOOK WM_NCLBUTTONUP {N.HitName(wp)} -> PostMessage WM_SYSCOMMAND 0x{sc:X4}");
                return IntPtr.Zero;
            }
        }
        if (msg == 0x0112 && (wParam.ToInt64() & 0xFFF0) == 0xF100 && lParam.ToInt64() == 0x20)
        {
            // Keyboard window menu (Alt+Space arrives as SC_KEYMENU with lParam ' '): open the system menu at the
            // top-left of the client area, where the app-drawn caption is.
            handled = true;
            var menu = N.GetSystemMenu(hWnd, false);
            N.GetWindowRect(hWnd, out var wr);
            Add($"HOOK WM_SYSCOMMAND SC_KEYMENU ' ' -> TrackPopupMenu(GetSystemMenu=0x{menu.ToInt64():X}) at ({wr.L + 8},{wr.T + 44})");
            int cmd = N.TrackPopupMenu(menu, 0x0100, wr.L + 8, wr.T + 44, 0, hWnd, IntPtr.Zero);
            Add($"HOOK TrackPopupMenu returned 0x{cmd:X4}");
            if (cmd != 0) N.PostMessageW(hWnd, 0x0112, new IntPtr(cmd), IntPtr.Zero);
            return IntPtr.Zero;
        }
        if (msg == 0x007B)
        {
            long lp = lParam.ToInt64();
            var pt = new N.POINT { X = (short)(lp & 0xFFFF), Y = (short)((lp >> 16) & 0xFFFF) };
            if ((lp & 0xFFFFFFFF) != 0xFFFFFFFF && N.NcHit(hWnd, pt) == 2)
            {
                handled = true;
                var menu = N.GetSystemMenu(hWnd, false);
                Add($"HOOK WM_CONTEXTMENU on HTCAPTION at {pt} -> TrackPopupMenu(GetSystemMenu=0x{menu.ToInt64():X})");
                int cmd = N.TrackPopupMenu(menu, 0x0100 | 0x0002, pt.X, pt.Y, 0, hWnd, IntPtr.Zero); // TPM_RETURNCMD | TPM_RIGHTBUTTON
                Add($"HOOK TrackPopupMenu returned 0x{cmd:X4}");
                if (cmd != 0) N.PostMessageW(hWnd, 0x0112, new IntPtr(cmd), IntPtr.Zero);
                return IntPtr.Zero;
            }
        }
        return IntPtr.Zero;
    }

    void Add(string s) { lock (Msgs) Msgs.Add($"{DateTime.UtcNow:HH:mm:ss.fff}Z {s}"); }

    public string[] TakeMsgs() { lock (Msgs) { var a = Msgs.ToArray(); Msgs.Clear(); return a; } }
}

sealed class PanelWindow : Window
{
    public readonly Field F;
    public readonly Grid RootGrid = new() { Background = new ImmutableSolidColorBrush(Color.FromRgb(0x30, 0x30, 0x40)) };
    public int MouseActivates;
    public readonly List<string> Events = new();
    public int CbtBlockedActivate, CbtBlockedFocus;
    N.HookProc? _cbtProc; IntPtr _cbt;

    /// Remedy measured separately: a thread-local WH_CBT hook (this process's UI thread only) that refuses
    /// HCBT_ACTIVATE and HCBT_SETFOCUS for this panel's HWND, so nothing (the system or the framework) can make it the
    /// active or focus window. Must run on the UI thread.
    public void InstallCbt(IntPtr panel)
    {
        _cbtProc = (code, w, l) =>
        {
            if (code == 5 && w == panel) { Interlocked.Increment(ref CbtBlockedActivate); lock (Events) Events.Add($"{DateTime.UtcNow:HH:mm:ss.fff}Z CBT refused HCBT_ACTIVATE"); return new IntPtr(1); }
            if (code == 9 && w == panel) { Interlocked.Increment(ref CbtBlockedFocus); lock (Events) Events.Add($"{DateTime.UtcNow:HH:mm:ss.fff}Z CBT refused HCBT_SETFOCUS"); return new IntPtr(1); }
            return N.CallNextHookEx(IntPtr.Zero, code, w, l);
        };
        _cbt = N.SetWindowsHookExW(5, _cbtProc, IntPtr.Zero, N.GetCurrentThreadId());
        lock (Events) Events.Add($"{DateTime.UtcNow:HH:mm:ss.fff}Z CBT hook installed=0x{_cbt.ToInt64():X} thread={N.GetCurrentThreadId()}");
    }
    public void RemoveCbt() { if (_cbt != IntPtr.Zero) { N.UnhookWindowsHookEx(_cbt); _cbt = IntPtr.Zero; } }
    public PanelWindow(string name, bool style, bool hook, FieldMode mode)
    {
        F = new Field(mode) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Title = "P7 panel " + name;
        WindowDecorations = WindowDecorations.None;
        ShowActivated = false;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Width = 360; Height = 120;
        RootGrid.Children.Add(F);
        Template = new FuncControlTemplate<Window>((_, _) => RootGrid);
        if (style) Win32Properties.AddWindowStylesCallback(this, (s, ex) => (s, ex | (uint)N.WS_EX_NOACTIVATE));
        Win32Properties.AddWndProcHookCallback(this, (IntPtr h, uint msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == 0x0006 || msg == 0x0007 || msg == 0x0086)                // WM_ACTIVATE, WM_SETFOCUS, WM_NCACTIVATE
            {
                long wp = w.ToInt64();
                string what = msg == 0x0006 ? $"WM_ACTIVATE {wp & 0xFFFF}" : msg == 0x0007 ? "WM_SETFOCUS" : $"WM_NCACTIVATE {wp}";
                bool on = msg == 0x0007 || (wp & 0xFFFF) != 0;
                string stack = "";
                if (on)
                {
                    // Which managed call made this window active or focused: the frames above the WndProc.
                    var frames = new StackTrace(1, false).GetFrames()
                        .Select(fr => fr.GetMethod()).Where(m => m != null && m.DeclaringType != null)
                        .Select(m => $"{m!.DeclaringType!.Name}.{m.Name}")
                        .Where(n => !n.StartsWith("<", StringComparison.Ordinal)).Take(40);
                    stack = " stack: " + string.Join(" < ", frames);
                }
                lock (Events) Events.Add($"{DateTime.UtcNow:HH:mm:ss.fff}Z {what}{stack}");
                return IntPtr.Zero;
            }
            if (msg != 0x0021) return IntPtr.Zero;                              // WM_MOUSEACTIVATE
            Interlocked.Increment(ref MouseActivates);
            lock (Events) Events.Add($"{DateTime.UtcNow:HH:mm:ss.fff}Z WM_MOUSEACTIVATE -> {(hook ? "MA_NOACTIVATE (hook)" : "default")}");
            if (!hook) return IntPtr.Zero;
            handled = true;
            return new IntPtr(3);                                               // MA_NOACTIVATE
        });
    }
}

sealed class Pts { public N.POINT Caption, Close, Min, Max, Content; }

sealed class Anim
{
    public string Label = "";
    public List<(double ms, double frac, long area)> S = new();
    public long PreArea, PostArea;
    public int Intermediate;
    public bool Animated => Intermediate >= 2;
    public double FrameMs;
}

sealed class ChromeResult
{
    public string Name = "";
    public Variant V = null!;
    public string Style = "", ExStyle = "";
    public bool ScreenLive;
    public string MapSidebar = "", MapCollapsed = "", Row = "";
    public bool MapOk, MapCollapsedOk;
    public int DragDx, DragDy; public bool DragOk;
    public bool DblMax, DblRestore;
    public bool RMenu, AltMenu, KeyMenu;
    public bool Snap; public string SnapDetail = "";
    public bool MinOk, MaxOk, RestoreOk, CloseOk;
    public Anim? AMin, AMax, ARestore;
    public string MaxRect = "";
    public string Geometry = "";
    public bool ChromeAll => DragOk && DblMax && RMenu && MinOk && MaxOk && CloseOk;
}

sealed class PanelResult
{
    public string Name = "";
    public bool Style, Hook;
    public string Ex = "";
    public IntPtr FgBefore, FgAfter; public string FgBeforeWho = "", FgAfterWho = "";
    public bool Typed; public string PanelText = "", SinkText = "";
    public int MouseActivates, Presses, KeyDowns, TextInputs, Activations;
    public string Focus = "";
    public bool NoActivate => FgBefore != IntPtr.Zero && FgBefore == FgAfter && FgAfterWho == "host";
    public bool TextArrived => PanelText == Program.Secret;
}

static class Program
{
    public const string Secret = "Pw7!xyz";
    static string Out = "";
    static readonly StringBuilder LogBuf = new();
    static readonly object LogLock = new();
    static readonly List<Window> Open = new();
    static readonly uint Pid = (uint)Environment.ProcessId;
    static N.POINT Cursor0;
    static N.RECT Work;
    static bool Inconclusive; static string InconclusiveWhy = "";

    static void Log(string s)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff}Z {s}";
        lock (LogLock) { Console.WriteLine(line); LogBuf.AppendLine(line); }
    }

    static T UI<T>(Func<T> f) => Dispatcher.UIThread.Invoke(f);
    static void UI(Action a) => Dispatcher.UIThread.Invoke(a);
    static void Sleep(int ms) => Thread.Sleep(ms);

    [STAThread]
    static int Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++) if (args[i] == "--out") Out = args[++i];
        if (Out == "") { Console.Error.WriteLine("--out required"); return 4; }
        Directory.CreateDirectory(Out);
        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
        int exit = 4;
        var cts = new CancellationTokenSource();
        var driver = new Thread(() =>
        {
            try { exit = Run(); }
            catch (Exception e) { Log("CRASH " + e); exit = 4; }
            finally
            {
                try { Cleanup(); } catch (Exception e) { Log("cleanup: " + e.Message); }
                File.WriteAllText(System.IO.Path.Combine(Out, "run_log.txt"), LogBuf.ToString());
                Dispatcher.UIThread.Post(() => cts.Cancel());
            }
        }) { IsBackground = true, Name = "p7-driver" };
        driver.Start();
        Dispatcher.UIThread.MainLoop(cts.Token);
        driver.Join(5000);
        return exit;
    }

    static void Cleanup()
    {
        List<Window> ws; lock (Open) ws = Open.ToList();
        foreach (var w in ws.AsEnumerable().Reverse()) { try { UI(() => w.Close()); } catch (Exception e) { Log("close: " + e.Message); } }
        N.SetCursorPos(Cursor0.X, Cursor0.Y);
        N.GetCursorPos(out var c1);
        Log($"windows closed ({ws.Count}); cursor restored to {c1} (was {Cursor0})");
    }

    static IntPtr Hwnd(Window w) => UI(() => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);

    // ---------------- input, always guarded to our own HWNDs ----------------

    static uint Send(params N.INPUT[] i) => N.SendInput((uint)i.Length, i, Marshal.SizeOf<N.INPUT>());
    static N.INPUT M(uint flags) => new() { type = N.INPUT_MOUSE, mi = new N.MOUSEINPUT { dwFlags = flags } };
    static N.INPUT K(ushort vk, bool up) => new() { type = N.INPUT_KEYBOARD, ki = new N.KEYBDINPUT { wVk = vk, dwFlags = up ? N.KEYEVENTF_KEYUP : 0 } };
    static N.INPUT U(char c, bool up) => new() { type = N.INPUT_KEYBOARD, ki = new N.KEYBDINPUT { wScan = c, dwFlags = N.KEYEVENTF_UNICODE | (up ? N.KEYEVENTF_KEYUP : 0) } };

    static void MoveTo(N.POINT p) { N.SetCursorPos(p.X, p.Y); Send(M(N.MOUSEEVENTF_MOVE)); }

    static bool Guard(N.POINT p, IntPtr h, string what)
    {
        var r = N.Root(p);
        if (r == h) return true;
        Log($"GUARD refused {what}: point {p} root 0x{r.ToInt64():X} is not our HWND 0x{h.ToInt64():X}; no input sent");
        return false;
    }

    static bool Click(N.POINT p, IntPtr h, string what, bool right = false, int count = 1)
    {
        if (!Guard(p, h, what)) return false;
        MoveTo(p);
        Sleep(60);
        int hit = N.NcHit(h, p);
        if (!Guard(p, h, what)) return false;
        var seq = new List<N.INPUT>();
        for (int i = 0; i < count; i++)
        {
            seq.Add(M(right ? N.MOUSEEVENTF_RIGHTDOWN : N.MOUSEEVENTF_LEFTDOWN));
            seq.Add(M(right ? N.MOUSEEVENTF_RIGHTUP : N.MOUSEEVENTF_LEFTUP));
        }
        uint sent = Send(seq.ToArray());
        Log($"INPUT {what}: {(right ? "right" : "left")} x{count} at {p} sent={sent} (WM_NCHITTEST there just before = {N.HitName(hit)})");
        return sent == seq.Count;
    }

    static bool OursForeground(out IntPtr fg) { fg = N.GetForegroundWindow(); return fg != IntPtr.Zero && N.PidOf(fg) == Pid; }

    static bool Keys(string what, params N.INPUT[] seq)
    {
        if (!OursForeground(out var fg))
        {
            Log($"GUARD refused keys {what}: foreground 0x{fg.ToInt64():X} is not ours; no keys sent");
            return false;
        }
        uint sent = Send(seq);
        Log($"INPUT keys {what}: foreground 0x{fg.ToInt64():X} ({N.ClassOf(fg)}) sent={sent}/{seq.Length}");
        return sent == seq.Length;
    }

    static bool Foreground(IntPtr h, N.POINT clickAt, string label)
    {
        N.SetForegroundWindow(h);
        Sleep(300);
        if (N.GetForegroundWindow() == h) { Log($"{label}: foreground via SetForegroundWindow"); return true; }
        Click(clickAt, h, label + " (SetForegroundWindow refused)");
        Sleep(400);
        bool ok = N.GetForegroundWindow() == h;
        Log($"{label}: foreground via click on own window = {ok}");
        return ok;
    }

    // ---------------- capture ----------------

    static string SavePng(string name, byte[] bgra, int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var gh = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            using var sk = SKImage.FromPixels(info, gh.AddrOfPinnedObject(), w * 4);
            using var data = sk.Encode(SKEncodedImageFormat.Png, 100);
            var png = data.ToArray();
            string file = name + ".png";
            if (png.Length > 950_000)
            {
                using var bmp = SKBitmap.FromImage(sk);
                using var half = bmp.Resize(new SKImageInfo(w / 2, h / 2, SKColorType.Bgra8888, SKAlphaType.Opaque), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                using var hi = SKImage.FromBitmap(half);
                using var hd = hi.Encode(SKEncodedImageFormat.Png, 100);
                png = hd.ToArray(); file = name + "_half.png";
            }
            File.WriteAllBytes(System.IO.Path.Combine(Out, file), png);
            return $"{file} ({png.Length} B)";
        }
        finally { gh.Free(); }
    }

    static byte[] Grab(string? name, N.RECT r)
    {
        r = Clamp(r);
        var b = N.Capture(r, r.W, r.H);
        if (name != null) Log($"CAPTURE {name} rect={r} -> {SavePng(name, b, r.W, r.H)}");
        return b;
    }

    static N.RECT Clamp(N.RECT r)
    {
        var m = MonitorRect;
        return new N.RECT { L = Math.Max(r.L, m.L), T = Math.Max(r.T, m.T), R = Math.Min(r.R, m.R), B = Math.Min(r.B, m.B) };
    }

    static N.RECT MonitorRect;

    static bool IsMagenta(byte[] b, int o) => b[o + 2] > 235 && b[o + 1] < 25 && b[o] > 235;

    static double MagentaFraction(byte[] b)
    {
        long n = 0; for (int o = 0; o < b.Length; o += 4) if (IsMagenta(b, o)) n++;
        return n / (double)(b.Length / 4);
    }

    static (double frac, long area) SampleMonitor(out byte[] buf, out int w, out int h)
    {
        w = MonitorRect.W / 4; h = MonitorRect.H / 4;
        buf = N.Capture(MonitorRect, w, h);
        long n = 0; int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (IsMagenta(buf, (y * w + x) * 4)) { n++; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        long area = maxX < 0 ? 0 : (long)(maxX - minX + 1) * (maxY - minY + 1);
        return (n / (double)(w * h), area);
    }

    /// Clicks, then samples the monitor (1/4 scale) for 800 ms and counts frames whose magenta bounding box lies strictly
    /// between the start and end states. Two or more such frames = the transition was animated.
    static Anim Animate(string label, Action act)
    {
        var a = new Anim { Label = label };
        var pre = SampleMonitor(out _, out _, out _);
        a.PreArea = pre.area;
        var sw = Stopwatch.StartNew();
        act();
        var frames = new List<(double, double, long, byte[])>();
        int w = 0, h = 0;
        while (sw.ElapsedMilliseconds < 800)
        {
            var s = SampleMonitor(out var buf, out w, out h);
            frames.Add((sw.Elapsed.TotalMilliseconds, s.frac, s.area, buf));
        }
        a.PostArea = frames[^1].Item3;
        a.FrameMs = frames.Count > 1 ? (frames[^1].Item1 - frames[0].Item1) / (frames.Count - 1) : 0;
        long lo = Math.Min(a.PreArea, a.PostArea), hi = Math.Max(a.PreArea, a.PostArea), margin = (long)((hi - lo) * 0.08);
        var mids = frames.Where(f => f.Item3 > lo + margin && f.Item3 < hi - margin).ToList();
        a.Intermediate = mids.Select(f => f.Item3).Distinct().Count();
        foreach (var f in frames) a.S.Add((f.Item1, f.Item2, f.Item3));
        Log($"ANIM {label}: frames={frames.Count} meanFrameMs={a.FrameMs:F1} preArea={a.PreArea} postArea={a.PostArea} distinctIntermediateAreas={a.Intermediate} => animated={a.Animated}");
        Log($"ANIM {label} series: " + string.Join(" ", frames.Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Item1:F0}ms:{f.Item3}"))));
        if (mids.Count > 0)
        {
            long target = (lo + hi) / 2;
            var best = mids.OrderBy(f => Math.Abs(f.Item3 - target)).First();
            Log($"ANIM {label}: mid-transition frame at {best.Item1:F0} ms -> {SavePng("anim_" + label + "_mid_mask", Mask(best.Item4), w, h)}");
        }
        return a;
    }

    /// Foreign top-level windows above ours in z-order that intersect the zone (class/process only, never titles).
    static List<string> ForeignAbove(IntPtr ours, N.RECT zone)
    {
        var list = new List<string>();
        bool reached = false;
        N.EnumWindows((h, _) =>
        {
            if (h == ours) { reached = true; return false; }
            if (!N.IsWindowVisible(h) || N.Cloaked(h)) return true;
            uint pid = N.PidOf(h);
            if (pid == Pid) return true;
            N.GetWindowRect(h, out var r);
            if (r.W > 0 && r.H > 0 && r.Intersects(zone)) list.Add($"0x{h.ToInt64():X} {N.ClassOf(h)}/{ProcName(pid)} {r}");
            return true;
        }, IntPtr.Zero);
        _ = reached;
        return list;
    }

    static double Changed(byte[] a, byte[] b)
    {
        long d = 0;
        for (int o = 0; o < a.Length; o += 4) if (Math.Abs(a[o] - b[o]) + Math.Abs(a[o + 1] - b[o + 1]) + Math.Abs(a[o + 2] - b[o + 2]) > 48) d++;
        return d / (double)(a.Length / 4);
    }

    static byte[] Mask(byte[] b)
    {
        var m = new byte[b.Length];
        for (int o = 0; o < b.Length; o += 4) { byte v = IsMagenta(b, o) ? (byte)255 : (byte)0; m[o] = v; m[o + 1] = v; m[o + 2] = v; m[o + 3] = 255; }
        return m;
    }

    // ---------------- window enumeration ----------------

    static List<(IntPtr h, string cls, uint pid, N.RECT r)> TopWindows(bool foreignOnly)
    {
        var list = new List<(IntPtr, string, uint, N.RECT)>();
        N.EnumWindows((h, _) =>
        {
            if (!N.IsWindowVisible(h) || N.Cloaked(h)) return true;
            uint pid = N.PidOf(h);
            if (foreignOnly && pid == Pid) return true;
            N.GetWindowRect(h, out var r);
            if (r.W <= 0 || r.H <= 0) return true;
            list.Add((h, N.ClassOf(h), pid, r));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static string ProcName(uint pid) { try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; } }

    static (IntPtr h, N.RECT r) FindOurMenu()
    {
        foreach (var w in TopWindows(false)) if (w.cls == "#32768" && w.pid == Pid) return (w.h, w.r);
        return (IntPtr.Zero, default);
    }

    // ---------------- geometry ----------------

    static N.POINT Scr(Visual v, Point p) => UI(() => { var s = v.PointToScreen(p); return new N.POINT { X = s.X, Y = s.Y }; });
    static N.POINT Center(Control c) => UI(() => { var s = c.PointToScreen(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2)); return new N.POINT { X = s.X, Y = s.Y }; });

    static List<(int code, int x0, int x1)> ScanRow(IntPtr h, int y, N.RECT f)
    {
        var seg = new List<(int, int, int)>();
        int cur = int.MinValue, start = 0;
        for (int x = f.L; x < f.R; x += 2)
        {
            int c = N.NcHit(h, new N.POINT { X = x, Y = y });
            if (c != cur) { if (cur != int.MinValue) seg.Add((cur, start, x - 2)); cur = c; start = x; }
        }
        seg.Add((cur, start, f.R - 1));
        return seg;
    }

    static string RowText(List<(int code, int x0, int x1)> s) => string.Join(" ", s.Select(t => $"{N.HitName(t.code)}[{t.x0}..{t.x1}]"));

    static Pts Points(ChromeWindow w, IntPtr h)
    {
        if (w.V.Extend)
            return new Pts
            {
                Caption = Scr(w.TitleBar, new Point(300, 22)),
                Close = Center(w.CloseB), Min = Center(w.MinB), Max = Center(w.MaxB),
                Content = Scr(w.RootGrid, new Point(500, 360)),
            };
        // OS-drawn caption: the buttons DWM actually draws (DWMWA_CAPTION_BUTTON_BOUNDS, window-relative), min | max | close.
        // Run 2 showed that a WM_NCHITTEST scan returns DefWindowProc's classic, narrower button layout, which is not
        // what input is hit-tested against: a click at the scanned min centre arrived as HTMAXBUTTON.
        var f = N.Frame(h);
        N.GetWindowRect(h, out var wr);
        if (N.DwmGetWindowAttribute(h, 5, out var cb, Marshal.SizeOf<N.RECT>()) != 0) throw new InvalidOperationException("DWMWA_CAPTION_BUTTON_BOUNDS failed");
        int bw = cb.W / 3, y = wr.T + cb.T + cb.H / 2;
        N.POINT B(int i) => new() { X = wr.L + cb.L + bw * i + bw / 2, Y = y };
        return new Pts { Caption = new N.POINT { X = f.L + f.W / 2, Y = y }, Min = B(0), Max = B(1), Close = B(2), Content = Scr(w.RootGrid, new Point(500, 360)) };
    }

    static string Desc(long st) =>
        $"0x{st:X8} CAPTION={(st & (N.WS_BORDER | N.WS_DLGFRAME)) == (N.WS_BORDER | N.WS_DLGFRAME)} SYSMENU={(st & N.WS_SYSMENU) != 0} THICKFRAME={(st & N.WS_THICKFRAME) != 0} " +
        $"MINBOX={(st & N.WS_MINIMIZEBOX) != 0} MAXBOX={(st & N.WS_MAXIMIZEBOX) != 0} POPUP={(st & N.WS_POPUP) != 0}";

    // ---------------- the chrome window run ----------------

    static int BaseX, BaseY; const int BaseW = 1000, BaseH = 640;

    static void PlaceBase(IntPtr h)
    {
        N.SetWindowPos(h, N.HWND_TOPMOST, BaseX, BaseY, BaseW, BaseH, N.SWP_SHOWWINDOW);
        N.SetWindowPos(h, N.HWND_NOTOPMOST, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_SHOWWINDOW);
    }

    static void Msgs(ChromeWindow w, string step)
    {
        var m = w.TakeMsgs();
        Log($"MSGS {w.V.Name} {step}: " + (m.Length == 0 ? "(none)" : string.Join(" | ", m)));
    }

    static ChromeResult RunChrome(Variant v, bool first)
    {
        var r = new ChromeResult { Name = v.Name, V = v };
        Log($"=== VARIANT {v.Name}: WindowDecorations={v.Deco} ExtendClientAreaToDecorationsHint={v.Extend} TitleBarHeightHint={(v.Extend ? ChromeWindow.TitlebarHeight : -1)} SetNonClientHitTestResult declared={v.Declare}");
        var w = UI(() => { var x = new ChromeWindow(v, false); x.Show(); return x; });
        lock (Open) Open.Add(w);
        Sleep(1000);
        var h = Hwnd(w);
        PlaceBase(h);
        Sleep(900);
        var p = Points(w, h);
        Foreground(h, p.Content, v.Name + " focus");
        Sleep(400);
        w.TakeMsgs();

        var f = N.Frame(h);
        N.GetWindowRect(h, out var wr);
        uint dpi = N.GetDpiForWindow(h);
        double scale = dpi / 96.0;
        r.Style = Desc(N.Style(h)); r.ExStyle = $"0x{N.ExStyle(h):X8}";
        var info = UI(() => $"IsExtendedIntoWindowDecorations={w.IsExtendedIntoWindowDecorations} WindowDecorationMargin={w.WindowDecorationMargin} ClientSize={w.ClientSize} WindowDecorations={w.WindowDecorations}");
        Log($"{v.Name}: hwnd=0x{h.ToInt64():X} dpi={dpi} GetWindowRect={wr} frame={f} style={r.Style} ex={r.ExStyle} {info}");

        // The screen must be live: the content interior must read as our magenta.
        var interior = new N.RECT { L = f.L + 60, T = f.T + (int)(90 * scale), R = f.R - 60, B = f.B - 40 };
        double mf = MagentaFraction(Grab(null, interior));
        r.ScreenLive = mf > 0.9;
        Log($"{v.Name}: SCREEN content interior {interior} magentaFraction={mf:F4} live={r.ScreenLive}");
        if (!r.ScreenLive && first)
        {
            Grab("inconclusive_screen", f);
            Inconclusive = true; InconclusiveWhy = $"first window's interior read magentaFraction={mf:F4}; screen not live (locked, off, or covered)";
            UI(() => w.Close());
            return r;
        }
        Grab(v.Name + "_titlebar", new N.RECT { L = f.L, T = f.T, R = f.R, B = f.T + (int)(90 * scale) });

        // Geometry: where the app-drawn buttons actually landed, in physical px and in DIPs from the client origin.
        if (v.Extend)
        {
            var origin = Scr(w.RootGrid, new Point(0, 0));
            var g = new StringBuilder($"client origin {origin} scale {scale:F2}; ");
            foreach (var (nm, e) in new[] { ("close", w.CloseB), ("min", w.MinB), ("max", w.MaxB) })
            {
                var tl = Scr(e, new Point(0, 0)); var br = UI(() => { var s = e.PointToScreen(new Point(e.Bounds.Width, e.Bounds.Height)); return new N.POINT { X = s.X, Y = s.Y }; });
                g.Append(string.Create(CultureInfo.InvariantCulture, $"{nm} phys [{tl.X},{tl.Y}]-[{br.X},{br.Y}] dip left={(tl.X - origin.X) / scale:F1} top={(tl.Y - origin.Y) / scale:F1} size={(br.X - tl.X) / scale:F1}; "));
            }
            var tb = Scr(w.TitleBar, new Point(0, 0)); var tb2 = UI(() => { var s = w.TitleBar.PointToScreen(new Point(0, w.TitleBar.Bounds.Height)); return new N.POINT { X = s.X, Y = s.Y }; });
            g.Append(string.Create(CultureInfo.InvariantCulture, $"titlebar height dip={(tb2.Y - tb.Y) / scale:F1}"));
            r.Geometry = g.ToString();
            Log($"{v.Name}: GEOMETRY {r.Geometry}");
        }

        // 1. WM_NCHITTEST map, sent to our own HWND (no input).
        string Map(Pts q, out bool ok)
        {
            int c = N.NcHit(h, q.Caption), cl = N.NcHit(h, q.Close), mi = N.NcHit(h, q.Min), ma = N.NcHit(h, q.Max), ct = N.NcHit(h, q.Content);
            ok = c == 2 && cl == 20 && mi == 8 && ma == 9 && ct == 1;
            return $"caption{q.Caption}={N.HitName(c)} close{q.Close}={N.HitName(cl)} min{q.Min}={N.HitName(mi)} max{q.Max}={N.HitName(ma)} content{q.Content}={N.HitName(ct)}";
        }
        r.MapSidebar = Map(p, out r.MapOk);
        Log($"{v.Name}: NCHITTEST sidebar placement (18,16): {r.MapSidebar} => ok={r.MapOk}");
        var row = ScanRow(h, p.Max.Y, f);
        r.Row = RowText(row);
        Log($"{v.Name}: NCHITTEST row y={p.Max.Y}: {r.Row}");
        if (v.Extend)
        {
            UI(() => w.Place(true)); Sleep(400);
            var pc = Points(w, h);
            r.MapCollapsed = Map(pc, out r.MapCollapsedOk);
            Log($"{v.Name}: NCHITTEST collapsed placement (14,21): {r.MapCollapsed} => ok={r.MapCollapsedOk}");
            UI(() => w.Place(false)); Sleep(400);
        }

        // 2. Drag from the caption region.
        {
            N.GetWindowRect(h, out var before);
            if (Guard(p.Caption, h, "drag start"))
            {
                MoveTo(p.Caption); Sleep(80);
                Send(M(N.MOUSEEVENTF_LEFTDOWN)); Sleep(120);
                int dx = 150, dy = 90;
                for (int k = 1; k <= 12; k++) { MoveTo(new N.POINT { X = p.Caption.X + dx * k / 12, Y = p.Caption.Y + dy * k / 12 }); Sleep(25); }
                Sleep(100);
                Send(M(N.MOUSEEVENTF_LEFTUP)); Sleep(400);
                N.GetWindowRect(h, out var after);
                r.DragDx = after.L - before.L; r.DragDy = after.T - before.T;
                r.DragOk = Math.Abs(r.DragDx - dx) <= 4 && Math.Abs(r.DragDy - dy) <= 4;
                Log($"{v.Name}: DRAG from {p.Caption} by (+{dx},+{dy}): window moved ({r.DragDx},{r.DragDy}) before={before} after={after} => ok={r.DragOk}");
            }
            Msgs(w, "drag");
            PlaceBase(h); Sleep(500);
        }

        // 3. Double-click the caption region: maximise, then again: restore.
        {
            Foreground(h, p.Content, v.Name + " refocus");
            Click(p.Caption, h, "double-click caption", count: 2); Sleep(900);
            r.DblMax = N.IsZoomed(h);
            Log($"{v.Name}: DOUBLE-CLICK caption => IsZoomed={r.DblMax}");
            Msgs(w, "dblclick-max");
            if (r.DblMax)
            {
                var pm = Points(w, h);
                Click(pm.Caption, h, "double-click caption (maximised)", count: 2); Sleep(900);
                r.DblRestore = !N.IsZoomed(h);
                Log($"{v.Name}: DOUBLE-CLICK caption while maximised => restored={r.DblRestore}");
                Msgs(w, "dblclick-restore");
                if (N.IsZoomed(h)) { N.ShowWindow(h, N.SW_RESTORE); Sleep(700); }
            }
            PlaceBase(h); Sleep(500);
        }

        // 4. System menu: right-click on the caption region, then Alt+Space.
        {
            Foreground(h, p.Content, v.Name + " refocus");
            Click(p.Caption, h, "right-click caption", right: true); Sleep(800);
            var (mh, mr) = FindOurMenu();
            r.RMenu = mh != IntPtr.Zero;
            Log($"{v.Name}: RIGHT-CLICK caption => our #32768 menu visible={r.RMenu} {(r.RMenu ? mr.ToString() : "")}");
            if (r.RMenu)
            {
                Grab(v.Name + "_sysmenu_rightclick", f);
                Keys("Escape (close system menu)", K(0x1B, false), K(0x1B, true)); Sleep(400);
                if (FindOurMenu().h != IntPtr.Zero) { Keys("Escape again", K(0x1B, false), K(0x1B, true)); Sleep(400); }
            }
            Msgs(w, "rightclick");
            Foreground(h, p.Content, v.Name + " refocus");
            Sleep(200);
            Keys("Alt+Space", K(0x12, false), K(0x20, false), K(0x20, true), K(0x12, true)); Sleep(800);
            (mh, mr) = FindOurMenu();
            r.AltMenu = mh != IntPtr.Zero;
            Log($"{v.Name}: ALT+SPACE => our #32768 menu visible={r.AltMenu} {(r.AltMenu ? mr.ToString() : "")}");
            if (r.AltMenu)
            {
                Grab(v.Name + "_sysmenu_altspace", f);
                Keys("Escape (close system menu)", K(0x1B, false), K(0x1B, true)); Sleep(400);
                if (FindOurMenu().h != IntPtr.Zero) { Keys("Escape again", K(0x1B, false), K(0x1B, true)); Sleep(400); }
            }
            Msgs(w, "altspace");
            // Run 2: the Space of an injected Alt+Space never reached any of our windows (only WM_SYSKEYDOWN VK_MENU was
            // seen, on the OS-framed control too), so something on this desktop takes Alt+Space system-wide. Substitute:
            // post what DefWindowProc would generate for it, WM_SYSCOMMAND SC_KEYMENU with ' ', to our own window.
            Foreground(h, p.Content, v.Name + " refocus");
            N.PostMessageW(h, 0x0112, new IntPtr(0xF100), new IntPtr(0x20)); Sleep(800);
            (mh, mr) = FindOurMenu();
            r.KeyMenu = mh != IntPtr.Zero;
            Log($"{v.Name}: SC_KEYMENU ' ' (keyboard window-menu path) => our #32768 menu visible={r.KeyMenu} {(r.KeyMenu ? mr.ToString() : "")}");
            if (r.KeyMenu)
            {
                Grab(v.Name + "_sysmenu_keyboard", f);
                Keys("Escape (close system menu)", K(0x1B, false), K(0x1B, true)); Sleep(400);
                if (FindOurMenu().h != IntPtr.Zero) { Keys("Escape again", K(0x1B, false), K(0x1B, true)); Sleep(400); }
            }
            Msgs(w, "sc_keymenu");
        }

        // 5. App-drawn buttons: minimise (with animation sampling), then maximise and restore.
        {
            Foreground(h, p.Content, v.Name + " refocus");
            p = Points(w, h);
            r.AMin = Animate(v.Name + "_minimize", () => Click(p.Min, h, "min button"));
            r.MinOk = N.IsIconic(h);
            Log($"{v.Name}: MIN button => IsIconic={r.MinOk}");
            Msgs(w, "min");
            if (r.MinOk) { N.ShowWindow(h, N.SW_RESTORE); Sleep(900); }
            if (N.IsZoomed(h)) { N.ShowWindow(h, N.SW_RESTORE); Sleep(700); }
            PlaceBase(h); Sleep(500);
            Foreground(h, p.Content, v.Name + " refocus");
            p = Points(w, h);
            r.AMax = Animate(v.Name + "_maximize", () => Click(p.Max, h, "max button"));
            r.MaxOk = N.IsZoomed(h);
            var mon = N.Monitor(h);
            r.MaxRect = $"maximised GetWindowRect={(N.GetWindowRect(h, out var mxr) ? mxr.ToString() : "?")} frame={N.Frame(h)} monitor={mon.rcMonitor} work={mon.rcWork}";
            Log($"{v.Name}: MAX button => IsZoomed={r.MaxOk}; {r.MaxRect}");
            Msgs(w, "max");
            if (r.MaxOk)
            {
                Sleep(300);
                var pm = Points(w, h);
                r.ARestore = Animate(v.Name + "_restore", () => Click(pm.Max, h, "max button (restore)"));
                r.RestoreOk = !N.IsZoomed(h);
                Log($"{v.Name}: MAX button while maximised => restored={r.RestoreOk}");
                Msgs(w, "restore");
                if (N.IsZoomed(h)) N.ShowWindow(h, N.SW_RESTORE);
            }
            if (N.IsIconic(h) || N.IsZoomed(h)) { N.ShowWindow(h, N.SW_RESTORE); Sleep(700); }
            PlaceBase(h); Sleep(600);
        }

        // 6. Snap Layouts: park in the content, then hover the maximise button for 2 s. Decided by what changed on screen
        // inside OUR window below the caption (where the flyout opens), with the cursor parked vs hovering.
        {
            Foreground(h, p.Content, v.Name + " refocus");
            p = Points(w, h);
            f = N.Frame(h);
            var zone = new N.RECT { L = f.L, T = f.T + (int)(50 * scale), R = Math.Min(f.R, p.Max.X + 420), B = Math.Min(f.B, f.T + (int)(340 * scale)) };
            if (Guard(p.Content, h, "snap pre-hover park")) MoveTo(p.Content);
            Sleep(900);
            var aboveBefore = ForeignAbove(h, zone);
            var pre = Grab(null, zone);
            if (Guard(p.Max, h, "snap hover"))
            {
                Log($"{v.Name}: NCHITTEST at hover point {p.Max} = {N.HitName(N.NcHit(h, p.Max))}");
                MoveTo(new N.POINT { X = p.Max.X - 3, Y = p.Max.Y - 2 }); Sleep(60);
                MoveTo(p.Max); Sleep(60);
                MoveTo(new N.POINT { X = p.Max.X + 1, Y = p.Max.Y }); Sleep(60);
                MoveTo(p.Max);
                Sleep(2000);
                var aboveAfter = ForeignAbove(h, zone);
                var hover = Grab(null, zone);
                Grab(v.Name + "_snap_hover", new N.RECT { L = f.L, T = f.T, R = f.R, B = Math.Min(f.B, f.T + (int)(420 * scale)) });
                double df = Changed(pre, hover);
                var newAbove = aboveAfter.Where(x => !aboveBefore.Contains(x)).ToList();
                r.Snap = df > 0.05;
                r.SnapDetail = string.Create(CultureInfo.InvariantCulture, $"zone={zone} zonePixelsChanged={df:F4} foreignWindowsAboveOursInZone before={aboveBefore.Count} after={aboveAfter.Count} new=[{string.Join("; ", newAbove)}]");
                Log($"{v.Name}: SNAP hover max {p.Max} 2000 ms: {r.SnapDetail} => flyout={r.Snap}");
                if (Guard(p.Content, h, "snap park")) MoveTo(p.Content);
                Sleep(1500);
                var post = Grab(null, zone);
                Log(string.Create(CultureInfo.InvariantCulture, $"{v.Name}: SNAP 1.5 s after parking in the content: zonePixelsChanged vs pre={Changed(pre, post):F4} (flyout dismissed if near 0)"));
            }
            Msgs(w, "snap");
        }

        // 7. Close button.
        {
            Foreground(h, p.Content, v.Name + " refocus");
            p = Points(w, h);
            Click(p.Close, h, "close button"); Sleep(1000);
            r.CloseOk = !N.IsWindow(h) || w.ClosedFlag;
            Log($"{v.Name}: CLOSE button => closed={r.CloseOk} (IsWindow={N.IsWindow(h)} ClosedEvent={w.ClosedFlag})");
            Msgs(w, "close");
        }
        if (!r.CloseOk) UI(() => w.Close());
        lock (Open) Open.Remove(w);
        Sleep(500);
        Log($"RESULT-ROW {v.Name}: map={r.MapOk} mapCollapsed={r.MapCollapsedOk} drag={r.DragOk} dblMax={r.DblMax} dblRestore={r.DblRestore} rightClickMenu={r.RMenu} altSpaceMenu={r.AltMenu} keyMenu={r.KeyMenu} " +
            $"min={r.MinOk} max={r.MaxOk} restore={r.RestoreOk} close={r.CloseOk} snapFlyout={r.Snap} animMin={r.AMin?.Animated} animMax={r.AMax?.Animated} animRestore={r.ARestore?.Animated}");
        return r;
    }

    // ---------------- raw Win32 control for the keyboard window menu ----------------

    static bool RawAltMenu, RawKeyMenu, RawRightMenu;

    /// A window whose procedure IS DefWindowProcW (no Avalonia, no managed WndProc). Separates "the desktop takes
    /// Alt+Space" and "Avalonia swallows SC_KEYMENU" from anything the probe's own chrome does.
    static void RunRawMenuControl()
    {
        Log("=== RAW control: class with lpfnWndProc = user32!DefWindowProcW, WS_OVERLAPPEDWINDOW, created on the UI thread");
        IntPtr raw = UI(() =>
        {
            var inst = N.GetModuleHandleW(null);
            var wc = new N.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<N.WNDCLASSEXW>(), lpfnWndProc = N.GetProcAddress(N.GetModuleHandleW("user32.dll"), "DefWindowProcW"),
                hInstance = inst, lpszClassName = "P7RawControl", hbrBackground = new IntPtr(6),
            };
            ushort atom = N.RegisterClassExW(ref wc);
            var h = N.CreateWindowExW(0, "P7RawControl", "P7 raw control", 0x10CF0000, BaseX, BaseY, 700, 360, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            Log($"RAW atom={atom} wndproc=0x{wc.lpfnWndProc.ToInt64():X} hwnd=0x{h.ToInt64():X} style={Desc(N.Style(h))}");
            return h;
        });
        if (raw == IntPtr.Zero) { Log("RAW control could not be created; skipped"); return; }
        try
        {
            Sleep(800);
            N.GetWindowRect(raw, out var wr);
            var content = new N.POINT { X = wr.L + wr.W / 2, Y = wr.T + wr.H / 2 };
            var caption = new N.POINT { X = wr.L + 250, Y = wr.T + 15 };
            Log($"RAW rect={wr} NCHITTEST caption{caption}={N.HitName(N.NcHit(raw, caption))} content{content}={N.HitName(N.NcHit(raw, content))}");

            void CloseMenu() { for (int i = 0; i < 2 && FindOurMenu().h != IntPtr.Zero; i++) { Keys("Escape (close raw menu)", K(0x1B, false), K(0x1B, true)); Sleep(400); } }

            Foreground(raw, content, "raw focus"); Sleep(200);
            Click(caption, raw, "raw right-click caption", right: true); Sleep(800);
            RawRightMenu = FindOurMenu().h != IntPtr.Zero;
            Log($"RAW RIGHT-CLICK caption => our #32768 menu visible={RawRightMenu}");
            CloseMenu();

            Foreground(raw, content, "raw focus"); Sleep(200);
            Keys("raw Alt+Space", K(0x12, false), K(0x20, false), K(0x20, true), K(0x12, true)); Sleep(800);
            RawAltMenu = FindOurMenu().h != IntPtr.Zero;
            Log($"RAW ALT+SPACE => our #32768 menu visible={RawAltMenu}");
            if (RawAltMenu) Grab("raw_sysmenu_altspace", wr);
            CloseMenu();

            Foreground(raw, content, "raw focus"); Sleep(200);
            N.PostMessageW(raw, 0x0112, new IntPtr(0xF100), new IntPtr(0x20)); Sleep(800);
            RawKeyMenu = FindOurMenu().h != IntPtr.Zero;
            Log($"RAW posted SC_KEYMENU ' ' => our #32768 menu visible={RawKeyMenu}");
            if (RawKeyMenu) Grab("raw_sysmenu_keyboard", wr);
            CloseMenu();
        }
        finally
        {
            UI(() => N.DestroyWindow(raw));
            Log($"RAW destroyed, IsWindow={N.IsWindow(raw)}");
        }
    }

    // ---------------- the non-activating panel ----------------

    static PanelResult RunPanel(string name, bool style, bool hook, FieldMode mode, ChromeWindow host, IntPtr hh, bool cbt = false)
    {
        var r = new PanelResult { Name = name, Style = style, Hook = hook };
        Field.ForwardTarget = null;
        Log($"=== PANEL {name}: WS_EX_NOACTIVATE via AddWindowStylesCallback={style} WM_MOUSEACTIVATE->MA_NOACTIVATE via AddWndProcHookCallback={hook} field={mode} WH_CBT activation/focus refusal={cbt}");
        var hp = Scr(host.RootGrid, new Point(500, 360));
        Foreground(hh, hp, "host focus");
        var sinkC = Center(host.Sink!);
        Click(sinkC, hh, "host sink field (give it focus)"); Sleep(300);
        UI(() => { host.Sink!.Text = ""; host.Sink.Child?.InvalidateVisual(); });
        Keys("type into host sink (baseline: host keyboard path works)", Secret.SelectMany(c => new[] { U(c, false), U(c, true) }).ToArray()); Sleep(400);
        var baseline = UI(() => host.Sink!.Text);
        Log($"PANEL {name}: host sink baseline text=\"{baseline}\" (expects \"{Secret}\")");
        UI(() => { host.Sink!.Text = ""; host.Sink.Child?.InvalidateVisual(); });

        var p = UI(() => { var x = new PanelWindow(name, style, hook, mode); x.Show(); return x; });
        lock (Open) Open.Add(p);
        Sleep(900);
        var ph = Hwnd(p);
        if (cbt) UI(() => p.InstallCbt(ph));
        var hf = N.Frame(hh);
        N.SetWindowPos(ph, N.HWND_TOPMOST, hf.L + 500, hf.T + 200, 0, 0, N.SWP_NOSIZE | N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
        Sleep(600);
        r.Ex = $"0x{N.ExStyle(ph):X8} NOACTIVATE={(N.ExStyle(ph) & N.WS_EX_NOACTIVATE) != 0}";
        Log($"PANEL {name}: hwnd=0x{ph.ToInt64():X} frame={N.Frame(ph)} ex={r.Ex} style={Desc(N.Style(ph))}");
        string Who(IntPtr f) => f == hh ? "host" : f == ph ? "panel" : N.PidOf(f) == Pid ? "ours-other" : "foreign";
        r.FgBefore = N.GetForegroundWindow(); r.FgBeforeWho = Who(r.FgBefore);
        Log($"PANEL {name}: foreground BEFORE click = 0x{r.FgBefore.ToInt64():X} ({r.FgBeforeWho})");
        var fc = Center(p.F);
        Click(fc, ph, "panel field");
        Sleep(500);
        r.FgAfter = N.GetForegroundWindow(); r.FgAfterWho = Who(r.FgAfter);
        Log($"PANEL {name}: foreground AFTER click = 0x{r.FgAfter.ToInt64():X} ({r.FgAfterWho}); Win32 GetFocus(driver thread)=0x{N.GetFocus().ToInt64():X}");
        r.Focus = UI(() => $"panel.IsActive={p.IsActive} host.IsActive={host.IsActive} panelField.IsFocused={p.F.IsFocused} panel.Focused={p.FocusManager?.GetFocusedElement()?.GetType().Name ?? "null"} host.Focused={host.FocusManager?.GetFocusedElement()?.GetType().Name ?? "null"}");
        Log($"PANEL {name}: {r.Focus}");
        r.Typed = Keys($"type \"{Secret}\" (VK_PACKET unicode)", Secret.SelectMany(c => new[] { U(c, false), U(c, true) }).ToArray());
        Sleep(700);
        (r.PanelText, r.SinkText, r.Presses, r.KeyDowns, r.TextInputs) = UI(() => (p.F.Text, host.Sink!.Text, p.F.Presses, p.F.KeyDowns, p.F.TextInputs));
        r.MouseActivates = p.MouseActivates;
        var fgEnd = N.GetForegroundWindow();
        Log($"PANEL {name}: panel field text=\"{r.PanelText}\" (presses={r.Presses} keyDowns={r.KeyDowns} textInputs={r.TextInputs}) host sink text=\"{r.SinkText}\" WM_MOUSEACTIVATE seen={r.MouseActivates} foreground at end=0x{fgEnd.ToInt64():X} ({Who(fgEnd)})");
        string[] ev; lock (p.Events) ev = p.Events.ToArray();
        foreach (var e in ev) Log($"PANEL {name} EVENT {e}");
        r.Activations = ev.Count(e => e.Contains("WM_ACTIVATE 1") || e.Contains("WM_ACTIVATE 2"));
        Grab("panel_" + name, N.Frame(ph));
        Log($"PANEL {name}: noActivate={r.NoActivate} textArrived={r.TextArrived} cbtRefusedActivate={p.CbtBlockedActivate} cbtRefusedFocus={p.CbtBlockedFocus}");
        UI(() => { p.RemoveCbt(); p.Close(); });
        lock (Open) Open.Remove(p);
        Sleep(500);
        return r;
    }

    // ---------------- main ----------------

    static int Run()
    {
        N.GetCursorPos(out Cursor0);
        Log($"P7 start; cursor0={Cursor0}; pid={Pid}");
        Log($"avalonia {typeof(Window).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Log($"os {Environment.OSVersion.VersionString}; .NET {Environment.Version}; MARROW_PORT_DATA_DIR={Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")}");
        N.SystemParametersInfoW(N.SPI_GETWORKAREA, 0, out Work, 0);
        var ai = new N.ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<N.ANIMATIONINFO>() };
        N.SpiAnim(N.SPI_GETANIMATION, (int)ai.cbSize, ref ai, 0);
        N.SpiBool(N.SPI_GETCLIENTAREAANIMATION, 0, out int caa, 0);
        Log($"primary work area {Work}; SPI_GETANIMATION iMinAnimate={ai.iMinAnimate}; SPI_GETCLIENTAREAANIMATION={caa}; double-click time={N.GetDoubleClickTime()} ms");
        BaseX = Work.L + 160; BaseY = Work.T + 140;

        var variants = new[]
        {
            new Variant("os_standard", WindowDecorations.Full, false, false),          // positive control: OS-drawn caption
            new Variant("full_declared", WindowDecorations.Full, true, true),
            new Variant("borderonly_declared", WindowDecorations.BorderOnly, true, true),
            new Variant("none_declared", WindowDecorations.None, true, true),
            new Variant("full_undeclared", WindowDecorations.Full, true, false),       // negative controls: no hit-test declarations
            new Variant("borderonly_undeclared", WindowDecorations.BorderOnly, true, false),
            new Variant("none_undeclared", WindowDecorations.None, true, false),
            new Variant("full_hooked", WindowDecorations.Full, true, true, true),        // remedy: declared + WndProc handling
            new Variant("borderonly_hooked", WindowDecorations.BorderOnly, true, true, true),
            new Variant("none_hooked", WindowDecorations.None, true, true, true),
        };
        var results = new List<ChromeResult>();
        bool first = true;
        foreach (var v in variants)
        {
            if (first)
            {
                // The monitor used for animation sampling: the one the probe windows open on.
                var tmp = UI(() => { var x = new ChromeWindow(v, false); x.Show(); return x; });
                Sleep(500);
                var th = Hwnd(tmp);
                N.SetWindowPos(th, N.HWND_TOP, BaseX, BaseY, BaseW, BaseH, N.SWP_NOACTIVATE);
                MonitorRect = N.Monitor(th).rcMonitor;
                UI(() => tmp.Close());
                Sleep(400);
                Log($"sampling monitor {MonitorRect}");
            }
            var r = RunChrome(v, first);
            first = false;
            if (Inconclusive) break;
            results.Add(r);
        }
        if (Inconclusive) { Log("RESULT: INCONCLUSIVE - " + InconclusiveWhy); return 2; }
        RunRawMenuControl();

        // Panel phase: host is an ordinary OS-framed window with a sink field.
        var hostV = new Variant("panel_host", WindowDecorations.Full, false, false);
        var host = UI(() => { var x = new ChromeWindow(hostV, true); x.Show(); return x; });
        lock (Open) Open.Add(host);
        Sleep(900);
        var hh = Hwnd(host);
        UI(() => host.AddHandler(InputElement.TextInputEvent, (object? s, TextInputEventArgs e) =>
        {
            var t = Field.ForwardTarget;
            if (t == null) return;
            t.Append(e.Text ?? "");
            e.Handled = true;
        }, RoutingStrategies.Tunnel));
        PlaceBase(hh); Sleep(700);
        var panels = new List<PanelResult>
        {
            RunPanel("noactivate", true, true, FieldMode.Focus, host, hh),          // the packet configuration
            RunPanel("control_plain", false, false, FieldMode.Focus, host, hh),     // control: neither flag
            RunPanel("style_only", true, false, FieldMode.Focus, host, hh),
            RunPanel("hook_only", false, true, FieldMode.Focus, host, hh),
            RunPanel("noactivate_nofocus", true, true, FieldMode.NoFocus, host, hh), // control for forwarding
            RunPanel("noactivate_forward", true, true, FieldMode.Forward, host, hh), // remedy: host diverts its text
            RunPanel("cbt_focus", true, true, FieldMode.Focus, host, hh, cbt: true),         // + WH_CBT refusal, field takes Avalonia focus
            RunPanel("cbt_nofocus", true, true, FieldMode.NoFocus, host, hh, cbt: true),     // control for cbt_forward: no diversion
            RunPanel("cbt_forward", true, true, FieldMode.Forward, host, hh, cbt: true),     // remedy: WH_CBT refusal + host diverts its text
        };
        UI(() => host.Close());
        lock (Open) Open.Remove(host);
        Sleep(400);

        // ---------------- verdict ----------------
        Log("=== SUMMARY");
        foreach (var r in results)
            Log($"SUMMARY {r.Name,-22} map={r.MapOk,-5} drag={r.DragOk,-5} dblMax={r.DblMax,-5} dblRestore={r.DblRestore,-5} rMenu={r.RMenu,-5} altMenu={r.AltMenu,-5} keyMenu={r.KeyMenu,-5} min={r.MinOk,-5} max={r.MaxOk,-5} restore={r.RestoreOk,-5} close={r.CloseOk,-5} " +
                $"snap={r.Snap,-5} animMin={r.AMin?.Animated,-5} animMax={r.AMax?.Animated,-5} animRestore={r.ARestore?.Animated,-5} chromeAll={r.ChromeAll}");
        foreach (var q in panels)
            Log($"SUMMARY panel {q.Name,-14} ex={q.Ex} fgBefore={q.FgBeforeWho} fgAfter={q.FgAfterWho} noActivate={q.NoActivate} panelText=\"{q.PanelText}\" sinkText=\"{q.SinkText}\" textArrived={q.TextArrived} mouseActivates={q.MouseActivates} activations={q.Activations}");

        var os = results.First(x => x.Name == "os_standard");
        bool osControlOk = os.ChromeAll;
        var declared = results.Where(x => x.V.Declare && !x.V.Hooked).ToList();
        var hooked = results.Where(x => x.V.Hooked).ToList();
        var undeclared = results.Where(x => x.V.Extend && !x.V.Declare).ToList();
        bool negativesFail = undeclared.All(x => !x.DragOk && !x.MinOk && !x.MaxOk && !x.CloseOk && !x.Snap);
        var passing = declared.Where(x => x.ChromeAll).Select(x => x.Name).ToList();
        var passingHooked = hooked.Where(x => x.ChromeAll).Select(x => x.Name).ToList();
        var np = panels.First(x => x.Name == "noactivate");
        var ctl = panels.First(x => x.Name == "control_plain");
        var fw = panels.First(x => x.Name == "noactivate_forward");
        var nf = panels.First(x => x.Name == "noactivate_nofocus");
        bool panelPass = np.NoActivate && np.TextArrived;
        bool panelControlFails = !ctl.NoActivate;
        bool forwardPass = fw.NoActivate && fw.TextArrived && fw.SinkText == "";
        bool forwardControlFails = !nf.TextArrived;
        Log($"VERDICT osStandardPositiveControl={osControlOk} undeclaredNegativeControlsFail={negativesFail} " +
            $"verbatimChromePassingDecorations=[{string.Join(",", passing)}] verbatimPanelPass={panelPass} panelControlActivates={panelControlFails}");
        Log($"VERDICT remedy: hookedChromePassingDecorations=[{string.Join(",", passingHooked)}] forwardPanelPass={forwardPass} forwardControl(nofocus)TextMissing={forwardControlFails}");
        Log($"VERDICT raw DefWindowProc control: rightClickMenu={RawRightMenu} altSpaceMenu={RawAltMenu} postedKeyMenu={RawKeyMenu}");
        var cf = panels.First(x => x.Name == "cbt_forward");
        var cn = panels.First(x => x.Name == "cbt_nofocus");
        var cfo = panels.First(x => x.Name == "cbt_focus");
        Log($"VERDICT remedy WH_CBT: cbtForwardPass={cf.NoActivate && cf.TextArrived && cf.SinkText == ""} (noActivate={cf.NoActivate} text=\"{cf.PanelText}\" sink=\"{cf.SinkText}\") " +
            $"control cbt_nofocus: noActivate={cn.NoActivate} panelTextMissing={!cn.TextArrived} sink=\"{cn.SinkText}\"; cbt_focus: noActivate={cfo.NoActivate} text=\"{cfo.PanelText}\" sink=\"{cfo.SinkText}\"");
        if (!osControlOk) { Log("RESULT: INCONCLUSIVE - the OS-framed positive control did not respond to the injected input"); return 2; }
        bool pass = passing.Count > 0 && negativesFail && panelPass && panelControlFails;
        Log("RESULT (packet configuration, verbatim): " + (pass ? "PASS" : "FAIL"));
        return pass ? 0 : 1;
    }
}
