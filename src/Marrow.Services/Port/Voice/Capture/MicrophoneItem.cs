// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-19 B; first-release voice typing, VT-5): one line of the Microphone row's menu.
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>A line of the Microphone row: <see cref="Value"/> is what <c>voiceTyping.mic</c> stores (null for "Windows default"), <see cref="Label"/> what the menu shows.</summary>
public sealed record MicrophoneItem(string? Value, string Label);
