// PORT-ONLY: DroppyCode/App/Views/Terminal/TerminalPanel.swift's LocalProcessTerminalView is SwiftTerm on macOS.
// Windows has no SwiftTerm twin in this tree, so this control supplies the same long-lived ConPTY-backed surface:
// process input/output, a selectable cell grid, appearance values, focus and pseudoconsole resizing.
using System.Text;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
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
    private readonly ProcessLauncher launcher = new();
    private readonly TerminalSurface scrollbackText;
    private readonly object scrollbackGate = new();
    private readonly TerminalScreenBuffer screen = new(80, 25);
    private readonly Decoder outputDecoder = Encoding.UTF8.GetDecoder();
    private ProcessPipe? input;
    private ProcessPipe? output;
    private SwiftFont? font;
    private SwiftColor? nativeBackgroundColor;
    private SwiftColor? nativeForegroundColor;
    private SwiftColor? caretColor;
    private SwiftColor? selectedTextBackgroundColor;
    private PseudoConsoleSize lastPseudoConsoleSize;
    private bool outputUpdateQueued;
    private int inputGeneration;
    private Task pendingPaste = Task.CompletedTask;

    public TerminalHostView()
    {
        Width = 640; // TerminalStore.swift:21
        Height = 240; // TerminalStore.swift:21
        Focusable = true;
        ClipToBounds = true;
        // PORT-ONLY (web W4, ED-NEW-web-w4-2, DESIGN 3.7): the terminal is a content scope. Its keys are the shell's, so Ctrl+D
        // reaches it as end of input and Ctrl+\ as the quit character; only the reserved list is nth's.
        ChordScope.SetIsContent(this, true);

        scrollbackText = new TerminalSurface(MeasureCell)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            Focusable = true,
        };
        Child = scrollbackText;
        scrollbackText.UpdateFrame(screen.Snapshot());
        // MacTerminalView.swift:2878-3190: a program that asked for the mouse (DECSET 9, 1000-1003, 1005, 1006, 1015, 1016) is told of presses, drags, motion and the wheel.
        scrollbackText.MouseState = () =>
        {
            lock (scrollbackGate) return new TerminalMouseState(screen.MouseMode, screen.MouseEncoding, screen.MouseShiftCapture);
        };
        scrollbackText.MouseOutput += SendBytes;

        KeyDown += OnKeyDown;
        TextInput += OnTextInput;
        // The cell painter handles the press to capture selection. The long-lived
        // terminal view must still own keyboard focus (TerminalPanel.swift:234-238).
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                Focus();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // MacCaretView.swift:104-123: the caret is a block that blinks while the terminal has the keyboard, an outline when it has not.
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        scrollbackText.FocusChanged();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        scrollbackText.FocusChanged();
    }

    /// <summary>:22 <c>view.font = TerminalFont.preferred()</c>.</summary>
    public SwiftFont? Font
    {
        get => font;
        set
        {
            font = value;
            if (value is not null) value.Apply(scrollbackText.TextControl);
            scrollbackText.InvalidateMeasure();
            scrollbackText.InvalidateVisual();
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
            scrollbackText.Background = value?.Brush(scrollbackText);
            UpdatePalette();
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
            UpdatePalette();
        }
    }

    /// <summary>:55.</summary>
    public SwiftColor? CaretColor
    {
        get => caretColor;
        set
        {
            caretColor = value;
            scrollbackText.CursorBrush = value?.Brush(scrollbackText);
            scrollbackText.InvalidateVisual();
        }
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

    /// <summary>The screen and retained history after interpreting the child's terminal output.</summary>
    public string ScrollbackText
    {
        get
        {
            lock (scrollbackGate) return screen.AllText();
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
        inputGeneration++;
        launcher.ExecutableURL = executable;
        launcher.Arguments = args;
        launcher.Environment = environment;
        launcher.CurrentDirectoryURL = currentDirectory;
        // SwiftTerm 1.20.0 AppleTerminalView.swift:329-345 derives the initial
        // grid from the 640x240 frame and the selected font before spawning.
        lastPseudoConsoleSize = MeasureGrid(InitialFrame);
        lock (scrollbackGate) screen.Resize(lastPseudoConsoleSize.Columns, lastPseudoConsoleSize.Rows);
        ApplyOutput();
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
        inputGeneration++; // A delayed clipboard read must not write after termination.
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
        lock (scrollbackGate) screen.Resize(next.Columns, next.Rows);
        ApplyOutput();
        ResizeCount++;
        try { launcher.ResizePseudoConsole(next); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        ProcessDelegate?.SizeChanged(next.Columns, next.Rows);
    }

    private PseudoConsoleSize MeasureGrid(Size size)
    {
        var cell = MeasureCell();
        return new PseudoConsoleSize(
            Math.Clamp((int)Math.Floor(size.Width / cell.Width), 1, short.MaxValue),
            Math.Clamp((int)Math.Floor(size.Height / cell.Height), 1, short.MaxValue));
    }

    private Size MeasureCell()
    {
        var resolved = (Font ?? SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular)).Resolve();
        // SwiftTerm 1.20.0 AppleTerminalView.swift:410-444 measures the W advance and
        // the font's line metrics, then snaps both dimensions to the backing pixel grid.
        // FormattedText uses the resolved Avalonia face's real glyph and line metrics.
        var typeface = new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight);
        var measured = new FormattedText("W", CultureInfo.InvariantCulture,
            Avalonia.Media.FlowDirection.LeftToRight, typeface, resolved.FontSize, Brushes.White);
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var cellWidth = Math.Max(1, Math.Round(measured.Width * scale) / scale);
        var cellHeight = Math.Clamp(Math.Ceiling(measured.Height * scale) / scale, 1, 8192);
        return new Size(cellWidth, cellHeight);
    }

    private void UpdatePalette()
    {
        lock (scrollbackGate)
            screen.SetDefaultColors(nativeBackgroundColor?.Resolve(ActualThemeVariant),
                nativeForegroundColor?.Resolve(ActualThemeVariant));
        scrollbackText.InvalidateVisual();
    }

    /// <summary>Escape is handled by the parent host; every other special key reaches the ConPTY input.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // The reserved chords (F6, Ctrl+Shift+\, Ctrl+Alt+Arrow, ...) are nth's even from here: they are not the shell's, so they
        // are neither sent to it nor handled here; the window's dispatch (or the workspace) takes them.
        if (ChordScope.IsReservedKey(e)) return;
        // Selection belongs to the composed text control even while this view owns
        // focus. Route its copy/select-all chords before terminal input forwarding.
        scrollbackText.HandleSelectionKey(e);
        if (e.Handled) return;
        if (e.Key == Key.Escape) return;

        // Windows edit-chord adaptation (ED-050): the source's paste action reads
        // plain text from the clipboard. Ctrl+C with a selection was handled above;
        // without a selection it continues below as the shell's interrupt byte.
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            pendingPaste = PasteFromClipboard(pendingPaste);
            return;
        }

        // MacTerminalView.swift:1568 clears selection on terminal key input.
        scrollbackText.ClearSelection();
        bool applicationCursor;
        bool alternate;
        lock (scrollbackGate)
        {
            applicationCursor = screen.ApplicationCursorMode;
            alternate = screen.IsAlternateScreen;
        }
        // MacTerminalView.swift:1583-1593 and AppleTerminalView.swift:2710-2727:
        // unmodified Page keys scroll normal history, but reach full-screen programs.
        if (e.Key is Key.PageUp or Key.PageDown && e.KeyModifiers == KeyModifiers.None
            && !applicationCursor && !alternate)
        {
            scrollbackText.ScrollPage(e.Key == Key.PageUp);
            e.Handled = true;
            return;
        }
        if (TerminalInput.EncodeKey(e.Key, e.KeyModifiers, e.KeySymbol, applicationCursor, OptionAsMetaKey) is { Length: > 0 } bytes)
        {
            SendBytes(bytes);
            e.Handled = true;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Handled) return;
        if (string.IsNullOrEmpty(e.Text)) return;
        scrollbackText.ClearSelection();
        SendBytes(Encoding.UTF8.GetBytes(e.Text));
        e.Handled = true;
    }

    internal void PasteText(string text)
    {
        bool bracketed;
        lock (scrollbackGate) bracketed = screen.BracketedPasteMode;
        scrollbackText.ClearSelection();
        SendBytes(TerminalInput.EncodePaste(text, bracketed));
    }

    private async Task PasteFromClipboard(Task previous)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var generation = inputGeneration;
        if (topLevel?.Clipboard is not { } clipboard) return;
        // AppKit reads synchronously. Serialize the Windows asynchronous reads so
        // repeated paste gestures retain their order, and bind each to its session.
        try { await previous; }
        catch (Exception) when (previous.IsFaulted)
        {
            // A failed read belongs to that paste gesture. It must not poison
            // the ordering chain and prevent every later clipboard request.
        }
        if (generation != inputGeneration || !ReferenceEquals(topLevel, TopLevel.GetTopLevel(this))) return;
        var text = await clipboard.TryGetTextAsync();
        if (generation == inputGeneration && ReferenceEquals(topLevel, TopLevel.GetTopLevel(this)))
            PasteText(text ?? "");
    }

    internal void SendBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        // SwiftTerm AppleTerminalView.swift:2873-2882 ensures the caret is visible
        // whenever input is sent, including pasted text after scrolling into history.
        scrollbackText.EnsureCaretVisible();
        try { input?.Write(bytes); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void OnOutput(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        lock (scrollbackGate)
        {
            // A pipe read can end inside a scalar or CSI. Decoder and screen parser retain
            // their independent states until the next read (SwiftTerm UTF8Decoder/Terminal.feed).
            var characters = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            var count = outputDecoder.GetChars(bytes, 0, bytes.Length, characters, 0, flush: false);
            if (count == 0) return;
            screen.Feed(new string(characters, 0, count));
            if (outputUpdateQueued) return;
            outputUpdateQueued = true;
        }
        Dispatcher.UIThread.Post(ApplyOutput, DispatcherPriority.Background);
    }

    private void ApplyOutput()
    {
        TerminalFrame frame;
        lock (scrollbackGate)
        {
            outputUpdateQueued = false;
            frame = screen.Snapshot();
        }
        scrollbackText.UpdateFrame(frame);
        InvalidateMeasure();
        InvalidateVisual();
    }

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
        LoginEnvironment.WhichLaunchable("pwsh.exe", environment) // a Store PowerShell cannot join the job (WhichLaunchable)
        ?? LoginEnvironment.WhichLaunchable("powershell.exe", environment)
        ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
}
