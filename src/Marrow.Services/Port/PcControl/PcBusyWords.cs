// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-37; DESIGN.md 10.1 "Pausing after this step: dragging in Paint",
// 4.5; canvas pc-control-ui `unit(a, tok, label)` labels such as "dragging the attachment in Outlook"). The step a pause
// waits for, in the pill's words, without the app: what the step does, and to which element when it has a name
// ("clicking Save", "typing in Subject", "dragging"). The pill adds "in <app>". The same step's other forms (ED-NEW-pcctl-38;
// DESIGN.md 2.7, 4.5 "Hands off", "Done", "Borrow chip"): what Tactus asks to do ("click Save": "Tactus needs the mouse
// for one step: click Save in Paint."), what it did ("clicked Save": "Clicked Save in Paint" under "Done, all yours")
// and what a borrow used the mouse or keyboard for ("one click": "Used the mouse for one click in Paint").
namespace Marrow.Services.Port.PcControl;

/// <summary>A step in the pill's words, without the app: what Tactus asks to do, what it did, what a borrow was for.</summary>
public sealed record PcStepWords(string Do, string Did, string Chip);

public static class PcBusyWords
{
    /// <summary>An element name longer than this is cut, with an ellipsis, so the pill's line keeps the app in view.</summary>
    public const int MaxName = 32;

    public static string Of(PcInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = Name(request.Element?.Name);
        return request.Kind switch
        {
            PcInputKind.Click => name is null ? "clicking" : "clicking " + name,
            PcInputKind.Type => name is null ? "typing" : "typing in " + name,
            PcInputKind.Key => request.KeysText is { Length: > 0 and <= MaxName } keys ? "pressing " + keys : "pressing keys",
            PcInputKind.Scroll => "scrolling",
            PcInputKind.Drag => "dragging",
            _ => name is null ? "working" : "using " + name,
        };
    }

    /// <summary>The step's other forms (lower case; the pill capitalises a line's first letter).</summary>
    public static PcStepWords Words(PcInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = Name(request.Element?.Name);
        var keys = request.KeysText is { Length: > 0 and <= MaxName } text ? text : null;
        var click = request.Count == 2 ? "double-click" : "click";
        return request.Kind switch
        {
            PcInputKind.Click => new(Join(click, name), Join(click + "ed", name), request.Count == 2 ? "a double-click" : "one click"),
            PcInputKind.Type => new(Join("type", name, "in "), Join("typed", name, "in "), "typing"),
            PcInputKind.Key => new("press " + (keys ?? "keys"), "pressed " + (keys ?? "keys"), keys ?? "keys"),
            PcInputKind.Scroll => new("scroll", "scrolled", "a scroll"),
            PcInputKind.Drag => new("drag", "dragged", "a drag"),
            _ => name is null ? new("do one step", "did one step", "one step") : new("use " + name, "used " + name, name),
        };
    }

    private static string Join(string verb, string? name, string link = "") => name is null ? verb : verb + " " + link + name;

    private static string? Name(string? raw)
    {
        var name = raw?.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return name.Length <= MaxName ? name : name[..(MaxName - 1)].TrimEnd() + "…";
    }
}
