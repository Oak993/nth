// PORT-ONLY: multi-account steps 3b-5, job 0. The config line of an account, as multi-account.html draws it (.cfgl): the yellow Fast bolt first, when Fast is on, then "<model> · <effort>" in small secondary text that gives way to an ellipsis in a narrow row.
// The bolt is the glyph the composer's model chip draws (EffortSlider.cs:180-183: bolt.fill, the inline icon font, yellow), never the word. The page gives it the spoken label "Fast mode" and nothing is written. A change fades in (OD-0076).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Shared;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Shared;

internal sealed class AccountConfigLine : StackPanel
{
    private readonly TextBlock text;
    private readonly Control bolt;
    private readonly AnimatedScalar boltFade;
    private readonly AnimatedScalar textFade;
    private bool boltOn;

    /// <param name="size">The text's size in points: 11 in the popover and in a pill step (the page's .cfgl).</param>
    internal AccountConfigLine(AccountConfigText start, double size = 11)
    {
        Orientation = Orientation.Horizontal;
        text = LibraryUi.Text(start.Text, size, secondary: true, trim: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        bolt = SwiftImage.SystemName("bolt.fill").Font(ChromeTokens.InlineIconFont).ForegroundStyle(SwiftColor.Yellow);
        bolt.Margin = new Thickness(0, 0, 4, 0);
        bolt.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(bolt, "Fast mode");
        Children.Add(bolt);
        Children.Add(text);
        boltOn = start.Bolt;
        boltFade = new AnimatedScalar(this, boltOn ? 1 : 0, v => { bolt.Opacity = v; bolt.IsVisible = v > 0.001; });
        textFade = new AnimatedScalar(this, 1, v => text.Opacity = v);
        AutomationProperties.SetName(this, start.Text);
    }

    /// <summary>The words shown: "&lt;model&gt; · &lt;effort&gt;".</summary>
    internal string TextShown => text.Text ?? "";

    /// <summary>Fast is on: the bolt is shown (or fading in).</summary>
    internal bool BoltShown => boltOn;

    internal void Apply(AccountConfigText next)
    {
        if (next.Text != TextShown)
        {
            text.Text = next.Text;
            textFade.Set(0, null);
            textFade.Set(1, LibraryUi.Ease(this, LibraryUi.Fade));
            AutomationProperties.SetName(this, next.Text);
        }
        if (next.Bolt == boltOn) return;
        boltOn = next.Bolt;
        boltFade.Set(boltOn ? 1 : 0, LibraryUi.Ease(this, LibraryUi.Fade));
    }
}
