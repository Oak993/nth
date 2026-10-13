using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>The recorded events and recordings the teach tests build their drafts from.</summary>
public static class TeachSamples
{
    public static readonly DateTimeOffset Started = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    public static TeachElement Button(string name, string app = "outlook", string window = "Inbox - Outlook") => new(app, window, "button", name);

    public static TeachElement Field(string name, string app = "outlook", string window = "New message - Outlook", bool password = false) =>
        new(app, window, "edit", name) { IsPassword = password };

    public static TeachEvent Click(int seq, double seconds, TeachElement element, string verb = "click") =>
        new(seq, TimeSpan.FromSeconds(seconds), TeachEventKind.Click) { Verb = verb, Element = element, Frame = "frames/" + seq.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) + ".jpg" };

    public static TeachEvent Type(int seq, double seconds, TeachElement element, string? text, int? length = null, bool secret = false) =>
        new(seq, TimeSpan.FromSeconds(seconds), TeachEventKind.Type)
        {
            Verb = "type", Element = element, Text = secret ? "[secret]" : text, Length = length ?? text?.Length ?? 0, Secret = secret,
        };

    public static TeachEvent Shortcut(int seq, double seconds, string chord, string app = "outlook") =>
        new(seq, TimeSpan.FromSeconds(seconds), TeachEventKind.Shortcut) { Verb = "press", Text = chord, Element = new TeachElement(app, "", "", "") };

    public static TeachEvent Browser(int seq, double seconds, string verb, string? url = null, string? text = null, TeachElement? element = null) =>
        new(seq, TimeSpan.FromSeconds(seconds), TeachEventKind.Browser) { Verb = verb, Url = url, Text = text, Element = element };

    public static TeachEvent Private(int seq, double seconds) => new(seq, TimeSpan.FromSeconds(seconds), TeachEventKind.Private) { Verb = "click" };

    public static TeachRecordingInfo Info(string id = "r1") => new(id, "dextra", Started, TeachRecordingState.Recorded);

    public static TeachNarrationLine Say(double start, string text) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 2), text);

    /// <summary>A short mail: open a new message, address it, type the subject, press Send, confirm.</summary>
    public static IReadOnlyList<TeachEvent> Mail() =>
    [
        Click(1, 2, Button("New mail")),
        Click(2, 5, Field("To")),
        Type(3, 6, Field("To"), "sam@example.com"),
        Click(4, 10, Field("Subject")),
        Type(5, 11, Field("Subject"), "Weekly report"),
        Click(6, 20, Button("Send")),
    ];
}
