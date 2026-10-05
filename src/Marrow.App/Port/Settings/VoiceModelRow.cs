// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-13 B; first-release voice typing, VT-14): one model of the Voice typing page's model list (voice-SPEC V-16.3.3; DESIGN gen.py:648-662).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>
/// One row: the model's name with the `Recommended` badge, the version button (`{Version}, {size}`, folding open a version control, the English only switch and the model's note), the
/// meters, and the state column (`In use`, the progress and its cross, `Use`, `Download`, with the trash that removes an installed model); a 2 pt progress bar under the row while it downloads. The
/// variant the row stands for is the one the user picked in it; until they pick, it is the in-use variant, else the recommended one, else Compact (ED). Every change of what it shows is a
/// transition: the meta text and the state faces crossfade, the badge scales in, the progress fill moves linearly, the versions fold.
/// </summary>
internal sealed class VoiceModelRow : Decorator
{
    private readonly WhisperModel model;
    private readonly SpeechModelController controller;
    private readonly SpeechModelRowChoice english = new();
    private readonly Panel versionsHost = new();
    private ModelRowChoice choice;
    private bool touched;
    private bool pointerOver;

    public VoiceModelRow(WhisperModel model, SpeechModelController controller, string? inUseId, string? recommendedId)
    {
        this.model = model;
        this.controller = controller;
        choice = SpeechModelList.InitialChoice(model.Name, inUseId, recommendedId);
        english.EnglishOnly = choice.EnglishOnly;

        var file = ChosenFile;
        var nameText = new TextBlock { Text = model.Name, TextWrapping = Avalonia.Media.TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(nameText);
        nameText.Foreground = Chrome.PrimaryText.Brush(nameText);
        Badge = new VoiceBadge(SpeechModelText.Recommended);
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { nameText, Badge } };
        VersionButton = new VoiceVersionButton(SpeechModelList.Meta(file), SpeechModelText.VersionOf(model.Name));
        VersionButton.Invoked += () => VersionsToggled?.Invoke(model.Name);
        var texts = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Children = { nameLine, VersionButton } };
        Meters = new VoiceMeters(model.Speed, model.Accuracy, SpeechModelList.MeterName(model));
        StateColumn = new VoiceStateColumn();
        StateColumn.UseButton.Invoked += () => controller.Use(ChosenFile);
        StateColumn.DownloadButton.Invoked += () => controller.Download(ChosenFile);
        StateColumn.CancelButton.Invoked += () => controller.Cancel(ChosenFile);
        StateColumn.Trash.Confirmed += () => controller.Remove(ChosenFile);
        StateColumn.Trash.GotFocus += (_, _) => StateColumn.Trash.SetRevealed(true);
        StateColumn.Trash.LostFocus += (_, _) => StateColumn.Trash.SetRevealed(pointerOver);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,112,14,96"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(texts, 0);
        Grid.SetColumn(Meters, 2);
        Grid.SetColumn(StateColumn, 4);
        grid.Children.Add(texts);
        grid.Children.Add(Meters);
        grid.Children.Add(StateColumn);
        var line = new Border { Padding = new Thickness(16, 8, Chrome.RowControlTrailingPadding, 8), MinHeight = 36, Background = Brushes.Transparent, Child = grid };

        Bar = new VoiceProgressBar();
        BuildVersions();
        var versionLine = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 14, LineSpacing = 8, Margin = new Thickness(16, 2, 16, 4) };
        versionLine.Children.Add(versionsHost);
        if (model.HasEnglishOnly)
        {
            EnglishSwitch = new CzSwitch(new SettingsSwitch(new Binding<bool>(() => english.EnglishOnly, OnEnglishPicked)), SpeechModelText.EnglishOnly);
            var englishLabel = new CzCrossfadeText(SpeechModelText.EnglishOnly) { FontSize = 12, Secondary = true, VerticalAlignment = VerticalAlignment.Center };
            versionLine.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { EnglishSwitch, englishLabel } });
        }
        Note = new CzCrossfadeText(model.Note ?? "") { FontSize = 11.5, Wrap = true, Secondary = true, Margin = new Thickness(16, 2, 16, 10) };
        Note.SetValue(SwiftFont.LineBoxProperty, 16.0);
        VersionFold = new CzFoldRow(new StackPanel { Children = { versionLine, Note } }, initiallyOpen: false);

        Child = new StackPanel { Orientation = Orientation.Vertical, Children = { line, Bar, VersionFold } };
        PointerEntered += (_, _) => SetPointerOver(true);
        PointerExited += (_, _) => SetPointerOver(false);
        Apply();
    }

    /// <summary>Raised with the model's name when the version button is pressed: the list opens this row's versions and closes the others'.</summary>
    public event Action<string>? VersionsToggled;

    public string ModelName => model.Name;

    public VoiceBadge Badge { get; }

    public VoiceVersionButton VersionButton { get; }

    public VoiceMeters Meters { get; }

    public VoiceStateColumn StateColumn { get; }

    public VoiceProgressBar Bar { get; }

    public CzFoldRow VersionFold { get; }

    /// <summary>The version control (it is replaced, never moved, when the row changes its variant by itself).</summary>
    public CzSegmentedControl Versions { get; private set; } = null!;

    /// <summary>The English only switch; null for the models that have no English only form (Large v3 Turbo, Large v3).</summary>
    public CzSwitch? EnglishSwitch { get; }

    /// <summary>The model's note.</summary>
    public CzCrossfadeText Note { get; }

    /// <summary>The variant the row stands for now.</summary>
    public ModelRowChoice Choice => choice;

    /// <summary>The file of that variant.</summary>
    public ModelFile ChosenFile => SpeechModelList.File(model.Name, choice);

    /// <summary>The pointer is over the row (or left it): the trash shows, or hides unless it has the focus.</summary>
    internal void SetPointerOver(bool over)
    {
        pointerOver = over;
        StateColumn.Trash.SetRevealed(over || StateColumn.Trash.IsFocused);
    }

    /// <summary>Opens or folds the row's versions (the list keeps one open at a time).</summary>
    public void SetOpen(bool open)
    {
        VersionFold.Open = open;
        VersionButton.SetOpen(open);
    }

    /// <summary>Shows what the section knows now: the variant (until the user picks one), the badge, the state column, the bar and the trash.</summary>
    public void Refresh(string? inUseId, string? recommendedId)
    {
        if (!touched)
        {
            var next = SpeechModelList.InitialChoice(model.Name, inUseId, recommendedId);
            if (next != choice)
            {
                var versionChanged = next.Version != choice.Version;
                choice = next;
                english.EnglishOnly = next.EnglishOnly;
                if (versionChanged) BuildVersions();
            }
        }
        Badge.SetShown(ModelCatalog.Find(recommendedId) is { } recommended && string.Equals(recommended.Model, model.Name, StringComparison.Ordinal));
        Apply();
    }

    private void BuildVersions()
    {
        var options = SpeechModelList.VersionsOf(model.Name)
            .Select(version => (version.ToString(), SpeechModelList.SegmentText(SpeechModelList.File(model.Name, new ModelRowChoice(version, false)))))
            .ToList();
        var versions = new CzSegmentedControl(SpeechModelText.VersionOf(model.Name), options, choice.Version.ToString(), small: true);
        versions.SelectionChanged += id =>
        {
            touched = true;
            choice = choice with { Version = Enum.Parse<ModelVersion>(id) };
            Apply();
        };
        Versions = versions;
        versionsHost.Children.Clear();
        versionsHost.Children.Add(versions);
    }

    private void OnEnglishPicked(bool value)
    {
        english.EnglishOnly = value;
        touched = true;
        choice = choice with { EnglishOnly = value };
        Apply();
    }

    // The row as its variant says: the version button's text, the state face, the progress, the trash's name.
    private void Apply()
    {
        var file = ChosenFile;
        VersionButton.SetText(SpeechModelList.Meta(file));
        var state = controller.State(file);
        StateColumn.SetState(state.Kind switch
        {
            ModelInstallKind.InUse => VoiceModelState.InUse,
            ModelInstallKind.Downloading => VoiceModelState.Downloading,
            ModelInstallKind.Installed => VoiceModelState.Installed,
            _ => VoiceModelState.NotInstalled,
        }, state.Fraction);
        Bar.SetActive(state.Kind == ModelInstallKind.Downloading);
        if (state.Kind == ModelInstallKind.Downloading) Bar.SetFraction(state.Fraction);
        StateColumn.Trash.SetSubject(ModelCatalog.ModelName(file.Id) ?? model.Name);
    }
}
