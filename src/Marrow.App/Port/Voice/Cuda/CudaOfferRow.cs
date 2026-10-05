// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): the row as the Speech model card draws it (look A).
namespace Marrow.App.Port.Voice.Cuda;

/// <summary>
/// The row under the recommendation: <see cref="Title"/> and <see cref="Detail"/> on the left; on the right the model list's state column (<see cref="StateText"/>, with the
/// check mark when <see cref="ShowsCheck"/>, or the <see cref="Action"/>'s capsule or x) and its 2 px bar (<see cref="ShowsBar"/>, <see cref="Progress"/> 0 to 1).
/// Drawing it in VoiceTypingSettingsPage is NOT DONE here (VT-14's page).
/// </summary>
public sealed record CudaOfferRow(bool Shown, CudaOfferState State, string Title, string Detail, string StateText, double Progress, CudaOfferAction Action, string ActionText,
    string CancelTitle, bool ShowsCheck, bool ShowsBar)
{
    public static CudaOfferRow Hidden { get; } = new(false, CudaOfferState.Hidden, "", "", "", 0, CudaOfferAction.None, "", "", false, false);
}
