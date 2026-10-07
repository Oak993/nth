namespace Marrow.App.Tests.Fixtures;

// Independent literal oracle transcribed from customization-SPEC CZ-9 (OD-0097) and voicecmp.js:207-222.
internal static class VoiceRowsOracle
{
    internal static readonly IReadOnlyDictionary<string, (string Id, string Text, string Detail)[]> Options = new Dictionary<string, (string, string, string)[]>
    {
        ["Orb"] = [("siri", "Siri Wave", "Siri Wave, the default"), ("membrane", "Voice Membrane", "Voice Membrane")],
        ["Listening style"] = [("pill", "Pill in the composer", "The orb opens into a glass pill that says what it hears; the cross at its far end cancels"), ("cross", "Cross beside the orb", "A cross slides out beside the orb to cancel; the text field shows the words"), ("float", "Floating pill", "The orb lifts into a glass pill above the composer, the text keeps its full width")],
        ["Pill words"] = [("L1", "Listening… · Transcribing… · Finishing…", "What the app is doing, plainly"), ("L2", "Listening… · Writing… · Finishing…", "Writing says what you see: the words appearing in the composer"), ("L3", "Hearing you… · Writing… · Tidying up…", "Warmer and personal"), ("L4", "Speak now · Transcribing… · Almost done…", "An invitation in silence, then what the app is doing"), ("L5", "Listening… · Typing… · Wrapping up…", "Names the composer’s own state: it is typing"), ("L6", "Listening · Transcribing · Finishing", "Without the ellipses: quieter, and the pill stays narrower")],
        ["Orb size"] = [("32", "32 px", "32 px, the default"), ("26", "26 px", "26 px, a quieter orb")],
        ["Attachment button"] = [("in", "Inside the composer", "At the composer’s left, inside it"), ("out", "Outside", "In the margin beside the composer")],
        ["Composer light while you speak"] = [("away", "Steps aside", "The light fades out as you start speaking and returns when the text is done: one light on the composer at a time"), ("dim", "Dims to 35 %", "The light keeps running at 35 % under the voice glow"), ("stay", "Stays on", "The light keeps running at full strength under the voice glow")],
        ["Spoken language"] = [("auto", "Auto-detect, silent", "Whisper detects the language each time; nothing is shown"), ("show", "Auto-detect, named in the pill", "Whisper detects the language, and the pill names it once it is heard, so a wrong guess shows at once"), ("windows", "Windows display language", "Windows’ display language is not in the list, so Whisper detects the language"), ("ask", "Asked on first use", "The first click on the orb asks which language you will speak; the answer is kept on the Voice typing page")],
        ["After voice input"] = [("leave", "Leave the text in the composer", "The words wait in the composer until you press send"), ("send", "Send it as the prompt", "The words go out as your prompt as soon as they arrive")],
    };
}
