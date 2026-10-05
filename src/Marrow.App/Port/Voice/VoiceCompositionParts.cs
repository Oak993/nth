// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the production parts of the voice seams, built and not yet bound.
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Port.Voice;

/// <summary>What <see cref="VoiceComposition.Build"/> makes: the endpoint host, the privacy gate, the microphone, the hardware probe and the model store with its downloads.</summary>
internal sealed record VoiceCompositionParts(ICaptureHost Host, IMicrophoneAccess Microphone, IAudioCapture Capture, IHardwareProbe Probe, ISpeechModelBackend Models);
