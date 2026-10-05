// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-13 B; first-release voice typing, VT-14): the model list card of the Voice typing page (voice-SPEC V-16.3.3, V-16.3.4; DESIGN gen.py:645-663).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>
/// The second card of the Speech model section: the caption `Whisper` with its subtitle, one <see cref="VoiceModelRow"/> per model (six, in the design's order, a hairline between them), and the
/// footer `Installed: {size}, in Marrow Code’s models folder` (it crossfades as models arrive and go). One row's versions are open at a time. The rows follow the controller: it tells the list when
/// anything changed, and the list shows each row what the settings and the recommendation say.
/// </summary>
internal sealed class VoiceModelList : Decorator
{
    private readonly SpeechModelController controller;
    private readonly AppSettings settings;
    private string? openModel;

    public VoiceModelList(SpeechModelController controller, AppSettings settings)
    {
        this.controller = controller;
        this.settings = settings;
        var caption = new TextBlock { Text = SpeechModelList.Caption, VerticalAlignment = VerticalAlignment.Bottom };
        SwiftFont.System(12, SwiftFont.Weight.Semibold).Apply(caption);
        caption.Foreground = Chrome.SecondaryText.Brush(caption);
        var subtitle = new TextBlock { Text = SpeechModelList.Subtitle, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom, Opacity = .8 };
        SwiftFont.System(12).Apply(subtitle);
        subtitle.Foreground = Chrome.SecondaryText.Brush(subtitle);
        var captionLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 12, 16, 3), Children = { caption, subtitle } };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(captionLine);
        var rows = new List<VoiceModelRow>();
        var inUse = settings.VoiceTyping.Model;
        var recommended = controller.Recommendation?.ModelId;
        foreach (var model in ModelCatalog.Models)
        {
            if (rows.Count > 0) stack.Children.Add(new ChromeRowDivider());
            var row = new VoiceModelRow(model, controller, inUse, recommended);
            row.VersionsToggled += Toggle;
            rows.Add(row);
            stack.Children.Add(row);
        }
        Rows = rows;
        Footer = new CzCrossfadeText(SpeechModelList.Footer(controller.InstalledBytes)) { FontSize = 11.5, Secondary = true, Margin = new Thickness(16, 8, 16, 8) };
        Footer.SetValue(SwiftFont.LineBoxProperty, 16.0);
        var rule = new ChromeRowDivider { Margin = new Thickness(0, 4, 0, 0) }; // `.mfoot` border-top 1px --div, margin-top 4, across the card
        stack.Children.Add(rule);
        stack.Children.Add(Footer);
        stack.Margin = new Thickness(0, 0, 0, 4); // `.mlist` padding-bottom 4
        Child = new ChromeCard(() => stack);
    }

    /// <summary>The six rows.</summary>
    public IReadOnlyList<VoiceModelRow> Rows { get; }

    /// <summary>The footer, crossfading.</summary>
    public CzCrossfadeText Footer { get; }

    /// <summary>The name of the model whose versions are open, or null.</summary>
    public string? OpenModel => openModel;

    /// <summary>Shows every row what the controller and the settings say now.</summary>
    public void Refresh()
    {
        var inUse = settings.VoiceTyping.Model;
        var recommended = controller.Recommendation?.ModelId;
        foreach (var row in Rows) row.Refresh(inUse, recommended);
        Footer.SetText(SpeechModelList.Footer(controller.InstalledBytes));
    }

    private void Toggle(string modelName)
    {
        openModel = string.Equals(openModel, modelName, StringComparison.Ordinal) ? null : modelName;
        foreach (var row in Rows) row.SetOpen(string.Equals(row.ModelName, openModel, StringComparison.Ordinal));
    }
}
