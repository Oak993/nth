namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>One server-sent event of the streaming reply: a text delta, or the end with the whole text (V-18.1).</summary>
public readonly record struct OpenAiEvent(bool Done, string Text);
