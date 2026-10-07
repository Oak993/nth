// PORT-ONLY: multi-account steps 3b-5, job 0. The "No 5-hour limit" cell (multi-account.html .no5t): small, tertiary, one line, in the place the 5-hour bar would take. It is there only when the service reported that the account has no 5-hour window
// (AccountUsageLine.NoFiveHour), never from a plan's name. It fades in and out when a later report changes that (OD-0076).
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Shared;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Shared;

internal sealed class NoFiveHourCell : Decorator
{
    private readonly TextBlock text;
    private readonly AnimatedScalar fade;
    private bool shown;

    /// <param name="size">10.5 in a popover row; 11 in the big usage panel (the page's .ubig).</param>
    internal NoFiveHourCell(AccountUsageLine? usage, double size = 10.5)
    {
        text = LibraryUi.Text(MultiAccountWords.NoFiveHour, size, trim: true);
        text.Foreground = ChromeTokens.SecondaryText.Opacity(0.75).Brush(text);
        Child = text;
        shown = NoFiveHourText.For(usage) is not null;
        fade = new AnimatedScalar(this, shown ? 1 : 0, v => { Opacity = v; IsVisible = v > 0.001; });
        AutomationProperties.SetName(this, MultiAccountWords.NoFiveHour);
    }

    /// <summary>The words shown when it is there; always the page's.</summary>
    internal string TextShown => text.Text ?? "";

    internal void Apply(AccountUsageLine? usage)
    {
        var next = NoFiveHourText.For(usage) is not null;
        if (next == shown) return;
        shown = next;
        fade.Set(next ? 1 : 0, LibraryUi.Ease(this, LibraryUi.Fade));
    }
}
