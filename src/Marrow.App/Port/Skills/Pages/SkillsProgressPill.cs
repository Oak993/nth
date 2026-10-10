// PORT-ONLY: RR-17, OD-0099 Minimize A. Geometry/words: locked sd-page.css:456-493 and sd-page.js:326,364-370.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>The same app-lifetime runs in the main sidebar and beneath the Settings sidebar entries.</summary>
internal sealed class SkillsProgressPills : Decorator
{
    public SkillsProgressPills(bool nested = false)
    {
        var stack = new StackPanel { Spacing = 0 };
        foreach (var section in new[] { LibrarySection.Plugins, LibrarySection.Skills })
            if (SkillsPageSeams.Controllers.Current.For(section) is { } model)
                stack.Children.Add(new SkillsProgressPill(model, Open, nested));
        if (SkillsPageSeams.Controllers.Current.Tools() is { } tools)
            stack.Children.Add(new SkillsProgressPill(tools, _ => { SettingsNavigation.Shared.RequestedPage = SettingsPage.Updates; WindowManager.Shared.ShowSettings(); }, nested));
        Child = stack;
    }

    private static void Open(LibrarySection section)
    {
        SettingsNavigation.Shared.RequestedPage = section == LibrarySection.Plugins ? SettingsPage.Plugins : SettingsPage.Skills;
        WindowManager.Shared.ShowSettings();
    }
}

internal sealed class SkillsProgressPill : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool presented;
        public bool Presented { get => Read(presented); set => Set(ref presented, value); }
    }

    private readonly LibraryModel model;
    private readonly Action<LibrarySection> open;
    private readonly ViewState state = new();
    private readonly Panel mark = new();
    private readonly CzCrossfadeText words = new("") { FontSize = 12.5, FontWeight = FontWeight.Medium };
    private readonly ProgressRing ring = new() { Width = 20, Height = 20 };
    private readonly SkillsMotionNumber reveal, opacity;
    private readonly SkillsLeadFace ringLead, pauseLead, checkLead, warningLead;
    private readonly Control face;
    private RunPhase lastPhase;
    private bool shown;
    private bool primed;

    public SkillsProgressPill(LibraryModel model, Action<LibrarySection> open, bool nested = false)
    {
        this.model = model;
        this.open = open;
        mark.Width = mark.Height = 20;
        ringLead = new SkillsLeadFace(ring);
        pauseLead = new SkillsLeadFace(LibraryUi.Glyph("pause", 12, ChromeTokens.SecondaryText));
        checkLead = new SkillsLeadFace(LibraryUi.Glyph("checkmark.circle.fill", 20, SwiftColor.Rgb(0x34C759)));
        warningLead = new SkillsLeadFace(LibraryUi.Glyph("exclamationmark.triangle.fill", 12, ChromeTokens.Warning)) { Width = 12, Height = 12 };
        warningLead.WithWarningPosition();
        mark.Children.Add(ringLead); mark.Children.Add(pauseLead); mark.Children.Add(checkLead); mark.Children.Add(warningLead);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mark, words }, VerticalAlignment = VerticalAlignment.Center };
        Button = PlainControls.Button(row, () => state.Presented = !state.Presented).KeyboardFocus();
        Button.Height = 34;
        Button.Padding = new Thickness(9, 0, 14, 0);
        face = Button.ChromeGlassCapsule();
        face.HorizontalAlignment = HorizontalAlignment.Left;
        face.IsHitTestVisible = false;
        Button.IsEnabled = false;
        face.RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative);
        Popover = PlainControls.Popover(Button, new Binding<bool>(() => state.Presented, value => state.Presented = value), BuildPopover,
            PlacementMode.TopEdgeAlignedLeft, buildBeforeOpen: !nested);
        Popover.SetValue(Presentation.MotionProperty, new SkillsPopoverMotion(this));
        Child = new Panel { Children = { face, Popover } };
        opacity = new SkillsMotionNumber(face, 0, value => face.Opacity = value);
        var scale = new ScaleTransform(.92, .92);
        var shift = new TranslateTransform(0, 10);
        var transform = new TransformGroup { Children = { scale, shift } };
        reveal = new SkillsMotionNumber(face, 0, value =>
        {
            scale.ScaleX = scale.ScaleY = .92 + .08 * value;
            shift.Y = (1 - value) * 10;
            face.RenderTransform = LibraryUi.Still(this) ? null : transform;
            InvalidateMeasure();
        });
        this.Track(() => (model.Revision, model.DialogHidden), _ => Refresh());
    }

    internal Button Button { get; }
    internal Popup Popover { get; }
    internal bool IsShown => shown;
    internal double Revealed => reveal.Value;
    internal Control Face => face;
    internal Control RingLead => ringLead;
    internal Control CheckLead => checkLead;
    internal string LabelText => words.Text;
    internal SkillsProgressPopover BuildPopover() => new(model, () => { state.Presented = false; model.DialogHidden = false; open(model.Controller.Section); });

    private void Refresh()
    {
        var view = model.Dialog;
        var nextShown = model.DialogHidden && view.Phase is RunPhase.Downloading or RunPhase.Paused or RunPhase.Done or RunPhase.Partial;
        if (!nextShown) state.Presented = false;
        if (shown != nextShown)
        {
            shown = nextShown;
            face.IsHitTestVisible = shown;
            Button.IsEnabled = shown;
            var delay = shown ? .12 : 0;
            opacity.Set(shown ? 1 : 0, .3, delay: delay, animate: primed);
            reveal.Set(shown ? 1 : 0, .4, movement: true, delay: delay, animate: primed);
        }
        primed = true;
        if (!shown) return;
        var done = view.Lines.Count(l => l.State == RunLineState.Downloaded);
        var count = view.Lines.Count(l => l.State is not (RunLineState.AlreadyHere or RunLineState.NeedsTerms or RunLineState.NoSize));
        var label = view.Phase switch
        {
            RunPhase.Downloading => "Downloading " + Math.Min(count, done + 1) + " of " + count,
            RunPhase.Paused => "Paused \u00b7 " + done + " of " + count,
            RunPhase.Done => "Downloaded",
            _ => view.Failed + " didn\u2019t download",
        };
        words.SetText(label);
        Button.Named(label, "skills-progress-pill-" + (model.Tools ? "Tools" : model.Controller.Section.ToString()));
        ring.Value = view.Overall; // the nth ring springs to it itself (step 4)
        if (lastPhase != view.Phase)
        {
            lastPhase = view.Phase;
            var donePhase = view.Phase == RunPhase.Done;
            ringLead.Show(view.Phase is RunPhase.Downloading or RunPhase.Partial, animate: true, delay: donePhase ? .45 : 0, completedRing: donePhase);
            pauseLead.Show(view.Phase == RunPhase.Paused, animate: true);
            checkLead.Show(donePhase, animate: true, delay: donePhase ? .45 : 0);
            warningLead.Show(view.Phase == RunPhase.Partial, animate: true);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        return new Size(measured.Width, measured.Height * reveal.Value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(new Size(finalSize.Width, face.DesiredSize.Height));
        return finalSize;
    }
}

internal static class SkillsWarningPosition
{
    public static Control WithWarningPosition(this Control glyph)
    {
        glyph.HorizontalAlignment = HorizontalAlignment.Right;
        glyph.VerticalAlignment = VerticalAlignment.Bottom;
        glyph.Margin = new Thickness(0, 0, -5, -4);
        return glyph;
    }
}

internal sealed class SkillsProgressPopover : Border
{
    private readonly LibraryModel model;
    private readonly CzCrossfadeText title = new("") { FontSize = 13, FontWeight = FontWeight.SemiBold };
    private readonly CzCrossfadeText progress = new("") { FontSize = 12, Secondary = true };
    private readonly SkillsCompactBody body = new();
    private readonly CrossfadeSlot buttonsSlot = new() { Margin = new Thickness(0, 9, 0, 0) };
    private string? key;

    public SkillsProgressPopover(LibraryModel model, Action enlarge)
    {
        this.model = model;
        Width = 360;
        Padding = new Thickness(14, 12);
        this.RoundedRectangle(16, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(this, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        var enlargeGlyph = LibraryUi.Glyph("arrow.up.right.square", 12, ChromeTokens.SecondaryText);
        enlargeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        enlargeGlyph.VerticalAlignment = VerticalAlignment.Center;
        var enlargeFace = new Border { Width = 26, Height = 26, Child = enlargeGlyph }.RoundedRectangle(8, RoundedCornerStyle.Continuous);
        var hover = new AnimatedColor(enlargeFace);
        enlargeFace.Background = hover.Brush;
        hover.Set(ChromeTokens.Overlay(.06), null);
        Enlarge = PlainControls.Button(enlargeFace, enlarge).KeyboardFocus();
        Enlarge.Width = Enlarge.Height = 26;
        Enlarge.Named("Open in Settings", "skills-download-enlarge");
        Enlarge.OnHover(over => hover.Set(ChromeTokens.Overlay(over ? .12 : .06), LibraryUi.Still(this) ? Animation.EaseOut(.01) : CzMotion.Ease(.2)));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,26"), ColumnSpacing = 8, Children = { title, Enlarge } };
        Grid.SetColumn(Enlarge, 1);
        Child = new StackPanel { Children = { header, body, buttonsSlot } };
        this.Track(() => model.Revision, _ => Refresh());
        Refresh();
    }

    internal Button Enlarge { get; }
    internal Button Close { get; private set; } = null!;
    internal string ProgressText => progress.Text;
    internal string? PhaseKey => key;

    private void Refresh()
    {
        var view = model.Dialog;
        title.SetText(view.Phase == RunPhase.Done ? "All " + model.Noun + " downloaded" : view.Phase == RunPhase.Partial ? view.Failed + " didn\u2019t download" : view.Title);
        progress.SetText(view.ProgressText ?? "");
        var next = view.Phase switch { RunPhase.Downloading => "downloading", RunPhase.Paused => "paused", RunPhase.Done => "done", RunPhase.Partial => "partial", _ => "none" };
        if (key == next && view.Phase != RunPhase.Partial) return;
        var first = key is null;
        key = next;
        var stack = new StackPanel { Spacing = 9, Margin = new Thickness(0, 9, 0, 0) };
        if (view.IsRunning)
        {
            var bar = new LibraryProgressBar();
            var current = new CzCrossfadeText("") { FontSize = 12, Secondary = true };
            var ring = new ProgressRing { Width = 14, Height = 14 };
            var progressLine = new CzCrossfadeText(progress.Text) { FontSize = 12, Secondary = true };
            var overall = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { bar, progressLine } };
            Grid.SetColumn(progressLine, 1);
            stack.Children.Add(overall);
            stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ring, current } });
            stack.Track(() => model.Revision, _ =>
            {
                var run = model.Controller.Dialog;
                progressLine.SetText(run.ProgressText ?? "");
                bar.Set(run.Overall, true);
                var item = run.Lines.FirstOrDefault(l => l.State is RunLineState.Downloading or RunLineState.Checking or RunLineState.Waiting);
                current.SetText(item is null ? "" : item.Title + " \u00b7 " + Math.Round(item.Fraction * 100) + "%");
                ring.Value = item?.Fraction ?? 0; // never runs backwards: the next item starts from nothing
            });
        }
        else if (view.Phase == RunPhase.Done) stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { LibraryUi.Glyph("checkmark.circle.fill", 14, ChromeTokens.Success), LibraryUi.Text(view.Sub, 12, wrap: true) } });
        else foreach (var item in view.Lines.Where(l => l.State == RunLineState.Failed))
        {
            var failedName = LibraryUi.Text(item.Title, 12, weight: SwiftFont.Weight.Semibold);
            var reason = LibraryUi.Text(item.Right, 12, secondary: true, trim: true);
            var failure = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 7, Margin = new Thickness(0, 3),
                Children = { LibraryUi.Glyph("exclamationmark.triangle.fill", 12, ChromeTokens.Warning), failedName, reason } };
            Grid.SetColumn(failedName, 1); Grid.SetColumn(reason, 2);
            stack.Children.Add(failure);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        Button Make(string label, Action action, ButtonLook look = ButtonLook.Plain) { var b = LibraryButtons.Make(label, action, look, "skills-compact-" + label, 11.5, 24); buttons.Children.Add(b); return b; }
        if (view.IsRunning)
        {
            Make(LibraryWords.Cancel, model.Controller.StopRun);
            if (view.Phase == RunPhase.Paused) Make(LibraryWords.Resume, model.Controller.Resume, ButtonLook.Primary);
            else Make(LibraryWords.Pause, model.Controller.Pause, ButtonLook.Accent);
        }
        else
        {
            Close = Make(LibraryWords.Close, model.Controller.CloseRun);
            if (view.Phase == RunPhase.Partial) Make(LibraryWords.RetryCount(view.Failed), () => _ = model.Controller.RetryFailed(), ButtonLook.Primary).IsEnabled = model.Controller.CanFetch;
        }
        body.Show(next, stack, animate: !first);
        buttonsSlot.Show(buttons, first || !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(.01) : CzMotion.Ease(.22));
    }
}
