namespace Marrow.UI.Port;

internal delegate void AmbientWinEventCallback(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
