// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-37; DESIGN.md 10.1 "Pausing after this step: dragging in Paint",
// 4.5; canvas pc-control-ui `unit(a, tok, label)` labels such as "dragging the attachment in Outlook"). The step a pause
// waits for, in the pill's words, without the app: what the step does, and to which element when it has a name
// ("clicking Save", "typing in Subject", "dragging"). The pill adds "in <app>".
namespace Marrow.Services.Port.PcControl;

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

    private static string? Name(string? raw)
    {
        var name = raw?.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrEmpty(name)) return null;
        return name.Length <= MaxName ? name : name[..(MaxName - 1)].TrimEnd() + "…";
    }
}
