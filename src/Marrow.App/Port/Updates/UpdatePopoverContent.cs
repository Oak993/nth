// PORT-ONLY: first-release feature unit UE-5 (OD-0100 place D). The pill's popover: the run's summary and button over the list of items. Design: update-everything/src/ue-page.css (.vD .dpop) and the D shots.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Updates;

/// <summary>
/// The popover that opens from the pill: a header with the run's summary line, its detail line and the one button ("Update everything", "Updating…", "Restart Marrow" or "Retry"), the failure
/// banner while an item failed, and a row for each installed item with its versions, its status and, while it updates, its bar. It reads <see cref="UpdateEverythingService.State"/> and
/// follows it: every change is transitioned (OD-0076), the words crossfade, rows and the banner arrive and leave, bars ease, and the card follows the folds frame by frame. ENGINEERING DECISIONS
/// (ED-344): the card is 440 pt wide as the design page draws it; a state with no pill (Hidden) leaves the card as it was, since the pill closes it.
/// </summary>
internal sealed class UpdatePopoverContent : Decorator
{
    public const double CardWidth = 440;

    private readonly UpdateEverythingService service;
    private readonly OpacityCrossfade title = new();
    private readonly OpacityCrossfade detail = new();
    private readonly OpacityCrossfade action = new();
    private readonly UpdateFold bannerHost = new();
    private readonly StackPanel rowsPanel = new() { Orientation = Orientation.Vertical };
    private readonly ForEach<UpdatePillRow, string> rows;
    private UpdatePillState? shown;
    private string? actionKey;
    private UpdatePillPhase? lastPhase;
    private Control? banner;
    private bool bannerWanted;
    private OpacityCrossfade? bannerTitle;
    private OpacityCrossfade? bannerText;

    public UpdatePopoverContent(UpdateEverythingService service)
    {
        this.service = service;
        rows = new ForEach<UpdatePillRow, string>(rowsPanel, row => row.Id, row => new UpdateRowView(row), (control, row) => ((UpdateRowView)control).Apply(row));

        var words = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(14, 0, 12, 10),
            Children = { words, action },
        };
        Grid.SetColumn(words, 0);
        Grid.SetColumn(action, 1);
        action.Margin = new Thickness(10, 0, 0, 0);
        action.VerticalAlignment = VerticalAlignment.Center;

        var body = new Border
        {
            Width = CardWidth,
            Padding = new Thickness(0, 12, 0, 6),
            Child = new StackPanel { Orientation = Orientation.Vertical, Children = { new Border { Margin = new Thickness(14, 0, 12, 0), Child = new UpdateWhatsNew(service) }, header, bannerHost, rowsPanel } },
        };
        Child = body;
        AutomationProperties.SetName(this, "Updates");

        var primed = false;
        this.Track(() => service.State, state =>
        {
            if (state.Phase == UpdatePillPhase.Hidden) return; // the pill closes the card
            Apply(state, primed ? Animation.EaseOut(0.22) : null);
            primed = true;
        });
    }

    /// <summary>The state the card shows.</summary>
    internal UpdatePillState? Shown => shown;

    /// <summary>The row controls, in list order.</summary>
    internal IEnumerable<UpdateRowView> RowViews => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(rowsPanel).OfType<UpdateRowView>();

    /// <summary>The button on show (null while the card has not been given a state).</summary>
    internal Control? ActionControl => action.Current;

    internal Control? Banner => bannerWanted ? banner : null;

    /// <summary>How far the banner's fold is open now (0 shut, 1 whole).</summary>
    internal double BannerFold => bannerHost.Amount;

    private void Apply(UpdatePillState state, Animation? animation)
    {
        shown = state;
        Transaction.WithAnimation(animation, () =>
        {
            // step 10: at 100 % the title cross-fades over 0.25 s
            if (animation is not null && title.Current is TextBlock && state.Phase is UpdatePillPhase.Restart or UpdatePillPhase.Done && lastPhase == UpdatePillPhase.Running)
                Transaction.WithAnimation(Animation.EaseOut(0.25), () => Show(title, state.Title, 13, SwiftFont.Weight.Semibold, Chrome.PrimaryText));
            else Show(title, state.Title, 13, SwiftFont.Weight.Semibold, Chrome.PrimaryText);
            Show(detail, state.Detail, 11, SwiftFont.Weight.Regular, Chrome.SecondaryText, wraps: true);
            ShowAction(state);
            ShowBanner(state, animation);
            rows.Apply(state.Rows);
        });
        lastPhase = state.Phase;
    }

    private static void Show(OpacityCrossfade into, string text, double size, SwiftFont.Weight weight, SwiftColor color, bool wraps = false)
    {
        if (into.Current is TextBlock current && current.Text == text) return;
        var label = new TextBlock { Text = text, TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left };
        SwiftFont.System(size, weight).Apply(label);
        label.Foreground = color.Brush(label);
        into.Show(label);
    }

    private void ShowAction(UpdatePillState state)
    {
        var key = $"{state.Action}|{state.ButtonText}|{state.ButtonEnabled}";
        if (key == actionKey) return;
        actionKey = key;
        var act = state.Action;
        var button = SmallAccentGlassButton.Create(state.ButtonText, () => service.Perform(act), state.ButtonEnabled);
        // step 10: when the run reaches 100 % the button for what is next rises 6 to 0 px over 0.32 s (the cross-fade of the words around it is the card's own)
        if (lastPhase == UpdatePillPhase.Running && state.Phase is UpdatePillPhase.Restart or UpdatePillPhase.Done && action.Current is not null)
        {
            var rising = new Decorator { Child = button };
            action.Show(rising);
            NthArrive.Rise(rising, 6, .32);
        }
        else action.Show(button);
    }

    private void ShowBanner(UpdatePillState state, Animation? animation)
    {
        if (state.BannerTitle is null)
        {
            if (bannerWanted)
            {
                bannerWanted = false;
                bannerHost.Set(false, animation); // the card keeps the words while it folds shut
            }
            return;
        }
        if (banner is null)
        {
            bannerTitle = new OpacityCrossfade();
            bannerText = new OpacityCrossfade();
            var warning = SwiftImage.SystemName("exclamationmark.triangle.fill").Font(SwiftFont.System(13, SwiftFont.Weight.Medium)).ForegroundStyle(Chrome.Orange);
            warning.VerticalAlignment = VerticalAlignment.Top;
            warning.Margin = new Thickness(0, 1, 0, 0);
            var words = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, Children = { bannerTitle, bannerText } };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { warning, words } };
            var card = new Border { Padding = new Thickness(12, 10), Margin = new Thickness(14, 0, 12, 4), Child = line }.RoundedRectangle(10, RoundedCornerStyle.Continuous);
            card.Background = Chrome.Orange.Opacity(0.16).Brush(card);
            AutomationProperties.SetName(card, state.BannerTitle);
            banner = card;
            bannerHost.Child = card;
        }
        if (!bannerWanted)
        {
            bannerWanted = true;
            bannerHost.Set(true, animation);
        }
        Show(bannerTitle!, state.BannerTitle, 12, SwiftFont.Weight.Semibold, Chrome.PrimaryText);
        Show(bannerText!, state.BannerText ?? "", 11.5, SwiftFont.Weight.Regular, Chrome.PrimaryText, wraps: true);
    }
}

/// <summary>
/// One item of the list: its icon chip, name, versions, status and, while it updates, its bar. The words crossfade as the item moves through its states (OD-0076); the bar folds open
/// under the name while the item updates and shuts after.
/// </summary>
internal sealed class UpdateRowView : Decorator
{
    private const double BarSlot = 9; // the 3 pt bar and the 6 pt gap above it

    private readonly OpacityCrossfade name = new();
    private readonly OpacityCrossfade sub = new();
    private readonly OpacityCrossfade versions = new();
    private readonly OpacityCrossfade status = new();
    private readonly OpacityCrossfade glyph = new();
    private readonly UpdateProgressBar bar = new();
    private readonly Border barHost = new() { ClipToBounds = true, Height = 0 };
    private readonly AnimatedScalar fold;
    private readonly TextBlock chipText = new();
    private bool primed;

    public UpdateRowView(UpdatePillRow row)
    {
        Id = row.Id;
        var chip = new Border { Width = 22, Height = 22, Child = chipText, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) }.RoundedRectangle(6, RoundedCornerStyle.Continuous);
        chip.Background = Chrome.Overlay(0.10).Brush(chip);
        chipText.HorizontalAlignment = HorizontalAlignment.Center;
        chipText.VerticalAlignment = VerticalAlignment.Center;
        SwiftFont.System(9, SwiftFont.Weight.Semibold).Apply(chipText);
        chipText.Foreground = Chrome.SecondaryText.Brush(chipText);
        chipText.Text = Initials(row.Title);

        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.Margin = new Thickness(0, 0, 6, 0);
        var statusLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { glyph, status } };
        versions.Margin = new Thickness(10, 0, 14, 0);
        versions.VerticalAlignment = VerticalAlignment.Center;
        var words = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };
        var line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("22,*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { chip, words, versions, statusLine },
        };
        Grid.SetColumn(chip, 0);
        Grid.SetColumn(words, 1);
        Grid.SetColumn(versions, 2);
        Grid.SetColumn(statusLine, 3);

        barHost.Child = new Border { Margin = new Thickness(30, 6, 0, 0), Child = bar };
        fold = new AnimatedScalar(this, 0, value => barHost.Height = BarSlot * value);
        Child = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(14, 6, 12, 6), Children = { line, barHost } };
        Apply(row);
    }

    public string Id { get; }

    /// <summary>The row as last given.</summary>
    internal UpdatePillRow? Row { get; private set; }

    internal UpdateProgressBar Bar => bar;

    internal string? StatusText => (status.Current as TextBlock)?.Text;

    internal string? VersionsText => (versions.Current as TextBlock)?.Text;

    internal string? SubText => (sub.Current as TextBlock)?.Text;

    internal string? NameText => (name.Current as TextBlock)?.Text;

    /// <summary>Whether the bar's slot is open (or opening).</summary>
    internal bool BarOpen { get; private set; }

    public void Apply(UpdatePillRow row)
    {
        Row = row;
        var animation = primed ? Animation.EaseOut(0.22) : null;
        primed = true;
        Transaction.WithAnimation(animation, () =>
        {
            Text(name, row.Title, 13, SwiftFont.Weight.Medium, Chrome.PrimaryText);
            Text(sub, row.Subtitle ?? "", 11, SwiftFont.Weight.Regular, Chrome.SecondaryText);
            Text(versions, VersionLine(row), 11, SwiftFont.Weight.Regular, Chrome.SecondaryText);
            var color = StatusColor(row);
            Text(status, row.Status, 11.5, row.State is UpdateRowState.UpdateAvailable or UpdateRowState.Failed || row.Status == "Restart to finish" ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular, color);
            ShowGlyph(row);
        });
        var open = row.State == UpdateRowState.Updating;
        BarOpen = open;
        bar.Active = open;
        bar.Set(open ? row.Progress : null, animation);
        fold.Set(open ? 1 : 0, animation);
        AutomationProperties.SetName(this, $"{row.Title}, {row.Status}");
    }

    private static string VersionLine(UpdatePillRow row) =>
        row.Installed is null ? "" : row.ShowsLatest && row.Latest is not null ? $"{row.Installed} → {row.Latest}" : row.Installed;

    private static SwiftColor StatusColor(UpdatePillRow row) => row.State switch
    {
        UpdateRowState.UpdateAvailable => Chrome.Accent,
        UpdateRowState.Failed => Chrome.Orange,
        UpdateRowState.Updated when row.Status == "Restart to finish" => Chrome.Accent,
        _ => Chrome.SecondaryText,
    };

    private static void Text(OpacityCrossfade into, string text, double size, SwiftFont.Weight weight, SwiftColor color)
    {
        if (into.Current is TextBlock current && current.Text == text) return;
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left };
        SwiftFont.System(size, weight).Apply(label);
        label.Foreground = color.Brush(label);
        into.Show(label);
    }

    private string? glyphKey;

    private void ShowGlyph(UpdatePillRow row)
    {
        var key = row.State switch
        {
            UpdateRowState.UpToDate => "check",
            UpdateRowState.UpdateAvailable => "dot-accent",
            UpdateRowState.Queued => "dot-grey",
            UpdateRowState.Updating => "spinner",
            UpdateRowState.Updated => row.Status == "Restart to finish" ? "restart" : "check",
            UpdateRowState.Failed => "warning",
            _ => "none",
        };
        if (key == glyphKey) return;
        glyphKey = key;
        glyph.Show(key switch
        {
            "check" => SwiftImage.SystemName("checkmark.circle.fill").Font(SwiftFont.System(12, SwiftFont.Weight.Medium)).ForegroundStyle(SwiftColor.Rgb(0x34C759)),
            "dot-accent" => Dot(Chrome.Accent),
            "dot-grey" => Dot(Chrome.SecondaryText),
            "spinner" => new NthSpinner(),
            "restart" => SwiftImage.SystemName("arrow.clockwise").Font(SwiftFont.System(11, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.Accent),
            "warning" => SwiftImage.SystemName("exclamationmark.triangle.fill").Font(SwiftFont.System(12, SwiftFont.Weight.Medium)).ForegroundStyle(Chrome.Orange),
            _ => null,
        });
    }

    private static Control Dot(SwiftColor color)
    {
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(2, 0, 2, 0) };
        dot.Fill = color.Brush(dot);
        return dot;
    }

    /// <summary>"CC" for Claude Code, "Co" for Codex: the first letters of the first two words, or the first two letters of a single word.</summary>
    internal static string Initials(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        if (words.Length >= 2) return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0]));
        return words[0].Length == 1 ? words[0].ToUpperInvariant() : string.Concat(char.ToUpperInvariant(words[0][0]), words[0][1]);
    }
}

/// <summary>
/// A block whose height folds open and shut (the design page's `fold`): its content is laid out at its own height and the block is as tall as the fold has got to, clipped, with the
/// content's opacity following. The failure banner arrives and leaves through one, so the card around it grows frame by frame instead of jumping (OD-0076).
/// </summary>
internal sealed class UpdateFold : Decorator
{
    private readonly AnimatedScalar open;
    private double amount;

    public UpdateFold()
    {
        ClipToBounds = true;
        open = new AnimatedScalar(this, 0, value =>
        {
            amount = Math.Clamp(value, 0, 1);
            if (Child is { } child) child.Opacity = amount;
            InvalidateMeasure();
        });
    }

    /// <summary>How far open the fold is now, 0 to 1.</summary>
    public double Amount => amount;

    public void Set(bool shown, Animation? animation) => open.Set(shown ? 1 : 0, animation);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is not { } child) return default;
        child.Opacity = amount;
        child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(child.DesiredSize.Width, child.DesiredSize.Height * amount);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));
        return finalSize;
    }
}
