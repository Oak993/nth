// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): where the CUDA offer's row is.
namespace Marrow.App.Port.Voice.Cuda;

/// <summary>Hidden: nothing is offered. Offer: the Download capsule. Downloading: the percentage, the cancel x and the 2 px bar. Installed: the check and "Installed". Failed: the Download capsule again.</summary>
public enum CudaOfferState { Hidden, Offer, Downloading, Installed, Failed }
