using Avalonia.Controls;
using Marrow.Setup.Flow;
using Marrow.Setup.Shell;

namespace Marrow.Setup.Screens;

/// <summary>One built screen: its view, the Close-paused line it carries (Installing, Update, Removing) and Welcome's signature line.</summary>
public interface ISetupScreen
{
    SetupScreen Kind { get; }

    Control View { get; }

    /// <summary>The Close-paused line at the bottom of the screen, if it has one.</summary>
    HoldLine? Hold { get; }

    /// <summary>Welcome's signature line.</summary>
    SignatureLine? Signature { get; }

    /// <summary>The screen becomes the shown one (unit U2): it is set up in its final state at once, before it fades in.</summary>
    void Entered(SetupScreen? from) { }

    /// <summary>The screen stops being the shown one (its timers stop; it keeps fading out with the screen change).</summary>
    void Left() { }
}

/// <summary>A screen with Enter and Esc of its own (unit U2): true when it used the key.</summary>
public interface IKeyedScreen
{
    bool OnKey(Avalonia.Input.Key key);
}

/// <summary>Builds the screens (unit U2: <see cref="CanvasScreens"/>, the approved canvas's screens).</summary>
public interface ISetupScreens
{
    ISetupScreen Create(SetupScreen screen, SetupSession session);
}
