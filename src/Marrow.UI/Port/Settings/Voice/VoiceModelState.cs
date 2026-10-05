// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): which of the four faces a model row's state column shows.
namespace Marrow.UI.Port.Settings;

/// <summary>The state column's four faces (voice-SPEC V-16.3.3, DESIGN voice.js:129): `In use`, the download's progress, the `Use` capsule and the `Download` capsule.</summary>
public enum VoiceModelState { InUse, Downloading, Installed, NotInstalled }
