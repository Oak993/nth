// PORT-ONLY, owner-ruled addition (OD-0106 CUDA A, OD-0099 Q-V-9 C, OD-0104; first-release voice typing, VT-17): what the row's one control does now.
namespace Marrow.App.Port.Voice.Cuda;

/// <summary>None; Download (the capsule starts the download); Cancel (the x stops it and keeps the partial).</summary>
public enum CudaOfferAction { None, Download, Cancel }
