// PORT-ONLY: T11 step 2. One step of a pill in the editor: a grip (drag, or Alt+Up and Alt+Down), its number, its account's mark and name, the service ("Claude Code · No limit" for a local one), "New service" where the pill crosses to another service,
// "Skipped · off" when its account is off, then its model line ("Same as the account · Opus 5.5 · High", with the yellow bolt for Fast; press it to set the step on its own) and its Handshake slider ("Never runs out" for a local one), and a
// cross to remove the step (a pill keeps at least two).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class PillStepRow : Decorator
{
    internal const double RowHeight = 78;

    private readonly PillEditorModel editor;
    private readonly ModelPulse pulse;
    private readonly Func<ProviderKind, IReadOnlyList<ModelOption>> models;
    private readonly TextBlock number = LibraryUi.Text("", 12, secondary: true);
    private readonly TextBlock name = LibraryUi.Text("", 14, weight: SwiftFont.Weight.Medium);
    private readonly CzCrossfadeText sub = new("") { FontSize = 12, Secondary = true };
    private readonly CzCrossfadeText config = new("") { FontSize = 12.5 };
    private readonly Border newService;
    private readonly Border skipped;
    private readonly Control bolt;
    private readonly PillStepSettingsPopup settingsPopup;
    private readonly OpacityTransition feedback;
    private readonly Border host = new();
    private readonly Grid line2;
    private readonly StackPanel words;
    private HandshakeSliderControl? slider;
    private StepSettingsPanel? panel;
    private Control? never;
    private StepView step;
    private readonly Button configButton;
    private readonly TextBlock mono = MultiAccountText.Text("", 9, weight: SwiftFont.Weight.Bold);
    private readonly Border mark;
    private readonly OpacityTransition dragFade;
    private bool dragging;
    private double dragStart;

    internal PillStepRow(PillEditorModel editor, StepView first, ModelPulse pulse, Func<ProviderKind, IReadOnlyList<ModelOption>> models)
    {
        this.editor = editor;
        this.pulse = pulse;
        this.models = models;
        step = first;
        dragFade = new OpacityTransition(this, 1, value => Opacity = value);
        var key = first.AccountId;
        var grip = new Border { Width = 22, Height = 22, Background = Avalonia.Media.Brushes.Transparent, Child = LibraryUi.Glyph("line.3.horizontal", 12, ChromeTokens.SecondaryText, SwiftFont.Weight.Medium) };
        ((Control)grip.Child!).HorizontalAlignment = HorizontalAlignment.Center;
        ((Control)grip.Child!).VerticalAlignment = VerticalAlignment.Center;
        grip.Opacity = 0.7;
        grip.Cursor = new Cursor(StandardCursorType.SizeAll);
        grip.Named("Drag to reorder", "step-grip-" + key);
        grip.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return; dragging = true; dragStart = e.GetPosition(null).Y; e.Pointer.Capture(grip); e.Handled = true; };
        grip.PointerMoved += (_, e) => { if (dragging) { if (CapsuleMotion.Reduce(this)) { RenderTransform = null; dragFade.Set(.7, FrameClock.IsBound ? Animation.EaseOut(duration: .3) : null); } else RenderTransform = new Avalonia.Media.TranslateTransform(0, e.GetPosition(null).Y - dragStart); ZIndex = 1; } };
        grip.PointerReleased += (_, e) => EndDrag(e.GetPosition(null).Y - dragStart);
        grip.PointerCaptureLost += (_, _) => { if (dragging) EndDrag(0); };

        newService = Pip(MultiAccountWords.NewService, "step-new-" + key);
        skipped = Pip(MultiAccountWords.SkippedOff, "step-skipped-" + key);
        var remove = LibraryButtons.Glyph("xmark", "Remove step", "step-remove-" + key, Remove);
        words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { newService, skipped } };
        var line1 = new Grid { ColumnDefinitions = new ColumnDefinitions("22,18,20,*,Auto,22"), ColumnSpacing = 8 };
        number.VerticalAlignment = VerticalAlignment.Center;
        mono.HorizontalAlignment = HorizontalAlignment.Center; mono.VerticalAlignment = VerticalAlignment.Center;
        mark = new Border { Width = 24, Height = 24, Child = mono, VerticalAlignment = VerticalAlignment.Center }.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        mark.Background = ChromeTokens.Overlay(.08).Brush(mark);
        grip.VerticalAlignment = VerticalAlignment.Center;
        remove.VerticalAlignment = VerticalAlignment.Center;
        foreach (var (control, column) in new (Control, int)[] { (grip, 0), (number, 1), (mark, 2), (words, 3), (tags, 4), (remove, 5) })
        {
            Grid.SetColumn(control, column);
            line1.Children.Add(control);
        }

        bolt = SwiftImage.SystemName("bolt.fill").Font(ChromeTokens.InlineIconFont).ForegroundStyle(SwiftColor.Yellow);
        bolt.Named("Fast mode");
        bolt.VerticalAlignment = VerticalAlignment.Center;
        var chevron = LibraryUi.Glyph("chevron.down", 9, ChromeTokens.SecondaryText, SwiftFont.Weight.Semibold);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        config.VerticalAlignment = VerticalAlignment.Center;
        configButton = PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { bolt, config, chevron } }, ToggleSettings);
        configButton.Focusable = true;
        configButton.Cursor = new Cursor(StandardCursorType.Hand);
        configButton.Named("Model, effort and fast mode", "step-config-" + key);
        line2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14, Margin = new Thickness(48, 0, 0, 0) };
        configButton.HorizontalAlignment = HorizontalAlignment.Left;
        line2.Children.Add(configButton);
        Grid.SetColumn(host, 1);
        line2.Children.Add(host);

        settingsPopup = new PillStepSettingsPopup(configButton, () => panel = new StepSettingsPanel(editor, step.Index, step, models(step.Service ?? ProviderKind.Claude), pulse));
        feedback = new OpacityTransition(this, 1, progress =>
        {
            if (CapsuleMotion.Reduce(this)) { RenderTransform = null; Opacity = .6 + .4 * progress; }
            else
            {
                // HTML 4466: three equal-time CSS-ease segments, 0 -> -4 -> 4 -> 0.
                var segment = Math.Min(2, (int)(progress * 3));
                var local = progress * 3 - segment;
                var eased = Animation.TimingCurve(.25, .1, .25, 1, duration: 1).Progress(local);
                var x = segment switch { 0 => -4 * eased, 1 => -4 + 8 * eased, _ => 4 * (1 - eased) };
                RenderTransform = progress >= 1 ? null : new Avalonia.Media.TranslateTransform(x, 0);
            }
        });
        Child = new Border { Padding = new Thickness(16, 10, 12, 10), Child = new StackPanel { Spacing = 8, Children = { line1, line2, settingsPopup.Popup } } };
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Alt) == 0) return;
            if (e.Key == Key.Up) { editor.Do(step.Index, "up"); e.Handled = true; }
            if (e.Key == Key.Down) { editor.Do(step.Index, "down"); e.Handled = true; }
        };
        Apply(first);
    }

    private static Border Pip(string text, string id)
    {
        var label = LibraryUi.Text(text, 11, secondary: true);
        var tag = new Border { Padding = new Thickness(7, 2), Child = label }.RoundedRectangle(100, RoundedCornerStyle.Continuous);
        tag.Background = ChromeTokens.Overlay(0.08).Brush(tag);
        tag.Named(text, id);
        return tag;
    }

    internal StepView Step => step;

    internal string ConfigShown => config.Text;

    internal string SubShown => sub.Text;

    internal bool BoltShown => bolt.IsVisible;

    internal bool NewServiceShown => newService.IsVisible;

    internal bool SkippedShown => skipped.IsVisible;

    // Retained inspection API means inline expansion, not a floating popup.
    internal bool SettingsOpen => panel?.GetVisualAncestors().Contains(this) == true;

    internal HandshakeSliderControl? Slider => slider;

    internal StepSettingsPanel? Panel => panel;

    internal Button ConfigButton => configButton;

    internal bool NeverShown => never is not null && never.IsVisible;

    internal void Apply(StepView next)
    {
        step = next;
        number.Text = next.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        name.Text = next.AccountName;
        mono.Text = next.Mono;
        var hue = Avalonia.Media.Color.FromUInt32(0xFF000000u | next.MarkHue);
        mark.Background = new Avalonia.Media.SolidColorBrush(hue, .22);
        mark.BorderBrush = new Avalonia.Media.SolidColorBrush(hue, .62); mark.BorderThickness = new Thickness(1);
        sub.SetText(next.ServiceText + (next.NeverRunsOut ? " · " + MultiAccountWords.NoLimit.Split(" · ")[0] : ""));
        newService.IsVisible = next.NewService;
        skipped.IsVisible = next.Skipped;
        words.Opacity = line2.Opacity = mark.Opacity = number.Opacity = next.Skipped ? .45 : 1;
        config.SetText((next.SameAsAccount ? MultiAccountWords.SameAsAccount : "") + ModelLine(next));
        bolt.IsVisible = next.Fast;
        AutomationProperties.SetName(this, MultiAccountWords.StepStatement(next.Number, next.AccountName, next.Model));
        if (next.NeverRunsOut)
        {
            slider = null;
            never ??= LibraryUi.Text(MultiAccountWords.NeverRunsOut, 12, secondary: true);
            never.VerticalAlignment = VerticalAlignment.Center;
            host.Child = never;
        }
        else
        {
            if (slider is null)
            {
                var brand = EffortBrand.Create(next.Service);
                slider = new HandshakeSliderControl(() => Point(), p => editor.SetStepHandshake(step.Index, Core(p)), "Handshake point for " + next.AccountName, brand, pulse);
                host.Child = slider;
            }
            slider.Lock(next.HandshakeLocked, "Same for every step: set it with the slider at the top");
        }
        panel?.Apply();
    }

    private Marrow.Core.Port.Accounts.HandshakePoint Point() => step.HandshakeAt.Percent is { } p ? new Marrow.Core.Port.Accounts.HandshakePoint(p) : Marrow.Core.Port.Accounts.HandshakePoint.Smart;

    private static Marrow.Core.Accounts.HandshakePoint Core(Marrow.Core.Port.Accounts.HandshakePoint p) => p.Percentage is { } percent ? Marrow.Core.Accounts.HandshakePoint.At(percent) : Marrow.Core.Accounts.HandshakePoint.Smart;

    private static string ModelLine(StepView s) => s.Effort is { Length: > 0 } effort ? s.Model + " · " + ModelOption.EffortTitle(effort) : s.Model;

    private void ToggleSettings() => settingsPopup.Toggle();

    private void Remove()
    {
        if (editor.RemoveStep(step.Index)) return;
        feedback.Set(0, null);
        feedback.Set(1, FrameClock.IsBound ? Animation.Linear(duration: .3) : null);
    }

    private void EndDrag(double dy)
    {
        if (!dragging) return;
        dragging = false;
        dragFade.Set(1, FrameClock.IsBound ? Animation.EaseOut(duration: .3) : null);
        RenderTransform = null;
        ZIndex = 0;
        var target = AccountRowControl.DragTarget(step.Index, dy, RowHeight, editor.Steps.Count);
        if (target != step.Index) editor.MoveStep(step.Index, target);
    }
}
