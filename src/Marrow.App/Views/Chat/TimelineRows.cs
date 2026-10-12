using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.App.Views.Tour;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :1-2859.
// All six parts, including the inherited cloud bodies, audited against the pinned 1.7.0 source.
// The small control-size modifier remains unwritten pending an approved metric mapping (TimelineRowsTests STOP #5).
// Additional record-verification gaps are explicit in TimelineRowsTests STOP #6-11; passing subsets are not full parity.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; a Shape is a record whose Path(in:) returns an Avalonia Geometry; NSView is Control;
// a parameter without a default that follows one with a default moves before it and callers name their arguments.
// The file's free functions (:31-34, :38-54) and its `extension View` (:1415-1429) are the static class TimelineRows,
// named after the file (R-NAME-4); the two free functions are fileprivate in Swift and read by other types of this
// file, so they are internal. An enum with associated values is an abstract record (R-ENUM-2); `[T]` in a value is
// ValueList<T>; a Swift tuple result keeps its labels as C# tuple element names, first letter upper-cased. A Swift
// argument label that is a C# keyword is written with `@` (MCPProxy.Url(string @for) precedent).
// `[ObjectIdentifier]` (:2519) is represented by entry identity in DerivedKey. Environment/state storage and
// private Swift helpers live on their owning views. The Swift-private view types remain public, as the App views
// ported so far do (ShortcutsSettingsPage.cs).

namespace Marrow.App.Views.Chat;

/// <summary>:8-28 <c>enum TimelineMetrics</c>.</summary>
public static class TimelineMetrics
{
    public const double RowSpacing = 20;      // :10
    public const double HoverLineHeight = 22; // :14
    public const double IconWidth = 16;       // :17
    public const double IconSpacing = 8;      // :18
    public const double PillLeading = 12;     // :24
    public const double PillTrailing = 14;    // :25
    public const double PillVertical = 8;     // :26
    public const double PillRadius = 18;      // :27
}

/// <summary>The file's free functions (:31-54) and its <c>extension View</c> (:1415-1429), R-NAME-4.</summary>
public static class TimelineRows
{
    /// <summary>A text a row lets the reader select (.textSelection(.enabled): :2121, :2124, :2152, :2394 and the head popover's
    /// :1900, :1922, :1982, :1989, :2000), made by the one recipe every selectable text of the chat shares (VeiledText.SelectableText,
    /// ED-256 (5)). The I-beam over it: SOURCE FACT for the notice's message, which the reference video shows
    /// under the pointer at 2-chat-use.mov 14.74 s (CATALOG segment 15, "Pointer becomes an I-beam over message text"); the other
    /// sites carry the same modifier. A selection drawn in the system's selected-text colour, as on the Mac. A SelectableTextBlock has
    /// neither a cursor nor a SelectionBrush of its own (no control theme is loaded), so without them a drag selected text that was
    /// never painted. PR-CHAT-166.</summary>
    internal static SelectableTextBlock SelectableText(bool wraps) => VeiledText.SelectableText(wraps);

    // :2023, :2449; UI/Common/Components.swift:37-40. ED-197 / O-65: shared Components.Duration is still B2.
    internal static string Duration(double interval)
    {
        var seconds = Math.Max(0, SwiftNumber.TruncatingToInt(SwiftNumber.Rounded(interval)));
        return SwiftFormatters.DurationUnits(seconds,
            [SwiftFormatters.DurationUnit.Seconds, SwiftFormatters.DurationUnit.Minutes, SwiftFormatters.DurationUnit.Hours], SwiftFormatters.CurrentLocale.Current);
    }

    // :30-34 private func copyMessageText(_:): the pasteboard half of CopyButton (MarkdownView.swift), for the message
    // menus. PR-CHAT-081.
    internal static void CopyMessageText(string text)
    {
        var reflowed = ProseReflow.Reflowed(text); // :33
        // ENGINEERING DECISION: NSPasteboard.general is the one system clipboard, which Avalonia reaches only through a
        // TopLevel (MarkdownView.cs CopyButton, RootView.cs CopyCommandButton). The menu captures values only (:168-171),
        // so there is no view here: the TopLevel is the window of the current event, else the key window, else the main
        // window, the order WindowRectAnchor.Target (AttachmentPreviewPanel.cs) takes for NSApp.currentEvent?.window,
        // keyWindow and mainWindow. Every Windows TopLevel's clipboard is the same system clipboard.
        if (PasteboardWindow()?.Clipboard is not { } clipboard) return;
        _ = Write(clipboard, reflowed);

        static async Task Write(IClipboard clipboard, string value)
        {
            await clipboard.ClearAsync(); // :32 NSPasteboard.general.clearContents()
            await clipboard.SetTextAsync(value); // :33 setString(_:forType: .string): plain text only
        }

        static TopLevel? PasteboardWindow()
        {
            if (AppCurrentEvent.CurrentWindow is { } current) return current;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow;
            return null;
        }
    }

    // :36-54 private func lastCodeBlock(in:) -> String?: the last whole fenced code block's code, if the text holds one:
    // a simple scan for lines starting with three backticks. PR-CHAT-089.
    internal static string? LastCodeBlock(string @in)
    {
        string? last = null; // :39
        List<string>? open = null; // :40
        foreach (var line in @in.Split("\n")) // :41 components(separatedBy: "\n") (UNVERIFIED O-13)
        {
            if (SwiftString.HasPrefix(line, "```")) // :42
            {
                if (open is { } done) // :43-45
                {
                    last = string.Join("\n", done);
                    open = null;
                }
                else
                {
                    open = []; // :47
                }
            }
            else
            {
                open?.Add(line); // :50
            }
        }
        return last; // :53
    }

    // :1415-1428 func messageMenu(text:runtime:) on View: the right-click popover of a reply, opened at the pointer,
    // wherever its text is shown. Snapshots only: the popover builder must not capture the row. PR-CHAT-089.
    public static Control MessageMenu(this Control view, string text, ThreadRuntime runtime) =>
        view.RightClickPopover(() => AssistantMessageRow.MessageActions(text, LastCodeBlock(text), runtime)); // :1421-1427

    // :2739-2756, :264-284 and the other standard pill labels in this file.
    internal static TextBlock ChatText(string text, SwiftFont.TextStyle style = SwiftFont.TextStyle.Callout,
        SwiftFont.Weight weight = SwiftFont.Weight.Regular, SwiftColor? color = null, bool singleLine = false)
    {
        var label = new TextBlock { Text = text, TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap };
        ChatFont.Chat(style, weight).Apply(label);
        label.Foreground = (color ?? SwiftColor.Primary).Brush(label);
        if (singleLine) { label.MaxLines = 1; label.TextTrimming = TextTrimming.CharacterEllipsis; }
        return label;
    }

    // :280-284, :458-460, :570-572, :2754-2756.
    internal static SwiftImage Chevron() => SwiftImage.SystemName("chevron.right")
        .Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.SecondaryText);

    /// <summary>PORT-ONLY. The animation of a change OD-0076 adds where the Mac snaps, or none when the system asks for reduced motion (ED-264 (3)): the delegation
    /// card's changes that :703-704 do not key. The Mac's own animations (the work group's fold, the checklist's strike, the card's count and `sent` changes) are
    /// not passed through it (ED-264 (2)).</summary>
    internal static Animation? UnlessReduced(Animation? animation) => Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion ? null : animation;

    // :313-316, :464-467, :579-582, :691-694.
    /// <param name="fillsSlot">The pill takes the whole size of the slot it is eased in (a <see cref="SizeSlot"/> with ContentFollowsSize), larger or smaller
    /// than its contents', so its edge, background and corners move along the slot's curve in both directions (:652-704, OD-0076); any other pill hugs its contents.</param>
    internal static Border Pill(Control child, SwiftColor? fill = null, Thickness? padding = null, bool fillsSlot = false)
    {
        var pill = new Border { Child = child };
        pill.HorizontalAlignment = fillsSlot ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        pill.VerticalAlignment = fillsSlot ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        pill.Padding = padding ?? new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical);
        pill.RoundedRectangle(TimelineMetrics.PillRadius, RoundedCornerStyle.Continuous);
        pill.Background = (fill ?? Chrome.Accent.Opacity(0.1)).Brush(pill);
        return pill;
    }

    // :2298-2306, :2789-2793. Same glass button recipe as RequestCards / FollowUpEditor (OD-0005).
    internal static Button GlassCommand(string title, Action action, bool prominent, bool small = false)
    {
        if (small)
        {
            // :2796 `.controlSize(.small)` on Cancel (.glass) and Revert (.glassProminent): inside the popover's tint (Popovers.swift:8-12, presentedChrome) both are the small accent
            // glass capsule ED-280 (2) measured in the reference video (20 pt high, an 11 pt white label, 10.5 pt either side), and ED-257 (3) draws `.glassProminent` with the
            // same capsule, always filled. ENGINEERING DECISION (ED-314): the window's measurement is carried into the popover, which has the same accent tint.
            var smallButton = PlainControls.Button(new TextBlock { Text = title }, action);
            SwiftGlass.StyleAccentGlassButton(smallButton, small: true);
            return smallButton;
        }
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(label);
        var frame = new Border { Child = label, Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0),
            Height = Chrome.CapsuleContentHeight };
        frame.Capsule(RoundedCornerStyle.Continuous);
        var effect = GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive();
        if (prominent) { effect = effect.Tint(Chrome.Accent, 0.3); frame.Background = Chrome.Accent.Opacity(0.3).Brush(frame); }
        SwiftGlass.SetEffect(frame, effect);
        return PlainControls.Button(frame, action);
    }
}

/// <summary>:59-62 <c>@Observable private final class MessageMenuRequests</c> (R-OBS-1).</summary>
public sealed class MessageMenuRequests : ObservableModel
{
    private bool confirmRevert; // :61 = false

    // :61 var confirmRevert = false
    public bool ConfirmRevert { get => Read(confirmRevert); set => Set(ref confirmRevert, value); }
}

/// <summary>:64-229 <c>struct UserMessageRow: View</c>. PR-CHAT-077 to PR-CHAT-082.</summary>
public sealed class UserMessageRow : Decorator
{
    private readonly TimelineEntry entry;   // :65
    private readonly ThreadRuntime runtime; // :66
    private bool canRevert;                 // :69, handed again by a pass of the timeline's body (SetCanRevert)
    private TimelineItem.Content? built;    // the content the body was last made from

    // :71-72 @State private var isHovering = false, isConfirmingRevert = false (both read by the body).
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;
        private bool isConfirmingRevert;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

        public bool IsConfirmingRevert { get => Read(isConfirmingRevert); set => Set(ref isConfirmingRevert, value); }
    }

    private readonly ViewState state = new();

    // :73-74 @State private var menuRequests = MessageMenuRequests(): carries the edit-from-here intent out of the
    // context menu without retaining the row.
    private readonly MessageMenuRequests menuRequests = new();

    // :75-76 @State private var editor = PromptEditorPopover<MessageEditor>(): the editor popover, anchored to the pencil.
    private readonly PromptEditorPopover<MessageEditor> editor = new();

    /// <param name="entry">:65 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:66 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="canRevert">:67-69 <c>let canRevert: Bool</c>: whether the message can be edited right now, which
    /// rewinds its turn. Decided by the timeline.</param>
    // :78-200 body. It reads entry.item (an observed property), so the branch is rebuilt whenever the item changes;
    // the row's own state above survives, as a SwiftUI view's @State does across bodies.
    public UserMessageRow(TimelineEntry entry, ThreadRuntime runtime, bool canRevert)
    {
        this.entry = entry;
        this.runtime = runtime;
        this.canRevert = canRevert; revertable.Value = canRevert;

        void Build(TimelineItem.Content content)
        {
            if (ReferenceEquals(content, built)) return;
            built = content;
            Child = Body(content);
        }
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, Build);

        // :198 .onDisappear { editor.close() }: the edit itself survives on the runtime. The row has disappeared only if it is
        // still out of the tree once the timeline's redraw is over: ThreadTimeline.Draw takes every kept row out of its old
        // stack and puts it into a new one inside one synchronous pass, and an entry that arrives while the editor is open
        // must not close it (its @State, MessageEditor.swift:26-29, would be built anew). The same reading of onDisappear
        // as BadgePopoverCoordinator.Disappear and the row menu (PR-CHAT-080). ENGINEERING DECISION ED-256.
        // This covers the row's own close only: the editor's real Popup hangs from the AttachmentAnchorCapture inside this row and
        // closes itself in the very Remove call that takes the row out of its stack, and the anchor capture opens it again on a new
        // MessageEditor, until ThreadTimeline.Draw keeps its kept rows in place (ThreadTimeline.swift:384-391; STOP #11).
        DetachedFromVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(this)) editor.Close();
        });
    }

    /// <summary>The row handed whether its message can be edited again (:67-69, decided by the timeline, which flips it for every row at every turn start and end on a provider that
    /// rewinds): the same row and the same body. `canRevert` reaches two places of the body only, the hover line's pencil (:131 `if canRevert`) and the right-click menu's entry
    /// (:168-178, read when the menu opens); the attachment strip with its preview, the quote chips, the bubble's selectable text, a brief's popover and the row's hover, its
    /// pending confirmation and its editor stand where they are, as views whose place in the body does not change keep their @State. ED-255 (3).</summary>
    internal void SetCanRevert(bool next)
    {
        if (canRevert == next) return;
        canRevert = next; revertable.Value = next;
        if (hoverLine is { } line) FillHoverLine(line.Line, line.Message, line.IsEditing);
    }

    // :168 the menu's snapshot of `canRevert`, held outside the row so the popover's builder reads it when the menu opens without capturing the row (the source's
    // capture list keeps the row out of the builder for the same reason).
    private sealed class RevertFlag { public bool Value; }
    private readonly RevertFlag revertable = new();
    // The hover line of the body on show (null for a Hydra report or brief), so a new `canRevert` changes its pencil and nothing else.
    private (StackPanel Line, UserMessage Message, Func<bool> IsEditing)? hoverLine;

    // :79-199
    private Control? Body(TimelineItem.Content content)
    {
        hoverLine = null;
        if (content is TimelineItem.Content.User { Value.IsFromHydra: true } report) return new HydraReportRow(report.Value); // :79-80
        if (content is TimelineItem.Content.User { Value.IsHydraBrief: true } brief) return new HydraBriefRow(brief.Value, entry, runtime); // :81-82
        if (content is not TimelineItem.Content.User user) return null; // :83
        var message = user.Value;

        // :84-87 which message is open for editing is the thread's, so the editor survives the row: leaving the
        // thread closes the popover only, and the row coming back reopens it on the edit as it was left.
        bool IsEditing() => entry.TurnID is { } turnID && runtime.MessageEdit?.Id == turnID;

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right }; // :88
        if (message.Attachments.Count != 0) // :89-91
            column.Children.Add(new AttachmentStrip(message.Attachments) { HorizontalAlignment = HorizontalAlignment.Right });
        var parts = ReplyQuote.Peel(message.Text); // :92
        if (parts.Quotes.Count != 0) // :93-117
        {
            // :94-95 the quotes the message led with, as the chips the chat box showed them in, so a reply reads as a
            // reply and the bubble keeps only the typed words.
            var quotes = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4, // :96
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, UserBubble.Tail, 0), // :116 .padding(.trailing, UserBubble.tail)
            };
            foreach (var quote in parts.Quotes) quotes.Children.Add(QuoteChip(quote)); // :97
            column.Children.Add(quotes);
        }
        if (parts.Body.Length != 0) column.Children.Add(Bubble(parts.Body)); // :118-129
        column.Children.Add(HoverLine(message, IsEditing)); // :130-161

        // :163-164 .frame(maxWidth: .infinity, alignment: .trailing).padding(.leading, 96). The row is hit-testable
        // across its whole frame, as the SwiftUI hover and menu regions are.
        var row = new Border { Padding = new Thickness(96, 0, 0, 0), Background = Brushes.Transparent, Child = column };
        // :165-167 .onHover { [hovering = $isHovering] in hovering.wrappedValue = $0 }: the state box alone, so the hover
        // responder never keeps this row (and its message) alive after it scrolls away.
        var hover = state;
        row.OnHover(hovering => hover.IsHovering = hovering);
        // :168-178 .rightClickPopover { [message, canRevert, confirming = $menuRequests.confirmRevert] in ... }: snapshots
        // only; the revert intent goes through the binding alone. The flag is the one the row holds when the menu opens (a body run
        // again with a new `canRevert` hands the modifier a new snapshot), read from its box, not from the row.
        row.RightClickPopover(MenuBuilder(message.Text, revertable, menuRequests));
        // :179-183 the room for the hover line lives inside the row but is taken back out of its height, so the
        // controls show up in the gap to the next row. (6 = the VStack's spacing above them.)
        row.Margin = new Thickness(0, 0, 0, -(TimelineMetrics.HoverLineHeight + 6));

        OnChange<bool>? confirmWatch = null;
        OnChange<bool>? editingWatch = null;
        row.AttachedToVisualTree += (_, _) =>
        {
            // :184-190 .onChange(of: menuRequests.confirmRevert): the menu posts the intent, the row takes it up, like
            // the sidebar's menuRequests; the confirmation dialog then opens on the row itself.
            confirmWatch ??= new OnChange<bool>(() => menuRequests.ConfirmRevert, (_, asked) =>
            {
                if (!asked) return;
                menuRequests.ConfirmRevert = false;
                state.IsConfirmingRevert = true;
            });
            // :197 .onChange(of: isEditing) { _, editing in syncEditor(editing) }
            editingWatch ??= new OnChange<bool>(IsEditing, (_, editing) => SyncEditor(editing));
        };
        row.DetachedFromVisualTree += (_, _) =>
        {
            confirmWatch?.Dispose();
            confirmWatch = null;
            editingWatch?.Dispose();
            editingWatch = null;
        };
        // :191-196, ED-216. Dismissal resets the source's isPresented binding, so this row may ask again.
        row.Track(() => state.IsConfirmingRevert, presented =>
        {
            if (!presented) return;
            _ = MainActor.Spawn(async () =>
            {
                try
                {
                    await AlertPresentation.Show(new AlertRequest("Edit from this message?",
                        "This message and everything after it leave the thread, and your prompt returns to the composer.",
                        [new AlertButton("Revert and keep file changes", () => Revert(false)),
                         new AlertButton("Revert files too", () => Revert(true)) { Role = AlertButtonRole.Destructive }],
                        AlertKind.ConfirmationDialog, Owner: TopLevel.GetTopLevel(row) as Avalonia.Controls.Window));
                }
                finally { state.IsConfirmingRevert = false; }
            });
        });
        return row;
    }

    // :97-114 one quote chip.
    private static Control QuoteChip(ReplyQuote quote)
    {
        var glyph = SwiftImage.SystemName("text.quote")
            .Font(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold)); // :99-100
        glyph.Foreground = Chrome.SecondaryText.Brush(glyph); // :101
        var excerpt = new TextBlock
        {
            Text = quote.Excerpt, // :102 Text(verbatim: quote.excerpt)
            MaxLines = 1, // :105 .lineLimit(1)
            TextTrimming = TextTrimming.CharacterEllipsis, // :106 .truncationMode(.tail)
            MaxWidth = 260, // :107 .frame(maxWidth: 260, alignment: .leading)
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        SwiftFont.Subheadline.Apply(excerpt); // :103 .font(.system(.subheadline))
        excerpt.Foreground = Chrome.PrimaryText.Opacity(0.9).Brush(excerpt); // :104
        // :107 .frame(maxWidth: 260) under the chip's .fixedSize(): the frame is the words' own width up to 260, and a longer excerpt gets the whole 260 and is cut at
        // the tail inside it. A TextBlock reports only the width of the line it drew (the cut one, a pixel or two short of 260), which left the capsule narrower
        // than the frame: so a longer excerpt is given the frame's width itself.
        var probe = new TextBlock { Text = quote.Excerpt };
        SwiftFont.Subheadline.Apply(probe);
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (probe.DesiredSize.Width > 260) excerpt.Width = 260;
        // :98 HStack(spacing: 5); :109 .fixedSize(): a horizontal StackPanel measures its children at their ideal size.
        var chip = new HStack { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right }; // HStack :98: its children share one centre line
        chip.Children.Add(glyph);
        chip.Children.Add(excerpt);
        // :110 .modifier(ChipChrome()).
        var chrome = new ChipChrome().Body(chip);
        chrome.HorizontalAlignment = HorizontalAlignment.Right; // :96 VStack(alignment: .trailing): each chip at its own width, flush right
        ToolTip.SetTip(chrome, quote.Text); // :111 .help(quote.text)
        AutomationProperties.SetName(chrome, "Quote: " + quote.Excerpt); // :112-113 children combined, one label
        return chrome;
    }

    // :118-129 the bubble: the user's own words render like the answers do; hugsContent keeps the bubble hugging its
    // text instead of filling the row.
    private static Control Bubble(string body)
    {
        // :122 InlineText(parts.body, hugsContent: true); :123 .textSelection(.enabled): InlineText's text is selectable
        // itself (MarkdownBlockSelection's note, MarkdownView.cs).
        var text = new InlineText(body, hugsContent: true);
        // :124-127 14 leading, 14 + tail trailing (the tail hangs past the body; the text keeps its inset from it), 9
        // vertical.
        var inset = new Border { Padding = new Thickness(14, 9, 14 + UserBubble.Tail, 9), Child = text };
        // :128 .background(.tint.opacity(0.14), in: UserBubble()): .tint is the view tint, Chrome.accent (ChatBadge.cs).
        var shape = new BackgroundPath { Stretch = Stretch.None };
        shape.Fill = Chrome.Accent.Opacity(0.14).Brush(shape);
        inset.SizeChanged += (_, e) => shape.Data = new UserBubble().Path(new Rect(e.NewSize));
        var bubble = PlainControls.Background(inset, shape);
        bubble.HorizontalAlignment = HorizontalAlignment.Right;
        return bubble;
    }

    // :128 the shape is a SwiftUI background: it takes the bubble's size and never changes it. The tail's curve
    // (UserBubble :1190-1194) reaches a fraction of a point past the rect it is drawn in, so a Path measured at its
    // geometry would make the Panel taller, stretch the inset, redraw the shape taller, and so on every layout pass
    // (until the bubble was millions of pixels tall). Measuring the shape at zero keeps it out of layout; it is still
    // arranged at the bubble's size and draws its geometry where it was.
    private sealed class BackgroundPath : Avalonia.Controls.Shapes.Path
    {
        protected override Size MeasureOverride(Size availableSize) => default;
    }

    // :130-161 the hover line: the pencil when the message can be edited, in a line pinned to the hover line's room, so
    // the row keeps it (and the gap math holds) with no copy button left to size it.
    private Control HoverLine(UserMessage message, Func<bool> isEditing)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2, // :130
            Height = TimelineMetrics.HoverLineHeight, // :160
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        hoverLine = (line, message, isEditing);
        FillHoverLine(line, message, isEditing);
        // :161 .opacity(isHovering || isEditing ? 1 : 0). The Mac cuts here (2-chat-use.mov 23.415-24.187 s, catalogue
        // hover.message-edit-pencil); OD-0076 rules every change transitioned (ENGINEERING DECISION ED-256), so the line fades in the Mac's own hover
        // language for a message's hover line (the reply row's :1381 easeOut 0.12), and reduce motion stills it.
        var opacity = new AnimatedOpacity(line, state.IsHovering || isEditing() ? 1 : 0);
        line.Track(() => state.IsHovering || isEditing(), shown => opacity.Set(shown ? 1 : 0,
            Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion ? null : Animation.EaseOut(duration: 0.12)));
        return line;
    }

    // :131-159 what the hover line holds: the pencil when the message can be edited (the line itself, its room and its fade stand whatever `canRevert` says).
    private void FillHoverLine(StackPanel line, UserMessage message, Func<bool> isEditing)
    {
        line.Children.Clear();
        if (canRevert && entry.TurnID is { } turnID) // :131
        {
            var target = runtime;
            var glyph = SwiftImage.SystemName("pencil"); // :136
            // :137-138 .frame(width: 22, height: 22).contentShape(.rect)
            var frame = new Border { Width = 22, Height = 22, Background = Brushes.Transparent, Child = glyph };
            // :132-135 the pencil toggles: a second tap while open closes the editor. :140 .buttonStyle(.borderless): no
            // distinct borderless template exists (CopyButton's ENGINEERING DECISION, MarkdownView.cs), so the plain one.
            var pencil = PlainControls.Button(frame, () =>
                target.MessageEdit = isEditing() ? null : new PromptEdit(turnID, message.Text, message.Attachments));
            ToolTip.SetTip(pencil, "Edit message"); // :142
            // :143-147 the pointer settling on the pencil is the editor about to open: its file preview starts now, so
            // the click finds it done or nearly so.
            pencil.OnHover(hovering =>
            {
                if (hovering) _ = target.RevertPreview(turnID);
            });
            // :141 .foregroundStyle(isEditing ? AnyShapeStyle(.tint) : AnyShapeStyle(.secondary))
            pencil.Track(isEditing, editing => glyph.Foreground = (editing ? Chrome.Accent : SwiftColor.Secondary).Brush(glyph));
            // :148-155 .background { AttachmentAnchorCapture { anchor in editor.setAnchor(anchor); syncEditor(isEditing) } }:
            // the anchor lands in its window after the row appears, the moment a reopened thread can show the editor again.
            line.Children.Add(PlainControls.Background(pencil, new AttachmentAnchorCapture(anchor =>
            {
                editor.SetAnchor(anchor);
                SyncEditor(isEditing());
            })));
        }
    }

    // :202-206 private func revert(restoreFiles:), used by the confirmation dialog's two buttons (:192-193).
    private void Revert(bool restoreFiles)
    {
        if (entry.TurnID is not { } turnID) return; // :203
        // :204-205 a revert that cannot start (a turn still running) says so in the thread itself.
        var target = runtime;
        MainActor.Spawn(() => SwiftTry.IgnoreAsync(() => target.Revert(turnID, restoreFiles)));
    }

    // :208-212 private func syncEditor(_:)
    private void SyncEditor(bool isEditing)
    {
        var target = runtime;
        editor.Sync(isEditing ? runtime.MessageEdit : null, dismiss: () => target.MessageEdit = null, edit => new MessageEditor(edit, target));
    }

    // :168-171 the right-click popover's builder, made from the values it needs and nothing else. It is made here, in a static method, on purpose: the lambdas of
    // Body's own scope share one closure class with every other lambda of that scope, and that class holds `this` (Body's local functions read the row's entry and
    // runtime), so a builder written beside them would pin the whole row (its entry, its text, its state) for as long as the menu lives, which is what the
    // source's capture list is there to prevent (:56-62, :165-178; PR-CHAT-161). A static method cannot capture the row.
    private static Func<IReadOnlyList<RowAction>> MenuBuilder(string text, RevertFlag flag, MessageMenuRequests requests)
    {
        var confirming = new Binding<bool>(() => requests.ConfirmRevert, value => requests.ConfirmRevert = value);
        return () => MessageActions(text, flag.Value, confirming);
    }

    // :214-228 private static func messageActions(text:canRevert:confirmRevert:) -> [RowAction]: from value snapshots,
    // because the menu outlives the right-click and its callbacks must not retain the row.
    private static IReadOnlyList<RowAction> MessageActions(string text, bool canRevert, Binding<bool> confirmRevert)
    {
        var textSnapshot = text; // :217
        var items = new List<RowAction> // :218-221
        {
            new() { Title = "Copy message", Symbol = "doc.on.doc", Confirms = "Copied", Action = () => TimelineRows.CopyMessageText(MessageText.Plain(textSnapshot)) }, // :219
            new() { Title = "Copy as markdown", Symbol = "number", Confirms = "Copied", Action = () => TimelineRows.CopyMessageText(textSnapshot) }, // :220
        };
        if (canRevert) // :222-226
        {
            var confirm = confirmRevert;
            items.Add(new() { Title = "Edit from here", Symbol = "arrow.uturn.backward", StartsGroup = true, Action = () => confirm.Value = true });
        }
        return items; // :227
    }
}

/// <summary>:236-388 <c>struct HydraReportRow: View</c>.</summary>
public sealed class HydraReportRow : Decorator
{
    private sealed class ViewState : MainActorModel // :239
    {
        private bool showing;
        public bool IsShowingReport { get => Read(showing); set => Set(ref showing, value); }
    }
    private readonly ViewState state = new();
    /// <param name="message">:237 <c>let message: UserMessage</c>.</param>
    // :241-329 body
    public HydraReportRow(UserMessage message)
    {
        var personas = (message.HydraHeads ?? ValueList<long>.Empty).Select(index => HydraRoster.Persona(at: index)).ToList();
        var parts = SwiftString.Split(message.Text, "\n", maxSplits: 1);
        var title = personas.Count == 0 ? WithoutTrailingStop(parts.FirstOrDefault() ?? "Hydra") : ReportSummary(personas.Select(p => p.Name).ToList(), message.Text);
        var body = personas.Count == 0 ? (parts.Count > 1 ? parts[1] : "").Trimmed() : message.Text;
        var link = personas.Count == 0 ? LeadingURL(body) : null;
        var details = link is null ? body : WithoutFirstLine(body);
        var label = new HStack { Spacing = 8 }; // HStack :263: its children share one centre line
        if (personas.Count == 0) label.Children.Add(Mark(18));
        else
        {
            var glyphs = new HStack { Spacing = -4 }; // HStack :271: its children share one centre line
            foreach (var persona in personas) glyphs.Children.Add(new HydraGlyph(persona, 18));
            label.Children.Add(glyphs);
        }
        label.Children.Add(TimelineRows.ChatText(title, weight: SwiftFont.Weight.Medium, color: Chrome.PrimaryText.Opacity(0.9), singleLine: true));
        if (details.Length != 0) label.Children.Add(TimelineRows.Chevron());
        var box = state;
        var button = PlainControls.Button(label, () => { if (details.Length != 0) box.IsShowingReport = !box.IsShowingReport; });
        button.IsEnabled = details.Length != 0;
        ToolTip.SetTip(button, details.Length == 0 ? null : "Show the message");
        var row = new HStack { Spacing = 8 }; // HStack :258: its children share one centre line
        var report = button.BadgePopover(new Binding<bool>(() => box.IsShowingReport, value => box.IsShowingReport = value), () =>
        {
            if (link is not null && MergeRequestLink.Create(link) is { } request && HydraMergeNote.Parse(details, request) is { } note)
                return new HydraMergePopover(note);
            return personas.Count != 0 ? new HydraReportsPopover(title, body, personas) : new HydraReportPopover(body);
        });
        row.Children.Add(report);
        if (link is not null) row.Children.Add(OpenLink(link));
        Child = TimelineRows.Pill(row);
        this.Task(() => body, async _ =>
        {
            if (details.Length == 0) return;
            var texts = personas.Count == 0 ? new[] { body } : HydraReportDigest.Parse(body).Heads.Select(head => head.Body).ToArray();
            await SwiftTry.IgnoreAsync(() => MarkdownView.Warm(texts));
        });
        Padding = new Thickness(0, 0, 96, 0);
        AutomationProperties.SetName(this, title);
    }

    // :265-268, :660-663 and :719-721.
    internal static HydraMarkImage Mark(double side)
    {
        var mark = new HydraMarkImage { Width = side, Height = side };
        if (mark.Child is SwiftImage image) image.Foreground = Chrome.SecondaryText.Brush(image);
        return mark;
    }

    // :304-311 and :1130-1135.
    internal static Button OpenLink(SwiftURL link)
    {
        var open = PlainControls.Button(TimelineRows.ChatText("Open", SwiftFont.TextStyle.Caption, SwiftFont.Weight.Semibold, Chrome.SecondaryText),
            () => Marrow.Services.Port.Workspace.Open.Current(link));
        ToolTip.SetTip(open, "Open in the browser");
        return open;
    }

    // :332-334 nonisolated static func withoutTrailingStop(_:) -> String
    public static string WithoutTrailingStop(string title) => SwiftString.HasSuffix(title, ".") ? SwiftString.DropLast(title) : title;

    // :340-352 private static func reportSummary(names:text:) -> String
    private static string ReportSummary(IReadOnlyList<string> names, string text)
    {
        var outcomes = ReportOutcomes(text);
        var done = names.Where(name => !outcomes.ContainsKey(name)).ToList();
        var parts = new List<string>();
        if (done.Count != 0) parts.Add(List(done) + (done.Count == 1 ? " is done" : " are done"));
        foreach (var name in names.Where(outcomes.ContainsKey)) parts.Add(name + " " + outcomes[name]);
        return TextCleanup.SingleLine(string.Join(", ", parts), limit: 140);
    }

    // :355-358 private static func list(_:) -> String
    private static string List(IReadOnlyList<string> names) => names.Count <= 1 ? names.FirstOrDefault() ?? "" : string.Join(", ", names.SkipLast(1)) + " and " + names[^1];

    // :362-373 private static func reportOutcomes(in:) -> [String: String]
    private static ValueMap<string, string> ReportOutcomes(string @in)
    {
        var outcomes = new Dictionary<string, string>(SwiftStringComparer.Instance);
        foreach (var raw in SwiftString.ComponentsSeparatedByNewlines(@in).Where(line => line.Length != 0))
        {
            var line = FoundationText.Trim(raw);
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (!SwiftString.HasPrefix(line, "## ") || colon < 3) continue;
            var name = FoundationText.Trim(line[3..colon]);
            var lower = SwiftString.Lowercased(line[colon..]);
            if (lower.Contains("(failed", StringComparison.Ordinal)) outcomes[name] = "failed";
            else if (lower.Contains("(stopped", StringComparison.Ordinal)) outcomes[name] = "stopped";
        }
        return new ValueMap<string, string>(outcomes);
    }

    // :376-381 nonisolated static func leadingURL(in:) -> URL?
    public static SwiftURL? LeadingURL(string @in)
    {
        var first = SwiftString.ComponentsSeparatedByNewlines(@in).FirstOrDefault(line => line.Length != 0);
        if (first is null) return null;
        var line = FoundationText.Trim(first);
        var lower = SwiftString.Lowercased(line);
        if ((!SwiftString.HasPrefix(lower, "http://") && !SwiftString.HasPrefix(lower, "https://")) || SwiftString.Characters(line).Any(SwiftCharacter.IsWhitespace)) return null;
        return SwiftURL.Create(line);
    }

    // :384-387 nonisolated static func withoutFirstLine(_:) -> String
    public static string WithoutFirstLine(string body)
    {
        var index = 0;
        foreach (var character in SwiftString.Characters(body))
        {
            if (character.Any(FoundationText.IsNewline)) return body[index..].Trimmed();
            index += character.Length;
        }
        return "";
    }
}

/// <summary>:393-434 <c>struct HydraHeadsWorkingRow: View</c>.</summary>
public sealed class HydraHeadsWorkingRow : Decorator
{
    private ValueList<int> requestedHeads;
    private readonly ThreadRuntime runtime;
    private readonly StackPanel headsColumn = new() { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<long, (HydraHeadInfo Head, Control View)> shownHeads = [];
    private bool built; // the row has applied its team once: a pill made after that joins a parent on screen
    /// <param name="heads">:395 <c>let heads: [Int]</c>.</param>
    /// <param name="runtime">:396 <c>let runtime: ThreadRuntime</c>.</param>
    // :398-418 body
    public HydraHeadsWorkingRow(ValueList<int> heads, ThreadRuntime runtime)
    {
        requestedHeads = heads; this.runtime = runtime;
        var column = headsColumn;
        Child = column;
        Padding = new Thickness(0, 0, 96, 0);
        this.Track(() => AppEnvironment.GetAppModel(this).Helpers(runtime.ThreadID)
            .Where(thread => thread.IsHydraHead).ToArray(), team => ApplyTeam(team)); // :401-403
    }

    /// <summary>The row handed its heads again (:395, the block's value changed under the same id: a head finished): the same row, the pills already in it the same views (an open
    /// report stays open), a pill that joined gliding in beside them under the list's own animation (:411-415 ForEach(finished, id: \.index)). ED-255 (3).</summary>
    internal void Update(ValueList<int> heads)
    {
        if (requestedHeads.Equals(heads)) return;
        requestedHeads = heads;
        ApplyTeam(AppEnvironment.GetAppModel(this).Helpers(runtime.ThreadID).Where(thread => thread.IsHydraHead).ToArray());
    }

    // :401-418 body after the environment lookup.
    internal void ApplyTeam(IReadOnlyList<ChatThread> team)
    {
        var column = headsColumn;
        var heads = requestedHeads;
        var shown = shownHeads;
        var infos = team.Where(thread => thread.IsHydraHead).Select(thread => thread.Hydra).OfType<HydraHeadInfo>()
            .GroupBy(info => info.Index).ToDictionary(group => group.Key, group => group.First());
        var finished = heads.Where(index => infos.ContainsKey(index)).Select(index => infos[index]).ToList();
        Transaction.WithAnimation(Animation.Smooth(duration: 0.3), () =>
        {
            foreach (var index in shown.Keys.Where(index => finished.All(head => head.Index != index)).ToArray())
            { SwiftTransition.Remove(column, shown[index].View); shown.Remove(index); }
            for (var i = 0; i < finished.Count; i++)
            {
                var head = finished[i];
                if (shown.TryGetValue(head.Index, out var prior) && prior.View is FinishedHeadPill shownPill) { shownPill.Update(head); shown[head.Index] = (head, shownPill); continue; } // the same pill, the new record
                if (prior.View is not null) SwiftTransition.Remove(column, prior.View);
                var pill = new FinishedHeadPill(head).Transition(SwiftTransition.SettleGlide);
                shown[head.Index] = (head, pill);
                // :411-415 SwiftUI runs a transition only for a view that joins a parent already on screen: the pills a row has when it first appears are in place, with no
                // arrival (ED-314 (9)); the settle glide's wrappers are still built so a later removal can glide them away.
                if (built) SwiftTransition.Insert(column, Math.Min(i, column.Children.Count), pill);
                else SwiftTransition.Place(column, Math.Min(i, column.Children.Count), pill);
            }
        });
        built = true;
    }

    // :420-433 static func finishedHeads(in:) -> [HydraHeadInfo]: the finished heads whose report has not reached the
    // lead yet: a finished head whose batch still has a head at work, or, with no batch, a head that finished since the
    // earliest running head set out, so heads done in an earlier turn stay out. Order is the caller's. PR-CHAT-036.
    public static ValueList<HydraHeadInfo> FinishedHeads(IReadOnlyList<ChatThread> @in)
    {
        var infos = @in.Select(thread => thread.Hydra).OfType<HydraHeadInfo>().ToList(); // :424 compactMap(\.hydra)
        var running = infos.Where(info => info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running).ToList(); // :425
        if (running.Count == 0) return ValueList<HydraHeadInfo>.Empty; // :426
        var since = running[0].StartedAt; // :427 running.map(\.startedAt).min() (never nil here)
        foreach (var info in running)
            if (info.StartedAt < since) since = info.StartedAt;
        return new ValueList<HydraHeadInfo>(infos.Where(info => // :428-432
        {
            if (!info.IsFinished) return false; // :429
            if (info.BatchID is not { } batch) return (info.FinishedAt ?? SwiftDate.DistantPast) >= since; // :430
            return running.Any(head => head.BatchID == batch); // :431
        }));
    }
}

/// <summary>:438-476 <c>private struct FinishedHeadPill: View</c>.</summary>
public sealed class FinishedHeadPill : Decorator
{
    /// <param name="head">:439 <c>let head: HydraHeadInfo</c>.</param>
    // :443-475 body
    public FinishedHeadPill(HydraHeadInfo head)
    {
        this.head = head;
        var box = this.box;
        button = PlainControls.Button(TimelineRows.Pill(Label()), () => box.Showing = !box.Showing);
        ToolTip.SetTip(button, "Show the report");
        Child = button.BadgePopover(new Binding<bool>(() => box.Showing, value => box.Showing = value), () => new HydraHeadReportPopover(this.head));
        AutomationProperties.SetName(this, this.head.Persona.Name + Suffix);
    }

    private HydraHeadInfo head;
    private readonly Button button;
    private readonly ReportState box = new(); // :441 @State isShowingReport
    private string Suffix => head.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed ? " failed" : head.HydraHeadInfoStatus == HydraHeadInfo.Status.Stopped ? " was stopped" : " finished working";

    // :449-466 the glyph, the name and what became of the head, and the chevron.
    private Control Label()
    {
        var text = new TextBlock { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        (HydraNameStyle.Text(head.Persona, Chrome.PrimaryText.Opacity(0.75)) + new StyledText(Suffix))
            .Font(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)).Render(text);
        var row = new HStack { Spacing = 8 }; // HStack :452: its children share one centre line
        row.Children.Add(new HydraGlyph(head.Persona, 18, status: head.HydraHeadInfoStatus));
        row.Children.Add(text); row.Children.Add(TimelineRows.Chevron());
        return row;
    }

    /// <summary>The pill handed its head again (:439, `ForEach(finished, id: \.index)`: a head whose record changed under its index is the same view with the new value, and its @State
    /// stays): its words and glyph follow the head, the report it opens is the head's as it is, and an open report stays open. ED-255 (3).</summary>
    internal void Update(HydraHeadInfo next)
    {
        if (head == next) return;
        head = next;
        button.Content = TimelineRows.Pill(Label());
        AutomationProperties.SetName(this, head.Persona.Name + Suffix);
    }
    private sealed class ReportState : MainActorModel // :441
    {
        private bool showing;
        public bool Showing { get => Read(showing); set => Set(ref showing, value); }
    }
}

/// <summary>:480-521 <c>private struct HydraShimmer: ViewModifier</c>.</summary>
public sealed record HydraShimmer
{
    // :484 private static let sweep: TimeInterval = 1.8
    private const double Sweep = 1.8;

    // :486-520 func body(content:)
    public Control Body(Control content)
    {
        if (Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion) return content;
        var band = new Border { HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        var layer = new Panel { IsHitTestVisible = false, OpacityMask = new VisualBrush(content) { Stretch = Stretch.Fill } };
        layer.Children.Add(band);
        var root = new Panel { Children = { content, layer } };
        var width = 0.0;
        DateTimeOffset date = SwiftDate.Now;
        void Draw()
        {
            var side = Math.Max(24, width / 3);
            band.Width = side;
            var phase = SwiftDate.ReferenceSeconds(date) % Sweep / Sweep;
            band.RenderTransform = new TranslateTransform(-side + phase * (width + side), 0);
            var primary = Chrome.PrimaryText;
            band.Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(primary.Opacity(0).Resolve(band.ActualThemeVariant), 0),
                    new(primary.Opacity(0.9).Resolve(band.ActualThemeVariant), 0.5), new(primary.Opacity(0).Resolve(band.ActualThemeVariant), 1) }
            };
        }
        content.SizeChanged += (_, e) => { width = e.NewSize.Width; Draw(); };
        band.ActualThemeVariantChanged += (_, _) => Draw();
        root.TimelineView(() => new AnimationTimelineSchedule(1.0 / 30.0, ScrollActivity.Shared.Current.IsScrolling), now => { date = now; Draw(); });
        return root;
    }
}

/// <summary>:527-596 <c>struct HydraBriefRow: View</c>.</summary>
public sealed class HydraBriefRow : Decorator
{
    /// <param name="message">:528 <c>let message: UserMessage</c>.</param>
    /// <param name="entry">:529 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:530 <c>let runtime: ThreadRuntime</c>.</param>
    // :537-595 body
    public HydraBriefRow(UserMessage message, TimelineEntry entry, ThreadRuntime runtime)
    {
        var box = new BriefState();
        (int Count, bool Output)? scanned = null;
        Padding = new Thickness(96, 0, 0, 0);
        // :552-582 one Button with one `.badgePopover(isPresented:)` for the life of the row: the body re-evaluates for a new entry count
        // (:540-546) and rewrites only what the pill says, so a brief opened while the head works stays open (PR-CHAT-147). The button, its
        // badge popover and the column are made once; what a re-evaluation replaces is the pill the button holds, and the popover's content
        // reads the persona and the task as they are when it is presented.
        var persona = HydraRoster.Persona(at: 0);
        var column = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        if (message.Attachments.Count != 0) column.Children.Add(new AttachmentStrip(message.Attachments));
        var button = PlainControls.Button(new Border(), () => box.Showing = !box.Showing);
        button.Focusable = false;
        ToolTip.SetTip(button, "Show the brief");
        column.Children.Add(button.BadgePopover(new Binding<bool>(() => box.Showing, value => box.Showing = value), () => new HydraBriefPopover(persona, runtime.Thread?.Hydra?.Task, message.Text)));
        Child = column;
        void Build()
        {
            persona = runtime.Thread?.Hydra?.Persona ?? HydraRoster.Persona(at: 0);
            var count = runtime.Entries.Count;
            var output = scanned is { } prior && prior.Count == count ? prior.Output
                : entry.TurnID is not null && runtime.Entries.Any(candidate => candidate.TurnID == entry.TurnID && candidate.Id != entry.Id);
            scanned = (count, output);
            var cooking = !output && runtime.IsRunning;
            var title = cooking ? persona.Name + " is starting to cook\u2026" : persona.Name + "'s brief";
            var row = new HStack { Spacing = 8 }; // HStack :556: its children share one centre line
            row.Children.Add(new HydraGlyph(persona, 18, isRunning: cooking));
            var text = TimelineRows.ChatText(title, weight: SwiftFont.Weight.Medium, color: Chrome.PrimaryText.Opacity(0.9), singleLine: true);
            row.Children.Add(cooking ? new HydraShimmer().Body(text) : text);
            row.Children.Add(TimelineRows.Chevron());
            var pill = TimelineRows.Pill(row); pill.HorizontalAlignment = HorizontalAlignment.Right;
            button.Content = pill;
            AutomationProperties.SetName(button, title);
        }
        Build();
        this.Track(() => (runtime.Entries.Count, runtime.IsRunning, runtime.Thread?.Hydra), _ => Build());
    }
    private sealed class BriefState : MainActorModel // :532
    {
        private bool showing;
        public bool Showing { get => Read(showing); set => Set(ref showing, value); }
    }
}

/// <summary>:607-640 <c>private struct HydraReportPopover: View</c>.</summary>
public sealed class HydraReportPopover : Decorator
{
    /// <param name="text">:608 <c>let text: String</c>.</param>
    /// <param name="width">:609 <c>var width: CGFloat = 460</c>.</param>
    // :612-617 @MainActor init(text:width:); :619-639 body
    public HydraReportPopover(string text, double width = 460)
    {
        var blocks = MarkdownView.Blocks(text);
        var column = new StackPanel { Spacing = 10 };
        column.Font(SwiftFont.System(SwiftFont.TextStyle.Callout));
        MarkdownView.SetMarkdownPointSize(column, 12);
        MarkdownView.SetMarkdownDimmed(column, false);
        MarkdownView.SetMarkdownBlockSelection(column, true);
        foreach (var block in blocks)
        {
            var view = new MarkdownBlockView(block);
            MarkdownView.SetMarkdownPointSize(view, 12); MarkdownView.SetMarkdownDimmed(view, false);
            MarkdownView.SetMarkdownBlockSelection(view, true);
            column.Children.Add(view);
        }
        Child = new PopoverScroll(() => new Border { Padding = new Thickness(16, 14), Child = column }, maxHeight: 460, width: width);
        Width = width;
    }
}

/// <summary>:648-783 <c>struct HydraDelegationBlock: View</c>.</summary>
public sealed class HydraDelegationBlock : Decorator
{
    private readonly StackPanel column = new() { Spacing = 6 }; // :658
    private readonly OpacityCrossfade titleText; // :663-664 the title Text, `.contentTransition(.opacity)`: its words crossfade
    private readonly HStack header;
    private readonly Panel overlay = new();
    // The card as drawn: its slot and, over it and outside the slot's clip (nothing clips the card in SwiftUI), what leaves the card and the sent wave.
    private readonly CardFrame frame = new();
    private readonly Canvas leaving = new(); // what leaves the card under an animation, drawn fading where it was; it takes no room (a Canvas asks for none)
    private readonly Border pill;
    private readonly SizeSlot slot; // :703-704 the card's size (height and width) follows its contents along Chrome.panelSlide (OD-0076: a card that changes size eases to it)
    private StackPanel? rows;                       // :677 the heads' list, there once a brief has closed
    private Control? writing;                       // :681-686 "Writing the next brief…"
    private SwiftImage? check;                      // :667-675 the green check, once sent
    private DotFieldSweep? sweep;                   // :694-701 one wave through the card, once sent
    private ValueList<HydraDelegation> shown = ValueList<HydraDelegation>.Empty;
    private int count;                              // :704 the value `delegations.count` the card's animation is keyed on

    /// <summary>The block as it was last handed (:649-650): the card is the same card for every flush of a streaming reply, so what changes in it is animated.</summary>
    internal string Json { get; private set; }

    internal bool Sent { get; private set; }

    /// <param name="json">:649 <c>let json: String</c>.</param>
    /// <param name="sent">:650 <c>var sent = false</c>.</param>
    // :652-707 body
    public HydraDelegationBlock(string json, bool sent = false)
    {
        Json = json; Sent = sent;
        // :655-666 the mark and the title share a line; the heads run under both, each glyph in the mark's column.
        header = new HStack { Spacing = TimelineMetrics.IconSpacing }; // HStack :659: its children share one centre line
        header.Children.Add(HydraReportRow.Mark(16));
        titleText = new OpacityCrossfade(TimelineRows.ChatText("", weight: SwiftFont.Weight.Medium));
        header.Children.Add(titleText);
        column.Children.Add(header);
        pill = TimelineRows.Pill(column, fillsSlot: true);
        overlay.Children.Add(pill);
        pill.SizeChanged += (_, e) => { if (sweep is not null) sweep.Clip = SwiftShapes.RoundedRectangleGeometry(new Rect(e.NewSize), TimelineMetrics.PillRadius, RoundedCornerStyle.Continuous); }; // :698 .clipShape(RoundedRectangle(cornerRadius: pillRadius, style: .continuous)), through the one helper of ED-181
        slot = new SizeSlot(overlay) { ContentFollowsSize = true };
        frame.Children.Add(slot);
        frame.Children.Add(leaving);
        Child = frame;
        Apply(json, sent, animated: false);
        this.Transition(SwiftTransition.Opacity);
    }

    /// <summary>The card handed the block again (a flush of the streaming reply, or the reply sent): the same card, so its changes animate with Chrome.panelSlide
    /// (:703-704 `.animation(Chrome.panelSlide, value: sent)` and `value: delegations.count`): the new head rows come in with the opacity transition, the title changes
    /// in place, the check and the wave arrive, and the card's height follows its contents as a spring.</summary>
    internal void Update(string json, bool sent)
    {
        if (json == Json && sent == Sent) return;
        // :703-704 `.animation(Chrome.panelSlide, value: sent)` and `value: delegations.count`: only a change of either is animated on the Mac, along panelSlide
        // (its reduced-motion form, `.easeOut(duration: 0.18)`, included: Chrome.swift:100-104, ED-264 (1)). Any other change of a streamed block (a brief that
        // closes with the same count: the title's words, the next-brief line, the card's size) is made in no animation on the Mac (the streamed flush has no
        // withAnimation; ThreadRuntime.swift has it only at :2947 and :2953), so it eases under OD-0076 in the Mac's motion language, and reduced motion
        // stills that (ED-264 (3)). ENGINEERING DECISION: OD-0076's size change, .easeOut(duration: 0.3) (ED-285, fifth round (2)).
        var keyed = sent != Sent || Reading(json).Delegations.Count != count;
        Json = json; Sent = sent;
        var animation = keyed ? Chrome.PanelSlide : TimelineRows.UnlessReduced(OwnerChange);
        Transaction.WithAnimation(animation, () =>
        {
            slot.Note(Transaction.Current); // the layout pass that follows reads the animation the change was made under
            Apply(json, sent, animated: true);
        });
    }

    /// <summary>OD-0076: a change the Mac makes with no animation eases on the Mac's motion language for a size change ("about 0.3 s on an ease curve").</summary>
    internal static Animation OwnerChange => Animation.EaseOut(duration: 0.3);

    /// <summary>:706 `.accessibilityElement(children: .combine)`: one element, its children hidden (a peer with no children, as CommandPalette's row).</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new CombinedPeer(this);

    private sealed class CombinedPeer(HydraDelegationBlock owner) : ControlAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => []; // :706 children: .combine
    }

    /// <summary>PORT-ONLY. The card's own box: its slot, and the leaving layer and the sent wave over it, laid out at the slot's size and place on every layout
    /// (:694-701 the wave is the card's `.overlay`, which SwiftUI sizes to the card), outside the slot's clip (nothing clips the card).</summary>
    private sealed class CardFrame : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(availableSize);
            return Children.Count == 0 ? default : Children[0].DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var card = Children.Count == 0 ? default : new Rect(Children[0].DesiredSize);
            foreach (var child in Children) child.Arrange(card);
            return finalSize;
        }
    }

    // :653-703 what the body reads: the delegations so far, whether the block is still streaming, and `sent`.
    private void Apply(string json, bool sent, bool animated)
    {
        var reading = Reading(json);
        var delegations = reading.Delegations;
        var streaming = !reading.IsComplete && !sent;
        count = delegations.Count;
        AutomationProperties.SetName(this, Label(delegations, streaming, sent));
        // :663-664 `.contentTransition(.opacity)` under :703-704 `.animation(Chrome.panelSlide, ...)`: new words crossfade with the old (OD-0076).
        var title = Title(delegations, streaming, sent);
        if (((TextBlock)titleText.Current!).Text != title) titleText.Show(TimelineRows.ChatText(title, weight: SwiftFont.Weight.Medium));
        // :667-675 once sent, a green check follows the title.
        if (sent && check is null)
        {
            // ED-092 (1): documented framework .green, same HIG Default mapping as AppTheme.FrameworkGreen;
            // not theme success. Exact appearance is UNVERIFIED under OD-0001.
            var mark = SwiftImage.SystemName("checkmark.circle.fill").Font(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)).ForegroundStyle(SwiftColor.Rgb(0x34C759));
            AutomationProperties.SetName(mark, "sent");
            mark.Transition(SwiftTransition.Scale().Combined(SwiftTransition.Opacity));
            check = mark;
            if (animated) SwiftTransition.Insert(header, header.Children.Count, mark); else header.Children.Add(mark);
        }
        // :694-701 once sent, one wave of the accent flows through the card, clipped to the pill.
        if (sent && sweep is null)
        {
            sweep = new DotFieldSweep { Tint = Chrome.AccentNSColor, Clip = SwiftShapes.RoundedRectangleGeometry(new Rect(pill.Bounds.Size), TimelineMetrics.PillRadius, RoundedCornerStyle.Continuous) }; // :698, as above
            sweep.Transition(SwiftTransition.Opacity); // :700 .transition(.opacity)
            if (animated) SwiftTransition.Insert(frame, frame.Children.Count, sweep); else frame.Children.Add(sweep);
        }
        // :677-692 one row per head, the new ones in with the opacity transition; a block that is not a continuation of what was shown starts the list over.
        var continues = delegations.Count >= shown.Count && shown.Select((item, index) => Equals(item, delegations[index])).All(same => same);
        if (!continues && rows is not null)
        {
            if (delegations.Count != 0) rows.Children.Clear(); // with none to list the whole list goes (below), its rows with it
            writing = null;
            shown = ValueList<HydraDelegation>.Empty;
        }
        // :676 `if !delegations.isEmpty`: with no head to list there is no list, and no gap for it in the card's column.
        if (delegations.Count == 0 && rows is { } empty)
        {
            rows = null;
            if (animated) Leave(column, empty); else column.Children.Remove(empty);
        }
        // The list comes in as one view with its heads and its next-brief line (:676-692): they take no transition of their own then.
        var listArrives = false;
        if (delegations.Count != 0 && rows is null)
        {
            rows = new StackPanel { Spacing = 5 };
            listArrives = true;
            if (animated) SwiftTransition.Insert(column, 1, rows); else column.Children.Add(rows);
        }
        if (rows is not null)
        {
            var transitions = animated && !listArrives;
            for (var index = shown.Count; index < delegations.Count; index++)
            {
                var head = HeadRow(delegations[index]);
                var at = writing is not null ? rows.Children.IndexOf(writing) : rows.Children.Count;
                if (transitions) SwiftTransition.Insert(rows, at, head); else rows.Children.Insert(at, head);
            }
            shown = delegations;
            // :681-686 while a brief is still streaming a line says the next one is on its way, past a 16-point clear column.
            if (streaming && writing is null)
            {
                var next = new HStack { Spacing = TimelineMetrics.IconSpacing }; // HStack :681: its children share one centre line
                next.Children.Add(new Control { Width = 16, Height = 16 });
                next.Children.Add(TimelineRows.ChatText("Writing the next brief\u2026", SwiftFont.TextStyle.Caption, color: Chrome.SecondaryText));
                writing = next;
                // :681-686 a view that joins a stack comes in by the default opacity transition.
                if (transitions) SwiftTransition.Insert(rows, rows.Children.Count, next); else rows.Children.Add(next);
            }
            else if (!streaming && writing is { } gone)
            {
                writing = null;
                if (transitions) Leave(rows, gone); else rows.Children.Remove(gone);
            }
        }
        if (shown.Count == 0 && delegations.Count == 0) writing = null;
    }

    /// <summary>A view that leaves the card under the change's animation: SwiftUI lays the card out without it at once, so the card's frame eases to its new size
    /// along Chrome.panelSlide (:703-704) while the view fades out where it was (its removal transition; documented SwiftUI behaviour, UNVERIFIED under OD-0001 as
    /// ED-092 records). Left in its stack while it fades, it would hold the card at its old size and let it drop once the fade is done (OD-0076: nothing changes
    /// by a cut), so it is drawn from the leaving layer at the place and size it had. There it still takes the opacity its old parents give it (a removed view is
    /// drawn inside its parent: a list still fading in goes on fading in under it), it leaves from where its own arrival has got to if that is still running
    /// (RemoveContinuing, ED-278 (10)), and nothing clips it (the layer is outside the slot's clip).</summary>
    private void Leave(Panel from, Control gone)
    {
        var at = gone.TranslatePoint(default, leaving) ?? default;
        var size = gone.Bounds.Size;
        var parents = Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(from).TakeWhile(visual => !ReferenceEquals(visual, frame)).ToList();
        from.Children.Remove(gone);
        gone.Width = size.Width; gone.Height = size.Height; // its words keep the layout they had
        var holder = new Panel();
        Canvas.SetLeft(holder, at.X); Canvas.SetTop(holder, at.Y);
        void Follow() => holder.Opacity = parents.Aggregate(1.0, (product, parent) => product * parent.Opacity);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == Visual.OpacityProperty) Follow(); }
        Follow();
        foreach (var parent in parents) parent.PropertyChanged += Changed;
        holder.Children.Add(gone);
        leaving.Children.Add(holder);
        holder.Children.CollectionChanged += (_, _) =>
        {
            if (holder.Children.Count != 0) return;
            foreach (var parent in parents) parent.PropertyChanged -= Changed;
            leaving.Children.Remove(holder);
        };
        SwiftTransition.RemoveContinuing(holder, gone);
    }

    // :711-741
    private static Control HeadRow(HydraDelegation delegation)
    {
        var row = new HStack { Spacing = TimelineMetrics.IconSpacing }; // HStack :712: its children share one centre line
        if (Persona(delegation.Name) is { } persona)
        {
            var glyph = new HydraGlyph(persona, 16); AutomationProperties.SetName(glyph, persona.Name); row.Children.Add(glyph);
        }
        else row.Children.Add(HydraReportRow.Mark(16));
        row.Children.Add(TimelineRows.ChatText(delegation.Task, SwiftFont.TextStyle.Caption, color: Chrome.PrimaryText.Opacity(0.75), singleLine: true));
        if (ProjectName(delegation.Project) is { } project)
        {
            var tag = new HStack { Spacing = 3, Margin = new Thickness(2, 0, 0, 0) }; // HStack :728: its children share one centre line
            tag.Children.Add(SwiftImage.SystemName("arrow.right").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Medium)).ForegroundStyle(Chrome.SecondaryText));
            tag.Children.Add(TimelineRows.ChatText(project, SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Medium, Chrome.SecondaryText, true));
            AutomationProperties.SetName(tag, "in " + project); row.Children.Add(tag);
        }
        return row.Transition(SwiftTransition.Opacity);
    }

    /// <summary>:706 `.accessibilityElement(children: .combine)`: the label is the children's labels in order, joined with ", " as the port's other combined
    /// labels are (UsageViews.swift:237, "Banked resets, 2 available"): the title (:665), the check's "sent" (:672), each head's glyph by its own label, the
    /// persona's name (:716), its task (:721) and "in <project>" (:739), and the next-brief line (:683). The marks and the arrow are hidden (:664, :719, :731).</summary>
    private static string Label(IReadOnlyList<HydraDelegation> delegations, bool streaming, bool sent)
    {
        var parts = new List<string> { Title(delegations, streaming, sent) };
        if (sent) parts.Add("sent");
        foreach (var delegation in delegations)
        {
            if (Persona(delegation.Name) is { } persona) parts.Add(persona.Name);
            parts.Add(delegation.Task);
            if (ProjectName(delegation.Project) is { } project) parts.Add("in " + project);
        }
        if (delegations.Count != 0 && streaming) parts.Add("Writing the next brief\u2026");
        return string.Join(", ", parts);
    }

    // :745-753 private static func title(for:streaming:sent:) -> String
    private static string Title(IReadOnlyList<HydraDelegation> @for, bool streaming, bool sent)
    {
        if (streaming || @for.Count == 0) return "Summoning heads";
        var verb = sent ? "Summoned" : "Summoning";
        if (@for.Count == 1) return verb + " " + (Persona(@for[0].Name)?.Name ?? "a head");
        return verb + $" {@for.Count} heads";
    }

    // :756-759 private static func persona(named:) -> HydraPersona?
    private static HydraPersona? Persona(string? named) => named is null || named.Trimmed().Length == 0 ? null : HydraRoster.Persona(named: named);

    // :762-766 private static func projectName(_:) -> String?
    private static string? ProjectName(string? project)
    {
        var value = project?.Trimmed();
        if (string.IsNullOrEmpty(value)) return null;
        var name = value.Contains('/', StringComparison.Ordinal) ? SwiftPath.LastPathComponent(value) : value;
        return name.Length == 0 ? value : name;
    }

    // :776-782 @MainActor private static func reading(of:) -> HydraPrompts.StreamedDelegations
    private static readonly RecentCache<string, HydraPrompts.StreamedDelegations> readingCache = new(200); // :771
    private static HydraPrompts.StreamedDelegations Reading(string of)
    {
        MainActor.AssumeIsolated();
        if (readingCache.TryGetValue(of, out var cached)) return cached;
        var reading = HydraPrompts.StreamedDelegationsInBody(of);
        readingCache.Insert(reading, of); return reading;
    }
}

/// <summary>:786-789 <c>enum HydraMergePhase: Equatable</c> (R-ENUM-2).</summary>
public abstract record HydraMergePhase
{
    private HydraMergePhase() { }

    // :787 case merging
    public sealed record Merging : HydraMergePhase;

    // :788 case outcome(UserMessage)
    public sealed record Outcome(UserMessage Value) : HydraMergePhase;
}

/// <summary>:793-809 <c>private struct HydraMergeOutcome</c>.</summary>
public sealed record HydraMergeOutcome
{
    public string Title { get; init; }   // :794
    public string Body { get; init; }    // :796
    public SwiftURL? Link { get; init; } // :798
    public string Details { get; init; } // :800

    // :802-808 init(_ message: UserMessage)
    public HydraMergeOutcome(UserMessage message)
    {
        var parts = SwiftString.Split(message.Text, "\n", maxSplits: 1);
        Title = HydraReportRow.WithoutTrailingStop(parts.FirstOrDefault() ?? "Hydra");
        Body = (parts.Count > 1 ? parts[1] : "").Trimmed();
        Link = HydraReportRow.LeadingURL(Body);
        Details = Link is null ? Body : HydraReportRow.WithoutFirstLine(Body);
    }
}

/// <summary>:821-841 <c>struct HydraMergeTrack: View</c>.</summary>
public sealed class HydraMergeTrack : Decorator
{
    private readonly HydraMergeTrackBody body;
    private int step, count;
    private Mode mode;
    private double done, rest;
    /// <summary>:822 <c>enum Mode { case running, done, resting }</c>.</summary>
    public enum Mode
    {
        Running,
        Done,
        Resting,
    }

    /// <param name="step">:824 <c>let step: Int</c>.</param>
    /// <param name="count">:825 <c>let count: Int</c>.</param>
    /// <param name="mode">:826 <c>let mode: Mode</c>.</param>
    // :830-840 body
    public HydraMergeTrack(int step, int count, Mode mode)
    {
        this.step = step; this.count = count; this.mode = mode;
        done = mode == Mode.Running ? 0 : 1; rest = mode == Mode.Resting ? 1 : 0;
        body = new HydraMergeTrackBody(step, done, rest, count, !EnvironmentValues.GetAccessibilityReduceMotion(this));
        Child = body;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :971 accessibilityHidden(true).
        void Motion() => body.SetHops(!EnvironmentValues.GetAccessibilityReduceMotion(this));
        AttachedToVisualTree += (_, _) => Motion();
        PropertyChanged += (_, args) => { if (args.Property == EnvironmentValues.AccessibilityReduceMotionProperty) Motion(); };
        DetachedFromVisualTree += (_, _) => animation?.Cancel();
    }

    // :838-839: one interpolated triple, preserving its presented values on retargeting.
    public void Update(int step, int count, Mode mode)
    {
        if (this.count != count) { this.count = count; body.SetCount(count); }
        if (this.step == step && this.mode == mode) return;
        var from = body.AnimatableData;
        var newDone = mode == Mode.Running ? 0.0 : 1.0;
        var newRest = mode == Mode.Resting ? 1.0 : 0.0;
        // :838-839 .animation(.smooth(duration: 0.5), value: step) is the inner modifier, .smooth(duration: 0.45) value: mode the outer: a mode alone takes 0.45 s, a step (alone or with the mode) the inner 0.5 s (ED-260).
        var duration = this.step != step ? 0.5 : 0.45;
        this.step = step; this.mode = mode;
        // A single progress driver draws all three scalars with the same smooth timing.
        animation?.Cancel();
        var start = SwiftClock.Now;
        var frames = FrameClock.Source.Current;
        var curve = Animation.Smooth(duration);
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds;
            var progress = elapsed + 0.001 >= duration ? 1 : curve.Progress(elapsed);
            body.AnimatableData = (from.Position + (step - from.Position) * progress,
                (from.Outcome.Done + (newDone - from.Outcome.Done) * progress, from.Outcome.Resting + (newRest - from.Outcome.Resting) * progress));
            if (progress == 1) animation = null; else frames.RequestFrame(tick!);
        };
        animation = new TrackAnimation(frames, tick);
        frames.RequestFrame(tick);
    }
    private TrackAnimation? animation;
    private sealed record TrackAnimation(IFrameSource Frames, Action<TimeSpan> Tick)
    {
        public void Cancel() => Frames.CancelFrame(Tick);
    }
}

/// <summary>:844-916 <c>private struct HydraMergeTrackBody: View, Animatable</c>.</summary>
public sealed class HydraMergeTrackBody : Decorator
{
    private double position, done, rest;
    private int count;
    private bool hops;
    // :966-971 the track is a tap target. :912-915 .frame(width: <glided width>, height: 20, alignment: .topLeading): the drawing keeps its full width and stays at
    // the leading edge while the frame shrinks to the resting head ("centred in it, the stack overhung the frame on both sides and the settled head sat half outside
    // the pill"), so the canvas is pinned top-leading and not left to the Decorator's centring of a child wider than its frame.
    private readonly Canvas canvas = new() { Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas dots = new();
    private readonly HydraMarkImage head;
    private const double Dot = 4;          // :861
    private const double Gap = 3;          // :862
    private const double HeadSize = 12;    // :863
    private const double RestingSize = 18; // :864
    private new const double Height = 20;  // :865 (new: Swift's name, hides Layoutable.Height inside this class)
    private const double Lift = 4;         // :866

    /// <param name="position">:847 <c>var position: CGFloat</c>.</param>
    /// <param name="done">:850 <c>var done: CGFloat</c>.</param>
    /// <param name="rest">:852 <c>var rest: CGFloat</c>.</param>
    /// <param name="count">:853 <c>let count: Int</c>.</param>
    /// <param name="hops">:854 <c>let hops: Bool</c>.</param>
    // :884-915 body
    public HydraMergeTrackBody(double position, double done, double rest, int count, bool hops)
    {
        this.position = position; this.done = done; this.rest = rest; this.count = count; this.hops = hops;
        head = HydraReportRow.Mark(HeadSize);
        canvas.Children.Add(dots); canvas.Children.Add(head);
        Child = canvas;
        SetCount(count); Draw();
    }

    // :897-906
    internal void SetCount(int value)
    {
        count = value; dots.Children.Clear();
        for (var i = 0; i < count; i++)
        {
            var dot = new Panel { Width = Dot, Height = Dot };
            var baseDot = new Avalonia.Controls.Shapes.Ellipse(); baseDot.Fill = Chrome.Overlay(0.28).Brush(baseDot);
            var lit = new Avalonia.Controls.Shapes.Ellipse(); lit.Fill = Chrome.Accent.Brush(lit);
            dot.Children.Add(baseDot); dot.Children.Add(lit);
            Canvas.SetLeft(dot, i * (Dot + Gap)); Canvas.SetTop(dot, Height - Dot);
            dots.Children.Add(dot);
        }
        Draw();
    }

    public (double Position, (double Done, double Resting) Outcome) AnimatableData
    {
        get => (position, (done, rest));
        set { position = value.Position; done = value.Outcome.Done; rest = value.Outcome.Resting; Draw(); }
    }

    // :836 the environment can arrive through a parent after construction or change while the track is attached.
    internal void SetHops(bool enabled) { if (hops == enabled) return; hops = enabled; Draw(); }

    // :872-874 private static func dotCentre(_:) -> CGFloat
    private static double DotCentre(double i) => i * (Dot + Gap) + Dot / 2;

    // :877-915
    private void Draw()
    {
        var width = count * Dot + (count - 1) * Gap;
        base.Width = width + (RestingSize - width) * rest; base.Height = Height;
        canvas.Width = width; canvas.Height = Height;
        var arc = hops ? Lift * Math.Abs(Math.Sin(Math.PI * position)) : 0;
        var side = HeadSize + (RestingSize - HeadSize) * rest;
        head.Width = side; head.Height = side;
        Canvas.SetLeft(head, (DotCentre(position) - HeadSize / 2) * (1 - rest));
        Canvas.SetTop(head, -arc * (1 - rest) + (Height - RestingSize) / 2 * rest);
        var pop = hops ? 1 + 0.18 * Math.Sin(Math.PI * Math.Min(done, 1)) : 1;
        head.RenderTransform = new ScaleTransform(pop, pop);
        dots.Opacity = 1 - rest;
        for (var i = 0; i < dots.Children.Count; i++)
        {
            var dot = (Panel)dots.Children[i];
            dot.Children[1].Opacity = Math.Max(position + 0.35 >= i ? 1 : 0, Math.Clamp(done * (count + 1) - i, 0, 1));
        }
    }
}

/// <summary>:926-1162 <c>struct HydraMergeRow: View</c>.</summary>
public sealed class HydraMergeRow : Decorator
{
    private sealed class ViewState : MainActorModel // :930-931
    {
        private HydraMergePhase phase;
        private bool showingReport, flourish;
        public ViewState(HydraMergePhase phase) => this.phase = phase;
        public HydraMergePhase Phase { get => Read(phase); set => Set(ref phase, value); }
        public bool IsShowingReport { get => Read(showingReport); set => Set(ref showingReport, value); }
        public bool Flourish { get => Read(flourish); set => Set(ref flourish, value); }
    }
    private readonly ViewState state;
    private readonly ThreadRuntime runtime;
    private readonly Panel host = new();
    private Border? pill;
    private HydraMergeTrack? track;
    private MorphSlot? slot;
    private MorphSlot? stageSlot;
    private HydraMergeOutcome? outcome;
    private string? shownStage;
    private bool? wasMerging;
    private bool built; // the row has been built once: a pill made after that joins a parent on screen
    // :935 private static let change = Animation.smooth(duration: 0.3)
    private static Animation Change => Animation.Smooth(duration: 0.3);

    /// <param name="runtime">:927 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="phase">:928 <c>let phase: HydraMergePhase</c>.</param>
    // :937-952 body
    public HydraMergeRow(ThreadRuntime runtime, HydraMergePhase phase)
    {
        this.runtime = runtime; state = new ViewState(phase);
        Child = host;
        Padding = new Thickness(0, 0, 96, 0);
        Build();
        this.Track(() => (state.Phase, runtime.IsHydraMerging, runtime.HydraMergeStage, runtime.HydraMergeStep, state.Flourish), _ => Build());
        this.Task(() => state.Phase is HydraMergePhase.Outcome message ? new HydraMergeOutcome(message.Value).Body : null, async body =>
        {
            if (body is null || state.Phase is not HydraMergePhase.Outcome message) return;
            var note = new HydraMergeOutcome(message.Value);
            if (note.Details.Length != 0) await SwiftTry.IgnoreAsync(() => MarkdownView.Warm([body]));
        });
    }

    public void Update(HydraMergePhase phase) => state.Phase = phase;

    // :937-1042
    private void Build()
    {
        var next = state.Phase is HydraMergePhase.Outcome message ? new HydraMergeOutcome(message.Value) : null;
        var outcomeChanged = !Equals(outcome, next);
        var merging = next is null;
        if (merging && !runtime.IsHydraMerging)
        {
            if (pill is { } old)
                Transaction.WithAnimation(Animation.Spring(Chrome.GlideSpring), () => SwiftTransition.Remove(host, old));
            pill = null; track = null; slot = null; stageSlot = null; wasMerging = null;
            built = true; // a row first built with nothing under way: the pill that comes later joins a parent on screen
            return;
        }
        if (wasMerging == true && !merging && next is not null && next.Title.Contains("Hydra merged", StringComparison.Ordinal))
        {
            state.Flourish = true;
            var box = state;
            MainActor.Spawn(async () => { await SwiftTry.IgnoreAsync(() => SwiftTask.Sleep(TimeSpan.FromMilliseconds(1100))); box.Flourish = false; });
        }
        var changed = wasMerging != merging;
        wasMerging = merging;
        outcome = next;
        var stage = (runtime.HydraMergeStage ?? "Merging") + "\u2026";
        var count = HydraMergeStageExtensions.AllCases.Count;
        var mode = next is null ? HydraMergeTrack.Mode.Running : state.Flourish ? HydraMergeTrack.Mode.Done : HydraMergeTrack.Mode.Resting;
        var step = mode == HydraMergeTrack.Mode.Running ? (int)(runtime.HydraMergeStep ?? HydraMergeStage.Gathering) : count - 1;
        if (pill is null)
        {
            track = new HydraMergeTrack(step, count, mode);
            track.PointerReleased += (_, e) =>
            {
                if (outcome is { Details.Length: > 0 }) state.IsShowingReport = !state.IsShowingReport;
                e.Handled = true;
            };
            slot = new MorphSlot();
            var row = new HStack { Spacing = 8 }; // HStack :959: its children share one centre line
            row.Children.Add(track); row.Children.Add(slot);
            pill = TimelineRows.Pill(row);
            pill.Transition(SwiftTransition.SettleGlide);
            // :937-952 SwiftUI runs a transition only for a view that joins a parent already on screen: a row that first appears with the merge under way, or with its outcome
            // already there, has its pill in place (the settle glide's soft arrival now animates, UI-CHAT-052, so the first build must not play it).
            if (built) SwiftTransition.Insert(host, host.Children.Count, pill);
            else SwiftTransition.Place(host, host.Children.Count, pill);
        }
        else track!.Update(step, count, mode);
        built = true;
        AutomationProperties.SetName(pill, next?.Title ?? "Merging the team's work: " + stage);
        AutomationProperties.SetAccessibilityView(pill, AccessibilityView.Control); // :1018 `.accessibilityElement(children: .contain)`: a named container of the tree, its track, title and Open still elements of their own (as ChromeSegmentedPicker)
        if (changed || outcomeChanged)
        {
            var still = next is null ? MergingLabel(stage) : OutcomeLabel(next);
            stageSlot = next is null ? (MorphSlot)MergingContent(stage) : null;
            var live = next is null ? stageSlot! : OutcomeControls(next);
            slot!.Replace(still, live, Change);
            shownStage = stage;
        }
        else if (merging && shownStage != stage && stageSlot is not null)
        {
            // :1056-1079 the stage's sizing slot survives; only its id(stage) shimmer leaves and arrives.
            stageSlot.Replace(MergingLabel(stage), StageVisual(stage), Change);
            slot!.Resize(MergingLabel(stage), Change);
            shownStage = stage;
        }
    }

    // :1044-1094. The visible stage moves in its own slot with its own fresh shimmer/mask.
    private static Control MergingLabel(string stage) => TimelineRows.ChatText(stage, weight: SwiftFont.Weight.Medium);

    private static Control MergingContent(string stage)
    {
        var slot = new MorphSlot(horizontalPadding: 0);
        slot.Replace(MergingLabel(stage), StageVisual(stage), Change);
        return slot;
    }

    // :1061-1073 a fresh shimmer for each new text identity; its parent slot is never recreated by a stage change.
    private static Control StageVisual(string stage)
    {
        var text = TimelineRows.ChatText(stage, weight: SwiftFont.Weight.Medium, color: Chrome.PrimaryText.Opacity(0.9));
        var visible = new HydraShimmer().Body(text).Transition(SwiftTransition.Asymmetric(
            SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: 5)), SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: -5))));
        return visible;
    }

    // :1099-1137
    private Control OutcomeControls(HydraMergeOutcome note)
    {
        var box = state;
        var row = new HStack { Spacing = 8 }; // HStack :1100: its children share one centre line
        var title = PlainControls.Button(OutcomeTitle(note), () => { if (note.Details.Length != 0) box.IsShowingReport = !box.IsShowingReport; });
        title.IsEnabled = note.Details.Length != 0;
        ToolTip.SetTip(title, note.Details.Length == 0 ? "" : "Show the message");
        row.Children.Add(title);
        if (note.Link is { } link) row.Children.Add(HydraReportRow.OpenLink(link));
        row.Children.Add(PlainControls.Popover(title, new Binding<bool>(() => box.IsShowingReport, value => box.IsShowingReport = value), () =>
        {
            Control detail = note.Link is { } url && MergeRequestLink.Create(url) is { } request && HydraMergeNote.Parse(note.Details, request) is { } merge
                ? new HydraMergePopover(merge) : new HydraReportPopover(note.Body);
            return detail.PresentedChrome();
        }));
        return row.Transition(SwiftTransition.Opacity);
    }

    // :1140-1161
    private static Control OutcomeLabel(HydraMergeOutcome note)
    {
        var row = new HStack { Spacing = 8 }; // HStack :1133: its children share one centre line
        row.Children.Add(OutcomeTitle(note));
        if (note.Link is not null) row.Children.Add(TimelineRows.ChatText("Open", SwiftFont.TextStyle.Caption, SwiftFont.Weight.Semibold, Chrome.SecondaryText));
        return row;
    }
    private static Control OutcomeTitle(HydraMergeOutcome note)
    {
        var row = new HStack { Spacing = 8 }; // HStack :1143: its children share one centre line
        row.Children.Add(TimelineRows.ChatText(note.Title, weight: SwiftFont.Weight.Medium, color: Chrome.PrimaryText.Opacity(0.9), singleLine: true));
        if (note.Details.Length != 0) row.Children.Add(TimelineRows.Chevron());
        return row;
    }

    // :973-1009: the still copy alone sizes the slot; fading children neither widen nor shrink it.
    private sealed class MorphSlot : Panel
    {
        private readonly Panel visible = new();
        private Control? still, live;
        private readonly AnimatedDouble width;
        private double presentedWidth;
        private bool measured;
        private Animation? resize;
        private double horizontalPadding = 2;
        public MorphSlot()
        {
            ClipToBounds = true; Margin = new Thickness(-2, -6);
            Children.Add(visible);
            width = new AnimatedDouble(value => { presentedWidth = value; InvalidateMeasure(); });
        }
        public MorphSlot(double horizontalPadding) : this()
        { this.horizontalPadding = horizontalPadding; Margin = new Thickness(-horizontalPadding, -6); }
        public void Resize(Control size, Animation animation)
        {
            if (still is not null) Children.Remove(still);
            still = size; still.Opacity = 0; still.IsHitTestVisible = false;
            Children.Insert(0, still);
            resize = measured ? animation : null;
            InvalidateMeasure();
        }
        public void Replace(Control size, Control drawn, Animation animation)
        {
            Resize(size, animation);
            Transaction.WithAnimation(measured ? animation : null, () =>
            {
                if (live is not null) SwiftTransition.Remove(visible, live);
                live = drawn; SwiftTransition.Insert(visible, visible.Children.Count, drawn);
            });
            InvalidateMeasure();
        }
        protected override Size MeasureOverride(Size availableSize)
        {
            still?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = still?.DesiredSize ?? default;
            if (!measured || targetWidth != desired.Width)
            {
                targetWidth = desired.Width; width.Set(targetWidth, resize); measured = true; resize = null;
            }
            foreach (var child in visible.Children) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Size(presentedWidth + 2 * horizontalPadding, desired.Height + 12);
        }
        private double targetWidth;
        protected override Size ArrangeOverride(Size finalSize)
        {
            still?.Arrange(new Rect(horizontalPadding, 6, still.DesiredSize.Width, still.DesiredSize.Height));
            visible.Arrange(new Rect(horizontalPadding, 6, Math.Max(0, finalSize.Width - 2 * horizontalPadding), Math.Max(0, finalSize.Height - 12)));
            foreach (var child in visible.Children) child.Arrange(new Rect(new Point(), child.DesiredSize));
            return finalSize;
        }
    }
}

/// <summary>:1167-1207 <c>struct UserBubble: Shape</c>.</summary>
public sealed record UserBubble
{
    public const double CornerRadius = 18; // :1168
    public const double Tail = 4;          // :1169

    // :1164-1206 func path(in:) -> Path: iMessage's outgoing bubble, rounded on three corners, the bottom-right one drawn
    // out into the little tail that curls back under the bubble; the tail's tip sits `tail` points past the body's
    // trailing edge, at the very bottom. SwiftUI's addArc(clockwise: false) in its flipped space turns clockwise on
    // screen, which is Avalonia's SweepDirection.Clockwise. PR-CHAT-077.
    public Geometry Path(Rect @in)
    {
        var r = CornerRadius; // :1172
        var left = @in.X; // :1173
        var top = @in.Y; // :1174
        var bottom = @in.Bottom; // :1175
        var edge = @in.Right - Tail; // :1176-1177 the body's trailing edge; the tail reaches past it
        var figure = new PathFigure { StartPoint = new Point(left + r, top), IsClosed = true, IsFilled = true }; // :1179, :1204
        figure.Segments!.Add(new LineSegment { Point = new Point(edge - r, top) }); // :1180
        figure.Segments.Add(Arc(new Point(edge, top + r), r)); // :1181 centre (edge - r, top + r), -90 to 0 degrees
        figure.Segments.Add(new LineSegment { Point = new Point(edge, bottom - 11) }); // :1182
        figure.Segments.Add(new BezierSegment // :1183-1189 out to the tip at the bottom corner
        {
            Point1 = new Point(edge, bottom - 1),
            Point2 = new Point(edge + Tail, bottom),
            Point3 = new Point(edge + Tail, bottom),
        });
        figure.Segments.Add(new BezierSegment // :1190-1194 curling back in under the body
        {
            Point1 = new Point(edge, bottom + 0.5),
            Point2 = new Point(edge - 4, bottom - 1),
            Point3 = new Point(edge - 7, bottom - 4),
        });
        figure.Segments.Add(new BezierSegment // :1195-1199 easing into the bottom edge
        {
            Point1 = new Point(edge - 12, bottom),
            Point2 = new Point(edge - 16, bottom),
            Point3 = new Point(edge - 21, bottom),
        });
        figure.Segments.Add(new LineSegment { Point = new Point(left + r, bottom) }); // :1200
        figure.Segments.Add(Arc(new Point(left, bottom - r), r)); // :1201 centre (left + r, bottom - r), 90 to 180 degrees
        figure.Segments.Add(new LineSegment { Point = new Point(left, top + r) }); // :1202
        figure.Segments.Add(Arc(new Point(left + r, top), r)); // :1203 centre (left + r, top + r), 180 to 270 degrees
        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);
        return geometry;

        static ArcSegment Arc(Point to, double radius) =>
            new() { Point = to, Size = new Size(radius, radius), SweepDirection = SweepDirection.Clockwise, IsLargeArc = false };
    }
}

/// <summary>:1209-1233 <c>struct AttachmentStrip: View</c>.</summary>
public sealed class AttachmentStrip : Decorator
{
    // :1212-1214 @State private var preview = AttachmentPreviewSlot(): one preview panel for the strip, so every photo
    // opens in a single tap no matter how many are attached, and made on the first tap rather than with the row. Read
    // only by the thumbnails' actions and the anchor, never by the body, so a plain field (R-VIEW-2).
    private readonly AttachmentPreviewSlot preview = new();

    /// <param name="attachments">:1210 <c>let attachments: [Attachment]</c>.</param>
    // :1216-1232 body
    public AttachmentStrip(ValueList<Attachment> attachments)
    {
        // :1217-1219 weak/box captures only: hover responders and anchor views must not keep this strip (and its
        // panel) alive after it scrolls away (PR-CHAT-161).
        var slot = new WeakReference<AttachmentPreviewSlot>(preview);
        // :1220-1222 a plain stack hugs the thumbnails, so a trailing-aligned row keeps the photos over the bubble, and
        // the anchor view fills just the photos. The list is a `let`, so the ForEach (:1223) is built once.
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        foreach (var attachment in attachments) // :1223
        {
            var thumbnail = new AttachmentThumbnail(attachment, preview); // :1224
            ToolTip.SetTip(thumbnail, attachment.Name); // :1225 .help(attachment.name)
            row.Children.Add(thumbnail);
        }
        // :1228-1230 .background { AttachmentAnchorCapture { [weak slot] in slot?.setAnchor($0) } }
        Child = PlainControls.Background(row, new AttachmentAnchorCapture(view =>
        {
            if (slot.TryGetTarget(out var held)) held.SetAnchor(view);
        }));
        // :1231 .onDisappear { [weak slot] in slot?.close() }
        DetachedFromVisualTree += (_, _) =>
        {
            if (slot.TryGetTarget(out var held)) held.Close();
        };
    }
}

/// <summary>:1235-1351 <c>struct AttachmentThumbnail: View</c>.</summary>
public sealed class AttachmentThumbnail : Decorator
{
    private readonly Attachment attachment;               // :1236
    private readonly double size;                          // :1237
    private readonly AttachmentPreviewSlot? slot;          // :1241
    private readonly AttachmentPreviewCoordinator? panel;  // :1242
    private readonly bool anchorless;                      // :1254

    // :1244 @State private var image: CGImage?. Drawn straight into the plate, as AttachmentVideoThumbnail
    // (UI/Common/AttachmentPreview.cs) draws its frame: the body adds the image once and swaps its source after.
    private DecodedBitmap? image;
    private Image? imageView;
    private readonly Panel plate = new();
    // nth motion pack step 6 (OD-0136 amendment 2): the plate with the light while the photo loads; the photo settles over it.
    private NthSkeleton? waiting;

    // :1245-1249 @State private var ownAnchor = WeakView(): the thumbnail's own view, handed to the panel on tap so the
    // arrow lands on the tapped photo rather than the strip's middle.
    private readonly WeakView ownAnchor = new();

    // :1250 @State private var needsAnchor = false, read by the body (:1335-1339).
    private sealed class ViewState : MainActorModel
    {
        private bool needsAnchor;

        public bool NeedsAnchor { get => Read(needsAnchor); set => Set(ref needsAnchor, value); }
    }

    /// <summary>:1256-1258 <c>init(attachment:size:preview:anchorless:)</c> with a slot; preview has no default, so it
    /// moves before size (ED-095) and callers name their arguments.</summary>
    public AttachmentThumbnail(Attachment attachment, AttachmentPreviewSlot preview, double size = 56, bool anchorless = false)
        : this(attachment, size, preview, null, anchorless) // :1257
    {
    }

    /// <summary>:1260-1262 <c>init(attachment:size:preview:)</c> with a panel the caller holds; preview moves before
    /// size (ED-095).</summary>
    public AttachmentThumbnail(Attachment attachment, AttachmentPreviewCoordinator preview, double size = 56)
        : this(attachment, size, null, preview, false) // :1261
    {
    }

    // :1264-1272 private init(attachment:size:slot:panel:anchorless:); :1284-1350 body
    private AttachmentThumbnail(Attachment attachment, double size, AttachmentPreviewSlot? slot, AttachmentPreviewCoordinator? panel, bool anchorless)
    {
        this.attachment = attachment;
        this.size = size;
        this.slot = slot;
        this.panel = panel;
        this.anchorless = anchorless;
        var state = new ViewState();

        // :1285-1287 the anchor box alone: the hover responder and the anchor view must not keep this row (and its
        // attachment) alive after it scrolls away (PR-CHAT-161).
        var anchorBox = ownAnchor;

        Control label;
        if (attachment.IsImage) // :1291
        {
            label = Plate(plate); // :1292-1304
            // :1270-1271 a photo shown before starts out drawn, so scrolling back to it never fades it in again.
            if (ThumbnailMemory.ImageFor(attachment.Path, size) is { } known) ShowImage(known, fade: false);
            else plate.Children.Add(waiting = new NthSkeleton(NthSkeletonKind.Plate));
        }
        else if (attachment.IsVideo) // :1305
        {
            label = new AttachmentVideoThumbnail(attachment, size); // :1306
        }
        else // :1307-1323
        {
            // :1308-1311 a file fills its chip the way a photo does: the shell icon at the chip's full size on the same
            // plate, so a text file, a PDF and a folder all read at a glance.
            var icon = new Image
            {
                Source = FileIcons.Icon(attachment.Path), // :1315
                Stretch = Stretch.Uniform, // :1316-1318 .resizable() .aspectRatio(contentMode: .fit)
                Margin = new Thickness(size * 0.08), // :1319 .padding(size * 0.08)
            };
            RenderOptions.SetBitmapInterpolationMode(icon, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality); // :1317
            var files = new Panel();
            files.Children.Add(icon);
            label = Plate(files);
            Avalonia.Automation.AutomationProperties.SetName(label, attachment.Name); // :1323
        }

        var button = PlainControls.Button(label, TogglePanel); // :1288-1290, :1326 .buttonStyle(.plain)
        button.Focusable = false; // :1327 .focusable(false)
        // :1328-1331 .contentShape(.rect(cornerRadius: 12, style: .continuous)): the plate clips to its 12 point
        // continuous rectangle (ClipToBounds on a rounded Border), so the fill image's overflow is never hit and a
        // neighbour's remove badge stays reachable.
        var host = new Panel();
        host.Children.Add(button);
        Child = host;

        // :1332-1334 .onHover: mounting the anchor needs a hover first; a tap always follows one.
        button.OnHover(hovering =>
        {
            if (hovering && !anchorless && !state.NeedsAnchor) state.NeedsAnchor = true;
        });
        // :1335-1339 .background { if needsAnchor, !anchorless { AttachmentAnchorCapture { [weak anchorBox] ... } } }
        host.Track(() => state.NeedsAnchor && !anchorless, mounted =>
        {
            if (!mounted || host.Children.Count > 1) return;
            host.Children.Insert(0, new AttachmentAnchorCapture(view => anchorBox.Value = view));
        });

        // :1340-1349 .task(id: attachment.path)
        this.Task(() => attachment.Path, async path =>
        {
            if (!attachment.IsImage) return; // :1341
            if (ThumbnailMemory.ImageFor(path, size) is { } known) // :1342-1345
            {
                if (!ReferenceEquals(image, known)) ShowImage(known, fade: false);
                return;
            }
            var thumbnail = await ThumbnailCache.Shared.Current.ThumbnailFor(path, size, fillingSquare: true); // :1346
            if (thumbnail is null) return;
            ThumbnailMemory.Store(thumbnail.Image, path, size); // :1347
            // :1348 withAnimation(.easeOut(duration: 0.15)) { image = thumbnail.image }; nth step 6: the photo settles in over 0.6 s instead
            ShowImage(thumbnail.Image, fade: true);
        });
    }

    // :1292-1304 and :1312-1322: the plate, 12 point continuous rounding, .quaternary at 0.6, size by size, clipped.
    private Border Plate(Panel content)
    {
        var border = new Border
        {
            Width = size, // :1303, :1321 .frame(width: size, height: size)
            Height = size,
            ClipToBounds = true, // :1304, :1322 .clipShape(.rect(cornerRadius: 12, style: .continuous))
            Child = content,
        };
        border.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :1293, :1313
        // :1294, :1314 .fill(.quaternary.opacity(0.6)). UNVERIFIED OD-0001: SwiftColor.Quaternary has no assigned ED
        // yet, the same note AttachmentVideoThumbnail carries for the same plate.
        border.Background = SwiftColor.Quaternary.Opacity(0.6).Brush(border);
        return border;
    }

    // :1295-1301 if let image { Image(decorative: image, scale: 2).resizable().interpolation(.high)
    // .aspectRatio(contentMode: .fill).transition(.opacity) }
    private void ShowImage(DecodedBitmap next, bool fade)
    {
        image = next;
        if (imageView is { } shown)
        {
            shown.Source = next.ToBitmap();
            return;
        }
        var view = new Image { Source = next.ToBitmap(), Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapInterpolationMode(view, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        imageView = view;
        plate.Children.Add(view);
        if (waiting is not { } placeholder) return;
        waiting = null;
        if (!fade) { plate.Children.Remove(placeholder); return; }
        // nth step 6: opacity 0 to 1 and scale 1.02 to 1 over 0.6 s (in place of the 0.15 s fade); the plate stays under it, then goes
        NthArrive.Settle(view);
        placeholder.Finish();
        Avalonia.Threading.DispatcherTimer.RunOnce(() => plate.Children.Remove(placeholder), TimeSpan.FromSeconds(.6));
    }

    // :1274-1282 togglePanel(): opens (or closes) the panel for this photo, making one only now if the caller handed
    // over a slot.
    private void TogglePanel()
    {
        if (slot is { } held) held.Panel.Toggle(attachment, over: ownAnchor.Value); // :1277-1278
        else panel?.Toggle(attachment, over: ownAnchor.Value); // :1279-1280
    }
}

/// <summary>:1353-1413 <c>struct AssistantMessageRow: View</c>.</summary>
public sealed class AssistantMessageRow : Decorator
{
    // :1359 @State private var isHovering = false, read by the body.
    private sealed class ViewState : MainActorModel
    {
        private bool isHovering;

        public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
    }

    private readonly ViewState state = new();

    // PORT-ONLY (UI-THEME-047): the body is built again for every flush, so the veils of the reply's paragraphs (SwiftUI's @State, StreamVeil.swift:245)
    // live here, through the rebuilds, for as long as the row is on screen. Internal for the tests (InternalsVisibleTo Marrow.App.Tests).
    internal MarkdownVeils Veils { get; } = new();

    /// <param name="entry">:1354 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="summary">:1355-1357 <c>let summary: TurnSummary?</c>: the turn's summary, set only on the turn's last
    /// reply, precomputed by the timeline so rows never scan the thread.</param>
    /// <param name="runtime">:1358 <c>let runtime: ThreadRuntime</c>.</param>
    // :1361-1390 body. PR-CHAT-088. It reads entry.item, which a streaming reply keeps changing: the body is built again
    // whenever the text or the streaming flag moves (MarkdownView's .equatable(), :1364), the hover state surviving.
    public AssistantMessageRow(TimelineEntry entry, TurnSummary? summary, ThreadRuntime runtime)
    {
        this.summary = summary; this.runtime = runtime;
        DetachedFromVisualTree += (_, _) => Veils.Reset(); // PORT-ONLY (UI-THEME-047): the row leaves the screen, its @State with it
        void Build(TimelineItem.Content content)
        {
            if (content is not TimelineItem.Content.Assistant assistant) // :1362
            {
                built = null;
                Child = null;
                return;
            }
            var message = assistant.Value;
            if (built is { } previous && previous.Text == message.Text && previous.IsStreaming == message.IsStreaming) return;
            built = message;
            var body = Body(message, this.summary, runtime, state, Veils.Root);
            KeepCards(Child, body);
            Child = body;
        }
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, Build);
    }

    private TurnSummary? summary; // :1355-1357, handed again by a pass of the timeline's body (SetSummary)
    private readonly ThreadRuntime runtime;
    private AssistantMessage? built;

    /// <summary>The row handed its turn's summary again: the same row, its hover kept, its line saying the new summary. ED-255 (3).</summary>
    internal void SetSummary(TurnSummary? next)
    {
        if (Equals(summary, next)) return;
        summary = next;
        if (built is { } message)
        {
            var body = Body(message, summary, runtime, state, Veils.Root);
            KeepCards(Child, body);
            Child = body;
        }
    }

    // :1364 .equatable() with the blocks of MarkdownView kept by position: a block that is still the same block when the reply streams on or its summary arrives is
    // the same view, which keeps its state and animates what changes in it. A delegation card is the one block with state of its own that streams (:703-704), so
    // the cards of the body just made are the cards already on show, handed the new block: the n-th card of the old body becomes the n-th card of the new.
    private static void KeepCards(Control? old, Control next)
    {
        if (old is null) return;
        var shown = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(old).OfType<HydraDelegationBlock>().ToList();
        var made = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(next).OfType<HydraDelegationBlock>().ToList();
        for (var i = 0; i < Math.Min(shown.Count, made.Count); i++)
        {
            var (card, fresh) = (shown[i], made[i]);
            if (card.Parent is not Decorator from || fresh.Parent is not Decorator into) continue;
            card.Update(fresh.Json, fresh.Sent);
            // :326-329 the block's own look (the leaving card is clear, a little smaller and takes no taps) is the new block's.
            card.Opacity = fresh.Opacity; card.RenderTransform = fresh.RenderTransform; card.RenderTransformOrigin = fresh.RenderTransformOrigin; card.IsHitTestVisible = fresh.IsHitTestVisible;
            from.Child = null;
            into.Child = card;
        }
    }

    // :1363-1388
    private static Control Body(AssistantMessage message, TurnSummary? summary, ThreadRuntime runtime, ViewState state, MarkdownVeilScope veils)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Background = Brushes.Transparent }; // :1363
        column.Children.Add(new MarkdownView(message.Text, message.IsStreaming, veils)); // :1364
        // :1365-1374 one side for both kinds of message: the summary sits where the copy control used to be.
        // :1367 HStack(spacing: 8) with its default .center alignment: the summary sits on the centre line of the 22-point
        // line (UI-CHAT-019); a plain horizontal StackPanel stretched it to the top of the slot.
        var line = new HStack
        {
            Spacing = 8, // :1367
            Height = TimelineMetrics.HoverLineHeight, // :1375-1377 pinned to the hover line's room
        };
        if (summary is not null) // :1368-1372
        {
            var label = new TextBlock { Text = TurnEndRow.Label(summary) }; // :1369
            ChatFont.Chat(SwiftFont.TextStyle.Caption).Apply(label); // :1370
            label.Foreground = Chrome.SecondaryText.Brush(label); // :1371
            line.Children.Add(label);
        }
        line.Children.Add(new Control { MinWidth = 8 }); // :1373 Spacer(minLength: 8)
        column.Children.Add(line);
        // :1378 .opacity(isHovering && !message.isStreaming ? 1 : 0), faded under the hover's transaction (:1381).
        var opacity = new AnimatedOpacity(line, state.IsHovering && !message.IsStreaming ? 1 : 0);
        line.Track(() => state.IsHovering && !message.IsStreaming, shown => opacity.Set(shown ? 1 : 0, Transaction.Current));
        // :1380-1382 .onHover { [hovering = $isHovering] isOver in withAnimation(.easeOut(duration: 0.12)) { ... } }: the
        // state box alone, so a hover responder never keeps a scrolled-away message alive.
        column.OnHover(isOver => Transaction.WithAnimation(Animation.EaseOut(duration: 0.12), () => state.IsHovering = isOver));
        column.MessageMenu(message.Text, runtime); // :1383
        // :1384-1388 the hover line's room stays inside the row but not in its height (2 = the VStack's spacing above).
        column.Margin = new Thickness(0, 0, 0, -(TimelineMetrics.HoverLineHeight + 2));
        return column;
    }

    // :1392-1412 static func messageActions(text:code:runtime:) -> [RowAction]: from value snapshots with weak captures,
    // because the menu outlives the right-click and its callbacks must not retain the row. PR-CHAT-089.
    public static IReadOnlyList<RowAction> MessageActions(string text, string? code, ThreadRuntime runtime)
    {
        var textSnapshot = text; // :1395
        var codeSnapshot = code; // :1396
        var items = new List<RowAction> // :1397-1400
        {
            new() { Title = "Copy message", Symbol = "doc.on.doc", Confirms = "Copied", Action = () => TimelineRows.CopyMessageText(MessageText.Plain(textSnapshot)) }, // :1398
            new() { Title = "Copy as markdown", Symbol = "number", Confirms = "Copied", Action = () => TimelineRows.CopyMessageText(textSnapshot) }, // :1399
        };
        if (codeSnapshot is { } lastCode) // :1401-1405
        {
            items.Add(new() { Title = "Copy last code block", Symbol = "chevron.left.forwardslash.chevron.right", Confirms = "Copied", Action = () => TimelineRows.CopyMessageText(lastCode) });
        }
        var weakRuntime = new WeakReference<ThreadRuntime>(runtime); // :1406 [weak runtime]
        items.Add(new() // :1406-1410
        {
            Title = "Add to reply",
            Symbol = "arrow.turn.down.left",
            StartsGroup = true,
            Confirms = "Added",
            Action = () =>
            {
                if (!weakRuntime.TryGetTarget(out var target)) return; // :1407
                if (target.Draft.Quotes.Any(quote => SwiftString.Equal(quote.Text, textSnapshot))) return; // :1408
                target.Draft = target.Draft with { Quotes = target.Draft.Quotes.Appending(new ReplyQuote { Text = textSnapshot }) }; // :1409
            },
        });
        return items; // :1411
    }
}

/// <summary>:1431-1439 <c>private struct FoldedAnswerView: View</c>: the answer of a folded turn, with the same menu as a
/// reply row.</summary>
public sealed class FoldedAnswerView : Decorator
{
    /// <param name="text">:1432 <c>let text: String</c>.</param>
    /// <param name="runtime">:1433 <c>let runtime: ThreadRuntime</c>.</param>
    // :1435-1438 body
    public FoldedAnswerView(string text, ThreadRuntime runtime) =>
        Child = new MarkdownView(text).MessageMenu(text, runtime); // :1436-1437
}

/// <summary>:1441-1510 <c>struct WorkGroup: View</c>: consecutive tool calls, rendered inline with no card, under a
/// summary header whose chevron collapses the rows. A group the agent has moved on from starts collapsed; the live group
/// stays expanded; a lone tool renders as its row alone. PR-CHAT-090.</summary>
public sealed class WorkGroup : Decorator
{
    private readonly ThreadRuntime runtime;         // :1448
    private readonly string? workingDirectory;      // :1449

    // The inputs a re-render hands over (see Update) and :1451-1452 @State isCollapsed, showsAll.
    private sealed class ViewState : MainActorModel
    {
        private ValueList<TimelineEntry> entries;
        private bool startsCollapsed;
        private bool isCollapsed;
        private bool showsAll;

        public ViewState(ValueList<TimelineEntry> entries, bool startsCollapsed)
        {
            this.entries = entries;
            this.startsCollapsed = startsCollapsed;
            isCollapsed = startsCollapsed; // :1460 _isCollapsed = State(initialValue: startsCollapsed)
        }

        public ValueList<TimelineEntry> Entries { get => Read(entries); set => Set(ref entries, value); }

        public bool StartsCollapsed { get => Read(startsCollapsed); set => Set(ref startsCollapsed, value); }

        public bool IsCollapsed { get => Read(isCollapsed); set => Set(ref isCollapsed, value); }

        public bool ShowsAll { get => Read(showsAll); set => Set(ref showsAll, value); }
    }

    private readonly ViewState state;

    /// <summary>:1455-1461 <c>init(entries:runtime:workingDirectory:startsCollapsed:)</c>; :1463-1509 body.</summary>
    public WorkGroup(ValueList<TimelineEntry> entries, ThreadRuntime runtime, string? workingDirectory = null, bool startsCollapsed = false)
    {
        this.runtime = runtime;
        this.workingDirectory = workingDirectory;
        state = new ViewState(entries, startsCollapsed);

        // :1464-1466 `if entries.count == 1, let only = entries.first { ToolRow } else { ... }`
        TimelineEntry? lone = null;
        var grouped = false;
        void Build(ValueList<TimelineEntry> current)
        {
            if (current.Count == 1)
            {
                if (ReferenceEquals(lone, current[0])) return;
                lone = current[0];
                grouped = false;
                Child = new ToolRow(current[0], runtime, workingDirectory); // :1465
                return;
            }
            lone = null;
            if (grouped) return;
            grouped = true;
            Child = Group();
        }
        Build(entries);
        this.Track(() => state.Entries, Build);
    }

    /// <summary>The inputs of a later render of the same group (R-VIEW-1: a ForEach row's <c>let</c> inputs arrive
    /// through Update): the rows follow the new entries, and a group that now starts collapsed folds (:1503-1507).</summary>
    public void Update(ValueList<TimelineEntry> entries, bool startsCollapsed)
    {
        state.Entries = entries;
        state.StartsCollapsed = startsCollapsed;
    }

    // :1467-1507
    private Control Group()
    {
        // :1467 VStack: the header and, once open, the steps at rowSpacing. The gap belongs to the steps' own body (GrowingBody.LeadingSpace), so a closed group has none
        // and the steps' height, gap included, grows along the open's curve (:1470-1472 withAnimation(.snappy(duration: 0.24)); OD-0076: rows that appear fold open).
        var stack = new StackPanel { Orientation = Orientation.Vertical };

        var glyphSlot = new Decorator { Width = TimelineMetrics.IconWidth }; // :1484 .frame(width: TimelineMetrics.iconWidth)
        var summary = new TextBlock(); // :1485
        summary.Foreground = Chrome.SecondaryText.Brush(summary); // :1486
        var chevron = SwiftImage.SystemName("chevron.right").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)); // :1487-1488
        chevron.Foreground = Chrome.SecondaryText.Brush(chevron); // :1489
        var turn = new AnimatedDouble(angle => chevron.RenderTransform = new RotateTransform(angle)); // :1490 .rotationEffect
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = TimelineMetrics.IconSpacing, // :1480
            Margin = new Thickness(0, 0, 12, 0), // :1493 .padding(.trailing, 12)
            Background = Brushes.Transparent, // :1494 .contentShape(.rect)
        };
        row.Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)); // :1492
        row.Children.Add(glyphSlot);
        row.Children.Add(summary);
        row.Children.Add(chevron);

        Button header = null!;
        header = PlainControls.Button(row, () => // :1468-1479, :1496 .buttonStyle(.plain)
        {
            if (state.IsCollapsed)
            {
                Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () => state.IsCollapsed = false); // :1470-1472
                ThreadTimeline.GetRevealTimelineEnd(header)?.Action(); // :1473, outside the animation transaction
            }
            else
            {
                Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () => state.IsCollapsed = true); // :1475-1477
            }
        });
        // :1467-1468 VStack(alignment: .leading): the button hugs its label (icon, words, chevron and the 12-point
        // trailing padding) instead of taking the whole row, so a tap past the chevron does not toggle the group.
        header.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(header);

        // :1481-1486 the group's symbol and its "Edited files, ran commands" line, from the calls' kinds.
        header.Track(() => (WorkGroupSummary.Symbol(state.Entries), WorkGroupSummary.Text(state.Entries)), value =>
        {
            var (symbol, text) = value;
            if (glyphSlot.Child is not SwiftImage { } shown || shown.SymbolName != symbol)
            {
                var glyph = SwiftImage.SystemName(symbol).Font(ChatFont.Chat(SwiftFont.TextStyle.Caption)); // :1481-1482
                glyph.Foreground = Chrome.SecondaryText.Brush(glyph); // :1483
                glyph.Width = TimelineMetrics.IconWidth; // :1484
                glyphSlot.Child = glyph;
            }
            summary.Text = text;
        });

        WorkSteps? steps = null;
        GrowingBody? body = null;
        header.Track(() => state.IsCollapsed, collapsed =>
        {
            turn.Set(collapsed ? 0 : 90, Transaction.Current); // :1490
            var help = collapsed ? "Show these steps" : "Hide these steps";
            ToolTip.SetTip(header, help); // :1497
            AutomationProperties.SetName(header, help); // :1498
            if (!collapsed && steps is null) // :1499-1501 if !isCollapsed { WorkSteps(...) }
            {
                steps = new WorkSteps(state.Entries, runtime, new Binding<bool>(() => state.ShowsAll, value => state.ShowsAll = value), workingDirectory);
                // The steps fold open: the body's height (and its fade) follow the transaction the toggle was made in, so what follows the group moves down with it.
                var opening = body = new GrowingBody { LeadingSpace = TimelineMetrics.RowSpacing, Child = steps };
                stack.Children.Insert(1, opening);
                opening.Open(Transaction.Current); // :1469-1472 withAnimation(.snappy(duration: 0.24)), no reduced-motion gate on the Mac (ED-264 (2))
            }
            else if (collapsed && steps is not null && body is { } closing)
            {
                steps = null; body = null;
                closing.Close(Transaction.Current, () => stack.Children.Remove(closing)); // :1475-1477, the same (ED-264 (2))
            }
        });
        stack.Track(() => state.Entries, entries => steps?.Update(entries));

        // :1503-1507 .onChange(of: startsCollapsed): one-way. Arriving reply text collapses the group, but a group the
        // reader opened never snaps shut on its own.
        OnChange<bool>? fold = null;
        stack.AttachedToVisualTree += (_, _) => fold ??= new OnChange<bool>(() => state.StartsCollapsed, (_, collapsed) =>
        {
            if (collapsed) state.IsCollapsed = true;
        });
        stack.DetachedFromVisualTree += (_, _) =>
        {
            fold?.Dispose();
            fold = null;
        };
        return stack;
    }
}

/// <summary>:1512-1559 <c>struct WorkSteps: View</c>: a tool group's rows; only the two latest steps show, the rest
/// fold under one "N earlier steps" line until tapped. Shared by the expanded work group and the running turn's working
/// line. PR-CHAT-092.</summary>
public sealed class WorkSteps : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private ValueList<TimelineEntry> entries;

        public ViewState(ValueList<TimelineEntry> entries) => this.entries = entries;

        public ValueList<TimelineEntry> Entries { get => Read(entries); set => Set(ref entries, value); }
    }

    private readonly ViewState state;

    /// <param name="entries">:1516 <c>let entries: [TimelineEntry]</c>.</param>
    /// <param name="runtime">:1517 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="showsAll">:1519 <c>@Binding var showsAll: Bool</c>; no default, so it moves before
    /// workingDirectory (ED-095).</param>
    /// <param name="workingDirectory">:1518 <c>var workingDirectory: String?</c>.</param>
    // :1522-1558 body. SwiftUI lays the body's views out in the caller's stack at rowSpacing; here they sit in a stack of
    // their own at the same spacing.
    public WorkSteps(ValueList<TimelineEntry> entries, ThreadRuntime runtime, Binding<bool> showsAll, string? workingDirectory = null)
    {
        state = new ViewState(entries);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = TimelineMetrics.RowSpacing };

        // :1524-1544 the earlier-steps line.
        var ellipsis = SwiftImage.SystemName("ellipsis").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption)); // :1532-1533
        ellipsis.Foreground = Chrome.SecondaryText.Brush(ellipsis); // :1534
        ellipsis.Width = TimelineMetrics.IconWidth; // :1535
        var count = new TextBlock(); // :1536
        count.Foreground = Chrome.SecondaryText.Brush(count); // :1537
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = TimelineMetrics.IconSpacing, // :1531
            Margin = new Thickness(0, 0, 12, 0), // :1540
            Background = Brushes.Transparent, // :1541 .contentShape(.rect)
        };
        line.Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)); // :1539
        line.Children.Add(ellipsis);
        line.Children.Add(count);
        Button more = null!;
        // :1522-1525 the line sits in the caller's VStack(alignment: .leading): the button hugs its label, as the group header does.
        more = PlainControls.Button(line, () => // :1525-1530, :1543 .buttonStyle(.plain)
            Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () =>
            {
                showsAll.Value = true;
                ThreadTimeline.GetRevealTimelineEnd(more)?.Action(); // inside the animation transaction
            }));
        more.HorizontalAlignment = HorizontalAlignment.Left;
        // :1545-1557 the remaining entries: tool rows, and what the agent said between two steps as a step of the work.
        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = TimelineMetrics.RowSpacing };
        stack.Children.Add(rows);
        var forEach = new ForEach<TimelineEntry, string>(rows, entry => entry.Id,
            entry => entry.TimelineEntryKind == TimelineEntry.Kind.Assistant ? Narration(entry) : new ToolRow(entry, runtime, workingDirectory),
            (_, _) => { });

        var showsMore = false;
        stack.Track(() => (state.Entries, showsAll.Value), value =>
        {
            var (current, all) = value;
            var hidden = all ? 0 : Math.Max(0, current.Count - 2); // :1523
            if (hidden > 0) // :1524 if hidden > 0 { Button }
            {
                count.Text = hidden == 1 ? "1 earlier step" : hidden.ToString(CultureInfo.InvariantCulture) + " earlier steps"; // :1536
                if (!showsMore) SwiftTransition.Insert(stack, 0, more);
                showsMore = true;
            }
            else if (showsMore)
            {
                showsMore = false;
                SwiftTransition.Remove(stack, more);
            }
            forEach.Apply(current.Skip(hidden).ToList()); // :1545 entries.suffix(entries.count - hidden)
        });
        Child = stack;
    }

    /// <summary>The entries of a later render of the same steps (R-VIEW-1), keyed by entry id so rows keep their state.</summary>
    public void Update(ValueList<TimelineEntry> entries) => state.Entries = entries;

    // :1546-1553 what the agent said between two steps, read as a step of the work: small and dimmed like its thinking,
    // in line with the tool rows. It follows the entry as the reply streams.
    private static Control Narration(TimelineEntry entry)
    {
        var host = new Decorator();
        var veils = new MarkdownVeils(); // PORT-ONLY (UI-THEME-047): the narration's veils, kept through the rebuilds for as long as it is on screen
        host.DetachedFromVisualTree += (_, _) => veils.Reset();
        AssistantMessage? built = null;
        void Build(TimelineItem.Content content)
        {
            if (content is not TimelineItem.Content.Assistant assistant) // :1546
            {
                built = null;
                host.Child = null;
                return;
            }
            var message = assistant.Value;
            if (built is { } previous && previous.Text == message.Text && previous.IsStreaming == message.IsStreaming) return; // :1549 .equatable()
            built = message;
            var markdown = new MarkdownView(message.Text, message.IsStreaming, veils.Root); // :1549
            TextElement.SetForeground(markdown, Chrome.SecondaryText.Brush(markdown)); // :1550
            MarkdownView.SetMarkdownPointSize(markdown, 12); // :1551
            MarkdownView.SetMarkdownDimmed(markdown, true); // :1552
            markdown.Margin = new Thickness(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, 0, 0, 0); // :1553
            host.Child = markdown;
        }
        Build(entry.Item.TimelineItemContent);
        host.Track(() => entry.Item.TimelineItemContent, Build);
        return host;
    }
}

/// <summary>:1564-1577 <c>struct HydraHeadsList: View</c>.</summary>
public sealed class HydraHeadsList : Decorator
{
    /// <param name="entries">:1565 <c>let entries: [TimelineEntry]</c>.</param>
    /// <param name="runtime">:1566 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="workingDirectory">:1567 <c>var workingDirectory: String?</c>.</param>
    // :1569-1576 body
    public HydraHeadsList(ValueList<TimelineEntry> entries, ThreadRuntime runtime, string? workingDirectory = null)
    {
        this.runtime = runtime; this.workingDirectory = workingDirectory;
        foreach (var entry in entries) { var row = new ToolRow(entry, runtime, workingDirectory); rows.Add((entry, row)); column.Children.Add(row); }
        Child = column;
    }

    private readonly ThreadRuntime runtime;
    private readonly string? workingDirectory;
    private readonly StackPanel column = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<(TimelineEntry Entry, ToolRow Row)> rows = [];

    /// <summary>:1572 <c>ForEach(entries)</c> run again with the list as it is now: a row whose entry stands is the same view (its expansion, its head's popover, its anchor), a head
    /// that joined gets a row at its place, one that went loses its row. ED-255 (3).</summary>
    internal void Update(ValueList<TimelineEntry> entries)
    {
        var kept = rows.ToList(); var next = new List<(TimelineEntry Entry, ToolRow Row)>();
        foreach (var entry in entries)
        {
            var at = kept.FindIndex(pair => ReferenceEquals(pair.Entry, entry));
            if (at >= 0) { next.Add(kept[at]); kept.RemoveAt(at); }
            else next.Add((entry, new ToolRow(entry, runtime, workingDirectory)));
        }
        foreach (var gone in kept) SwiftTransition.Remove(column, gone.Row);
        rows.Clear(); rows.AddRange(next);
        for (var index = 0; index < next.Count; index++)
        {
            var row = next[index].Row; var container = SwiftTransition.Container(row); var at = column.Children.IndexOf(container);
            if (at == index) continue;
            if (at >= 0) column.Children.Move(at, index);
            else SwiftTransition.Insert(column, Math.Min(index, column.Children.Count), row);
        }
    }
}

/// <summary>:1586-1634 <c>@MainActor enum WorkGroupSummary</c>.</summary>
public static class WorkGroupSummary
{
    // :1588 private static let order: [ToolCall.Kind] = [.edit, .read, .command, .search, .web, .mcp, .agent, .other]
    private static ValueList<ToolCall.Kind> Order { get; } = new([
        ToolCall.Kind.Edit, ToolCall.Kind.Read, ToolCall.Kind.Command, ToolCall.Kind.Search,
        ToolCall.Kind.Web, ToolCall.Kind.Mcp, ToolCall.Kind.Agent, ToolCall.Kind.Other,
    ]);

    // :1579-1610 static func text(for:) -> String: the "Edited files, ran commands" line, derived from the calls' kinds
    // rather than their titles. First part capitalized, the rest lowered on their first character only, in the fixed
    // kind order; a kind with a call still running reads in the present tense. PR-CHAT-091.
    public static string Text(IReadOnlyList<TimelineEntry> @for)
    {
        // :1591-1597 kind to whether a call of it is still running, in one pass.
        var running = new Dictionary<ToolCall.Kind, bool>();
        foreach (var entry in @for)
        {
            if (entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue; // :1595
            var call = tool.Value;
            running[call.ToolCallKind] = (running.TryGetValue(call.ToolCallKind, out var was) && was) || call.ToolCallStatus == ToolCall.Status.Running; // :1596
        }
        var parts = new List<string>(); // :1598
        foreach (var kind in Order) // :1599-1602
        {
            if (!running.TryGetValue(kind, out var isRunning)) continue;
            parts.Add(Label(kind, isRunning));
        }
        if (parts.Count == 0) return "Worked"; // :1603
        if (parts.Count <= 1) return parts[0]; // :1604
        var rest = parts.Skip(1).Select(part => // :1605-1608
            part.Length == 0 ? part : SwiftString.Lowercased(SwiftString.Prefix(part, 1)) + SwiftString.DropFirst(part));
        return string.Join(", ", new[] { parts[0] }.Concat(rest)); // :1609
    }

    // :1612-1620 static func symbol(for:) -> String: the first kind present, in the same order, mapped through
    // ToolPresentation.symbol; a wrench when there is none. PR-CHAT-091.
    public static string Symbol(IReadOnlyList<TimelineEntry> @for)
    {
        var kinds = new HashSet<ToolCall.Kind>(); // :1613-1616
        foreach (var entry in @for)
        {
            if (entry.Item.TimelineItemContent is TimelineItem.Content.Tool tool) kinds.Add(tool.Value.ToolCallKind);
        }
        ToolCall.Kind[] order = [ // :1617
            ToolCall.Kind.Edit, ToolCall.Kind.Read, ToolCall.Kind.Command, ToolCall.Kind.Search,
            ToolCall.Kind.Web, ToolCall.Kind.Mcp, ToolCall.Kind.Agent, ToolCall.Kind.Other,
        ];
        foreach (var kind in order) // :1618
        {
            if (kinds.Contains(kind)) return ToolPresentation.Symbol(kind); // :1619
        }
        return "wrench.and.screwdriver"; // :1618
    }

    // :1622-1633 private static func label(for:running:) -> String
    private static string Label(ToolCall.Kind @for, bool running) => @for switch
    {
        ToolCall.Kind.Edit => running ? "Editing files" : "Edited files", // :1624
        ToolCall.Kind.Read => running ? "Reading files" : "Read files", // :1625
        ToolCall.Kind.Command => running ? "Running commands" : "Ran commands", // :1626
        ToolCall.Kind.Search => running ? "Searching" : "Searched", // :1627
        ToolCall.Kind.Web => running ? "Browsing" : "Browsed", // :1628
        ToolCall.Kind.Mcp => running ? "Calling tools" : "Called tools", // :1629
        ToolCall.Kind.Agent => running ? "Delegating tasks" : "Delegated tasks", // :1630
        ToolCall.Kind.Other => running ? "Using tools" : "Used tools", // :1631
    };
}

/// <summary>:1636-1854 <c>struct ToolRow: View</c>.</summary>
public sealed class ToolRow : Decorator
{
    private readonly TimelineEntry entry;       // :1638
    private readonly ThreadRuntime runtime;     // :1639
    private readonly string? workingDirectory;  // :1640

    // :1641 isExpanded, :1648-1652 needsAnchor and :1653-1654 isShowingHead: @State read by the body.
    private sealed class ViewState : MainActorModel
    {
        private bool isExpanded;
        private bool needsAnchor;
        private bool isShowingHead;

        public bool IsExpanded { get => Read(isExpanded); set => Set(ref isExpanded, value); }

        public bool NeedsAnchor { get => Read(needsAnchor); set => Set(ref needsAnchor, value); }

        public bool IsShowingHead { get => Read(isShowingHead); set => Set(ref isShowingHead, value); }
    }

    private readonly ViewState state = new();

    // :1642-1647 @State private var preview = AttachmentPreviewSlot(): the row's label, so the changes popover hangs from
    // the text that was tapped, and the panel for an image the agent looked at; both made on first use. Read only by the
    // actions and the anchor, never by the body, so a plain field (R-VIEW-2).
    private readonly AttachmentPreviewSlot preview = new();

    // PORT-ONLY (OD-0136 amendment 2, step 8): the status slot and the shining label, kept across this row's bodies (see NthToolRowParts).
    private readonly NthToolRowParts parts = new();

    /// <param name="entry">:1638 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:1639 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="workingDirectory">:1640 <c>var workingDirectory: String?</c>.</param>
    // :1657-1717 body. PR-CHAT-093 to PR-CHAT-100. It reads entry.item (a running call keeps changing) and, for a call
    // that sent out a head, that head's record through @Environment(AppModel.self); both are read when the row is in a
    // window, where the model is attached (AppEnvironment), and the body is built again whenever either changes.
    public ToolRow(TimelineEntry entry, ThreadRuntime runtime, string? workingDirectory = null)
    {
        this.entry = entry;
        this.runtime = runtime;
        this.workingDirectory = workingDirectory;

        (TimelineItem.Content Content, Guid? HeadID, HydraHeadInfo? Head)? built = null;
        this.Track(Snapshot, snapshot =>
        {
            if (built is { } previous && Equals(previous.Content, snapshot.Content) && previous.HeadID == snapshot.HeadID && Equals(previous.Head, snapshot.Head)) return;
            built = snapshot;
            Child = snapshot.Content is TimelineItem.Content.Tool tool ? Body(tool.Value, snapshot.HeadID, snapshot.Head) : null; // :1658
        });

        // :1715 .onDisappear { [weak slot] in slot?.close() }
        var slot = new WeakReference<AttachmentPreviewSlot>(preview);
        DetachedFromVisualTree += (_, _) =>
        {
            if (slot.TryGetTarget(out var held)) held.Close();
        };
    }

    // What the body reads from outside the row: the entry's item, the head a delegation call sent out (:1672) and that
    // head's record in the model (:1766).
    private (TimelineItem.Content Content, Guid? HeadID, HydraHeadInfo? Head) Snapshot()
    {
        var content = entry.Item.TimelineItemContent;
        var headID = content is TimelineItem.Content.Tool { Value.ToolCallKind: ToolCall.Kind.Agent } ? runtime.HydraHead(entry.Id) : null;
        var head = headID is { } id ? AppEnvironment.GetAppModel(this).Thread(id)?.Hydra : null;
        return (content, headID, head);
    }

    // :1658-1716
    private Control Body(ToolCall call, Guid? headID, HydraHeadInfo? head)
    {
        // :1659-1669 a row that carries a diff never unfolds: the diff opens in the changes popover, below the row's
        // label. A read of an image opens the image itself the same way, whatever text came with it, and so does any
        // other row with an image and nothing else to show. Only a row whose content is its own output still expands.
        var edits = new ValueList<FileEdit>(call.Edits.Where(edit => edit.Path.Length != 0)); // :1664
        var opensDiff = edits.Count != 0; // :1665
        var imagePath = opensDiff ? null : PreviewImages.ResolveToolImagePath(call, workingDirectory); // :1666
        var showsOutput = !opensDiff && (call.Output.Length != 0 || (call.Detail ?? "").Length != 0); // :1667
        var opensImage = imagePath is not null && (call.ToolCallKind == ToolCall.Kind.Read || !showsOutput); // :1668
        var opensPopover = opensDiff || opensImage; // :1669
        // :1670-1673 a row that sent out a head opens the head's steps in a popover of its own, never its raw output.
        var opensHead = headID is not null;
        // :1674-1677 a call with nothing to open is rendered as a line of text rather than a control.
        var isTappable = opensPopover || opensHead || showsOutput;
        // :1678-1683 values and weak boxes only below: the action must not keep this row's entry alive.
        var turn = entry.TurnID;
        var diffEdits = edits;
        var slot = new WeakReference<AttachmentPreviewSlot>(preview);
        var rt = new WeakReference<ThreadRuntime>(runtime);
        var viewState = state;

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = TimelineMetrics.RowSpacing }; // :1684
        var line = Line(call, imagePath, opensPopover, opensHead, showsOutput, head);
        if (isTappable) // :1685-1706
        {
            Button button = null!;
            button = PlainControls.Button(line, () => // :1686-1701, :1705 .buttonStyle(.plain)
            {
                if (opensHead) // :1688-1689
                {
                    viewState.IsShowingHead = !viewState.IsShowingHead;
                }
                else if (opensDiff) // :1690-1692
                {
                    if (!slot.TryGetTarget(out var held) || held.Anchor.Value is not { } view) return;
                    if (rt.TryGetTarget(out var target)) target.ShowDiff(view, turn, PreviewPanelEdge.MinY, diffEdits);
                }
                else if (opensImage && imagePath is { } path) // :1693-1694
                {
                    if (slot.TryGetTarget(out var held)) held.Panel.Toggle(PreviewImages.Attachment(path), held.Anchor.Value, PreviewPanelEdge.MinY);
                }
                else // :1695-1700
                {
                    var reveal = ThreadTimeline.GetRevealTimelineEnd(button);
                    Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () =>
                    {
                        viewState.IsExpanded = !viewState.IsExpanded;
                        if (viewState.IsExpanded) reveal?.Action();
                    });
                }
            });
            line.Background ??= Brushes.Transparent; // :1703 .contentShape(.rect)
            // :1706 .help(helpText(...)): the text reads isExpanded.
            button.Track(() => HelpText(opensDiff, opensImage, opensHead, showsOutput), help => ToolTip.SetTip(button, help));
            stack.Children.Add(button);
        }
        else
        {
            stack.Children.Add(line); // :1708
        }
        if (showsOutput && !opensImage) // :1710-1713 if isExpanded, showsOutput, !opensImage
        {
            ToolDetailView? detail = null;
            stack.Track(() => state.IsExpanded, expanded =>
            {
                if (expanded && detail is null)
                {
                    detail = new ToolDetailView(call, workingDirectory) { Margin = new Thickness(0, 0, 12, 0) }; // :1711-1712
                    SwiftTransition.Insert(stack, stack.Children.Count, detail);
                }
                else if (!expanded && detail is { } shown)
                {
                    detail = null;
                    SwiftTransition.Remove(stack, shown);
                }
            });
        }
        return stack;
    }

    // :1719-1735 private static func departingHead(for:): the head a delegation call is sending out, read from the call
    // itself before its thread is linked: a roster name leading the title ("Pip: the merge popover") names the face;
    // otherwise the team's first face stands in and the row speaks of "a head". The task is the title past the name.
    private static (HydraPersona Persona, string? Name, string Task) DepartingHead(ToolCall @for)
    {
        var title = @for.Title.Trimmed(); // :1724
        // :1725-1726 "Subagent" and the like are the tool's own word for itself, not a task.
        var generic = new HashSet<string>(["subagent", "agent", "task", ""], SwiftStringComparer.Instance);
        var colon = title.IndexOf(':', StringComparison.Ordinal); // :1727 title.firstIndex(of: ":")
        if (colon >= 0)
        {
            var name = title[..colon].Trimmed(); // :1728
            if (HydraRoster.Persona(named: name) is { } persona) // :1729
            {
                var task = title[(colon + 1)..].Trimmed(); // :1730
                return (persona, persona.Name, task); // :1731
            }
        }
        return (HydraRoster.Persona(at: 0), null, generic.Contains(SwiftString.Lowercased(title)) ? "" : title); // :1734
    }

    // :1737-1746 private static func departureLabel(for:name:task:): on its way, out, or never sent.
    private static string DepartureLabel(ToolCall @for, string? name, string task)
    {
        var who = name ?? "a head"; // :1739
        var rest = task.Length == 0 ? "" : ": " + task; // :1740
        return @for.ToolCallStatus switch // :1741-1745
        {
            ToolCall.Status.Running => "Sending out " + who + rest,
            ToolCall.Status.Failed => "Could not send out " + who + rest,
            _ => "Sent out " + who + rest,
        };
    }

    // :1748-1755 private static func departureStatus(for:): the glyph's badge for a departing row, nothing on its way,
    // the outcome once known.
    private static HydraHeadInfo.Status? DepartureStatus(ToolCall @for) => @for.ToolCallStatus switch
    {
        ToolCall.Status.Running => null,
        ToolCall.Status.Failed => HydraHeadInfo.Status.Failed,
        _ => HydraHeadInfo.Status.Completed,
    };

    // :1757-1845 private func line(call:imagePath:opensPopover:opensHead:showsOutput:): icon, text, stats and chevron,
    // laid out the same whether or not the line can be tapped.
    private Panel Line(ToolCall call, string? imagePath, bool opensPopover, bool opensHead, bool showsOutput, HydraHeadInfo? head)
    {
        // :1761-1763 the slot alone: the hover responder and the anchor view must not keep this row's entry alive.
        var previewSlot = new WeakReference<AttachmentPreviewSlot>(preview);
        var viewState = state;
        var running = call.ToolCallStatus == ToolCall.Status.Running;
        // :1767-1771 before the head's thread is linked to the call, the row still wears a head's glyph and speaks of
        // sending it out. Never a spinner and "Delegating Subagent".
        (HydraPersona Persona, string? Name, string Task)? departing = call.ToolCallKind == ToolCall.Kind.Agent && head is null ? DepartingHead(call) : null;
        var mcp = ToolPresentation.McpParts(call); // :1772
        var isBadge = head is not null || departing is not null || mcp is not null; // :1773

        var run = new ToolLinePanel(isBadge ? 8 : TimelineMetrics.IconSpacing); // :1775
        if (head is { } linked) // :1776-1777
            run.Children.Add(new HydraGlyph(linked.Persona, size: 18, isRunning: running && linked.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, status: linked.HydraHeadInfoStatus));
        else if (departing is { } leaving) // :1778-1780
            run.Children.Add(new HydraGlyph(leaving.Persona, size: 18, isRunning: running, status: DepartureStatus(call))
                .Transition(SwiftTransition.Scale(0.6).Combined(SwiftTransition.Opacity)));
        else if (mcp is { } server) // :1781-1782
            run.Children.Add(new MCPGlyph(server.Entry, size: 18, isRunning: running, status: call.ToolCallStatus));
        else // :1783-1784 a read that looked at an image wears the photo glyph
            run.Children.Add(new ToolStatusIcon(call, symbol: imagePath is not null && call.ToolCallKind == ToolCall.Kind.Read ? "photo" : null, mark: parts.Mark));

        // :1786-1795 one text run after the icon, so the row reads as icon + space + text.
        var text = head is { } sent ? (running ? "Sending out" : "Sent out") + " " + sent.Persona.Name + ": " + call.Title // :1788
            : departing is { } away ? DepartureLabel(call, away.Name, away.Task) // :1789
            : mcp is { } tool ? ToolPresentation.McpLabel(tool.Entry, tool.Tool, running) // :1790
            : ToolPresentation.Label(call); // :1791
        TextBlock label;
        if (!isBadge && (running || parts.Label is not null))
        {
            // PORT-ONLY (OD-0136 amendment 2, step 8): a plain row seen running wears the shining label, which the next bodies of this row take over, so the light stops
            // and the colour steps from --text to --text2 when the call is done. A row made already done has the plain label below.
            var fresh = parts.Label is null;
            var shine = parts.Label ??= new NthToolLabel { MaxLines = 1 };
            NthToolRow.TakeOut(shine);
            shine.FullText = text;
            ChatFont.Chat(SwiftFont.TextStyle.Callout).Apply(shine); // :1792
            shine.Rest(running, animate: !fresh);
            label = shine;
        }
        else
        {
            label = new MiddleTruncatingTextBlock { FullText = text, MaxLines = 1 }; // :1794-1795
            (isBadge ? ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium) : ChatFont.Chat(SwiftFont.TextStyle.Callout)).Apply(label); // :1792
            label.Foreground = (isBadge ? Chrome.PrimaryText.Opacity(0.9) : SwiftColor.Secondary).Brush(label); // :1793
        }
        run.Children.Add(label);
        if (ToolPresentation.Stats(call) is { } stats) // :1796-1798
            run.Children.Add(new DiffStatLabel { Additions = stats.Additions, Deletions = stats.Deletions });
        // :1799-1810 beside the subject, not out at the trailing edge: the chevron belongs to the row's own text.
        if (opensPopover || opensHead)
        {
            var down = SwiftImage.SystemName("chevron.down").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)); // :1802-1803
            down.Foreground = Chrome.SecondaryText.Brush(down); // :1804
            run.Children.Add(down);
        }
        else if (showsOutput)
        {
            var right = SwiftImage.SystemName("chevron.right").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)); // :1806-1807
            right.Foreground = Chrome.SecondaryText.Brush(right); // :1808
            var turn = new AnimatedDouble(angle => right.RenderTransform = new RotateTransform(angle));
            right.Track(() => viewState.IsExpanded, expanded => turn.Set(expanded ? 90 : 0, Transaction.Current)); // :1809
            run.Children.Add(right);
        }

        // :1812-1820 a badge sits in the timeline's pill; a plain row has no inset.
        var pill = new Border
        {
            Padding = isBadge
                ? new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical)
                : default,
            Child = run,
        };
        if (isBadge)
        {
            pill.RoundedRectangle(TimelineMetrics.PillRadius, RoundedCornerStyle.Continuous); // :1817
            pill.Background = SwiftColor.Quaternary.Opacity(0.32).Brush(pill); // :1818
        }
        else
        {
            pill.Background = Brushes.Transparent;
        }
        // :1821-1823 the anchor view is mounted once the pointer has been on the label: a click always follows a hover,
        // and hovering is off while the timeline scrolls, so a row scrolling past never mounts one.
        pill.OnHover(hovering =>
        {
            if (hovering && !viewState.NeedsAnchor) viewState.NeedsAnchor = true;
        });
        // :1824-1828 .background { if opensPopover, needsAnchor { AttachmentAnchorCapture { [weak previewSlot] ... } } }:
        // once mounted it stays, because taking it away under an open popover closes it.
        var anchored = new Panel();
        anchored.Children.Add(pill);
        if (opensPopover)
        {
            anchored.Track(() => viewState.NeedsAnchor, needed =>
            {
                if (!needed || anchored.Children.Count > 1) return;
                anchored.Children.Insert(0, new AttachmentAnchorCapture(view =>
                {
                    if (previewSlot.TryGetTarget(out var held)) held.SetAnchor(view);
                }));
            });
        }
        // :1829-1831 on the text run (the badge), not the full-width row, so the popover's arrow points at the words.
        var withHead = new HeadStepsPopoverModifier(
            opensHead ? runtime.HydraHead(entry.Id) : null,
            new Binding<bool>(() => viewState.IsShowingHead, value => viewState.IsShowingHead = value)).Body(anchored);

        // :1774 HStack(spacing: iconSpacing) { badge; Spacer(minLength: 8); outcome }: the outcome docks at the trailing
        // edge; the badge takes the rest, less the spacer's minimum and the stack's two gaps.
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 12, 0) }; // :1844 .padding(.trailing, 12)
        row.Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)); // :1843
        Control? outcome = null;
        if (call.ToolCallStatus == ToolCall.Status.Failed && call.ExitCode is { } exitCode) // :1833-1836
        {
            var exit = new TextBlock { Text = "exit " + exitCode.ToString(CultureInfo.InvariantCulture) };
            ChatFont.Chat(SwiftFont.TextStyle.Caption).MonospacedDigit().Apply(exit); // :1835
            exit.Foreground = Chrome.Danger.Brush(exit); // :1836
            outcome = exit;
        }
        else if (call.ToolCallStatus == ToolCall.Status.Declined) // :1837-1840
        {
            var declined = new TextBlock { Text = "Declined" };
            ChatFont.Chat(SwiftFont.TextStyle.Caption).Apply(declined); // :1839
            declined.Foreground = Chrome.Warning.Brush(declined); // :1840
            outcome = declined;
        }
        if (outcome is not null)
        {
            outcome.Margin = new Thickness(TimelineMetrics.IconSpacing, 0, 0, 0);
            outcome.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(outcome, Dock.Right);
            row.Children.Add(outcome);
        }
        withHead.Margin = new Thickness(0, 0, TimelineMetrics.IconSpacing + 8, 0); // :1832 Spacer(minLength: 8)
        DockPanel.SetDock(withHead, Dock.Left);
        row.Children.Add(withHead);
        return row;
    }

    // :1774-1810 HStack gives the text the remaining proposal, while the glyph, stats and chevron keep their size.
    // Its desired width stays content-sized so a short subject's chevron remains beside the words, not at the edge.
    private sealed class ToolLinePanel(double spacing) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var reserved = spacing * Math.Max(0, Children.Count - 1);
            var height = 0d;
            for (var index = 0; index < Children.Count; index++)
            {
                if (index == 1) continue;
                var child = Children[index];
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                reserved += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
            var label = Children[1];
            label.Measure(new Size(Math.Max(0, availableSize.Width - reserved), availableSize.Height));
            return new Size(reserved + label.DesiredSize.Width, Math.Max(height, label.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var reserved = spacing * Math.Max(0, Children.Count - 1)
                + Children.Where((_, index) => index != 1).Sum(child => child.DesiredSize.Width);
            var x = 0d;
            for (var index = 0; index < Children.Count; index++)
            {
                var child = Children[index];
                var width = index == 1 ? Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - reserved)) : child.DesiredSize.Width;
                var height = Math.Min(child.DesiredSize.Height, finalSize.Height);
                child.Arrange(new Rect(x, (finalSize.Height - height) / 2, width, height));
                x += width + spacing;
            }
            return finalSize;
        }
    }

    // :1847-1853 private func helpText(opensDiff:opensImage:opensHead:showsOutput:) -> String
    private string HelpText(bool opensDiff, bool opensImage, bool opensHead, bool showsOutput)
    {
        if (opensHead) return "Show the head's steps"; // :1848
        if (opensDiff) return "Show this change"; // :1849
        if (opensImage) return "Show the image"; // :1850
        if (!showsOutput) return ""; // :1851
        return state.IsExpanded ? "Hide the output" : "Show the output"; // :1852
    }
}

/// <summary>:1858-1872 <c>private struct HeadStepsPopoverModifier: ViewModifier</c>.</summary>
/// <param name="HeadID">:1859 <c>let headID: UUID?</c>.</param>
/// <param name="IsPresented">:1860 <c>@Binding var isPresented: Bool</c>.</param>
public sealed record HeadStepsPopoverModifier(Guid? HeadID, Binding<bool> IsPresented)
{
    // :1856-1871 func body(content:): hangs the head's steps popover from a row that sent out a head. A row that did not
    // mounts nothing: the popover, its state and its anchor exist only where they can open.
    public Control Body(Control content)
    {
        if (HeadID is not { } headID) return content; // :1863, :1868-1870
        // :1864-1867 content.popover(isPresented: $isPresented, arrowEdge: .bottom) { HydraHeadStepsPopover(headID:)
        // .presentedChrome() }
        var host = new Panel();
        host.Children.Add(content);
        host.Children.Add(PlainControls.Popover(content, IsPresented, () => new HydraHeadStepsPopover(headID).PresentedChrome()));
        return host;
    }
}

/// <summary>:1877-2065 <c>struct HydraHeadStepsPopover: View</c>.</summary>
public sealed class HydraHeadStepsPopover : Decorator
{
    private new const double Width = 440;     // :1881 (new: Swift's name, hides Layoutable.Width inside this class)
    private new const double MaxHeight = 520; // :1882 (new: Swift's name, hides Layoutable.MaxHeight inside this class)

    /// <param name="headID">:1878 <c>let headID: UUID</c>.</param>
    // :1884-1931 body
    public HydraHeadStepsPopover(Guid headID)
    {
        base.Width = Width;
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this);
            return (Head: model.Thread(headID)?.Hydra, Entries: model.Runtime(headID).Entries);
        }, value => Child = Body(value.Head, Steps(value.Entries)));
    }

    // :1884-1931 the list reads the head's runtime, not the lead's. Selection stays per text block.
    private static Control Body(HydraHeadInfo? head, IReadOnlyList<TimelineEntry> steps)
    {
        var column = new StackPanel { Spacing = 16 };
        if (head is not null)
        {
            column.Children.Add(Header(head));
            if (head.Task.Length != 0) column.Children.Add(Selectable(head.Task, Chrome.PrimaryText));
        }
        var list = new StackPanel { Spacing = TimelineMetrics.RowSpacing };
        list.Children.Add(SectionTitle("Steps"));
        if (steps.Count == 0) list.Children.Add(TimelineRows.ChatText("No steps yet.", color: Chrome.SecondaryText));
        else foreach (var entry in steps) list.Children.Add(Step(entry));
        column.Children.Add(list);
        if (head?.IsFinished == true && (head.Summary ?? "").Trimmed() is { Length: > 0 } report)
        {
            var section = new StackPanel { Spacing = 8 };
            section.Children.Add(SectionTitle("Report"));
            section.Children.Add(Selectable(report, Chrome.PrimaryText));
            column.Children.Add(section);
        }
        return new PopoverScroll(() => new Border { Padding = new Thickness(16), Width = Width, Child = column }, maxHeight: MaxHeight);
    }

    // :1934-1952 header and sectionTitle.
    private static Control Header(HydraHeadInfo head)
    {
        var header = new DockPanel();
        var status = TimelineRows.ChatText(StatusLine(head), SwiftFont.TextStyle.Caption, color: Chrome.SecondaryText, singleLine: true);
        status.Margin = new Thickness(12 + 2 * TimelineMetrics.IconSpacing, 0, 0, 0); // :1934-1946 Spacer(minLength: 12) between the stack's two 8-point gaps: 8 + 12 + 8
        status.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(status, Dock.Right); header.Children.Add(status);
        var name = new HStack { Spacing = TimelineMetrics.IconSpacing }; // HStack :1935: its children share one centre line
        name.Children.Add(new HydraGlyph(head.Persona, 22, head.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, head.HydraHeadInfoStatus));
        var text = TimelineRows.ChatText(head.Persona.Name, SwiftFont.TextStyle.Headline, SwiftFont.Weight.Semibold, Chrome.PrimaryText);
        text.VerticalAlignment = VerticalAlignment.Center; name.Children.Add(text); header.Children.Add(name);
        return header;
    }

    private static TextBlock SectionTitle(string title) => TimelineRows.ChatText(title, SwiftFont.TextStyle.Caption, SwiftFont.Weight.Semibold, Chrome.SecondaryText); // :1950-1952
    private static SelectableTextBlock Selectable(string text, SwiftColor color) // :1897-1902, :1919-1923, :1980-2001
    {
        var label = TimelineRows.SelectableText(wraps: true); label.Text = text;
        ChatFont.Chat(SwiftFont.TextStyle.Callout).Apply(label); label.Foreground = color.Brush(label); return label;
    }

    // :1955-2006 plain text only: no timeline menus, output expansion or per-step popovers here.
    private static Control Step(TimelineEntry entry)
    {
        switch (entry.Item.TimelineItemContent)
        {
            case TimelineItem.Content.Tool tool:
                var call = tool.Value;
                var mcp = ToolPresentation.McpParts(call);
                var row = new TimelineBaselineRow { Spacing = TimelineMetrics.IconSpacing };
                row.Children.Add(new Border { Width = TimelineMetrics.IconWidth, Child = mcp is { } badge
                    ? new MCPGlyph(badge.Entry, 14, call.ToolCallStatus == ToolCall.Status.Running, call.ToolCallStatus) : new ToolStatusIcon(call) });
                var words = mcp is { } server ? ToolPresentation.McpLabel(server.Entry, server.Tool, call.ToolCallStatus == ToolCall.Status.Running) : ToolPresentation.Label(call);
                var label = new MiddleTruncatingTextBlock { FullText = words, LineLimit = 2 }; // :1969-1973, ED-217
                ChatFont.Chat(SwiftFont.TextStyle.Callout).Apply(label); label.Foreground = Chrome.SecondaryText.Brush(label); row.Children.Add(label);
                if (ToolPresentation.Stats(call) is { } stats) row.Children.Add(new DiffStatLabel { Additions = stats.Additions, Deletions = stats.Deletions });
                return row;
            case TimelineItem.Content.Assistant reply: return Selectable(reply.Value.Text, Chrome.PrimaryText);
            case TimelineItem.Content.User user: return Selectable(user.Value.Text, Chrome.SecondaryText);
            case TimelineItem.Content.Notice notice:
                var line = new TimelineBaselineRow { Spacing = TimelineMetrics.IconSpacing };
                line.Children.Add(new Border { Width = TimelineMetrics.IconWidth, Child = SwiftImage.SystemName(NoticeSymbol(notice.Value.NoticeLevel))
                    .Font(ChatFont.Chat(SwiftFont.TextStyle.Caption)).ForegroundStyle(NoticeColor(notice.Value.NoticeLevel)) });
                line.Children.Add(Selectable(notice.Value.Message, NoticeColor(notice.Value.NoticeLevel))); return line;
            default: return new Control();
        }
    }

    // :2010-2025 private static func statusLine(for:) -> String
    private static string StatusLine(HydraHeadInfo @for)
    {
        var parts = new List<string> { @for.HydraHeadInfoStatus switch {
            HydraHeadInfo.Status.Running => "Working\u2026", HydraHeadInfo.Status.Completed => "Done",
            HydraHeadInfo.Status.Failed => "Failed", HydraHeadInfo.Status.Stopped => "Stopped" } };
        if (@for.ToolCalls > 0) parts.Add(@for.ToolCalls == 1 ? "1 tool" : $"{@for.ToolCalls} tools");
        if (@for.FinishedAt is { } end) parts.Add(TimelineRows.Duration((end - @for.StartedAt).TotalSeconds));
        return string.Join(" \u00b7 ", parts);
    }

    // :2030-2048 private static func steps(in:) -> [TimelineEntry]
    private static ValueList<TimelineEntry> Steps(IReadOnlyList<TimelineEntry> @in)
    {
        var sawBrief = false;
        return new ValueList<TimelineEntry>(@in.Where(entry =>
        {
            switch (entry.Item.TimelineItemContent)
            {
                case TimelineItem.Content.User user:
                    if (!sawBrief) { sawBrief = true; return false; }
                    return user.Value.Text.Trimmed().Length != 0;
                case TimelineItem.Content.Assistant reply: return reply.Value.Text.Trimmed().Length != 0;
                case TimelineItem.Content.Tool or TimelineItem.Content.Notice: return true;
                default: return false;
            }
        }));
    }

    // :2050-2056 private static func noticeSymbol(for:) -> String
    private static string NoticeSymbol(Notice.Level @for) => @for switch {
        Notice.Level.Info => "info.circle", Notice.Level.Warning => "exclamationmark.triangle", Notice.Level.Error => "exclamationmark.octagon" };

    // :2058-2064 private static func noticeColor(for:) -> Color
    private static SwiftColor NoticeColor(Notice.Level @for) => @for switch {
        Notice.Level.Info => SwiftColor.Secondary, Notice.Level.Warning => Chrome.Warning, Notice.Level.Error => Chrome.Danger };
}

/// <summary>:2067-2092 <c>private struct ToolStatusIcon: View</c>.</summary>
public sealed class ToolStatusIcon : Decorator
{
    /// <param name="call">:2068 <c>let call: ToolCall</c>.</param>
    /// <param name="symbol">:2069-2070 <c>var symbol: String?</c>: stands in for the kind's symbol (a photo for a read
    /// that looked at one).</param>
    // :2072-2091 body. PR-CHAT-099.
    /// <param name="mark">PORT-ONLY (OD-0136 amendment 2, step 8): the row's status slot from its last body, so the tick draws in when the call completes while on screen;
    /// a new slot (a row made already done) shows its state in place.</param>
    public ToolStatusIcon(ToolCall call, string? symbol = null, NthStatusMark? mark = null)
    {
        Control icon = call.ToolCallStatus switch
        {
            // :2075-2077 the running mark was a 10 x 10 pt mini ProgressView in the 16 pt icon column (PR-CHAT-099); step 8 of the nth motion pack: the 14 px
            // spinner, in the 16 px status slot that also draws the done tick.
            ToolCall.Status.Running => Mark(mark, NthStatus.Working),
            ToolCall.Status.Failed => Glyph("xmark.circle.fill", Chrome.Danger), // :2078-2080
            ToolCall.Status.Declined => Glyph("hand.raised.slash", Chrome.Warning), // :2081-2083
            // :2084-2086 the kind's symbol was the done look; step 8: the tick. A read that looked at an image keeps its photo glyph (:1783-1784, a marker of what the row opens).
            ToolCall.Status.Completed when symbol is not null => Glyph(symbol, Chrome.SecondaryText),
            ToolCall.Status.Completed => Mark(mark, NthStatus.Done),
        };
        var column = new Border { Width = TimelineMetrics.IconWidth, Child = icon }; // :2090 .frame(width: TimelineMetrics.iconWidth)
        var font = ChatFont.Chat(SwiftFont.TextStyle.Caption); // :2089 .font(.chat(.caption))
        column.Font(font);
        SwiftImage.SetFont(column, font);
        Child = column;

        static SwiftImage Glyph(string name, SwiftColor color)
        {
            var image = SwiftImage.SystemName(name);
            image.Foreground = color.Brush(image);
            return image;
        }

        // The status is set while the slot is still in its last body (so a change animates), then it is taken out for this one.
        static NthStatusMark Mark(NthStatusMark? kept, NthStatus status)
        {
            var mark = kept ?? NthToolRow.NewMark();
            mark.Status = status;
            NthToolRow.TakeOut(mark);
            return mark;
        }
    }
}

/// <summary>
/// PORT-ONLY (ED-256 (7)). A selectable text whose line limit (<see cref="TextBlock.MaxLines"/>) ends its last
/// line in its <see cref="TextBlock.TextTrimming"/> ellipsis whenever more text follows, as a SwiftUI Text cut by <c>.lineLimit</c>
/// shows (<c>truncationMode</c> .tail, the default): the tool detail's <c>.lineLimit(12)</c>, TimelineRows.swift:2125. Avalonia
/// 12.1.2's TextLayout collapses the last line only when the cut falls inside a wrapped line (TextLineBreak.IsSplit); a cut at a hard
/// line break (a multi-line command, a heredoc) kept the twelfth line whole with no ellipsis, so the reader could not tell the text
/// goes on. Here the layout's last line is collapsed in that case too: its words and the ellipsis when both fit the width, else the
/// line cut short to fit with its ellipsis, as Avalonia cuts a wrapped line. The text, its selection and copying keep every line.
/// </summary>
internal sealed class LineLimitedSelectableText : SelectableTextBlock
{
    // The same style key as any SelectableTextBlock, so a control theme that styles one styles this one.
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override Avalonia.Media.TextFormatting.TextLayout CreateTextLayout(string? text)
    {
        var layout = base.CreateTextLayout(text);
        if (MaxLines <= 0 || TextTrimming == TextTrimming.None || text is null) return layout;
        // TextLayout.TextLines is the layout's own array of lines (Avalonia 12.1.2), so the cut line can be put in its place.
        if (layout.TextLines is not Avalonia.Media.TextFormatting.TextLine[] lines || lines.Length != MaxLines) return layout;
        var last = lines[^1];
        if (last.HasCollapsed || last.FirstTextSourceIndex + last.Length >= text.Length) return layout; // already cut, or nothing follows
        var runProperties = new Avalonia.Media.TextFormatting.GenericTextRunProperties(
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, TextDecorations, Foreground);
        var trimming = TextTrimming.CreateCollapsingProperties(new TextCollapsingCreateInfo(layout.MaxWidth, runProperties, FlowDirection));
        lines[^1] = last.Collapse(new EllipsisAfterBreak(trimming));
        return layout;
    }

    // The trimming's own ellipsis, cut into a line too wide for it; a line with room for it keeps all its characters (its line break
    // aside) and takes the ellipsis after them.
    private sealed class EllipsisAfterBreak(Avalonia.Media.TextFormatting.TextCollapsingProperties trimming)
        : Avalonia.Media.TextFormatting.TextCollapsingProperties
    {
        public override double Width => trimming.Width;

        public override Avalonia.Media.TextFormatting.TextRun Symbol => trimming.Symbol;

        public override FlowDirection FlowDirection => trimming.FlowDirection;

        public override Avalonia.Media.TextFormatting.TextRun[]? Collapse(Avalonia.Media.TextFormatting.TextLine textLine) =>
            trimming.Collapse(textLine)
            ?? CreateCollapsedRuns(textLine, textLine.Length - textLine.NewLineLength,
                Avalonia.Media.TextFormatting.TextFormatter.CreateSymbol(Symbol, FlowDirection));
    }
}

/// <summary>:2094-2167 <c>private struct ToolDetailView: View</c>.</summary>
public sealed class ToolDetailView : Decorator
{
    // :2103 private static let outputLineLimit = 30
    private const int OutputLineLimit = 30;

    // :2098 @State private var showsFullOutput = false, read by the body.
    private sealed class ViewState : MainActorModel
    {
        private bool showsFullOutput;

        public bool ShowsFullOutput { get => Read(showsFullOutput); set => Set(ref showsFullOutput, value); }
    }

    /// <param name="call">:2095 <c>let call: ToolCall</c>.</param>
    /// <param name="workingDirectory">:2096 <c>var workingDirectory: String?</c>.</param>
    // :2115-2166 body. PR-CHAT-101. Tool output renders inline and collapsed (:2100-2102): a nested scroll view inside
    // the timeline's scroll view fights gestures and hitches.
    public ToolDetailView(ToolCall call, string? workingDirectory = null)
    {
        var state = new ViewState();
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = TimelineMetrics.RowSpacing }; // :2116
        if (PreviewImages.ResolveToolImagePath(call, workingDirectory) is { } imagePath) // :2117-2119
            stack.Children.Add(new ToolImagePreview(imagePath));
        if (call.Detail is { Length: > 0 } detail) // :2120-2126
        {
            // :2121 a Text with no fixedSize wraps at the width the row offers; :2124 .textSelection(.enabled).
            var text = VeiledText.SelectableText(new LineLimitedSelectableText(), wraps: true);
            // :2125 .lineLimit(12) counts the wrapped lines; a Text it cuts ends its last line in an ellipsis (SwiftUI's
            // truncationMode .tail, the default; RequestCards.cs:111 takes a lineLimit the same way), wherever the cut falls, at a
            // hard line break too (ED-256 (7); LineLimitedSelectableText).
            text.Text = detail; text.MaxLines = 12; text.TextTrimming = TextTrimming.CharacterEllipsis;
            ChatFont.Chat(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Apply(text); // :2122
            text.Foreground = Chrome.SecondaryText.Brush(text); // :2123
            stack.Children.Add(text);
        }
        foreach (var edit in call.Edits) // :2127 ForEach(Array(call.edits.enumerated()), id: \.offset)
        {
            if (edit.Diff is not { Length: > 0 } diff) continue; // :2128
            var file = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :2129
            var path = new TextBlock { Text = edit.Path, Margin = new Thickness(10, 6) }; // :2130, :2133-2134
            ChatFont.Chat(SwiftFont.TextStyle.Caption).Apply(path); // :2131
            path.Foreground = Chrome.SecondaryText.Brush(path); // :2132
            file.Children.Add(path);
            file.Children.Add(new DiffLinesView(DiffDetailCache.File(diff, edit.Path), showsLineNumbers: false)); // :2135
            var plate = new Border { Child = file, ClipToBounds = true }; // :2138 .clipShape(.rect(cornerRadius: 10, style: .continuous))
            plate.RoundedRectangle(10, RoundedCornerStyle.Continuous); // :2137
            plate.Background = SwiftColor.Quaternary.Opacity(0.45).Brush(plate); // :2137
            stack.Children.Add(plate);
        }
        if (call.Output.Length != 0) // :2141
        {
            // :2142-2146 a running command's row re-renders on every flush of its output: the preview scans for its
            // thirty-first newline rather than splitting the whole output into lines.
            var output = TrimNewlines(call.Output); // :2145 trimmingCharacters(in: .newlines)
            var lineCount = 1; // :2146 1 + output.utf8.count { $0 == 0x0A }
            foreach (var character in output)
            {
                if (character == '\n') lineCount++;
            }
            var outputStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = TimelineMetrics.RowSpacing }; // :2149
            // :2150 a Text with no fixedSize wraps at its plate's width (.frame(maxWidth: .infinity) :2153); :2152 .textSelection(.enabled).
            var text = TimelineRows.SelectableText(wraps: true);
            ChatFont.Chat(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Apply(text); // :2151
            var plate = new Border // :2153-2155 .frame(maxWidth: .infinity, alignment: .leading).padding(10)
            {
                Child = text,
                Padding = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            plate.RoundedRectangle(10, RoundedCornerStyle.Continuous); // :2155
            plate.Background = SwiftColor.Quaternary.Opacity(0.45).Brush(plate); // :2155
            outputStack.Children.Add(plate);
            if (lineCount > OutputLineLimit) // :2156-2162
            {
                // .buttonStyle(.link).font(.chat(.caption)): the accent-coloured plain-text button AttachmentPreview.cs's
                // LinkButton takes for the link style (ENGINEERING DECISION there; UNVERIFIED OD-0001 for underline and
                // hover).
                var title = new TextBlock();
                ChatFont.Chat(SwiftFont.TextStyle.Caption).Apply(title); // :2161
                // OD-0088 (ED-253): the link style draws in the view's tint, the theme's accent (WindowManager.swift:186-192):
                // Chrome.accent, the user's pick when there is one.
                title.Foreground = Chrome.Accent.Brush(title);
                var toggle = PlainControls.Button(title, () => state.ShowsFullOutput = !state.ShowsFullOutput); // :2158
                toggle.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                toggle.HorizontalAlignment = HorizontalAlignment.Left;
                toggle.Track(() => state.ShowsFullOutput, full => title.Text = full
                    ? "Show less"
                    : "Show full output (" + lineCount.ToString(CultureInfo.InvariantCulture) + " lines)"); // :2157
                outputStack.Children.Add(toggle);
            }
            // :2147-2148 collapsed = !showsFullOutput && lineCount > outputLineLimit
            text.Track(() => state.ShowsFullOutput, full =>
                text.Text = !full && lineCount > OutputLineLimit ? Head(output, OutputLineLimit) : output);
            stack.Children.Add(outputStack);
        }
        Child = stack;
    }

    // :2105-2113 private static func head(of:lines:) -> String: the first `lines` lines of `text`, without splitting the
    // rest. A newline is one UTF-16 unit as it is one UTF-8 byte, so the offsets agree.
    private static string Head(string of, int lines)
    {
        var remaining = lines; // :2107
        for (var index = 0; index < of.Length; index++) // :2108
        {
            if (of[index] != '\n') continue;
            remaining -= 1; // :2109
            if (remaining == 0) return of[..index]; // :2110
        }
        return of; // :2112
    }

    // trimmingCharacters(in: .newlines) (:2145): CharacterSet.newlines at both ends, nothing else.
    private static string TrimNewlines(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && FoundationText.IsNewline(text[start])) start++;
        while (end > start && FoundationText.IsNewline(text[end - 1])) end--;
        return text[start..end];
    }
}

/// <summary>:2169-2182 <c>private enum DiffDetailCache</c>: parsed per-file diffs for expanded tool rows. Parsing runs
/// once per distinct diff, so opening and closing a row is instant no matter how large the patch is. PR-CHAT-102.</summary>
public static class DiffDetailCache
{
    // :2172 @MainActor private static var cache = RecentCache<String, DiffFile>(limit: 60)
    private static readonly RecentCache<string, DiffFile> cache = new(limit: 60);

    // :2174-2181 @MainActor static func file(diff:path:) -> DiffFile
    public static DiffFile File(string diff, string path)
    {
        var key = path + "\n" + diff; // :2176
        if (cache.TryGetValue(key, out var hit)) return hit; // :2177
        var parsed = DiffParser.ParseHunks(diff, path); // :2178
        cache.Insert(parsed, key); // :2179
        return parsed; // :2180
    }
}

/// <summary>:2184-2255 <c>enum ToolPresentation</c>.</summary>
public static class ToolPresentation
{
    // :2189-2206 static func label(for:) -> String
    // Ported ahead of the rest of this skeleton, for HydraProgressBar's EventsIn (self-contained: only
    // ToolCall.Kind/Status and SwiftString). Root-word roots per :2192-2200; the two-part join per :2201-2205.
    public static string Label(ToolCall @for)
    {
        var roots = Roots(@for.ToolCallKind); // :2192-2200
        var subject = @for.Title; // :2201
        var words = SwiftString.Split(@for.Title, " ", maxSplits: 1); // :2202
        if (words.Count > 0 && roots.Any(root => SwiftString.Equal(root, SwiftString.Lowercased(words[0])))) // :2203
            subject = words.Count > 1 ? words[1] : ""; // :2204
        var parts = new List<string>();
        var verb = Verb(@for);
        if (verb.Length > 0) parts.Add(verb);
        if (subject.Length > 0) parts.Add(subject);
        return string.Join(" ", parts); // :2205
    }

    // :2192-2200, the switch inside label(for:) that builds `roots`.
    private static IReadOnlyList<string> Roots(ToolCall.Kind kind) => kind switch
    {
        ToolCall.Kind.Command => new[] { "run", "bash", "shell", "exec", "execute", "command" },
        ToolCall.Kind.Read => new[] { "read", "view", "open", "cat" },
        ToolCall.Kind.Edit => new[] { "edit", "write", "multiedit", "create", "notebookedit", "apply_patch", "patch" },
        ToolCall.Kind.Search => new[] { "search", "grep", "glob", "find" },
        ToolCall.Kind.Web => new[] { "fetch", "webfetch", "websearch", "browse" },
        ToolCall.Kind.Mcp => new[] { "call" },
        ToolCall.Kind.Agent => new[] { "delegate", "agent", "task" },
        ToolCall.Kind.Other => new[] { "use" },
        _ => throw new InvalidOperationException(),
    };

    // :2208-2220 static func verb(for:) -> String
    // Ported alongside label(for:), which calls it; self-contained (ToolCall.Kind/Status only).
    public static string Verb(ToolCall @for)
    {
        var running = @for.ToolCallStatus == ToolCall.Status.Running; // :2209
        return @for.ToolCallKind switch // :2210-2219
        {
            ToolCall.Kind.Command => running ? "Running" : "Ran",
            ToolCall.Kind.Read => running ? "Reading" : "Read",
            ToolCall.Kind.Edit => running ? "Editing" : "Edited",
            ToolCall.Kind.Search => running ? "Searching" : "Searched",
            ToolCall.Kind.Web => running ? "Browsing" : "Browsed",
            ToolCall.Kind.Mcp => running ? "Calling" : "Called",
            ToolCall.Kind.Agent => running ? "Delegating" : "Delegated",
            ToolCall.Kind.Other => running ? "Using" : "Used",
            _ => throw new InvalidOperationException(),
        };
    }

    // :2222-2233 static func symbol(for:) -> String
    // Ported ahead of the rest of this skeleton, for HydraProgressBar's EventsIn (self-contained: only
    // ToolCall.Kind).
    public static string Symbol(ToolCall.Kind @for) => @for switch
    {
        ToolCall.Kind.Command => "terminal",
        ToolCall.Kind.Read => "doc.text",
        ToolCall.Kind.Edit => "pencil",
        ToolCall.Kind.Search => "magnifyingglass",
        ToolCall.Kind.Web => "globe",
        ToolCall.Kind.Mcp => "puzzlepiece.extension",
        ToolCall.Kind.Agent => "person.2",
        ToolCall.Kind.Other => "wrench.and.screwdriver",
        _ => throw new InvalidOperationException(),
    };

    // :2235-2240 static func stats(for:) -> (additions: Int, deletions: Int)?: nil with no edits, and nil when the sums
    // come to nothing, so a row never shows "+0 -0". PR-CHAT-095. The skeleton's Int32 pair is kept; the sums are Swift
    // Ints (long) narrowed with a trap on overflow (R-NUM-4).
    public static (int Additions, int Deletions)? Stats(ToolCall @for)
    {
        if (@for.Edits.Count == 0) return null; // :2236
        long additions = 0; // :2237
        long deletions = 0; // :2238
        foreach (var edit in @for.Edits)
        {
            additions += edit.Additions;
            deletions += edit.Deletions;
        }
        return additions + deletions > 0 ? ((int)additions, (int)deletions) : null; // :2239
    }

    // :2242-2250 static func mcpParts(_:) -> (entry: MCPCatalogEntry, tool: String)?: an mcp call titled "server
    // U+00B7 tool" whose server is in the catalog. PR-CHAT-096.
    public static (MCPCatalogEntry Entry, string Tool)? McpParts(ToolCall call)
    {
        if (call.ToolCallKind != ToolCall.Kind.Mcp) return null; // :2243
        const string separator = " \U000000B7 "; // U+00B7 MIDDLE DOT, a space on each side (:2244)
        var at = call.Title.IndexOf(separator, StringComparison.Ordinal); // :2244 range(of:)
        if (at < 0) return null;
        var server = call.Title[..at].Trimmed(); // :2245
        var tool = call.Title[(at + separator.Length)..].Trimmed(); // :2246
        if (server.Length == 0 || tool.Length == 0) return null; // :2247
        if (MCPCatalog.EntryMatching(server) is not { } entry) return null; // :2248
        return (entry, tool); // :2249
    }

    // :2252-2254 static func mcpLabel(entry:tool:running:) -> String: the running flag is accepted and unused, as upstream.
    public static string McpLabel(MCPCatalogEntry entry, string tool, bool running) => entry.Name + ": " + tool; // :2253
}

/// <summary>:2257-2338 <c>struct PlanCard: View</c>.</summary>
public sealed class PlanCard : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool isShowingPlan;
        public bool IsShowingPlan { get => Read(isShowingPlan); set => Set(ref isShowingPlan, value); }
    }
    // :2261 @State private var isShowingPlan, and ChatBadge.swift:20's @State coordinator. SwiftUI keeps both for as long as the
    // card's identity lives: ThreadTimeline.swift:1979-1984 ForEach(groups) in TurnRunningBlock keys the card by its plan
    // entry, whichever render of the running turn's block holds it, so a plan popover that is open stays open while the
    // turn's other rows arrive (PR-CHAT-113). The port builds a block's rows anew whenever the block changes, so the
    // identity's state is held per plan entry and place here, and every card built for them reads it. A card in another
    // place (the finished turn's steps, its folded answer: :2642, :2672) is another view in SwiftUI, a new @State with its
    // own coordinator, as the running block's card going away closes its popover. ENGINEERING DECISION ED-256.
    private sealed class Identity
    {
        public readonly ViewState State = new();
        public readonly BadgePopoverCoordinator Coordinator = new();
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TimelineEntry, Dictionary<string, Identity>> identities = new();
    private readonly Identity identity;
    private readonly ViewState state; // :2261, the identity's
    private readonly TimelineEntry entry;
    private readonly ThreadRuntime runtime;
    /// <param name="entry">:2258 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:2259 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="place">PORT-ONLY: where the card sits, which with the entry is its SwiftUI identity: the running turn's
    /// block and the plain groups (the default), or one of the finished turn's two places.</param>
    // :2263-2316 body
    public PlanCard(TimelineEntry entry, ThreadRuntime runtime, string place = "turn")
    {
        this.entry = entry;
        this.runtime = runtime;
        var places = identities.GetValue(entry, _ => []);
        if (!places.TryGetValue(place, out var found)) places[place] = found = new Identity();
        identity = found;
        state = found.State;
        void Build(TimelineItem.Content content)
        {
            if (content is not TimelineItem.Content.Plan item) { Child = null; return; }
            var plan = item.Value;
            var box = state;
            Control Glyph() => plan.ProposedPlanState == ProposedPlan.State.Drafting
                ? new Border { Child = new NthStatusMark { Status = NthStatus.Working, MarkSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Width = 14 } // PORT-ONLY (OD-0136 amendment 2, step 8): the 14 px spinner, as a tool row's
                : SwiftImage.SystemName("list.bullet.clipboard").ForegroundStyle(Chrome.Accent);
            // :2265 the same ChatBadge across every render of this card: its coordinator is the badge's @State
            // (ChatBadge.swift:20), so an open plan popover outlives the plan's updates (PR-CHAT-113).
            if (Child is ChatBadge badge)
            {
                badge.Update(Title(plan.ProposedPlanState), Glyph, () => Detail(plan), Caption(plan.ProposedPlanState),
                    showsChevron: plan.Markdown.Length != 0, needsAttention: NeedsAttention(plan.ProposedPlanState), isEnabled: plan.Markdown.Length != 0);
                return;
            }
            Child = new ChatBadge(Title(plan.ProposedPlanState), new Binding<bool>(() => box.IsShowingPlan, value => box.IsShowingPlan = value),
                glyph: Glyph, detail: () => Detail(plan), caption: Caption(plan.ProposedPlanState), showsChevron: plan.Markdown.Length != 0,
                needsAttention: NeedsAttention(plan.ProposedPlanState), isEnabled: plan.Markdown.Length != 0, coordinator: identity.Coordinator);
        }
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, Build);
    }

    // :2279-2313
    private Control Detail(ProposedPlan plan)
    {
        var column = new StackPanel { Spacing = 12 };
        // :2281-2285 HStack { Text("Plan"); Spacer(); CopyButton }: an HStack's default .center alignment puts the heading and
        // the copy button on one centre line (UI-CHAT-030); in a DockPanel the shorter heading was drawn at the top.
        var header = new DockPanel();
        var copy = new CopyButton(plan.Markdown) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        var heading = TimelineRows.ChatText("Plan", SwiftFont.TextStyle.Headline, SwiftFont.Weight.Semibold); // :2282; ChatFont.swift:7.
        heading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(heading);
        column.Children.Add(header);
        column.Children.Add(new PopoverScroll(() => new MarkdownView(plan.Markdown, plan.ProposedPlanState == ProposedPlan.State.Drafting)
            { Margin = new Thickness(16, 0) }, maxHeight: 420));
        if (plan.ProposedPlanState == ProposedPlan.State.Proposed)
        {
            var buttons = new HStack { Spacing = 8 }; // HStack :2295: its children share one centre line
            var box = state;
            var target = runtime;
            var id = entry.Id;
            buttons.Children.Add(TimelineRows.GlassCommand("Implement plan", () => { box.IsShowingPlan = false; target.ImplementPlan(id); }, true));
            buttons.Children.Add(TimelineRows.GlassCommand("Dismiss", () => { box.IsShowingPlan = false; target.DismissPlan(id); }, false));
            void Busy(bool busy)
            {
                buttons.IsEnabled = !busy;
                ToolTip.SetShowOnDisabled(buttons, true);
                ToolTip.SetTip(buttons, busy ? "Wait for the current turn to finish" : null);
                foreach (var button in buttons.Children) ToolTip.SetTip(button, busy ? "Wait for the current turn to finish" : null);
            }
            Busy(runtime.PendingPlanApproval is not null || runtime.IsRunning);
            buttons.Track(() => runtime.PendingPlanApproval is not null || runtime.IsRunning, Busy);
            column.Children.Add(buttons);
        }
        return new Border { Padding = new Thickness(16), Width = 520, Child = column };
    }

    // :2318-2324 private static func title(for:) -> String
    private static string Title(ProposedPlan.State @for) => @for switch
    { ProposedPlan.State.Drafting => "Writing a plan", ProposedPlan.State.Proposed => "Plan ready", ProposedPlan.State.Accepted or ProposedPlan.State.Dismissed => "Plan", _ => throw new UnreachableException() };

    // :2326-2333 private static func caption(for:) -> String?
    private static string? Caption(ProposedPlan.State @for) => @for switch
    { ProposedPlan.State.Drafting => null, ProposedPlan.State.Proposed => "Implement or dismiss", ProposedPlan.State.Accepted => "Approved", ProposedPlan.State.Dismissed => "Dismissed", _ => throw new UnreachableException() };

    // :2335-2337 private static func needsAttention(for:) -> Bool
    private static bool NeedsAttention(ProposedPlan.State @for) => @for == ProposedPlan.State.Proposed;
}

/// <summary>:2340-2383 <c>struct TodoListRow: View</c>.</summary>
public sealed class TodoListRow : Decorator
{
    /// <param name="entry">:2341 <c>let entry: TimelineEntry</c>.</param>
    // :2343-2374 body
    public TodoListRow(TimelineEntry entry)
    {
        Padding = new Thickness(0, 0, 96, 0); // :2371
        var column = new StackPanel { Spacing = 7 };
        var header = new HStack { Spacing = 6 }; // HStack :2346: its children share one centre line
        header.Children.Add(SwiftImage.SystemName("checklist").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption)).ForegroundStyle(Chrome.SecondaryText));
        var count = new OpacityCrossfade(TimelineRows.ChatText("", SwiftFont.TextStyle.Caption, color: Chrome.SecondaryText));
        header.Children.Add(count);
        column.Children.Add(header);
        // The pill fills the slot it is eased in (the delegation card's pattern, PR-CHAT-149): its fill, edge and corners are drawn at the size the slot has got to, larger or
        // smaller, rather than at its contents' size and clipped by the slot, which is a cut when the card shrinks (ED-285 (12)(b)).
        var card = TimelineRows.Pill(column, SwiftColor.Quaternary.Opacity(0.32), new Thickness(14), fillsSlot: true);
        // :2372 .animation(.snappy(duration: 0.2), value: steps) sits on the whole card, so the card's size follows its contents too: a step that
        // arrives, a line that grows or wraps, a card that gets wider or narrower (OD-0076: a card that changes size eases to it).
        var slot = new SizeSlot(card) { ContentFollowsSize = true };
        var rows = new List<(TimelineBaselineRow Row, OpacityCrossfade Icon, TextBlock Text, AnimatedColor IconColour, AnimatedColor TextColour, Action<bool, Animation?> Strike)>();
        // A step that has gone has faded out before it leaves the column; the card then eases to the size it has without it.
        column.Children.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Remove) slot.Note(Animation.Snappy(duration: 0.2)); };
        // :2372 .animation(.snappy(duration: 0.2), value: steps): the colours a step is drawn in follow the change along the animation, as
        // SwiftUI interpolates a foreground style, a step's glyph and the header's count crossfade, and a step that arrives or goes fades in or
        // out (the default transition of a view in a stack). ENGINEERING DECISION (OD-0076: content that swaps crossfades).
        static SwiftColor IconColourFor(TodoStep.Status status) => status == TodoStep.Status.Pending ? SwiftColor.Tertiary : Chrome.Accent; // :2355
        static SwiftColor TextColourFor(TodoStep.Status status) => status == TodoStep.Status.Done ? SwiftColor.Secondary : SwiftColor.Primary; // :2357
        // :2357 `.strikethrough(step.status == .done, color: .secondary)` changes under the same animation: the strike's colour moves between clear and
        // .secondary along it, so the line fades in (or out) at its full length with the step's other changes instead of cutting (OD-0076; OD-0105 (2), the owner's
        // pick: "Fade in", not a line drawn across). The decoration is on the text
        // while the strike is drawn or on its way out, and off once it is gone, so a step that is not done has none.
        static Action<bool, Animation?> StrikeOf(TimelineBaselineRow row, TextBlock text)
        {
            var colour = new AnimatedColor(row);
            var decoration = new TextDecoration { Location = TextDecorationLocation.Strikethrough, Stroke = colour.Brush };
            var struck = false;
            colour.Brush.PropertyChanged += (_, e) =>
            {
                if (e.Property == SolidColorBrush.ColorProperty && !struck && colour.Brush.Color.A == 0) text.TextDecorations = null;
            };
            colour.Set(SwiftColor.Secondary.Opacity(0), null);
            return (done, animation) =>
            {
                struck = done;
                if (done && text.TextDecorations is null) text.TextDecorations = new TextDecorationCollection { decoration };
                colour.Set(done ? SwiftColor.Secondary : SwiftColor.Secondary.Opacity(0), animation);
                if (!done && colour.Brush.Color.A == 0) text.TextDecorations = null;
            };
        }
        void Build(TimelineItem.Content content)
        {
            if (content is not TimelineItem.Content.Todos todos || todos.Value.Count == 0) { Child = null; return; }
            var steps = todos.Value;
            slot.Note(Transaction.Current);
            var counted = $"{steps.Count(step => step.TodoStepStatus == TodoStep.Status.Done)} of {steps.Count} done";
            if (((TextBlock)count.Current!).Text != counted) count.Show(TimelineRows.ChatText(counted, SwiftFont.TextStyle.Caption, color: Chrome.SecondaryText));
            while (rows.Count > steps.Count) { SwiftTransition.Remove(column, rows[^1].Row); rows.RemoveAt(rows.Count - 1); }
            var arrived = new HashSet<int>();
            while (rows.Count < steps.Count)
            {
                var row = new TimelineBaselineRow { Spacing = 8 };
                var icon = new OpacityCrossfade();
                var text = TimelineRows.ChatText("");
                var iconColour = new AnimatedColor(row);
                var textColour = new AnimatedColor(row);
                iconColour.Set(IconColourFor(steps[rows.Count].TodoStepStatus), null); // a step arriving is not tweened from nothing
                textColour.Set(TextColourFor(steps[rows.Count].TodoStepStatus), null);
                text.Foreground = textColour.Brush;
                row.Children.Add(icon); row.Children.Add(text);
                arrived.Add(rows.Count);
                var strike = StrikeOf(row, text);
                strike(steps[rows.Count].TodoStepStatus == TodoStep.Status.Done, null); // a step arriving is not tweened from nothing
                rows.Add((row, icon, text, iconColour, textColour, strike));
                SwiftTransition.Insert(column, column.Children.Count, row);
            }
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i]; var row = rows[i];
                // :2352-2355 the symbol follows the step's status; a changed one crossfades with the one it replaces (a step that has just
                // arrived has no old one: it comes in with its row).
                var symbol = Symbol(step.TodoStepStatus);
                if (row.Icon.Current is not SwiftImage { } shown || shown.SymbolName != symbol)
                {
                    var icon = SwiftImage.SystemName(symbol).Font(ChatFont.Chat(SwiftFont.TextStyle.Callout));
                    icon.Foreground = row.IconColour.Brush;
                    if (arrived.Contains(i)) Transaction.WithAnimation(null, () => row.Icon.Show(icon)); else row.Icon.Show(icon);
                }
                row.IconColour.Set(IconColourFor(step.TodoStepStatus), Transaction.Current);
                row.Text.Text = step.Text;
                var done = step.TodoStepStatus == TodoStep.Status.Done;
                row.TextColour.Set(TextColourFor(step.TodoStepStatus), Transaction.Current);
                row.Strike(done, Transaction.Current); // :2372 .animation(.snappy(duration: 0.2), value: steps), no reduced-motion gate (ED-264 (2))
            }
            Child = slot;
        }
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, value => Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => Build(value)));
    }

    // :2376-2382 private static func symbol(for:) -> String
    private static string Symbol(TodoStep.Status @for) => @for switch
    { TodoStep.Status.Pending => "circle", TodoStep.Status.Active => "circle.dotted.circle", TodoStep.Status.Done => "checkmark.circle.fill", _ => throw new UnreachableException() };
}

/// <summary>PORT-ONLY. A place where one view is swapped for another by a crossfade: SwiftUI's <c>.contentTransition(.opacity)</c> on a Text
/// (TimelineRows.swift:664) and what a changed <c>Image(systemName:)</c> or <c>Text</c> does under a <c>.animation</c> (:2372); OD-0076: content
/// that swaps crossfades. The old view stays under the new one and fades out along the animation of the transaction the swap was made in while
/// the new one fades in; with no animation the swap is at once. Only the newest view takes part in layout, the old one is drawn at the same
/// corner. A swap made while a fade is still going carries on from the opacity each view has reached (no jump).</summary>
internal sealed class OpacityCrossfade : Panel
{
    private readonly Dictionary<Control, AnimatedDouble> fades = [];

    public OpacityCrossfade(Control? initial = null)
    {
        if (initial is null) return;
        Current = initial;
        Children.Add(initial);
    }

    /// <summary>The view on show (fading in, or in); what the last <see cref="Show"/> was given.</summary>
    public Control? Current { get; private set; }

    /// <summary>Every view mounted, the leaving ones with the opacity they have now.</summary>
    internal IEnumerable<(Control View, double Opacity)> Layers => Children.Select(child => (child, child.Opacity));

    /// <summary>Swaps in <paramref name="fresh"/>, along <see cref="Transaction.Current"/>.</summary>
    public void Show(Control? fresh)
    {
        var animation = Transaction.Current;
        var old = Current;
        Current = fresh;
        if (old is not null && !ReferenceEquals(old, fresh))
        {
            if (animation is null) Drop(old);
            else Fade(old).Set(0, animation);
        }
        if (fresh is not null && !Children.Contains(fresh))
        {
            Children.Add(fresh);
            var fade = Fade(fresh);
            if (animation is null) fade.Set(1, null);
            else { fade.Set(0, null); fade.Set(1, animation); }
        }
        InvalidateMeasure();
    }

    private AnimatedDouble Fade(Control view)
    {
        if (fades.TryGetValue(view, out var existing)) return existing;
        var fade = new AnimatedDouble(value =>
        {
            view.Opacity = Math.Clamp(value, 0, 1);
            if (value <= 0 && !ReferenceEquals(view, Current)) Drop(view);
        });
        fades[view] = fade;
        fade.Set(view.Opacity, null); // a view mounted with no fade of its own starts from the opacity it has (a fade that has begun is carried on, never restarted)
        return fade;
    }

    private void Drop(Control view)
    {
        fades.Remove(view);
        Children.Remove(view);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return Current is { } shown && Children.Contains(shown) ? shown.DesiredSize : default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(default, child.DesiredSize));
        return finalSize;
    }
}

/// <summary>PORT-ONLY. A card whose size follows its contents as a spring or curve rather than a jump (SwiftUI animates the frame a view takes
/// when its contents change under an <c>.animation</c>: TimelineRows.swift:2372, OD-0076: a card that changes size eases to it). The content
/// is laid out at its own natural size (the same idea as the merge pill's width slot) and the slot is as large as the animation has got to,
/// clipping it, so the card's edge moves along the curve and everything under it moves with it. A change made under an animation is noted with
/// <see cref="Note"/> before the layout pass that follows reads it; a size that changes with no animation noted (a window being resized) is
/// followed at once.</summary>
internal sealed class SizeSlot : Panel
{
    private readonly Control content;
    private readonly AnimatedDouble width;
    private readonly AnimatedDouble height;
    private double presentedWidth, presentedHeight, targetWidth, targetHeight;
    private bool measured;
    private Animation? pending;
    private int noted;

    public SizeSlot(Control content)
    {
        this.content = content;
        ClipToBounds = true;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(content);
        width = new AnimatedDouble(value => { presentedWidth = value; InvalidateMeasure(); });
        height = new AnimatedDouble(value => { presentedHeight = value; InvalidateMeasure(); });
    }

    /// <summary>Whether the content itself is arranged at the size the slot has got to (so its own edge, background and corners move along the curve), rather than at its
    /// natural size and clipped to the slot. The content then stretches over what it is given, larger or smaller (the delegation card's pill,
    /// TimelineRows.Pill with fillsSlot); its words keep their layout because the card's lines are horizontal stacks, which lay each child out at its own width
    /// whatever width they are given (TimelineRowsReviewAb3FixTests reads a head's words mid-ease both ways).</summary>
    internal bool ContentFollowsSize { get; init; }

    /// <summary>The size the card is drawn at now.</summary>
    internal Size Presented => new(presentedWidth, presentedHeight);

    /// <summary>A change under an animation is noted when it is made: the layout pass that follows reads it.</summary>
    public void Note(Animation? animation)
    {
        pending = animation;
        InvalidateMeasure();
        // A layout pass measures the slot before the card inside it has been told its contents changed (that reaches it as the pass goes on), so the
        // noted animation waits for the size to change; it lapses once the layout passes are done, so a later size change of another kind (a resize) is followed at once.
        var turn = ++noted;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (turn == noted) pending = null; }, Avalonia.Threading.DispatcherPriority.Background);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        content.Measure(availableSize);
        var natural = content.DesiredSize;
        if (!measured || natural.Width != targetWidth || natural.Height != targetHeight)
        {
            var animation = measured ? pending : null;
            pending = null;
            var widthChanged = !measured || natural.Width != targetWidth;
            var heightChanged = !measured || natural.Height != targetHeight;
            targetWidth = natural.Width;
            targetHeight = natural.Height;
            measured = true;
            if (widthChanged) width.Set(natural.Width, animation);
            if (heightChanged) height.Set(natural.Height, animation);
        }
        return new Size(Math.Max(0, presentedWidth), Math.Max(0, presentedHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // laid out at its own size, clipped to the slot; or, for a card whose own edge moves, at the size the slot has got to
        content.Arrange(ContentFollowsSize ? new Rect(default, new Size(Math.Max(0, presentedWidth), Math.Max(0, presentedHeight))) : new Rect(default, content.DesiredSize));
        return finalSize;
    }
}

/// <summary>PORT-ONLY (OD-0076, PR-CHAT-104). The height a block presents while it eases to the height its content has. The content is laid out at its own
/// natural height and the block is as tall as the animation has got to, clipping it while it runs, so a growing block reveals its content from the top and a
/// shrinking one hides it from the bottom (a height reveal: no opacity lies on the content, which for a finished turn's open steps can run to thousands of
/// points). A change made under an animation is noted with <see cref="Note"/> before the layout pass that reads it; a block that is to arrive at its place
/// one height and ease to its own is begun with <see cref="Begin"/>; a height that changes with no animation noted is followed at once, and one that changes
/// while an ease is running goes on easing to the new height on the same animation.</summary>
internal sealed class HeightEase
{
    private readonly AnimatedDouble height;
    private readonly Action invalidate;
    private readonly Action<bool> clip;
    private double presented, target;
    private bool measured;
    private Animation? pending, last;
    private int noted;
    private bool lapsing;

    public HeightEase(Action invalidate, Action<bool> clip)
    {
        this.invalidate = invalidate;
        this.clip = clip;
        height = new AnimatedDouble(value =>
        {
            presented = value;
            clip(IsEasing);
            invalidate();
        });
    }

    /// <summary>The height is on its way to another.</summary>
    public bool IsEasing => measured && Math.Abs(presented - target) > 0.01;

    /// <summary>A change under <paramref name="animation"/> is noted when it is made; the layout pass that follows reads it. It lapses once the layout passes are done, so a
    /// later change of another kind (a resize) is followed at once.</summary>
    public void Note(Animation? animation)
    {
        pending = animation;
        lapsing = false;
        invalidate();
        // The note lapses once the layout pass that reads it is done: the first pass after this job has run (a pass may come before or after the job, so the job
        // only marks the note as one that lapses at the next pass, and the pass takes it away).
        var turn = ++noted;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (turn == noted) lapsing = true; }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Takes the place of a block <paramref name="from"/> tall and eases to the content's own height under <paramref name="animation"/> at the first layout pass.</summary>
    public void Begin(double from, Animation animation)
    {
        presented = from;
        target = from;
        measured = true;
        height.Set(from, null);
        pending = animation;
        last = animation;
        clip(false);
        invalidate();
    }

    /// <summary>The height to present for a content <paramref name="natural"/> tall.</summary>
    public double Resolve(double natural)
    {
        if (!measured || natural != target)
        {
            var animation = measured ? pending ?? (IsEasing ? last : null) : null;
            pending = null;
            target = natural;
            measured = true;
            height.Set(natural, animation);
            if (animation is not null) last = animation;
            clip(IsEasing);
        }
        if (lapsing) { pending = null; lapsing = false; }
        return presented;
    }
}

/// <summary>PORT-ONLY (OD-0076, PR-CHAT-104). A host whose height follows its content's along the animation a change was noted under (<see cref="HeightEase"/>): the finished
/// turn's body, which folds open and shut on the header's <c>.snappy(duration: 0.24)</c> (TimelineRows.swift:2609-2616).</summary>
internal sealed class HeightFoldHost : Panel
{
    private readonly HeightEase ease;

    public HeightFoldHost(Control content)
    {
        Content = content;
        Children.Add(content);
        ease = new HeightEase(InvalidateMeasure, clip => ClipToBounds = clip);
    }

    /// <summary>The content, laid out at its own natural height.</summary>
    public Control Content { get; }

    /// <summary>A change to the content made under <paramref name="animation"/> (null: none).</summary>
    public void Note(Animation? animation) => ease.Note(animation);

    protected override Size MeasureOverride(Size availableSize)
    {
        Content.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var natural = Content.DesiredSize;
        return new Size(natural.Width, ease.Resolve(natural.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Content.Arrange(new Rect(0, 0, finalSize.Width, Content.DesiredSize.Height));
        return finalSize;
    }
}

// PORT-ONLY: source HStack(alignment: .firstTextBaseline) at :2356, :1959, :1991 and :2389. Give the
// text the remaining proposed width, and align native text metrics with SwiftImage's ED-182 baseline.
internal sealed class TimelineBaselineRow : Panel
{
    public TimelineBaselineRow() => UseLayoutRounding = false; // Preserve the font's fractional first baseline.
    public double Spacing { get; init; }
    private double baseline;
    private static double Baseline(Control child)
    {
        if (child is TextBlock text && text.TextLayout.TextLines.Count != 0) return text.TextLayout.TextLines[0].Baseline;
        if (child is OpacityCrossfade { Current: { } shown }) return Baseline(shown); // the view on show sits where it would alone
        if (child is Decorator { Child: { } inner } wrapper) return wrapper.Padding.Top + Baseline(inner);
        var offset = TextBlock.GetBaselineOffset(child);
        return offset > 0 ? offset : child.DesiredSize.Height;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var flexible = Children.OfType<TextBlock>().FirstOrDefault();
        var fixedWidth = Spacing * Math.Max(0, Children.Count - 1);
        foreach (var child in Children.Where(child => !ReferenceEquals(child, flexible)))
        { child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); fixedWidth += child.DesiredSize.Width; }
        flexible?.Measure(new Size(Math.Max(0, availableSize.Width - fixedWidth), double.PositiveInfinity));
        baseline = Children.Count == 0 ? 0 : Children.Max(Baseline);
        var height = Children.Count == 0 ? 0 : Children.Max(child => baseline - Baseline(child) + child.DesiredSize.Height);
        return new Size(fixedWidth + (flexible?.DesiredSize.Width ?? 0), height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var flexible = Children.OfType<TextBlock>().FirstOrDefault();
        var fixedWidth = Children.Where(child => !ReferenceEquals(child, flexible)).Sum(child => child.DesiredSize.Width)
            + Spacing * Math.Max(0, Children.Count - 1);
        var x = 0.0;
        foreach (var child in Children)
        {
            var width = ReferenceEquals(child, flexible) ? Math.Max(0, finalSize.Width - fixedWidth) : child.DesiredSize.Width;
            child.Arrange(new Rect(x, baseline - Baseline(child), width, child.DesiredSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}

/// <summary>:2385-2431 <c>struct NoticeRow: View</c>.</summary>
public sealed class NoticeRow : Decorator
{
    /// <param name="entry">:2386 <c>let entry: TimelineEntry</c>.</param>
    // :2388-2404 body
    public NoticeRow(TimelineEntry entry)
    {
        void Build(TimelineItem.Content content)
        {
            if (content is not TimelineItem.Content.Notice item) { Child = null; return; }
            if (Marrow.App.Port.Ignis.Views.IgnisViews.EventLineFor(entry.Item) is { } ignisLine) { Child = ignisLine; return; } // PORT-ONLY Ignis U4 (ED-NEW-ignis-views): an "ignis:event:" notice is a quiet event line
            var notice = item.Value;
            var row = new TimelineBaselineRow { Spacing = 8 };
            row.Children.Add(SwiftImage.SystemName(Symbol(notice.NoticeLevel)).Font(ChatFont.Chat(SwiftFont.TextStyle.Callout)).ForegroundStyle(Color(notice.NoticeLevel)));
            var message = TimelineRows.SelectableText(wraps: true); message.Text = notice.Message; // :2394 .textSelection(.enabled)
            ChatFont.Chat(SwiftFont.TextStyle.Callout).Apply(message);
            message.Foreground = (notice.NoticeLevel == Notice.Level.Info ? SwiftColor.Secondary : SwiftColor.Primary).Brush(message);
            row.Children.Add(message);
            var pill = TimelineRows.Pill(row, Background(notice.NoticeLevel), notice.NoticeLevel == Notice.Level.Info ? default(Thickness) : null);
            pill.HorizontalAlignment = HorizontalAlignment.Stretch;
            Child = pill;
        }
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, Build);
    }

    // :2406-2412 private static func symbol(for:) -> String
    private static string Symbol(Notice.Level @for) => @for switch
    { Notice.Level.Info => "info.circle", Notice.Level.Warning => "exclamationmark.triangle", Notice.Level.Error => "exclamationmark.octagon", _ => throw new UnreachableException() };

    // :2416-2422 private static func color(for:) -> Color
    private static SwiftColor Color(Notice.Level @for) => @for switch
    { Notice.Level.Info => SwiftColor.Secondary, Notice.Level.Warning => Chrome.Warning, Notice.Level.Error => Chrome.Danger, _ => throw new UnreachableException() };

    // :2424-2430 private static func background(for:) -> Color
    private static SwiftColor Background(Notice.Level @for) => @for switch
    { Notice.Level.Info => SwiftColor.Clear, Notice.Level.Warning => Chrome.Warning.Opacity(0.1), Notice.Level.Error => Chrome.Danger.Opacity(0.1), _ => throw new UnreachableException() };
}

/// <summary>:2433-2456 <c>struct TurnEndRow: View</c>.</summary>
public sealed class TurnEndRow : Decorator
{
    /// <param name="summary">:2434 <c>let summary: TurnSummary</c>.</param>
    /// <param name="hasReply">:2437 <c>let hasReply: Bool</c>.</param>
    // :2439-2446 body
    public TurnEndRow(TurnSummary summary, bool hasReply)
    {
        if (!hasReply) Child = TimelineRows.ChatText(Label(summary), SwiftFont.TextStyle.Caption, color: Chrome.SecondaryText);
    }

    // :2448-2455 static func label(for:) -> String: read by the reply's hover line (:1369) and the finished turn's header
    // (:2610), both part 1.
    public static string Label(TurnSummary @for)
    {
        var duration = TimelineRows.Duration(@for.Duration); // :2449; Components.swift:37-40
        return @for.Status switch // :2450-2454
        {
            TurnStatus.Completed or TurnStatus.Running => "Worked for " + duration,
            TurnStatus.Interrupted => "Stopped after " + duration,
            TurnStatus.Failed => "Failed after " + duration,
        };
    }
}

/// <summary>:2463-2478 <c>private struct MutedRoundGlyph: View</c>.</summary>
public sealed class MutedRoundGlyph : Decorator
{
    /// <param name="symbol">:2464 <c>let symbol: String</c>.</param>
    // :2467-2477 body
    public MutedRoundGlyph(string symbol)
    {
        var glyph = SwiftImage.SystemName(symbol).Font(Chrome.IconFont);
        var disc = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = glyph };
        var foreground = new AnimatedColor(glyph);
        var fill = new AnimatedColor(disc);
        foreground.Set(Chrome.SecondaryText, null); fill.Set(Chrome.Overlay(0.1), null);
        glyph.Foreground = foreground.Brush; disc.Background = fill.Brush;
        disc.OnHover(over => Transaction.WithAnimation(Chrome.Hover, () =>
        {
            foreground.Set(over ? Chrome.PrimaryText : Chrome.SecondaryText, Transaction.Current);
            fill.Set(Chrome.Overlay(over ? 0.18 : 0.1), Transaction.Current);
        }));
        Child = disc;
    }
}

/// <summary>:2480-2716 <c>struct TurnFinishedBlock: View</c>.</summary>
public sealed class TurnFinishedBlock : Decorator
{
    // The block's inputs (:2481-2491): a pass of the timeline's body hands them again under the same id (Update), and the body is read again with the
    // @State below kept. ED-255 (3).
    private readonly ThreadRuntime runtime;            // :2481
    private readonly Guid turnID;                      // :2482
    private TurnSummary summary;                       // :2483
    private ValueList<TimelineEntry> userEntries;      // :2484
    private ValueList<TimelineEntry> content;          // :2485-2487
    private string? workingDirectory;                  // :2488
    private bool canUndo;                              // :2489-2491
    private int contentVersion;                        // bumped when the entries or the directory the steps and the answer are made from changed
    private int directoryVersion;                      // bumped when the directory the heads' rows are made with changed
    private List<(TimelineEntry Entry, UserMessageRow Row)> userRows = [];
    private TextBlock? headerLabel;
    // PORT-ONLY (OD-0076, PR-CHAT-104): the finished block's height at the turn-end swap, and the rows that fade in with it.
    private readonly HeightEase ease;
    private Button? headerButton;
    private HeightFoldHost? bodyHost;
    private Decorator? headsHostRow, cardHostRow;

    // :2497 @State private var isExpanded: Bool and :2523 @State private var legacyChanges: FileChangeSummary?, both read
    // by the body; Revision stands for the inputs the body is handed again.
    private sealed class ViewState : MainActorModel
    {
        private bool isExpanded;
        private FileChangeSummary? legacyChanges;
        private int revision;

        public ViewState(bool isExpanded) => this.isExpanded = isExpanded;

        public bool IsExpanded { get => Read(isExpanded); set => Set(ref isExpanded, value); }

        public FileChangeSummary? LegacyChanges { get => Read(legacyChanges); set => Set(ref legacyChanges, value); }

        public int Revision { get => Read(revision); set => Set(ref revision, value); }
    }

    private readonly ViewState state;

    // :2511-2514 @State private var derived: (key: DerivedKey, value: Derived)?: what the body last derived from the
    // turn, keyed on its entries' identities and the fold; an evaluation with the same key reads it back. Swift writes it
    // after the body (:2704-2706); the port writes it once the body has used it, which no reader can tell apart.
    private (DerivedKey Key, Derived Value)? derived;

    /// <summary>:2499-2509 <c>init(runtime:turnID:summary:userEntries:content:workingDirectory:canUndo:chronological:)</c>;
    /// :2589-2715 body. PR-CHAT-104: a finished turn collapsed to its header, its final answer and its file summary; the
    /// chevron reopens the turn's full steps.</summary>
    public TurnFinishedBlock(
        ThreadRuntime runtime,
        Guid turnID,
        TurnSummary summary,
        ValueList<TimelineEntry> userEntries,
        ValueList<TimelineEntry> content,
        string? workingDirectory,
        bool canUndo,
        bool chronological)
    {
        this.runtime = runtime; // :2500
        this.turnID = turnID; // :2501
        this.summary = summary; // :2502
        this.userEntries = userEntries; // :2503
        this.content = content; // :2504
        this.workingDirectory = workingDirectory; // :2505
        this.canUndo = canUndo; // :2506
        // :2507-2508 _isExpanded = State(initialValue: chronological); the timeline always passes false
        // (ThreadTimeline.swift:330), so a finished turn opens folded.
        state = new ViewState(chronological);

        ease = new HeightEase(InvalidateMeasure, clip => ClipToBounds = clip);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :2598
        SyncUserRows(stack); // :2599-2602
        headerButton = Header();
        stack.Children.Add(headerButton); // :2603-2622

        // :2624-2701 if showsBody { the rule; the steps or the answer; the heads; the file card }
        var ruleHost = new Decorator();
        var middleHost = new Panel();
        var headsHost = new Decorator();
        var cardHost = new Decorator();
        // The rule and the steps or the answer are one body whose height glides when the fold changes what it holds: the header's action runs under
        // `.snappy(duration: 0.24)` (:2609-2616) and the branches below are animated inside that transaction. OD-0076, PR-CHAT-104.
        var bodyColumn = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        bodyColumn.Children.Add(ruleHost);
        bodyColumn.Children.Add(middleHost);
        var body = bodyHost = new HeightFoldHost(bodyColumn);
        headsHostRow = headsHost;
        cardHostRow = cardHost;
        stack.Children.Add(body);
        stack.Children.Add(headsHost);
        stack.Children.Add(cardHost);

        Control? middle = null;
        (bool ShowsBody, bool Expanded, int Version)? middleKey = null;
        FileChangeSummary? shownChanges = null;
        var shownDirectoryVersion = 0;
        Derived? rendered = null;
        stack.Track(() => (state.IsExpanded, state.LegacyChanges, state.Revision), value =>
        {
            var (expanded, legacy, _) = value;
            // The inputs as this pass of the body was handed them: the fields, not the constructor's parameters (which the closure would capture) of the first.
            var summary = this.summary; var canUndo = this.canUndo; var workingDirectory = this.workingDirectory;
            SyncUserRows(stack); headerLabel!.Text = TurnEndRow.Label(summary); // :2599-2602, :2610
            var turn = DerivedFor(expanded); // :2590-2591
            rendered = turn;
            var showsHeads = turn.HeadEntries.Count != 0; // :2592
            var changes = summary.Changes ?? legacy; // :2593-2595 the files as the turn's own diff saw them, or as worked out once
            var hasFiles = changes is { Files.Count: > 0 }; // :2596
            var showsBody = showsHeads || hasFiles || (expanded ? turn.DetailGroups.Count != 0 : turn.HasResponse); // :2597
            body.Note(Transaction.Current); // what the body holds changes under the fold's animation: its height glides (OD-0076)

            // :2625-2629 Divider().opacity(0.6).padding(.vertical, rowSpacing / 2): half a row gap on each side, so the
            // header and the body sit one row gap apart with the rule in between.
            ruleHost.Child = showsBody ? ruleHost.Child ?? Rule() : null;

            // :2631-2680 the full steps in a filled card, or the folded turn's answer, plans and notices.
            if (middleKey != (showsBody, expanded, contentVersion))
            {
                middleKey = (showsBody, expanded, contentVersion);
                if (middle is { } previous) SwiftTransition.Remove(middleHost, previous);
                middle = !showsBody ? null : expanded ? Steps(turn, showsHeads) : turn.HasResponse ? Answer(turn) : null;
                if (middle is { } next)
                {
                    SwiftTransition.Insert(middleHost, middleHost.Children.Count, next);
                    if (Transaction.Current is { } fold) FadeRowsIn(RowsOf(next), fold); // the incoming rows fade in, row by row: no opacity on the body
                }
            }

            // :2682-2686 the heads the turn sent out stay in view under the response, folded or not: one list for as long as there are heads, whose rows follow the heads.
            if (showsBody && showsHeads)
            {
                if (headsHost.Child is HydraHeadsList && shownDirectoryVersion != directoryVersion) headsHost.Child = null;
                if (headsHost.Child is HydraHeadsList shownHeads) shownHeads.Update(turn.HeadEntries);
                else headsHost.Child = new HydraHeadsList(turn.HeadEntries, runtime, workingDirectory);
                shownDirectoryVersion = directoryVersion;
                headsHost.Child.Margin = new Thickness(0, 0, 0, hasFiles ? TimelineMetrics.RowSpacing : 0); // :2685
            }
            else
            {
                headsHost.Child = null;
            }

            // :2688-2700 the file card, when the turn's summary has files: the same card while the files stand, handed whether the turn can be reverted.
            if (showsBody && hasFiles && changes is { } files)
            {
                if (!Equals(shownChanges, files)) cardHost.Child = FileCard(files);
                else if (cardHost.Child is TurnFileCard shownCard) shownCard.SetCanUndo(canUndo);
                shownChanges = files;
            }
            else
            {
                cardHost.Child = null;
                shownChanges = null;
            }
        });

        // :2703-2706 .onChange(of: key, initial: true): store the value the body just computed,
        // after the render callback; DerivedFor itself only reads the previously committed cache.
        stack.Track(() => { _ = state.Revision; return new DerivedKey(this.content.ToArray(), state.IsExpanded); }, key =>
        {
            if (rendered is not null && (derived is null || !derived.Value.Key.Equals(key)))
                derived = (key, rendered);
        });

        // :2707-2714 .task(id: turnID): a turn recorded before per-file summaries existed has its changes worked out once
        // and stored back.
        this.Task(() => turnID, async _ =>
        {
            if (summary.Changes is not null || !(summary.FilesChanged > 0 || rendered?.HasEdits == true)) return; // :2708
            var files = await runtime.ParsedDiff(selection: turnID); // :2709
            if (SwiftTask.IsCancelled) return; // :2710
            var changes = FileChangeSummaryDiff.Create(files); // :2711 FileChangeSummary(files:)
            state.LegacyChanges = changes; // :2712
            runtime.StoreLegacyChanges(changes, turnID); // :2713
        });
        Child = stack;
    }

    // PORT-ONLY (OD-0076): the rows the incoming branch of the fold brings: the steps card's groups, or the folded answer's column.
    private static IEnumerable<Control> RowsOf(Control middle) => middle switch
    {
        Border { Child: StackPanel steps } => steps.Children,
        StackPanel answer => answer.Children,
        _ => [],
    };

    private static void FadeRowsIn(IEnumerable<Control> rows, Animation animation)
    {
        foreach (var row in rows.ToArray())
        {
            var fade = new AnimatedDouble(value => row.Opacity = Math.Clamp(value, 0, 1));
            fade.Set(0, null);
            fade.Set(1, animation);
        }
    }

    /// <summary>PORT-ONLY (OD-0076, PR-CHAT-104). The turn-end swap: the block that showed the running turn is replaced by this one. The Mac cuts it in one frame
    /// (2-chat-use.mov 125.743 s to 125.762 s; ThreadTimeline.swift:1907-1926 <c>.transition(.identity)</c>); the port takes the running block's place at its height and eases to its own
    /// in about 0.3 s, its header, body, heads and file card fading in over about 0.2 s while the prompt, which was on screen already, stays as it is. The running block's rows are gone
    /// at once, so the two are never drawn together. Reduce Motion shows the end state (Q-RM A, OD-0099).</summary>
    internal void BeginTurnEnd(double fromHeight, bool reduceMotion)
    {
        if (reduceMotion || !(fromHeight > 0)) return;
        ease.Begin(fromHeight, Animation.Smooth(duration: 0.3));
        var fade = Animation.EaseOut(duration: 0.2);
        foreach (var row in new Control?[] { headerButton, bodyHost, headsHostRow, cardHostRow }.OfType<Control>())
        {
            var opacity = new AnimatedDouble(value => row.Opacity = Math.Clamp(value, 0, 1));
            opacity.Set(0, null);
            opacity.Set(1, fade);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return new Size(size.Width, ease.Resolve(size.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!ease.IsEasing || Child is not { } child) return base.ArrangeOverride(finalSize);
        child.Arrange(new Rect(0, 0, finalSize.Width, child.DesiredSize.Height)); // laid out at its own height, clipped to the height it has got to
        return finalSize;
    }

    /// <summary>The block handed its inputs again under the same id (:1896-1912, :2589): its @State (the fold, the files worked out once), the header and the rows that stand
    /// are the same views. What changed is read by the body that runs again: the summary's line, the user rows (each handed whether it can revert), the steps or the answer
    /// when the turn's entries changed, the heads' list, and the file card's revert button. ED-255 (3).</summary>
    internal void Update(TurnSummary nextSummary, ValueList<TimelineEntry> nextUserEntries, ValueList<TimelineEntry> nextContent, string? nextWorkingDirectory, bool nextCanUndo)
    {
        var sameContent = content.Count == nextContent.Count && content.Zip(nextContent).All(pair => ReferenceEquals(pair.First, pair.Second));
        if (!sameContent || workingDirectory != nextWorkingDirectory) contentVersion++;
        if (workingDirectory != nextWorkingDirectory) directoryVersion++;
        summary = nextSummary; userEntries = nextUserEntries; content = nextContent; workingDirectory = nextWorkingDirectory; canUndo = nextCanUndo;
        state.Revision++;
    }

    // :2599-2602 ForEach(userEntries) { UserMessageRow }: a row whose entry stands is the same view, handed whether it can revert; one that joined or left comes or goes.
    private void SyncUserRows(StackPanel stack)
    {
        var next = new List<(TimelineEntry Entry, UserMessageRow Row)>(); var kept = userRows.ToList();
        foreach (var entry in userEntries)
        {
            var at = kept.FindIndex(pair => ReferenceEquals(pair.Entry, entry));
            UserMessageRow row;
            if (at >= 0) { row = kept[at].Row; kept.RemoveAt(at); row.SetCanRevert(canUndo); }
            else row = new UserMessageRow(entry, runtime, canUndo) { Margin = new Thickness(0, 0, 0, TimelineMetrics.RowSpacing) };
            next.Add((entry, row));
        }
        foreach (var gone in kept) stack.Children.Remove(gone.Row);
        for (var index = 0; index < next.Count; index++)
        {
            var at = stack.Children.IndexOf(next[index].Row);
            if (at == index) continue;
            if (at >= 0) stack.Children.Move(at, index); else stack.Children.Insert(index, next[index].Row);
        }
        userRows = next;
    }

    // :2603-2622 the header: the turn-end line and a chevron that turns down while the steps show.
    private Button Header()
    {
        var label = new TextBlock { Text = TurnEndRow.Label(summary) }; // :2610
        headerLabel = label;
        ChatFont.Chat(SwiftFont.TextStyle.Callout).Apply(label); // :2611
        label.Foreground = Chrome.SecondaryText.Brush(label); // :2612
        var chevron = SwiftImage.SystemName("chevron.right").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)); // :2613-2614
        chevron.Foreground = Chrome.SecondaryText.Brush(chevron); // :2615
        var turn = new AnimatedDouble(angle => chevron.RenderTransform = new RotateTransform(angle)); // :2616
        var row = new HStack { Spacing = 6, Background = Brushes.Transparent }; // HStack :2609 centres its children (UI-CHAT finish); :2609, :2618 .contentShape(.rect)
        row.Children.Add(label);
        row.Children.Add(chevron);
        var viewState = state;
        Button header = null!;
        header = PlainControls.Button(row, () => // :2603-2608, :2620 .buttonStyle(.plain)
        {
            var reveal = ThreadTimeline.GetRevealTimelineEnd(header);
            Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () =>
            {
                viewState.IsExpanded = !viewState.IsExpanded;
                if (viewState.IsExpanded) reveal?.Action(); // inside the animation transaction
            });
        });
        header.HorizontalAlignment = HorizontalAlignment.Left;
        header.Track(() => viewState.IsExpanded, expanded =>
        {
            turn.Set(expanded ? 90 : 0, Transaction.Current); // :2616
            var help = expanded ? "Hide this turn's steps" : "Show this turn's steps";
            ToolTip.SetTip(header, help); // :2621
            AutomationProperties.SetName(header, help); // :2622
        });
        return header;
    }

    // :2625-2629 a plain SwiftUI Divider(): the hairline PopoverDivider (Popovers.cs) and ChromeDivider already draw for
    // it, Chrome.primaryText at 0.22 (ENGINEERING DECISION there), at the row's 0.6 opacity.
    private static Border Rule()
    {
        var rule = new Border
        {
            Height = 1,
            Opacity = 0.6, // :2626
            Margin = new Thickness(0, TimelineMetrics.RowSpacing / 2), // :2629
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        rule.Background = Chrome.PrimaryText.Opacity(0.22).Brush(rule);
        return rule;
    }

    // :2631-2663 the full steps: the detail groups in a stack of rowSpacing, padded 14 and filled with .quaternary at
    // 0.32 in the pill's continuous rounded rectangle, with a row gap below when a response or heads follow.
    private Control Steps(Derived turn, bool showsHeads)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = TimelineMetrics.RowSpacing }; // :2632
        foreach (var group in turn.DetailGroups) // :2633
        {
            Control? view = group switch // :2634-2655
            {
                TimelineGroup.Single single => single.Value.TimelineEntryKind switch // :2635-2649
                {
                    TimelineEntry.Kind.Assistant => new AssistantMessageRow(single.Value, null, runtime), // :2638
                    TimelineEntry.Kind.Tool => LiveAnswerRows.IsLive(single.Value) ? new LiveAnswerRow(single.Value, runtime) : new WorkGroup(new ValueList<TimelineEntry>([single.Value]), runtime, workingDirectory), // :2640 + W24: a live answer
                    TimelineEntry.Kind.Plan => new PlanCard(single.Value, runtime, "steps"), // :2642
                    TimelineEntry.Kind.Todos => new TodoListRow(single.Value), // :2644
                    TimelineEntry.Kind.Notice => new NoticeRow(single.Value), // :2646
                    TimelineEntry.Kind.User or TimelineEntry.Kind.Reasoning or TimelineEntry.Kind.TurnEnd => null, // :2647-2648
                },
                TimelineGroup.Work work => new WorkGroup(work.Entries, runtime, workingDirectory, work.StartsCollapsed), // :2650-2651
                TimelineGroup.Heads => null, // :2652-2654 never among the steps: the heads list below shows them
                _ => throw new UnreachableException(),
            };
            if (view is not null) column.Children.Add(view);
        }
        var card = new Border
        {
            Padding = new Thickness(14), // :2658
            Child = column,
            Margin = new Thickness(0, 0, 0, turn.HasResponse || showsHeads ? TimelineMetrics.RowSpacing : 0), // :2663
        };
        card.RoundedRectangle(TimelineMetrics.PillRadius, RoundedCornerStyle.Continuous); // :2660
        card.Background = SwiftColor.Quaternary.Opacity(0.32).Brush(card); // :2661
        // PR-CHAT-104.windows_path: no whole-layer fade over a potentially thousands-point body.
        return card.Transition(SwiftTransition.Identity);
    }

    // :2664-2680 the folded turn: the answer as the same menu-bearing text as a reply row, then the plans, then the
    // warning and error notices, with a row gap below.
    private Control Answer(Derived turn)
    {
        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = TimelineMetrics.RowSpacing, // :2665
            Margin = new Thickness(0, 0, 0, TimelineMetrics.RowSpacing), // :2679
        };
        foreach (var entry in turn.AnswerEntries) // :2666-2671
        {
            if (entry.Item.TimelineItemContent is TimelineItem.Content.Assistant assistant && assistant.Value.Text.Length != 0)
                column.Children.Add(new FoldedAnswerView(assistant.Value.Text, runtime));
        }
        foreach (var entry in turn.LiveEntries) column.Children.Add(new LiveAnswerRow(entry, runtime)); // PORT-ONLY (web surfaces W24): a live answer stays in the folded turn, after the words that introduce it
        foreach (var entry in turn.CollapsedPlans) column.Children.Add(new PlanCard(entry, runtime, "folded")); // :2672-2674
        foreach (var entry in turn.CollapsedNotices) column.Children.Add(new NoticeRow(entry)); // :2675-2677
        return column.Transition(SwiftTransition.Identity); // PR-CHAT-104.windows_path
    }

    // :2688-2699 the file card: a weak runtime for review, so the card (and its review closure) never keeps the thread
    // alive after its turn scrolls away; the revert keeps it, as the Swift closure does.
    private Control FileCard(FileChangeSummary changes)
    {
        var target = runtime;
        var turn = turnID;
        var reviewRuntime = new WeakReference<ThreadRuntime>(runtime);
        return new TurnFileCard(
            changes,
            canUndo,
            onRevert: () => MainActor.Spawn(() => SwiftTry.IgnoreAsync(() => target.Revert(turn, restoreFiles: true))), // :2695
            onReview: button => // :2696-2698 on the button itself, so the changes open where the reader clicked
            {
                if (reviewRuntime.TryGetTarget(out var shown)) shown.ShowDiff(button, turn);
            });
    }

    // :2590-2591, :2704-2706 the derivation for this fold, read back while the key holds.
    private Derived DerivedFor(bool expanded)
    {
        var key = new DerivedKey(content.ToArray(), expanded);
        if (derived is { } cached && cached.Key.Equals(key)) return cached.Value;
        return new Derived(content, expanded);
    }

    /// <summary>:2516-2521 <c>private struct DerivedKey: Equatable</c>: which entries the turn holds (each a class, so
    /// identity is enough) and whether the full steps are showing. <c>ids: [ObjectIdentifier]</c> (:2519) keeps the
    /// entries and compares them by identity.</summary>
    private sealed record DerivedKey(IReadOnlyList<TimelineEntry> Ids, bool Expanded) // :2519-2520
    {
        public bool Equals(DerivedKey? other) =>
            other is not null && Expanded == other.Expanded && Ids.Count == other.Ids.Count
            && Ids.Zip(other.Ids).All(pair => ReferenceEquals(pair.First, pair.Second));

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Expanded);
            foreach (var id in Ids) hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(id));
            return hash.ToHashCode();
        }
    }

    /// <summary>:2525-2587 <c>private struct Derived</c>: everything the body derives from the turn's content, gathered
    /// in one pass per render instead of a filter per use.</summary>
    private sealed record Derived
    {
        public ValueList<TimelineEntry> AssistantEntries { get; init; } = ValueList<TimelineEntry>.Empty; // :2528-2529 every reply with something to read
        public ValueList<TimelineEntry> AnswerEntries { get; init; } = ValueList<TimelineEntry>.Empty;    // :2530-2534 the replies after the last tool call
        public ValueList<TimelineEntry> LiveEntries { get; init; } = ValueList<TimelineEntry>.Empty;      // PORT-ONLY (W24): the turn's live answers, in order
        public ValueList<TimelineEntry> CollapsedPlans { get; init; } = ValueList<TimelineEntry>.Empty;   // :2535
        public ValueList<TimelineEntry> CollapsedNotices { get; init; } = ValueList<TimelineEntry>.Empty; // :2536-2538 errors and warnings
        public bool HasEdits { get; init; }                                                              // :2539
        public bool HasResponse { get; init; }                                                           // :2540
        public ValueList<TimelineEntry> HeadEntries { get; init; } = ValueList<TimelineEntry>.Empty;      // :2541-2542
        public ValueList<TimelineGroup> DetailGroups { get; init; } = ValueList<TimelineGroup>.Empty;     // :2543-2545

        // :2547-2586 @MainActor init(content:expanded:)
        public Derived(IReadOnlyList<TimelineEntry> content, bool expanded)
        {
            var assistantEntries = new List<TimelineEntry>();
            var answerEntries = new List<TimelineEntry>();
            var collapsedPlans = new List<TimelineEntry>();
            var collapsedNotices = new List<TimelineEntry>();
            var liveEntries = new List<TimelineEntry>();
            var hasEdits = false;
            var hasResponse = false;
            var headEntries = new List<TimelineEntry>();
            foreach (var entry in content) // :2549
            {
                switch (entry.TimelineEntryKind) // :2550
                {
                    case TimelineEntry.Kind.Assistant: // :2551-2559
                        // A message still streaming when its turn failed can be blank; asked character by character,
                        // which stops at the first word.
                        if (entry.Item.TimelineItemContent is not TimelineItem.Content.Assistant assistant
                            || !SwiftString.Characters(assistant.Value.Text).Any(character => !SwiftCharacter.IsWhitespace(character))) continue;
                        assistantEntries.Add(entry);
                        answerEntries.Add(entry);
                        hasResponse = true;
                        break;
                    case TimelineEntry.Kind.Plan: // :2560-2561
                        collapsedPlans.Add(entry);
                        break;
                    case TimelineEntry.Kind.Notice: // :2562-2565
                        if (entry.Item.TimelineItemContent is TimelineItem.Content.Notice notice && notice.Value.NoticeLevel != Notice.Level.Info)
                            collapsedNotices.Add(entry);
                        break;
                    case TimelineEntry.Kind.Tool when LiveAnswerRows.IsLive(entry): // PORT-ONLY (W24): a live answer is part of the reply, so it does not end the answer so far
                        liveEntries.Add(entry);
                        hasResponse = true;
                        break;
                    case TimelineEntry.Kind.Tool: // :2566-2573 reaching for a tool ends the answer so far
                        answerEntries.Clear();
                        if (entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue;
                        if (tool.Value.ToolCallKind == ToolCall.Kind.Agent) headEntries.Add(entry);
                        if (tool.Value.Edits.Count != 0) hasEdits = true;
                        break;
                    default: // :2574-2575
                        break;
                }
            }
            if (answerEntries.Count == 0 && assistantEntries.Count != 0) answerEntries = [assistantEntries[^1]]; // :2578
            if (collapsedPlans.Count != 0 || collapsedNotices.Count != 0) hasResponse = true; // :2579
            AssistantEntries = new ValueList<TimelineEntry>(assistantEntries);
            AnswerEntries = new ValueList<TimelineEntry>(answerEntries);
            LiveEntries = new ValueList<TimelineEntry>(liveEntries);
            CollapsedPlans = new ValueList<TimelineEntry>(collapsedPlans);
            CollapsedNotices = new ValueList<TimelineEntry>(collapsedNotices);
            HasEdits = hasEdits;
            HasResponse = hasResponse;
            HeadEntries = new ValueList<TimelineEntry>(headEntries);
            if (expanded) // :2580-2585 the full steps, built only while showing; the heads have their own list below
            {
                DetailGroups = new ValueList<TimelineGroup>(TimelineGroup.Build(content, showReasoning: false).Where(group => group is not TimelineGroup.Heads));
            }
        }
    }
}

/// <summary>:2718-2859 <c>private struct TurnFileCard: View</c>.</summary>
public sealed class TurnFileCard : Decorator
{
    private sealed class ViewState : MainActorModel // :2731-2733
    {
        private bool showingFiles, confirmingRevert, needsReviewAnchor;
        public bool IsShowingFiles { get => Read(showingFiles); set => Set(ref showingFiles, value); }
        public bool IsConfirmingRevert { get => Read(confirmingRevert); set => Set(ref confirmingRevert, value); }
        public bool NeedsReviewAnchor { get => Read(needsReviewAnchor); set => Set(ref needsReviewAnchor, value); }
    }
    private readonly ViewState state = new();
    private readonly WeakView reviewAnchor = new(); // :2730
    private readonly FileChangeSummary changes;
    private readonly Action onRevert;
    /// <param name="changes">:2721 <c>let changes: FileChangeSummary</c>.</param>
    /// <param name="canUndo">:2722 <c>let canUndo: Bool</c>.</param>
    /// <param name="onRevert">:2724 <c>let onRevert: () -> Void</c>.</param>
    /// <param name="onReview">:2726 <c>let onReview: (NSView) -> Void</c>.</param>
    // :2735-2827 body
    public TurnFileCard(FileChangeSummary changes, bool canUndo, Action onRevert, Action<Control> onReview)
    {
        this.changes = changes;
        this.onRevert = onRevert;
        var box = state;
        var title = changes.Files.Count == 1 ? "Edited 1 file" : $"Edited {changes.Files.Count} files";
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Background = Brushes.Transparent }; // :2745 .contentShape(.rect)
        var badge = SwiftImage.Named("badge-edit").ForegroundStyle(Chrome.SecondaryText);
        badge.Width = 18; badge.Height = 18;
        titleRow.Children.Add(badge);
        titleRow.Children.Add(TimelineRows.ChatText(title, weight: SwiftFont.Weight.Medium, color: Chrome.PrimaryText.Opacity(0.9)));
        titleRow.Children.Add(new DiffStatLabel { Additions = checked((int)changes.Additions), Deletions = checked((int)changes.Deletions) });
        if (changes.Files.Count != 0) titleRow.Children.Add(TimelineRows.Chevron());
        var files = PlainControls.Button(titleRow, () => box.IsShowingFiles = !box.IsShowingFiles);
        files.IsEnabled = changes.Files.Count != 0;
        ToolTip.SetTip(files, "Show the files this turn changed");
        // :2742-2766 .popover on the button is no item of the HStack: the popup shares its button's slot here, because a Popup
        // that is a child of the stack of its own would add a spacing gap (8 points) after its button (UI-CHAT-037).
        row.Children.Add(Hosted(files, PlainControls.Popover(files, new Binding<bool>(() => box.IsShowingFiles, value => box.IsShowingFiles = value), () => FilesPopover().PresentedChrome())));
        row.Children.Add(new Control { Width = 20, Height = 1 }); // :2768
        cardRow = row;
        if (canUndo) row.Children.Add(revertSlot = RevertSlot());
        var anchorBox = new WeakReference<WeakView>(reviewAnchor);
        var review = PlainControls.Button(new MutedRoundGlyph("plusminus"), () =>
        {
            if (anchorBox.TryGetTarget(out var weak) && weak.Value is { } view) onReview(view);
        });
        review.Focusable = false;
        ToolTip.SetTip(review, "Show this turn's changes");
        review.OnHover(over => { if (over && !box.NeedsReviewAnchor) box.NeedsReviewAnchor = true; });
        var anchor = new Decorator { IsHitTestVisible = false };
        anchor.Track(() => box.NeedsReviewAnchor, needed =>
        {
            if (needed && anchor.Child is null) anchor.Child = new AttachmentAnchorCapture(view =>
            {
                if (anchorBox.TryGetTarget(out var weak)) weak.Value = view;
            });
        });
        row.Children.Add(PlainControls.Background(review, anchor));
        // :2741, :2743 HStack's default alignment is .center: every item of both lines sits on one centre line (a
        // stretched text would draw at the top of the taller row).
        foreach (var item in titleRow.Children.Concat(row.Children)) item.VerticalAlignment = VerticalAlignment.Center;
        Child = TimelineRows.Pill(row, SwiftColor.Quaternary.Opacity(0.32), new Thickness(TimelineMetrics.PillLeading, 6, 6, 6));
        AutomationProperties.SetName(this, title);
    }

    // A button and the popover it hangs from, as one slot of the card's row.
    private static Panel Hosted(Button button, Avalonia.Controls.Primitives.Popup popup) => new() { Children = { button, popup } };

    private StackPanel? cardRow;
    private Control? revertSlot;

    // :2770-2779 the revert button and the confirmation it opens.
    private Control RevertSlot()
    {
        var box = state;
        var revert = PlainControls.Button(new MutedRoundGlyph("arrow.uturn.backward"), () => box.IsConfirmingRevert = true);
        revert.Focusable = false;
        ToolTip.SetTip(revert, "Revert this turn's files and conversation");
        var slot = Hosted(revert, PlainControls.Popover(revert,
            new Binding<bool>(() => box.IsConfirmingRevert, value => box.IsConfirmingRevert = value), RevertConfirmation)); // :2777
        slot.VerticalAlignment = VerticalAlignment.Center;
        return slot;
    }

    /// <summary>The card handed whether the turn can be reverted again (:2722, decided by the timeline, which flips it at every turn start and end on a provider that rewinds): the same
    /// card, its files popover and its review anchor kept, the revert button (:2770 `if canUndo`) coming or going in its place. ED-255 (3).</summary>
    internal void SetCanUndo(bool next)
    {
        if (cardRow is not { } row || (revertSlot is not null) == next) return;
        if (next) { revertSlot = RevertSlot(); row.Children.Insert(2, revertSlot); }
        else { row.Children.Remove(revertSlot!); revertSlot = null; state.IsConfirmingRevert = false; }
    }

    // :2777-2798
    private Control RevertConfirmation()
    {
        var column = new StackPanel { Spacing = 10 };
        var heading = new TextBlock { Text = "Revert up to this point?" };
        SwiftFont.System(14, SwiftFont.Weight.Semibold).Apply(heading);
        column.Children.Add(heading);
        var message = new TextBlock { Text = "Files go back to how they were before this turn, and the turn leaves the conversation.", TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(message);
        message.Foreground = Chrome.SecondaryText.Brush(message);
        column.Children.Add(message);
        var buttons = new HStack { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right }; // HStack :2785: its children share one centre line
        var box = state;
        var action = onRevert;
        buttons.Children.Add(TimelineRows.GlassCommand("Cancel", () => box.IsConfirmingRevert = false, false, true));
        var go = TimelineRows.GlassCommand("Revert", () => { box.IsConfirmingRevert = false; action(); }, true, true);
        go.IsDefault = true;
        buttons.Children.Add(go);
        column.Children.Add(buttons);
        return new Border { Width = 300, Padding = new Thickness(16), Child = column }.PresentedChrome();
    }

    // :2829-2858
    private Control FilesPopover()
    {
        var column = new StackPanel { Spacing = 0 };
        foreach (var file in changes.Files.Take(12))
        {
            var row = new DockPanel { Margin = new Thickness(12, 7) };
            Control stats = file.IsBinary ? TimelineRows.ChatText("Binary", SwiftFont.TextStyle.Caption, color: Chrome.SecondaryText)
                : new DiffStatLabel { Additions = checked((int)file.Additions), Deletions = checked((int)file.Deletions) };
            stats.Margin = new Thickness(24, 0, 0, 0); // :2833 spacing 8 + :2838 Spacer(minLength: 8) + spacing 8: the HStack puts its spacing on both sides of a Spacer
            // :2833 HStack(spacing: 8): the path and the stat (or Binary) share one centre line, whichever is taller (UI-CHAT-041).
            stats.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(stats, Dock.Right);
            row.Children.Add(stats);
            var path = new MiddleTruncatingTextBlock { FullText = file.Path, Text = file.Path, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center };
            ChatFont.Chat(SwiftFont.TextStyle.Callout).Apply(path);
            row.Children.Add(path);
            column.Children.Add(row);
        }
        if (changes.Files.Count > 12)
        {
            var more = TimelineRows.ChatText(changes.Files.Count == 13 ? "and 1 more file" : $"and {changes.Files.Count - 12} more files", color: Chrome.SecondaryText);
            more.Margin = new Thickness(12, 7); column.Children.Add(more);
        }
        return column;
    }
}
