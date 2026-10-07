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
    private readonly CrossfadeSlot slot = new();
    private readonly CrossfadeSlot mark = new();
    private readonly CzCrossfadeText words = new("") { FontSize = 12.5, FontWeight = FontWeight.Medium };
    private readonly ProgressRing ring = new() { Width = 20, Height = 20 };
    private readonly AnimatedScalar progress;
    private readonly AnimatedScalar reveal;
    private readonly Control face;
    private RunPhase lastPhase;
    private bool shown;
    private bool primed;

    public SkillsProgressPill(LibraryModel model, Action<LibrarySection> open, bool nested = false)
    {
        this.model = model;
        this.open = open;
        mark.Width = mark.Height = 20;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { mark, words }, VerticalAlignment = VerticalAlignment.Center };
        Button = PlainControls.Button(row, () => state.Presented = !state.Presented).KeyboardFocus();
        Button.Height = 34;
        Button.Padding = new Thickness(9, 0, 14, 0);
        face = Button.ChromeGlassCapsule();
        face.HorizontalAlignment = HorizontalAlignment.Left;
        face.RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative);
        Popover = PlainControls.Popover(Button, new Binding<bool>(() => state.Presented, value => state.Presented = value), BuildPopover,
            PlacementMode.TopEdgeAlignedLeft, buildBeforeOpen: !nested);
        Child = new Panel { Children = { slot, Popover } };
        progress = new AnimatedScalar(ring, 0, value => ring.Value = value);
        reveal = new AnimatedScalar(face, 0, value => face.RenderTransform = new TransformGroup { Children = { new ScaleTransform(.92 + .08 * value, .92 + .08 * value), new TranslateTransform(0, (1 - value) * 10) } });
        slot.Show(new Border { Height = 0 }, null);
        this.Track(() => (model.Revision, model.DialogHidden), _ => Refresh());
    }

    internal Button Button { get; }
    internal Popup Popover { get; }
    internal bool IsShown => shown;
    internal double Revealed => reveal.Value;
    internal string LabelText => words.Text;
    internal SkillsProgressPopover BuildPopover() => new(model, () => { state.Presented = false; model.DialogHidden = false; open(model.Controller.Section); });

    private Animation? Motion(double seconds, bool movement = false) => !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(.01) : movement ? CzMotion.Ez(seconds) : CzMotion.Ease(seconds);

    private void Refresh()
    {
        var view = model.Dialog;
        var nextShown = model.DialogHidden && view.Phase is RunPhase.Downloading or RunPhase.Paused or RunPhase.Done or RunPhase.Partial;
        if (!nextShown) state.Presented = false;
        if (shown != nextShown)
        {
            shown = nextShown;
            slot.Show(shown ? face : new Border { Height = 0 }, primed ? Motion(.4, movement: true) : null);
            reveal.Set(shown ? 1 : 0, primed ? Motion(.4, movement: true) : null);
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
        progress.Set(view.Overall, Motion(.35, movement: true));
        if (lastPhase != view.Phase)
        {
            lastPhase = view.Phase;
            if (ring.Parent is Panel previousParent) previousParent.Children.Remove(ring);
            Control lead = view.Phase switch
            {
                RunPhase.Paused => LibraryUi.Glyph("pause", 12, ChromeTokens.SecondaryText),
                RunPhase.Done => LibraryUi.Glyph("checkmark.circle.fill", 20, SwiftColor.Rgb(0x34C759)),
                RunPhase.Partial => new Panel { Children = { ring, LibraryUi.Glyph("exclamationmark.triangle.fill", 12, ChromeTokens.Warning).WithWarningPosition() } },
                _ => ring,
            };
            mark.Show(lead, Motion(.25));
        }
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
    private readonly CrossfadeSlot body = new();
    private string? key;

    public SkillsProgressPopover(LibraryModel model, Action enlarge)
    {
        this.model = model;
        Width = 360;
        Padding = new Thickness(14, 12);
        this.RoundedRectangle(16, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(this, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        Enlarge = LibraryButtons.Glyph("arrow.up.right.square", "Open in Settings", "skills-download-enlarge", enlarge);
        Enlarge.Width = Enlarge.Height = 26;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,26"), ColumnSpacing = 8, Children = { title, Enlarge } };
        Grid.SetColumn(Enlarge, 1);
        Child = new StackPanel { Spacing = 9, Children = { header, body } };
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
        var stack = new StackPanel { Spacing = 9 };
        if (view.IsRunning)
        {
            var bar = new LibraryBar();
            var current = new CzCrossfadeText("") { FontSize = 12, Secondary = true };
            var ring = new ProgressRing { Width = 16, Height = 16 };
            stack.Children.Add(bar);
            var progressLine = new CzCrossfadeText(progress.Text) { FontSize = 12, Secondary = true };
            stack.Children.Add(progressLine);
            stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ring, current } });
            stack.Track(() => model.Revision, _ =>
            {
                var run = model.Controller.Dialog;
                progressLine.SetText(run.ProgressText ?? "");
                bar.Set(run.Overall, true);
                var item = run.Lines.FirstOrDefault(l => l.State is RunLineState.Downloading or RunLineState.Checking or RunLineState.Waiting);
                current.SetText(item is null ? "" : item.Title + " \u00b7 " + Math.Round(item.Fraction * 100) + "%");
                ring.Value = item?.Fraction ?? 0;
            });
        }
        else if (view.Phase == RunPhase.Done) stack.Children.Add(LibraryUi.Text(view.Sub, 12, wrap: true));
        else foreach (var item in view.Lines.Where(l => l.State == RunLineState.Failed))
            stack.Children.Add(LibraryUi.Text(item.Title + " " + item.Right, 12, wrap: true));
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
        stack.Children.Add(buttons);
        body.Show(stack, first || !FrameClock.IsBound ? null : LibraryUi.Still(this) ? Animation.EaseOut(.01) : CzMotion.Ease(.22));
    }
}
