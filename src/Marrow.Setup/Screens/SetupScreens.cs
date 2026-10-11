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
}

/// <summary>Builds the screens (unit U2 replaces <see cref="InterimScreens"/> with the canvas's screens).</summary>
public interface ISetupScreens
{
    ISetupScreen Create(SetupScreen screen, SetupSession session);
}
