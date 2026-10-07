// PORT-ONLY: T11 step 2. A pill step's own model, effort and fast mode, under its row: "Step: <account>" / "Saved with the pill", the model as a menu and the app's EffortSliderCard as the composer draws it (the locked card: the model's name, the
// provider mark, the slider, Fast as its own button, Reset to default). A step starts as "Same as the account"; any change here saves a complete choice with the pill, and Reset clears it (back to the account's). Fast mode shows in the step's
// line only as the yellow bolt the composer's model chip draws (EffortSlider.cs:180-183).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Composer;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class StepSettingsPanel : Decorator
{
    private readonly PillEditorModel editor;
    private readonly int index;
    private readonly ProviderKind service;
    private readonly IReadOnlyList<ModelOption> models;
    private readonly ModelPulse pulse;
    private readonly CzCrossfadeText modelWords = new("") { FontSize = 13, Secondary = true };
    private readonly CrossfadeSlot card = new();
    private string? cardModel;
    private readonly StackPanel configuration;

    internal StepSettingsPanel(PillEditorModel editor, int index, StepView step, IReadOnlyList<ModelOption> models, ModelPulse pulse)
    {
        this.editor = editor;
        this.index = index;
        this.models = models;
        this.pulse = pulse;
        service = step.Service ?? ProviderKind.Claude;
        Width = 330;
        var key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var title = LibraryUi.Text(MultiAccountWords.StepTitle(step.AccountName), 12.5, weight: SwiftFont.Weight.Semibold);
        var sub = LibraryUi.Text(MultiAccountWords.StepSub, 11.5, secondary: true);
        var picker = MenuPopover.Value("Model", "step-model-" + key, modelWords, () => [.. this.models.Select(m => new MenuOption(m.Id, m.ChipName, false, m.Id == Current.ModelId))], Choose);
        var modelRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var modelLabel = LibraryUi.Text("Model", 13);
        modelLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(picker, 1);
        modelRow.Children.Add(modelLabel);
        modelRow.Children.Add(picker);
        var same = new SettingsSwitch(new Binding<bool>(() => { _ = pulse.Revision; return Current.SameAsAccount; }, on =>
        {
            if (on) editor.ResetStepSettings(index);
            else { var current = Current; editor.SetStepSettings(index, new StepSettings(current.ModelId, current.Effort, current.Fast)); }
        }));
        same.Named("Same as the account", "step-same-account-" + key);
        var sameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var sameLabel = LibraryUi.Text("Same as the account", 12.5); sameLabel.VerticalAlignment = VerticalAlignment.Center;
        sameRow.Children.Add(sameLabel); Grid.SetColumn(same, 1); sameRow.Children.Add(same);
        configuration = new StackPanel { Spacing = 8, Children = { modelRow, card } };
        Child = new Border { Padding = new Thickness(12), Child = new StackPanel { Spacing = 8, Children = { new StackPanel { Spacing = 1, Children = { title, sub } }, sameRow, configuration } } };
        Apply();
    }

    private StepView Current => editor.Steps[index];

    internal string ModelShown => modelWords.Text;

    internal EffortSliderCard? Card => card.Live as EffortSliderCard;

    internal void Apply()
    {
        var step = Current;
        configuration.Opacity = step.SameAsAccount ? .45 : 1;
        var option = models.FirstOrDefault(m => m.Id == step.ModelId) ?? models.FirstOrDefault();
        modelWords.SetText(option?.ChipName ?? step.Model);
        if (option is null || cardModel == option.Id) return;
        cardModel = option.Id;
        card.Show(new EffortSliderCard(option.ChipName, service, option.Efforts, option.DefaultEffort, option.SupportsFast,
            new Binding<string?>(() => { _ = pulse.Revision; return Current.Effort; }, v => Save(effort: v)),
            new Binding<bool>(() => { _ = pulse.Revision; return Current.Fast; }, v => Save(fast: v)),
            () => editor.ResetStepSettings(index)), LibraryUi.Ease(this, LibraryUi.Fade));
    }

    private void Choose(string modelId)
    {
        var option = models.FirstOrDefault(m => m.Id == modelId);
        if (option is null) return;
        // a new model starts at its own default effort; fast mode stays only where the model has it
        Save(modelId, option.DefaultEffort, Current.Fast && option.SupportsFast);
    }

    private void Save(string? model = null, string? effort = null, bool? fast = null)
    {
        var step = Current;
        editor.SetStepSettings(index, new StepSettings(model ?? step.ModelId, effort ?? step.Effort, fast ?? step.Fast));
    }
}
