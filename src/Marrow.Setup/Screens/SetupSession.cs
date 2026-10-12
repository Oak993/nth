using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Shell;
using Marrow.Setup.Text;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, steps 3 to 5, and "Close"; unit U1, ED-NEW-inst-u1-1). One run of the setup window:
/// the screen shown and how the window got there, the setup's own signature, the exit code if the window is closed now,
/// and Close pausing on the screen that carries the Close-paused line. The caption says "Uninstall nth" in uninstall mode
/// and "nth Setup" otherwise. A signature that is there but does not hold sends the window to Problem ("This copy of nth
/// Setup has been changed") and nothing can be installed from it. Screens are built once and kept, so going back to one
/// finds it as it was. Unit U2 (ED-NEW-inst-u2-1) adds what the screens share: the desktop shortcut choice (Welcome's
/// link and Options' switch stay in step through it), the install folder, what Ready reports (the hook's
/// <c>install-result.json</c>, never the choice), Problem's ending and details, the mark's one pop-in per window, the
/// actions outside the window, and Enter and Esc for the screen shown.
/// </summary>
public sealed class SetupSession
{
    private readonly ISetupScreens screens;
    private readonly Dictionary<SetupScreen, ISetupScreen> built = [];
    private int? exitCode;

    public SetupSession(SetupWindow window, SetupRoute route, ISetupScreens screens, string installerVersion, bool canInstall = true, ISetupActions? actions = null, IReadOnlyList<string>? arguments = null)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Route = route ?? throw new ArgumentNullException(nameof(route));
        this.screens = screens ?? throw new ArgumentNullException(nameof(screens));
        Version = installerVersion ?? "";
        CanInstall = canInstall;
        Screen = route.Screen;
        Actions = actions ?? new WindowsSetupActions();
        DesktopShortcut = InstallOptions.DesktopShortcutChoice(arguments ?? []);
        InstallRoot = route.TargetRoot;
        Window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Bubble);
    }

    /// <summary>Opening nth, copying, the licence, the folder picker.</summary>
    public ISetupActions Actions { get; }

    /// <summary>The desktop shortcut choice (on unless <c>--no-desktop-shortcut</c>): Welcome's link and Options' switch.</summary>
    public bool DesktopShortcut { get; private set; }

    /// <summary>Raised when the choice changes, from either place.</summary>
    public event Action<bool>? DesktopShortcutChanged;

    /// <summary>Where nth is installed (Options' Change… picks another folder; an update stays where nth is).</summary>
    public string InstallRoot { get; private set; }

    public event Action<string>? InstallRootChanged;

    /// <summary>What the install hook reported (<c>install-result.json</c>), shown by Ready; null until an install finished.</summary>
    public InstallResult? Result { get; private set; }

    /// <summary>Ready follows an update ("nth is updated", no desktop moment).</summary>
    public bool Updated { get; private set; }

    /// <summary>Raised when the result changes (Ready reveals the change if it is shown).</summary>
    public event Action? ResultChanged;

    /// <summary>Problem's ending: "Nothing was changed on your PC." (true) or "Some of nth's files may be left." (false).</summary>
    public bool ProblemSafe { get; private set; } = true;

    /// <summary>Problem's details under the header line (the step, the exit code and the log's last lines; unit U3 fills them).</summary>
    public string ProblemDetails { get; private set; } = "";

    /// <summary>Raised when the reason, the ending or the details change (Problem reveals the change if it is shown).</summary>
    public event Action? ProblemChanged;

    /// <summary>Update's what's new lines, from the package's release notes (STUB(U4): unit U4 reads them; none hides the card).</summary>
    public IReadOnlyList<string> ReleaseNotes { get; set; } = [];

    /// <summary>The user's wallpaper for the desktop previews (null until read, or when unreadable: the theme gradient).</summary>
    public Support.WallpaperScene? Wallpaper { get; private set; }

    public event Action<Support.WallpaperScene>? WallpaperChanged;

    /// <summary>The wallpaper read in the background arrived.</summary>
    public void SetWallpaper(Support.WallpaperScene scene)
    {
        Wallpaper = scene ?? throw new ArgumentNullException(nameof(scene));
        WallpaperChanged?.Invoke(scene);
    }

    /// <summary>nth's size on disk in MB (the build's figure, the same on Welcome and in the space check); null hides it.</summary>
    public long? SizeMegabytes { get; set; }

    private bool popped;

    /// <summary>True once per window: the first screen's mark pops in (Welcome, Update, Up to date, Newer); Ready always does.</summary>
    public bool TakeFirstPop()
    {
        if (popped) return false;
        popped = true;
        return true;
    }

    /// <summary>Sets the desktop shortcut choice; Welcome and Options follow.</summary>
    public void SetDesktopShortcut(bool on)
    {
        if (DesktopShortcut == on) return;
        DesktopShortcut = on;
        DesktopShortcutChanged?.Invoke(on);
    }

    /// <summary>Sets the install folder (Options' Change…).</summary>
    public void SetInstallRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (string.Equals(InstallRoot, path, StringComparison.OrdinalIgnoreCase)) return;
        InstallRoot = path;
        InstallRootChanged?.Invoke(path);
    }

    /// <summary>
    /// The folder nth goes in for a folder the user picked: the folder itself when it is empty (or new), otherwise an
    /// <c>nth</c> folder inside it, so nth never shares a folder with other files.
    /// </summary>
    public static string RootFor(string picked)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(picked);
        var trimmed = Path.TrimEndingDirectorySeparator(picked);
        try
        {
            if (!Directory.Exists(trimmed) || !Directory.EnumerateFileSystemEntries(trimmed).Any()) return trimmed;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Path.Combine(trimmed, "nth");
        }
        return string.Equals(Path.GetFileName(trimmed), "nth", StringComparison.OrdinalIgnoreCase) ? trimmed : Path.Combine(trimmed, "nth");
    }

    /// <summary>What the hook reported, and whether this was an update (unit U3 calls it, then shows Ready).</summary>
    public void SetResult(InstallResult result, bool updated)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Updated = updated;
        ResultChanged?.Invoke();
    }

    public SetupWindow Window { get; }

    public SetupRoute Route { get; }

    /// <summary>The setup's own version.</summary>
    public string Version { get; }

    /// <summary>False in a smoke run: nothing can be installed from this window.</summary>
    public bool CanInstall { get; }

    public SetupScreen Screen { get; private set; }

    /// <summary>
    /// Raised after a screen has been built and put in the window (the first screen, every <see cref="Go"/>). The staging folder
    /// is released from here when Ready or Problem shows (PLAN.md 5, "Leftovers"; unit U9), which is after the screen was built,
    /// so everything it reads from that folder must be read before the screen is shown.
    /// </summary>
    public event Action<SetupScreen>? ScreenShown;

    /// <summary>
    /// This run's staging folder (unit U9, <see cref="StagingScope.Attach"/>); null in a smoke run. Whatever the install writes
    /// outside the install root goes in it through <see cref="StagingScope.Ensure"/>, which claims it, so that it is deleted on
    /// every exit path and no sweep takes it while the run uses it.
    /// </summary>
    public StagingScope? Staging { get; internal set; }

    public ISetupScreen? Current => built.GetValueOrDefault(Screen);

    public SignatureResult Signature { get; private set; } = SignatureResult.Pending;

    /// <summary>Why Problem is shown (its sentence), if it is.</summary>
    public string? ProblemReason { get; private set; }

    /// <summary>The setup's exit code if the window closes now.</summary>
    public int ExitCode => exitCode ?? SetupFlow.ExitCodeWhenClosed(Screen);

    /// <summary>The person chose Install or Update (STUB(U3/U4): Install, Update and Retry hand the install to the unchanged gate until the progress engine runs it here).</summary>
    public bool InstallChosen { get; private set; }

    /// <summary>Shows the first screen with the window (no motion: the window's own appearance is the arrival).</summary>
    public void Start()
    {
        Window.CaptionTitle = Route.Screen == SetupScreen.Uninstall ? SetupStrings.CaptionUninstall : SetupStrings.CaptionSetup;
        Show(Route.Screen, immediately: true);
    }

    /// <summary>Moves to <paramref name="screen"/> (the screen change: the current one leaves, this one arrives).</summary>
    public void Go(SetupScreen screen) => Show(screen, immediately: false);

    /// <summary>A screen built for <paramref name="screen"/>, building it if needed.</summary>
    public ISetupScreen Get(SetupScreen screen)
    {
        if (built.TryGetValue(screen, out var existing)) return existing;
        var created = screens.Create(screen, this);
        created.Hold?.Attach(Window.Animator);
        if (created.Signature is { } line)
        {
            line.Attach(Window.Animator);
            if (Signature.State != SignatureState.Unknown) line.Set(Signature);
        }
        built.Add(screen, created);
        return created;
    }

    /// <summary>The self-signature check's answer (it finishes after the window shows).</summary>
    public void SignatureChecked(SignatureResult result)
    {
        Signature = result ?? throw new ArgumentNullException(nameof(result));
        if (SetupFlow.BlocksInstall(result))
        {
            ProblemReason = SetupStrings.ProblemChanged;
            if (Screen is SetupScreen.Welcome or SetupScreen.Update or SetupScreen.Options) Go(SetupScreen.Problem);
            return;
        }
        foreach (var screen in built.Values) screen.Signature?.Set(result);
    }

    /// <summary>Shows Problem with <paramref name="reason"/> (before <see cref="Start"/>, it becomes the first screen's sentence).</summary>
    public void Fail(string reason, bool safe = true, string? details = null)
    {
        ProblemReason = reason ?? throw new ArgumentNullException(nameof(reason));
        ProblemSafe = safe;
        if (details is not null) ProblemDetails = details;
        ProblemChanged?.Invoke();
        if (built.Count > 0 && Screen != SetupScreen.Problem) Go(SetupScreen.Problem);
    }

    /// <summary>
    /// Problem's Retry: the same install again, without going back to Welcome (STUB(U3): like Install, it hands the run to
    /// the unchanged gate until unit U3's progress engine runs it in this window). A changed copy of the setup never retries.
    /// </summary>
    public void Retry()
    {
        if (!CanInstall || SetupFlow.BlocksInstall(Signature) || Window.Guard.IsPaused || Screen != SetupScreen.Problem) return;
        InstallChosen = true;
        Window.CloseNow();
    }

    /// <summary>Asks the window to close (Close, Not now, Esc): refused with the line brightening while Close is paused.</summary>
    public void Close() => Window.Close();

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Current is not IKeyedScreen screen) return;
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key is Key.Enter or Key.Escape && screen.OnKey(e.Key)) e.Handled = true;
    }

    /// <summary>Install or Update was chosen. The window closes and the gate installs (STUB(U3/U4)).</summary>
    public void ChooseInstall()
    {
        if (!CanInstall || SetupFlow.BlocksInstall(Signature) || Window.Guard.IsPaused) return;
        if (Screen is not (SetupScreen.Welcome or SetupScreen.Update)) return;
        InstallChosen = true;
        Window.CloseNow();
    }

    /// <summary>Pauses Close on the current screen: its line shows why, and sign-out and shutdown wait.</summary>
    public void PauseClose()
    {
        var hold = Current?.Hold ?? throw new InvalidOperationException($"{Screen} has no Close-paused line.");
        Window.Guard.Pause(hold, HoldWords(Screen), ShutdownReason(Screen));
    }

    /// <summary>Ends the pause (the work is done).</summary>
    public void ResumeClose() => Window.Guard.Resume();

    /// <summary>Closes the window with <paramref name="code"/> as the setup's exit code.</summary>
    public void Finish(int code)
    {
        exitCode = code;
        Window.CloseNow();
    }

    public static string HoldWords(SetupScreen screen) => screen switch
    {
        SetupScreen.Update => SetupStrings.UpdateHold,
        SetupScreen.Uninstall => SetupStrings.UninstallHold,
        _ => SetupStrings.InstallingHold,
    };

    /// <summary>What Windows shows while sign-out or shutdown waits for the setup.</summary>
    public static string ShutdownReason(SetupScreen screen) => screen == SetupScreen.Uninstall ? SetupStrings.UninstallRemoving : SetupStrings.ShutdownInstalling;

    private void Show(SetupScreen screen, bool immediately)
    {
        var previous = built.Count > 0 && Current is { } current ? current : null;
        var next = Get(screen);
        if (previous is not null && !ReferenceEquals(previous, next)) previous.Left();
        var from = previous?.Kind;
        Screen = screen;
        next.Entered(from);
        Window.Body.Show(next.View, immediately);
        ScreenShown?.Invoke(screen);
    }
}
