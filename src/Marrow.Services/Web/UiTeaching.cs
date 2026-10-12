// PORT-ONLY: web surfaces W17, Teaching (DESIGN.md 7.3; ED-NEW-web-w17-1). The glue between what a session offers and what
// Marrow.Core.NthUi.NthUiTeaching says: from the names of the tools a session is offered (the nth-ui server's, or the
// native tool loop's) it works out the teaching options, so the text always matches the tools that exist. When the
// live-answer tools of W24 (ui_show, ui_update, html_widget) and ask_user (W25) join the lists, the text switches from
// the fenced fallback to calling them with no change here. The reader's "Visual answers" setting (Settings > Tabula) is
// a seam the settings unit binds.
using Marrow.Core.NthUi;
using Marrow.Core.Port;

namespace Marrow.Services.Web;

public static class UiTeaching
{
    private static readonly Func<VisualAnswerLevel> Balanced = () => VisualAnswerLevel.Balanced;

    /// <summary>The reader's "Visual answers" setting, read when a session starts. Balanced until the settings page binds it.</summary>
    public static Ambient<Func<VisualAnswerLevel>> Visual { get; } = new(Balanced);

    /// <summary>
    /// The options for a session offered <paramref name="offered"/> tools. <paramref name="toolsUsable"/> is false for a model that
    /// cannot call tools (a small local one): it then gets fenced blocks for everything.
    /// </summary>
    public static NthUiTeachingOptions Options(IEnumerable<string> offered, NthUiProfile profile = NthUiProfile.Full, bool toolsUsable = true)
    {
        var names = offered.ToHashSet(StringComparer.Ordinal);
        return new NthUiTeachingOptions
        {
            Profile = profile,
            LiveTools = toolsUsable && names.Contains(NthUiTeaching.ShowTool),
            ArtifactTools = toolsUsable && names.Contains(ArtifactTools.CreateName),
            HtmlWidget = names.Contains(NthUiTeaching.HtmlTool) && profile == NthUiProfile.Full,
            AskUser = toolsUsable && names.Contains(NthUiTeaching.AskTool),
            Reference = toolsUsable && names.Contains(NthUiTeaching.ReferenceTool),
            Visual = Visual.Current(),
        };
    }

    /// <summary>The nth-ui server's initialize.instructions for the tools it serves.</summary>
    public static string Instructions(IEnumerable<string> served) => NthUiTeaching.McpInstructions(Options(served));

    /// <summary>The block for an API provider's system prompt: the tools its loop offers, and whether the model can call them.</summary>
    public static string SystemBlock(IEnumerable<string> offered, NthUiProfile profile = NthUiProfile.Full, bool toolsUsable = true) =>
        NthUiTeaching.SystemBlock(Options(offered, profile, toolsUsable));
}
