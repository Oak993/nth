using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

// VT-12 test fixture: a composer on screen with a session under way (V-T132, V-T133).
internal sealed record VoicePillScene(VoiceComposerRig Rig, ComposerView View, Avalonia.Controls.Window Window) : IDisposable
{
    public void Dispose()
    {
        try { Rig.Close(Window); }
        finally { Rig.Dispose(); }
    }
}
