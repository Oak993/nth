using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Port;
using Marrow.App.Views.Tour;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/NewThreadPrompt.swift (B3 52bc792), :1-81.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` is
// read from the environment, not passed in.

/// <summary>:6-81 <c>struct NewThreadPrompt: View</c>: what a thread shows before its first message.</summary>
public sealed class NewThreadPrompt : Decorator
{
    private readonly Guid threadID;
    private string? projectName;
    private AppModel? shownModel;
    private SwapSlot? lineSlot;
    private SwapSlot? nameSlot;

    // :8-9 the view's inputs; :15-55 body
    public NewThreadPrompt(Guid threadID, string? projectName)
    {
        this.threadID = threadID; this.projectName = projectName;
        this.Track(() => AppEnvironment.GetAppModel(this), model => Build(model, threadID));
    }

    /// <summary>:9 <c>let projectName: String?</c> handed again (the reader picked another project in the prompt's picker, or the project was renamed): the same view, which words it says
    /// changed, and it does not appear again (:50-53 `isVisible` stays true and `.onAppear` does not run). The words change over the move's own motion (:77-78
    /// `withAnimation(Chrome.panelSlide)`), never by a cut (OD-0076: content that swaps crossfades and the line eases to its new width): the old name fades out as the new one fades
    /// in, and a name where there was none (or none where there was one) swaps the whole line the same way. ED-255 (3).</summary>
    internal void Update(string? nextProjectName)
    {
        if (nextProjectName == projectName) return;
        var renamed = projectName is { Length: > 0 } && nextProjectName is { Length: > 0 };
        projectName = nextProjectName;
        if (shownModel is not { } model || lineSlot is not { } line) return; // not built yet: the build reads the name
        var motion = Transaction.Current ?? Chrome.PanelSlide;
        if (renamed && nameSlot is { } names) names.Swap(NameText(nextProjectName!), motion); // :27-29 the same button, its picker and its hover, the words it shows changed
        else line.Swap(MakeLine(model), motion); // a name where there was none, or none where there was one: the line's other branch
    }

    private Control MakeLine(AppModel model)
    {
        // :22-45 the question line
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        nameSlot = null;
        if (projectName is { Length: > 0 })
        {
            line.Children.Add(MakeText("What should we build in ")); // :24
            line.Children.Add(MakeProjectNameButton(model, threadID, projectName)); // :25-37
            line.Children.Add(MakeText("?")); // :38
        }
        else
        {
            line.Children.Add(MakeText("What should we build?")); // :40
        }

        // :43-46 the whole line's font, color, single-line clamp and minimumScaleFactor(0.6) (ED-214).
        foreach (var child in line.Children)
            if (child is TextBlock text)
            {
                SwiftFont.System(28, SwiftFont.Weight.Regular).Apply(text);
                text.Foreground = Chrome.PrimaryText.Brush(text);
                text.MaxLines = 1;
                text.TextTrimming = TextTrimming.CharacterEllipsis;
            }
        return TextScaling.Apply(line, 0.6); // :46 also reaches the text inside the project-name button.
    }

    private void Build(AppModel model, Guid threadID)
    {
        if (shownModel is not null) { shownModel = model; lineSlot?.Swap(MakeLine(model), null); return; } // another window's model: the line's picker reads it (the same words)
        shownModel = model;
        // :17-21
        // :17-20 NSApp.applicationIconImage. OD-0136: the empty chat shows the 44 px nth tile B (nth-brand.html section 3, .hero .mk),
        // drawn as vectors, sweeping once as the chat opens, then still; 14 px above the line (.hero's gap).
        var icon = new NthTile();
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw); // :21 .accessibilityHidden(true)

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(icon);
        lineSlot = new SwapSlot(HorizontalAlignment.Center) { VerticalAlignment = VerticalAlignment.Center }; // the line stays centred in the column while it eases to a new width
        lineSlot.Swap(MakeLine(model), null);
        column.Children.Add(lineSlot); // :22-46 the question line

        var content = new Border { Padding = new Thickness(40, 0), Child = column };
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        // :50-53 fades and scales in from 0.97 over .easeOut(duration: 0.3) on appear. This build runs as the prompt attaches (Track applies on attach), so the
        // appearance is now: a handler a build adds to AttachedToVisualTree is not called for the attach it is running in, which left the prompt invisible.
        content.RenderTransformOrigin = RelativePoint.Center;
        Child = content;
        var fade = new AnimatedDouble(value => content.Opacity = value); fade.Set(0, null);
        var grow = new AnimatedDouble(value => content.RenderTransform = new ScaleTransform(value, value)); grow.Set(0.97, null);
        Transaction.WithAnimation(Animation.EaseOut(duration: 0.3), () => { fade.Set(1, Transaction.Current); grow.Set(1, Transaction.Current); });
    }

    // :23-38 the project name button, its dotted underline and its picker popover
    // :28-29 Text(verbatim: projectName), in the line's font and colour (:43-44) and with its minimumScaleFactor(0.6) (:46, ED-214).
    private static Control NameText(string projectName)
    {
        var text = new TextBlock { Text = projectName, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        SwiftFont.System(28, SwiftFont.Weight.Regular).Apply(text); // :43 the whole line shares one font
        text.Foreground = Chrome.PrimaryText.Brush(text); // :44
        return TextScaling.Apply(text, 0.6);
    }

    private Control MakeProjectNameButton(AppModel model, Guid threadID, string projectName)
    {
        // The name sits in a slot of its own, so a new name crossfades with the old one in the same button while the button (and its dotted underline) eases to the new width.
        var names = new SwapSlot(HorizontalAlignment.Left);
        names.Swap(NameText(projectName), null);
        nameSlot = names;
        var underline = new Line
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0), // stretched to the text's own width below
            StrokeThickness = 1,
            StrokeDashArray = [1, 2], // :29 .underline(true, pattern: .dot, ...)
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 1, 0, 0),
        };
        void SetUnderlineColor(bool hovering) =>
            underline.Stroke = (hovering ? Chrome.PrimaryText : Chrome.SecondaryText).Brush(underline); // :29
        SetUnderlineColor(false);

        var label = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        label.Children.Add(names);
        label.Children.Add(underline);

        var state = new PickerState();
        var stateHolder = new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value);

        var button = PlainControls.Button(label, () => stateHolder.Value = !stateHolder.Value); // :26 isChoosingProject.toggle()
        button.Cursor = new Cursor(StandardCursorType.Hand);
        button.PointerEntered += (_, _) => SetUnderlineColor(true); // :33 onHover
        button.PointerExited += (_, _) => SetUnderlineColor(false);
        ToolTip.SetTip(button, "Choose another project"); // :34

        var popover = PlainControls.Popover(button, stateHolder, () => MakeProjectChoices(model, threadID, () => stateHolder.Value = false), PlacementMode.Bottom); // :35 arrowEdge: .bottom

        var host = new Panel();
        host.Children.Add(button);
        host.Children.Add(popover);
        return host;
    }

    // :57-72
    private Control MakeProjectChoices(AppModel model, Guid threadID, Action close)
    {
        var current = model.Thread(threadID)?.ProjectID;
        var menu = new PopoverMenu(() =>
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            stack.Children.Add(new PopoverSectionHeader("Projects")); // :60
            foreach (var project in model.Projects) // :61-65
            {
                var thisProject = project;
                stack.Children.Add(new PopoverItem(
                    thisProject.Name,
                    () => Move(model, threadID, thisProject, close),
                    leading: new ProjectIconMark(thisProject.Icon, 12),
                    isChecked: thisProject.Id == current));
            }
            stack.Children.Add(new PopoverDivider()); // :66
            stack.Children.Add(new PopoverItem("Add project…", () => // :67-70, U+2026 HORIZONTAL ELLIPSIS
            {
                if (model.AddProjectsFromPanel() is { } added) Move(model, threadID, added, close);
            }, symbol: "plus"));
            return stack;
        });
        // DEFECT FIX (UI-CHAT-046/047): every PopoverItem row dismisses the popover it sits in before its action runs
        // (Popovers.swift:266, :271 `closePopoverBox?.close ?? dismiss()`); a SwiftUI .popover answers dismiss(), so here
        // the row's close is the picker's own isChoosingProject = false. Without it the popover stayed open after
        // choosing the current project (:75's guard) or cancelling the folder dialog (:69's guard).
        Popovers.SetClosePopover(menu, close);
        return menu;
    }

    // :74-80
    private static void Move(AppModel model, Guid threadID, Project project, Action close)
    {
        if (model.Thread(threadID) is not { } thread || thread.ProjectID == project.Id) return; // :75
        close(); // :76 isChoosingProject = false
        Transaction.WithAnimation(Chrome.PanelSlide, () => model.MoveThread(threadID, project)); // :77-78
    }

    private static TextBlock MakeText(string text) => new() { Text = text };

    /// <summary>
    /// ENGINEERING DECISION (OD-0076, ED-255 (3)): words that change in place, the old fading out where they stood as the new fade in, over a slot whose
    /// width glides from the old words' width to the new ones', so what sits beside it (the "?", the underline, the line's centre in the column) moves with it instead of
    /// jumping. The pattern the source uses for its own crossfading pill (TimelineRows.swift:973-1009 HydraMergeRow: only the new contents size the slot, the contents on
    /// show are drawn at their own size over it); the words keep their minimumScaleFactor (each child is measured at the room the slot is given, ED-214).
    /// </summary>
    private sealed class SwapSlot : Panel
    {
        private readonly HorizontalAlignment alignment;
        private readonly AnimatedDouble width;
        private Control? live;
        private double presented;
        private double target = double.NaN;
        private Animation? pending;
        private bool measuring;

        public SwapSlot(HorizontalAlignment alignment)
        {
            this.alignment = alignment;
            ClipToBounds = true; // as the source's slot (.clipped()): words wider than the slot on its way to its new width do not run over what sits beside it
            width = new AnimatedDouble(value => { presented = value; if (!measuring) Remeasure(); });
        }

        // The slot's own measure is retaken at the room it was last given, which the narrower width it had then capped: the rows above it are asked again too, up to
        // the prompt, so the room it is given grows with it (a horizontal row of the line hands each child a share of what it asks for, TextScaling's MeasuringRow).
        private void Remeasure()
        {
            for (Avalonia.Visual? at = this; at is Layoutable layoutable; at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at))
            {
                layoutable.InvalidateMeasure();
                if (at is NewThreadPrompt) break;
            }
        }

        /// <summary>Shows <paramref name="next"/> in place of the words on show: under <paramref name="animation"/> they crossfade and the slot eases to the new width; with
        /// none (the first words, or the same words again) they are simply there.</summary>
        public void Swap(Control next, Animation? animation)
        {
            var previous = live; live = next;
            if (previous is null || animation is null)
            {
                if (previous is not null) Children.Remove(SwiftTransition.Container(previous));
                Children.Add(next);
                pending = null;
            }
            else
            {
                pending = animation;
                Transaction.WithAnimation(animation, () =>
                {
                    SwiftTransition.Remove(this, previous);
                    SwiftTransition.Insert(this, Children.Count, next.Transition(SwiftTransition.Opacity));
                });
            }
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            measuring = true;
            try
            {
                if (live is null) return default;
                // The width the slot goes to is the new words' own (unscaled) width; it glides there only after a swap, any other change of it (a resize) is followed at once.
                foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                if (live.DesiredSize.Width != target) { target = live.DesiredSize.Width; width.Set(target, pending); }
                pending = null;
                // The words are drawn at their own size, not at the slot's width on its way: only the squeeze the line itself puts on the slot (its minimumScaleFactor, when the
                // line is wider than the room it has) reaches them, as a share of each one's own width.
                var squeeze = presented > 0 && double.IsFinite(availableSize.Width) ? Math.Min(1, availableSize.Width / presented) : 1;
                var height = 0.0;
                foreach (var child in Children)
                {
                    if (squeeze < 1) child.Measure(new Size(child.DesiredSize.Width * squeeze, availableSize.Height));
                    height = Math.Max(height, child.DesiredSize.Height);
                }
                return new Size(Math.Min(presented, availableSize.Width), height);
            }
            finally { measuring = false; }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children)
            {
                var size = child.DesiredSize;
                var x = alignment == HorizontalAlignment.Center ? (finalSize.Width - size.Width) / 2 : 0;
                child.Arrange(new Rect(x, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
            }
            return finalSize;
        }
    }

    // :12 @State private var isChoosingProject = false. A plain bool field is not observable: PlainControls.Popover
    // tracks its isPresented binding through Tracked<T>'s Read()/Notify() machinery, which only fires for a
    // property backed by an ObservableModel.
    private sealed class PickerState : MainActorModel
    {
        private bool isPresented;
        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }
}
