using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Port;
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
    // :8-9 the view's inputs; :15-55 body
    public NewThreadPrompt(Guid threadID, string? projectName)
    {
        this.Track(() => AppEnvironment.GetAppModel(this), model => Build(model, threadID, projectName));
    }

    private void Build(AppModel model, Guid threadID, string? projectName)
    {
        // :17-21
        var icon = SwiftImage.Named("droppy-logo")
            .Resizable()
            .Interpolation(BitmapInterpolationMode.HighQuality)
            .AspectRatio(ContentMode.Fit);
        icon.Width = 72;
        icon.Height = 72;
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw); // :21 .accessibilityHidden(true)

        // :22-45 the question line
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
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

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(TextScaling.Apply(line, 0.6)); // :46 also reaches the text inside the project-name button.

        var content = new Border { Padding = new Thickness(40, 0), Child = stack };
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        // :50-53 fades and scales in from 0.97 over .easeOut(duration: 0.3) on appear
        content.Opacity = 0;
        content.RenderTransform = new ScaleTransform(0.97, 0.97);
        content.RenderTransformOrigin = RelativePoint.Center;
        Child = content;

        AttachedToVisualTree += (_, _) =>
        {
            Transaction.WithAnimation(Animation.EaseOut(duration: 0.3), () =>
            {
                content.Opacity = 1;
                content.RenderTransform = new ScaleTransform(1, 1);
            });
        };
    }

    // :23-38 the project name button, its dotted underline and its picker popover
    private Control MakeProjectNameButton(AppModel model, Guid threadID, string projectName)
    {
        var text = new TextBlock { Text = projectName, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }; // :28-29 Text(verbatim: projectName)
        SwiftFont.System(28, SwiftFont.Weight.Regular).Apply(text); // :43 the whole line shares one font
        text.Foreground = Chrome.PrimaryText.Brush(text); // :44
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
        label.Children.Add(text);
        label.Children.Add(underline);

        var state = new PickerState();
        var stateHolder = new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value);

        var button = PlainControls.Button(label, () => stateHolder.Value = !stateHolder.Value); // :26 isChoosingProject.toggle()
        button.Cursor = new Cursor(StandardCursorType.Hand);
        button.PointerEntered += (_, _) => SetUnderlineColor(true); // :33 onHover
        button.PointerExited += (_, _) => SetUnderlineColor(false);
        ToolTip.SetTip(button, "Choose another project"); // :34

        var popover = PlainControls.Popover(button, stateHolder, () => MakeProjectChoices(model, threadID, () => stateHolder.Value = false));

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

    // :12 @State private var isChoosingProject = false. A plain bool field is not observable: PlainControls.Popover
    // tracks its isPresented binding through Tracked<T>'s Read()/Notify() machinery, which only fires for a
    // property backed by an ObservableModel.
    private sealed class PickerState : MainActorModel
    {
        private bool isPresented;
        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }
}
