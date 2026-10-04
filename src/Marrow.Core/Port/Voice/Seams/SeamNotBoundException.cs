// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-3): the exception every fail-closed seam default
// throws when touched (voice-SPEC V-2.5).
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>
/// A voice seam was touched without a production or test implementation bound (V-2.5, "CLI seams fail closed"). Production
/// implementations are bound only in the app's start-up composition; a test that forgets to bind a double gets this, never a
/// real microphone, model file, download or credential store.
/// </summary>
public sealed class SeamNotBoundException(string seam, string member)
    : InvalidOperationException($"The voice seam {seam} was touched ({member}) with no implementation bound (voice-SPEC V-2.5: seams fail closed).")
{
    /// <summary>The seam's interface name.</summary>
    public string Seam { get; } = seam;

    /// <summary>The member that was touched.</summary>
    public string Member { get; } = member;
}
