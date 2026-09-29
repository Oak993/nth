// PORT-ONLY: DroppyCode/App/Views/Terminal/TerminalPanel.swift's LocalProcessTerminalView is SwiftTerm on macOS.
// Windows has no SwiftTerm twin in this tree, so this control supplies the same long-lived ConPTY-backed surface:
// process input/output, a selectable scrollback text grid, appearance values, focus and pseudoconsole resizing.
using System.Text;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Views.Terminal.Port;

/// <summary>SwiftTerm's <c>LocalProcessTerminalViewDelegate</c>: the four callbacks TerminalBridge implements.</summary>
public interface ITerminalProcessDelegate
{
    void SizeChanged(int newCols, int newRows);

    void SetTerminalTitle(string? title);

    void HostCurrentDirectoryUpdate(string? directory);

    void ProcessTerminated(int? exitCode);
}

/// <summary>
/// The Windows twin of SwiftTerm's <c>LocalProcessTerminalView</c>. It is deliberately one control for the lifetime of
/// a <see cref="TerminalSession"/>: the ConPTY process, scrollback and focus survive the panel's layout passes, while
/// <see cref="TerminalHostContainer"/> only changes its frame during a window resize.
/// </summary>
public sealed class TerminalHostView : Border
{
    // TerminalStore.swift:21 creates a 640x240 view before the host immediately gives it its container frame.
    private const int MaximumScrollbackCharacters = 1_000_000;

    private readonly ProcessLauncher launcher = new();
    private readonly SelectableTextBlock scrollbackText;
    private readonly object scrollbackGate = new();
    private readonly StringBuilder scrollback = new();
    private ProcessPipe? input;
    private ProcessPipe? output;
    private SwiftFont? font;
    private SwiftColor? nativeBackgroundColor;
    private SwiftColor? nativeForegroundColor;
    private SwiftColor? caretColor;
    private SwiftColor? selectedTextBackgroundColor;
    private PseudoConsoleSize lastPseudoConsoleSize;
    private bool outputUpdateQueued;

    public TerminalHostView()
    {
        Width = 640; // TerminalStore.swift:21
        Height = 240; // TerminalStore.swift:21
        Focusable = true;
        ClipToBounds = true;

        scrollbackText = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Focusable = true,
        };
        Child = scrollbackText;

        KeyDown += OnKeyDown;
        TextInput += OnTextInput;
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                Focus();
        };
    }

    /// <summary>:22 <c>view.font = TerminalFont.preferred()</c>.</summary>
    public SwiftFont? Font
    {
        get => font;
        set
        {
            font = value;
            if (value is not null) value.Apply(scrollbackText);
        }
    }

    /// <summary>:23 <c>view.optionAsMetaKey = true</c>; the port's Alt-as-Meta equivalent.</summary>
    public bool OptionAsMetaKey { get; set; }

    /// <summary>:50-51.</summary>
    public SwiftColor? NativeBackgroundColor
    {
        get => nativeBackgroundColor;
        set
        {
            nativeBackgroundColor = value;
            Background = value?.Brush(this);
        }
    }

    /// <summary>:50-51.</summary>
    public SwiftColor? NativeForegroundColor
    {
        get => nativeForegroundColor;
        set
        {
            nativeForegroundColor = value;
            scrollbackText.Foreground = value?.Brush(scrollbackText);
        }
    }

    /// <summary>:55.</summary>
    public SwiftColor? CaretColor
    {
        get => caretColor;
        set => caretColor = value;
    }

    /// <summary>:56; also drives the selectable scrollback highlight.</summary>
    public SwiftColor? SelectedTextBackgroundColor
    {
        get => selectedTextBackgroundColor;
        set
        {
            selectedTextBackgroundColor = value;
            scrollbackText.SelectionBrush = value?.Brush(scrollbackText);
        }
    }

    /// <summary>:26 <c>view.processDelegate = bridge</c>.</summary>
    public ITerminalProcessDelegate? ProcessDelegate { get; set; }

    /// <summary>Swift <c>process.isRunning</c>, read through the view as SwiftTerm exposes it.</summary>
    public bool IsRunning => launcher.IsRunning;

    /// <summary>The real OS process id of the launched shell; 0 before <see cref="StartProcess"/>.</summary>
    public int ProcessId => launcher.ProcessIdentifier;

    /// <summary>The visible, ANSI-stripped scrollback used by the Windows text surface and its tests.</summary>
    public string ScrollbackText
    {
        get
        {
            lock (scrollbackGate) return scrollback.ToString();
        }
    }

    /// <summary>The initial Swift frame from TerminalStore.swift:21, before its host supplies the live bounds.</summary>
    internal Size InitialFrame { get; } = new(640, 240);

    /// <summary>The last pseudoconsole cell size sent to ConPTY.</summary>
    internal PseudoConsoleSize LastPseudoConsoleSize => lastPseudoConsoleSize;

    /// <summary>How many real ConPTY resize calls this view has made.</summary>
    internal int ResizeCount { get; private set; }

    /// <summary>
    /// :40 <c>view.startProcess(executable:args:environment:execName:currentDirectory:)</c>. DEVIATION OD-0008 C5 /
    /// PR-TERM-042: <paramref name="execName"/> is accepted for citation parity but Windows cannot set argv[0]
    /// independently of the executable path.
    /// </summary>
    public void StartProcess(
        string executable,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> environment,
        string execName,
        string currentDirectory)
    {
        _ = execName; // PR-TERM-042: dropped on Windows.
        launcher.ExecutableURL = executable;
        launcher.Arguments = args;
        launcher.Environment = environment;
        launcher.CurrentDirectoryURL = currentDirectory;
        // SwiftTerm 1.20.0 AppleTerminalView.swift:329-345 derives the initial
        // grid from the 640x240 frame and the selected font before spawning.
        lastPseudoConsoleSize = MeasureGrid(InitialFrame);
        launcher.PseudoConsole = lastPseudoConsoleSize;
        input = new ProcessPipe();
        output = new ProcessPipe();
        launcher.StandardInput = input;
        launcher.StandardOutput = output;
        launcher.UsesJobObject = true;
        output.ReadabilityHandler = OnOutput;
        launcher.TerminationHandler = host => ProcessDelegate?.ProcessTerminated(host.TerminationStatus);
        launcher.Run();
    }

    /// <summary>:61 <c>view.terminate()</c>: the graceful step only, no escalation.</summary>
    public void Terminate()
    {
        // TerminalSession.Terminate already marks the session stopped. Disarm callbacks before the asynchronous
        // process exit, since its former UI dispatcher may have been disposed when the view is torn down.
        ProcessDelegate = null;
        if (output is not null) output.ReadabilityHandler = null;
        launcher.Terminate();
    }

    /// <summary>Moves the existing surface to a new frame without rebuilding its process or scrollback.</summary>
    internal void ResizeTo(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        if (Font is null) return;
        var next = MeasureGrid(size);
        if (next == lastPseudoConsoleSize) return;
        lastPseudoConsoleSize = next;
        ResizeCount++;
        try { launcher.ResizePseudoConsole(next); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        ProcessDelegate?.SizeChanged(next.Columns, next.Rows);
    }

    private PseudoConsoleSize MeasureGrid(Size size)
    {
        var resolved = Font!.Resolve();
        // SwiftTerm 1.20.0 AppleTerminalView.swift:410-444 measures the W advance and
        // the font's line metrics, then snaps both dimensions to the backing pixel grid.
        // FormattedText uses the resolved Avalonia face's real glyph and line metrics.
        var typeface = new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight);
        var measured = new FormattedText("W", CultureInfo.InvariantCulture,
            Avalonia.Media.FlowDirection.LeftToRight, typeface, resolved.FontSize, Brushes.White);
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var cellWidth = Math.Max(1, Math.Round(measured.Width * scale) / scale);
        var cellHeight = Math.Clamp(Math.Ceiling(measured.Height * scale) / scale, 1, 8192);
        return new PseudoConsoleSize(
            Math.Clamp((int)Math.Floor(size.Width / cellWidth), 1, short.MaxValue),
            Math.Clamp((int)Math.Floor(size.Height / cellHeight), 1, short.MaxValue));
    }

    /// <summary>Escape is handled by the parent host; every other special key reaches the ConPTY input.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) return;

        if (IsTextKey(e.Key))
        {
            if (OptionAsMetaKey && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && TryKeyByte(e.Key, e.KeyModifiers, out var meta))
            {
                SendBytes([0x1B, meta]);
                e.Handled = true;
            }
            return; // TextInput carries the actual Unicode character, including IME text.
        }

        if (KeyBytes(e.Key, e.KeyModifiers) is { Length: > 0 } bytes)
        {
            SendBytes(bytes);
            e.Handled = true;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        SendBytes(Encoding.UTF8.GetBytes(e.Text));
        e.Handled = true;
    }

    internal void SendBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        try { input?.Write(bytes); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void OnOutput(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        var text = StripTerminalControlBytes(Encoding.UTF8.GetString(bytes));
        if (text.Length == 0) return;
        lock (scrollbackGate)
        {
            scrollback.Append(text);
            if (scrollback.Length > MaximumScrollbackCharacters)
                scrollback.Remove(0, scrollback.Length - MaximumScrollbackCharacters);
            if (outputUpdateQueued) return;
            outputUpdateQueued = true;
        }
        Dispatcher.UIThread.Post(ApplyOutput, DispatcherPriority.Background);
    }

    private void ApplyOutput()
    {
        string text;
        lock (scrollbackGate)
        {
            outputUpdateQueued = false;
            text = scrollback.ToString();
        }
        scrollbackText.Text = text;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static string StripTerminalControlBytes(string value)
    {
        var result = new StringBuilder(value.Length);
        var escape = false;
        var csi = false;
        foreach (var character in value)
        {
            if (csi)
            {
                if (character is >= '@' and <= '~') csi = false;
                continue;
            }
            if (escape)
            {
                if (character == '[') { csi = true; escape = false; }
                else if (character is >= '@' and <= '~') escape = false;
                continue;
            }
            if (character == '\x1B') { escape = true; continue; }
            if (character == '\a') continue;
            if (character == '\b')
            {
                if (result.Length > 0) result.Length--;
                continue;
            }
            if (character == '\r') continue;
            result.Append(character);
        }
        return result.ToString();
    }

    private static bool IsTextKey(Key key) =>
        key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9
            or Key.Space or Key.OemComma or Key.OemPeriod or Key.OemMinus or Key.OemPlus;

    private static bool TryKeyByte(Key key, KeyModifiers modifiers, out byte value)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            value = (byte)('a' + (key - Key.A));
            if (modifiers.HasFlag(KeyModifiers.Shift)) value = (byte)char.ToUpperInvariant((char)value);
            return true;
        }
        value = 0;
        return false;
    }

    private static byte[]? KeyBytes(Key key, KeyModifiers modifiers) => key switch
    {
        Key.Enter => [0x0D],
        Key.Tab => [0x09],
        Key.Back => [0x7F],
        Key.Space => [0x20],
        Key.Left => Encoding.ASCII.GetBytes("\x1B[D"),
        Key.Right => Encoding.ASCII.GetBytes("\x1B[C"),
        Key.Up => Encoding.ASCII.GetBytes("\x1B[A"),
        Key.Down => Encoding.ASCII.GetBytes("\x1B[B"),
        Key.Home => Encoding.ASCII.GetBytes("\x1B[H"),
        Key.End => Encoding.ASCII.GetBytes("\x1B[F"),
        Key.Delete => Encoding.ASCII.GetBytes("\x1B[3~"),
        Key.Insert => Encoding.ASCII.GetBytes("\x1B[2~"),
        Key.PageUp => Encoding.ASCII.GetBytes("\x1B[5~"),
        Key.PageDown => Encoding.ASCII.GetBytes("\x1B[6~"),
        _ => null,
    };
}

/// <summary>
/// DEVIATION OD-0008 C5. DroppyCode/App/Views/Terminal/TerminalStore.swift:28's LoginEnvironment.userShell() is
/// replaced by PowerShell 7 if installed, otherwise Windows PowerShell (OD-0008 C5 / ED-222).
/// </summary>
public static class WindowsShell
{
    public static string Resolve() => Resolve(LoginEnvironment.Current);

    // CreateProcessW does not search PATH for a bare lpApplicationName; resolve the preferred shell through PATH and
    // finally use the absolute System32 WindowsPowerShell path.
    internal static string Resolve(IReadOnlyDictionary<string, string>? environment) =>
        LoginEnvironment.Which("pwsh.exe", environment)
        ?? LoginEnvironment.Which("powershell.exe", environment)
        ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
}
