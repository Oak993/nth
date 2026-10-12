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
/// finds it as it was.
/// </summary>
public sealed class SetupSession
{
    private readonly ISetupScreens screens;
    private readonly Dictionary<SetupScreen, ISetupScreen> built = [];
    private int? exitCode;
    private bool signatureUnavailable;

    public SetupSession(SetupWindow window, SetupRoute route, ISetupScreens screens, string installerVersion, bool canInstall = true)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Route = route ?? throw new ArgumentNullException(nameof(route));
        this.screens = screens ?? throw new ArgumentNullException(nameof(screens));
        Version = installerVersion ?? "";
        CanInstall = canInstall;
        Screen = route.Screen;
        Window.Guard.PausedChanged += _ => InstallAvailabilityChanged?.Invoke();
    }

    public SetupWindow Window { get; }

    public SetupRoute Route { get; }

    /// <summary>The setup's own version.</summary>
    public string Version { get; }

    /// <summary>False in a smoke run: nothing can be installed from this window.</summary>
    public bool CanInstall { get; }

    /// <summary>The UI command and the session gate share this permission; Unknown never installs.</summary>
    public bool CanChooseInstall => CanInstall && !signatureUnavailable && !SetupFlow.BlocksInstall(Signature)
        && !Window.Guard.IsPaused && (Screen is SetupScreen.Welcome or SetupScreen.Update);

    public event Action? InstallAvailabilityChanged;

    public SetupScreen Screen { get; private set; }

    public ISetupScreen? Current => built.GetValueOrDefault(Screen);

    public SignatureResult Signature { get; private set; } = SignatureResult.Pending;

    /// <summary>Why Problem is shown (its sentence), if it is.</summary>
    public string? ProblemReason { get; private set; }

    /// <summary>The setup's exit code if the window closes now.</summary>
    public int ExitCode => exitCode ?? SetupFlow.ExitCodeWhenClosed(Screen);

    /// <summary>The person chose Install or Update (STUB(U3/U4): the interim screens hand the install to the unchanged gate).</summary>
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
        ArgumentNullException.ThrowIfNull(result);
        if (signatureUnavailable) return;
        Signature = result;
        InstallAvailabilityChanged?.Invoke();
        if (result.State == SignatureState.Invalid)
        {
            ProblemReason = SetupStrings.ProblemChanged;
            if (Screen is SetupScreen.Welcome or SetupScreen.Update or SetupScreen.Options) Go(SetupScreen.Problem);
            return;
        }
        foreach (var screen in built.Values) screen.Signature?.Set(result);
    }

    /// <summary>A bounded check did not produce an answer. Keep Unknown and show the plain failure; late results cannot reopen Install.</summary>
    public void SignatureCheckUnavailable()
    {
        signatureUnavailable = true;
        Signature = SignatureResult.Pending;
        InstallAvailabilityChanged?.Invoke();
        ProblemReason = SetupStrings.ProblemSignature;
        if (built.Count > 0 && Screen is SetupScreen.Welcome or SetupScreen.Update or SetupScreen.Options) Go(SetupScreen.Problem);
    }

    /// <summary>Shows Problem with <paramref name="reason"/> (before <see cref="Start"/>, it becomes the first screen's sentence).</summary>
    public void Fail(string reason)
    {
        ProblemReason = reason ?? throw new ArgumentNullException(nameof(reason));
        if (built.Count > 0) Go(SetupScreen.Problem);
    }

    /// <summary>Install or Update was chosen. The window closes and the gate installs (STUB(U3/U4)).</summary>
    public void ChooseInstall()
    {
        if (!CanChooseInstall) return;
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
        var next = Get(screen);
        Screen = screen;
        Window.Body.Show(next.View, immediately);
        InstallAvailabilityChanged?.Invoke();
    }
}
