using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/MessageEditor.swift (B3 52bc792), :1-176.

/// <summary>:7-176 <c>struct MessageEditor: View</c>: edits a message already sent.</summary>
public sealed class MessageEditor : Decorator
{
    /// <summary>:12-24 <c>private enum Changes</c>, a record union (R-ENUM-2).</summary>
    private abstract record Changes
    {
        private Changes() { }

        public sealed record Checking : Changes; // :13
        public sealed record Found(RevertPreview Value) : Changes; // :14
        public sealed record Unavailable(string Value) : Changes; // :15
    }

    // :26-29 @State values for the editor.
    private sealed class ViewState : MainActorModel
    {
        private Changes changes = new Changes.Checking();
        private bool restoresFiles = true;
        private string? error;
        private bool isSending;

        public Changes Changes { get => Read(changes); set => Set(ref changes, value); } // :26
        public bool RestoresFiles { get => Read(restoresFiles); set => Set(ref restoresFiles, value); } // :27
        public string? Error { get => Read(error); set => Set(ref error, value); } // :28
        public bool IsSending { get => Read(isSending); set => Set(ref isSending, value); } // :29
    }

    // :31-37 init(edit:runtime:)
    public MessageEditor(PromptEdit edit, ThreadRuntime runtime)
    {
        var state = new ViewState();

        // :34-36 A completed preview fetched by the pencil is shown without a checking state.
        if (runtime.CachedRevertPreview(edit.Id) is { } cached)
        {
            try
            {
                state.Changes = new Changes.Found(cached.GetAwaiter().GetResult());
            }
            catch (Exception exception) when (SwiftTry.IsThrownError(exception))
            {
                state.Changes = new Changes.Unavailable(SwiftErrors.LocalizedDescription(exception));
            }
        }

        // :46-55 are PromptEditor's @ViewBuilder `extra`, so in Swift each view is a child of PromptEditor.swift:59
        // VStack(spacing: 14): this stack keeps that 14-point gap between them.
        var extra = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // :47-55 fileChanges, notice and the optional resend error.
        extra.Track(
            () => (state.Changes, state.RestoresFiles, state.Error),
            _ =>
            {
                extra.Children.Clear();
                extra.Children.Add(BuildFileChanges(state));
                extra.Children.Add(Callout("Sending replaces this message and everything after it.", Chrome.SecondaryText)); // :48-50
                if (state.Error is { } error)
                    extra.Children.Add(IconLabel(error, "exclamationmark.triangle", Chrome.Danger)); // :51-54
            });

        // :56-62 action row.
        // :57-60 OD-0005: the established capsule/glass recipe with the app-owned material.
        var cancelLabel = new TextBlock { Text = "Cancel", VerticalAlignment = VerticalAlignment.Center };
        var cancelFrame = new Border { Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), Height = Chrome.CapsuleContentHeight, Child = cancelLabel };
        cancelFrame.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(cancelFrame, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        var cancelButton = PlainControls.Button(cancelFrame, () => Cancel(runtime)); // :57
        cancelButton.IsCancel = true;
        var sendButton = WelcomeView.GlassProminentButton(new TextBlock { Text = "Send" }, Chrome.CapsuleContentHeight, () => Send(edit, runtime, state)); // :59-60

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelButton, sendButton },
        };

        var editor = new PromptEditor(
            "Edit message", // :41
            new Binding<string>(() => edit.Text, value => edit.Text = value), // :42
            new Binding<ValueList<Attachment>>(() => edit.Attachments, value => edit.Attachments = value), // :43
            () => Send(edit, runtime, state), // :44
            () => Cancel(runtime), // :45
            () => extra, // :46
            () => actions); // :56
        Child = editor;

        // :61-63 The send button follows canSend and the whole editor is disabled while sending.
        editor.Track(() => state.IsSending, sending => editor.IsEnabled = !sending);
        sendButton.Track(
            () => (state.Changes, edit.IsEmpty, state.IsSending, runtime.IsRunning),
            _ => sendButton.IsEnabled = CanSend(edit, runtime, state));

        // :64-69 `.task(id: edit.id)`: cancellation on detach or edit-id change; a cancelled preview writes nothing.
        this.Task(() => edit.Id, async id =>
        {
            try
            {
                var preview = await runtime.RevertPreview(id).Value;
                if (SwiftTask.IsCancelled) return;
                state.Changes = new Changes.Found(preview);
            }
            catch (Exception exception) when (SwiftTry.IsThrownError(exception))
            {
                if (SwiftTask.IsCancelled) return;
                state.Changes = new Changes.Unavailable(SwiftErrors.LocalizedDescription(exception));
            }
        });

        // :173-175 Escape carries the cancel action.
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Cancel(runtime);
            args.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    // :72-76 canSend.
    private static bool CanSend(PromptEdit edit, ThreadRuntime runtime, ViewState state)
    {
        if (state.Changes is Changes.Checking) return false;
        return !edit.IsEmpty && !state.IsSending && !runtime.IsRunning;
    }

    // :78-121 fileChanges.
    private static Control BuildFileChanges(ViewState state)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        switch (state.Changes)
        {
            case Changes.Checking:
                var checking = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), HorizontalAlignment = HorizontalAlignment.Stretch };
                checking.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 }); // :82-83
                var checkingText = Callout("Checking for file changes\U00002026", Chrome.SecondaryText); // U+2026 HORIZONTAL ELLIPSIS; :84-86
                checkingText.Margin = new Thickness(8, 0, 0, 0); // HStack(spacing: 8); the text wraps at the width left
                Grid.SetColumn(checkingText, 1);
                checking.Children.Add(checkingText);
                content.Children.Add(checking);
                break;

            case Changes.Unavailable unavailable:
                content.Children.Add(IconLabel(unavailable.Value, "exclamationmark.triangle", Chrome.SecondaryText)); // :87-89
                break;

            case Changes.Found found when found.Value.Files.Count == 0:
                content.Children.Add(IconLabel("This thread changed no files since this message", "checkmark.circle", Chrome.SecondaryText)); // :90-92
                break;

            case Changes.Found found when found.Value.Restorable.Count == 0:
                var preview = found.Value;
                var message = preview.Files.Count == 1
                    ? "The file it changed has changed again since, so it stays as it is"
                    : $"The {preview.Files.Count} files it changed have changed again since, so they stay as they are";
                content.Children.Add(IconLabel(message, "hand.raised", Chrome.SecondaryText)); // :93-98
                content.Children.Add(BuildFileList(preview.Files, state.RestoresFiles)); // :99
                break;

            case Changes.Found found:
                content.Children.Add(BuildRestoreToggle(found.Value, state)); // :100-113
                content.Children.Add(BuildFileList(found.Value.Files, state.RestoresFiles)); // :114
                break;

            default:
                throw new UnreachableException();
        }

        var box = new Border
        {
            Padding = new Thickness(12), // :118
            HorizontalAlignment = HorizontalAlignment.Stretch, // :119
            Child = content,
        }.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        box.Background = Chrome.Overlay(0.05).Brush(box); // :120
        return box;
    }

    // :100-114 Undo toggle, count and aggregate diff stat.
    private static Control BuildRestoreToggle(RevertPreview preview, ViewState state)
    {
        var restorable = preview.Restorable;
        var label = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var title = Callout("Undo the file changes this thread made", Chrome.PrimaryText); // :104
        title.Margin = new Thickness(0, 0, 16, 0); // :105 Spacer(minLength: 8) between two HStack gaps of 8: 24 points at least before the count
        var count = Callout(restorable.Count == 1 ? "1 file" : $"{restorable.Count} files", Chrome.SecondaryText); // :106-107
        count.Margin = new Thickness(8, 0, 0, 0);
        var stat = CreateDiffStat(checked((int)preview.Additions), checked((int)preview.Deletions)); // :108
        stat.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(title, 0);
        Grid.SetColumn(count, 1);
        Grid.SetColumn(stat, 2);
        label.Children.Add(title);
        label.Children.Add(count);
        label.Children.Add(stat);

        // :112-113 .toggleStyle(.switch).controlSize(.small): the label (:110 maxWidth .infinity) and then the switch at the
        // trailing edge. The switch is Settings' SmallSwitch, drawn as the owner's Nocta (OD-0141) like every switch in the app (its ENGINEERING DECISION: Avalonia's ToggleSwitch needs the
        // Fluent template this app does not carry and threw KeyNotFoundException 'PART_MovingKnobs' when laid out).
        // ENGINEERING DECISION (UNVERIFIED metric): 8 points between the label and the switch, the HStack spacing used
        // across this row (:103); the source gives no number for a switch toggle's own label gap.
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var toggle = new Marrow.App.Views.Settings.SmallSwitch(new Binding<bool>(() => state.RestoresFiles, value => state.RestoresFiles = value))
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(label);
        row.Children.Add(toggle);
        return row;
    }

    // :125-154 The file list, with kept and binary labels and row dimming.
    private static Control BuildFileList(ValueList<RevertPreview.File> files, bool restoresFiles)
    {
        var rows = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6, // :126
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        foreach (var file in files)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,4,Auto"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var icon = Icon(file.IsKept ? "hand.raised" : "doc", Chrome.SecondaryText, SwiftFont.Caption); // :129-130
            icon.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);

            var path = new MiddlePathTextBlock(file.Path); // :131-133
            path.Margin = new Thickness(0, 0, 8, 0); // :128-134 path-to-spacer spacing, separate from spacer-to-value
            Grid.SetColumn(path, 1);
            row.Children.Add(path);

            if (file.IsKept)
            {
                var kept = Caption("Kept", Chrome.SecondaryText); // :135-136
                kept.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(kept, 3);
                row.Children.Add(kept);
            }
            else if (file.IsBinary)
            {
                var binary = Caption("Binary", Chrome.SecondaryText); // :137-138
                binary.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(binary, 3);
                row.Children.Add(binary);
            }
            else
            {
                var stat = CreateDiffStat(checked((int)file.Additions), checked((int)file.Deletions)); // :139-140
                stat.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(stat, 3);
                row.Children.Add(stat);
            }

            row.Opacity = restoresFiles && !file.IsKept ? 1 : 0.45; // :143-145
            ToolTip.SetTip(row, file.IsKept
                ? $"{file.Path}\nChanged again since this thread last touched it, so it is left alone."
                : file.Path); // :146
            rows.Children.Add(row);
        }

        if (files.Count <= 5) return rows;
        return new ScrollViewer
        {
            Height = 110, // :149-150
            Content = rows,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    // :156-171 send().
    private static void Send(PromptEdit edit, ThreadRuntime runtime, ViewState state)
    {
        if (!CanSend(edit, runtime, state)) return;

        var restore = false;
        if (state.Changes is Changes.Found found)
            restore = state.RestoresFiles && found.Value.Files.Count != 0; // :158-159

        state.IsSending = true; // :160
        state.Error = null; // :161
        _ = MainActor.Spawn(async () =>
        {
            try
            {
                await runtime.Resend(edit.Id, edit.Text, edit.Attachments, restore); // :162-165
                runtime.MessageEdit = null; // :165
            }
            catch (Exception exception) when (SwiftTry.IsThrownError(exception))
            {
                state.Error = SwiftErrors.LocalizedDescription(exception); // :166-168
            }
            state.IsSending = false; // :169
        });
    }

    // :173-175 cancel().
    private static void Cancel(ThreadRuntime runtime) => runtime.MessageEdit = null;

    // :82-99, :129-140 Small symbol image.
    private static SwiftImage Icon(string symbol, SwiftColor color, SwiftFont font) =>
        SwiftImage.SystemName(symbol).Font(font).ForegroundStyle(color);

    // :82-120 Callout text in the file-changes box.
    private static TextBlock Callout(string text, SwiftColor color)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; // no lineLimit: a Text wraps at the width it is offered
        SwiftFont.Callout.Apply(block);
        block.Foreground = color.Brush(block);
        return block;
    }

    // :129-140 Caption text in file rows.
    private static TextBlock Caption(string text, SwiftColor color)
    {
        var block = new TextBlock { Text = text };
        SwiftFont.Caption.Apply(block);
        block.Foreground = color.Brush(block);
        return block;
    }

    // :100-140 Shared diff-stat control.
    private static DiffStatLabel CreateDiffStat(int additions, int deletions) =>
        new() { Additions = additions, Deletions = deletions };

    // :87-99 A symbol and a label.
    private static Control IconLabel(string text, string symbol, SwiftColor color)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var icon = Icon(symbol, color, SwiftFont.Callout);
        var label = Callout(text, color);
        label.Margin = new Thickness(8, 0, 0, 0); // spacing 8; the label wraps at the width left of the icon
        Grid.SetColumn(label, 1);
        row.Children.Add(icon);
        row.Children.Add(label);
        return row;
    }

    // :131-133 Middle truncation for the verbatim path while retaining its readable end.
    private sealed class MiddlePathTextBlock : TextBlock
    {
        private readonly string path;
        private bool updating;

        public MiddlePathTextBlock(string path)
        {
            this.path = path;
            Text = path;
            TextWrapping = TextWrapping.NoWrap;
            TextTrimming = TextTrimming.None;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            SwiftFont.Caption.Apply(this);
            SizeChanged += (_, _) => UpdateDisplayedPath();
        }

        private void UpdateDisplayedPath()
        {
            if (updating || double.IsInfinity(Bounds.Width) || Bounds.Width <= 0) return;
            updating = true;
            try
            {
                if (MeasurePath(path) <= Bounds.Width)
                {
                    Text = path;
                    return;
                }

                var starts = StringInfo.ParseCombiningCharacters(path);
                var count = starts.Length;
                var displayed = "\U00002026";
                for (var retained = count - 1; retained >= 0; retained--)
                {
                    var leading = (retained + 1) / 2;
                    var trailing = retained / 2;
                    var prefixEnd = leading == count ? path.Length : starts[leading];
                    var suffixStart = trailing == 0 ? path.Length : starts[count - trailing];
                    var candidate = path[..prefixEnd] + "\U00002026" + path[suffixStart..];
                    if (MeasurePath(candidate) <= Bounds.Width)
                    {
                        displayed = candidate;
                        break;
                    }
                }

                Text = displayed;
            }
            finally
            {
                updating = false;
            }
        }

        private double MeasurePath(string text)
        {
            var font = SwiftFont.Caption.Resolve();
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(font.FontFamily, font.FontStyle, font.FontWeight),
                font.FontSize,
                Brushes.Black);
            return formatted.WidthIncludingTrailingWhitespace;
        }
    }
}
