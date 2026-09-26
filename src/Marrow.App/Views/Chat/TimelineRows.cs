using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
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

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :1-2859. The file is unchanged from B2 1fbf81e,
// so every line cited here is the same in both.
// Part 1 of 6 is ported: TimelineMetrics, copyMessageText, lastCodeBlock, messageMenu, UserMessageRow, finishedHeads,
// UserBubble, AssistantMessageRow, FoldedAnswerView, WorkGroup, WorkSteps, WorkGroupSummary, ToolRow (with its
// departure helpers, :1723-1755), HeadStepsPopoverModifier, ToolStatusIcon, ToolDetailView, DiffDetailCache,
// ToolPresentation, TurnEndRow.label (:2448-2455, read by the reply row and the turn header) and TurnFinishedBlock
// (with Derived, :2527-2587, which its body builds). Three sites wait on something outside this file (STOP notes at
// the end of tests/Marrow.App.Tests/Views/Chat/TimelineRowsTests.cs): the quote chips' ChipChrome (ComposerView.cs,
// MARROW_STUB), the Edit from here confirmationDialog (no port seam) and the expanded turn's TimelineGroup.build
// (ThreadTimeline.cs, MARROW_STUB). Animated numbers (a chevron's turn, the reply's hover line) move on the frame clock
// through App.Views.Tour's AnimatedDouble and AnimatedOpacity, the one port of that mechanic in this assembly.
// Everything else below that still throws MARROW_STUB belongs to parts 2 to 6.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; a Shape is a record whose Path(in:) returns an Avalonia Geometry; NSView is Control;
// a parameter without a default that follows one with a default moves before it and callers name their arguments.
// The file's free functions (:31-34, :38-54) and its `extension View` (:1415-1429) are the static class TimelineRows,
// named after the file (R-NAME-4); the two free functions are fileprivate in Swift and read by other types of this
// file, so they are internal. An enum with associated values is an abstract record (R-ENUM-2); `[T]` in a value is
// ValueList<T>; a Swift tuple result keeps its labels as C# tuple element names, first letter upper-cased. A Swift
// argument label that is a C# keyword is written with `@` (MCPProxy.Url(string @for) precedent).
// NEEDS DECISION (group report): `@Environment(AppModel.self)` is read by the bodies, not passed in, so it is no
// constructor parameter. `[ObjectIdentifier]` (:2519) has no port type: DerivedKey keeps the entries themselves, to
// be compared by identity. HydraMergeTrackBody is a View that is Animatable (:844-916); ED-095's IAnimatableModifier
// covers modifiers, so its animatableData is left to the implementing unit.
// Not declared yet, private in Swift and read by no other file: every view's @State/@Environment storage and private
// instance methods (UserMessageRow :202-212, HydraDelegationBlock.headRow :711-741, HydraMergeRow :954-1161,
// AttachmentThumbnail.togglePanel :1276-1282, ToolRow.line/helpText :1760-1853, HydraHeadStepsPopover :1934-2006,
// TurnFileCard.filesPopover :2829-2858), which the implementing unit adds with the bodies. The Swift-private view
// types are public, as the App views ported so far do (ShortcutsSettingsPage.cs).

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
    private readonly bool canRevert;        // :69

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
        this.canRevert = canRevert;

        TimelineItem.Content? built = null;
        void Build(TimelineItem.Content content)
        {
            if (ReferenceEquals(content, built)) return;
            built = content;
            Child = Body(content);
        }
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, Build);

        // :198 .onDisappear { editor.close() }: the edit itself survives on the runtime.
        DetachedFromVisualTree += (_, _) => editor.Close();
    }

    // :79-199
    private Control? Body(TimelineItem.Content content)
    {
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
        // only; the revert intent goes through the binding alone.
        var requests = menuRequests;
        var text = message.Text;
        var revertable = canRevert;
        var confirming = new Binding<bool>(() => requests.ConfirmRevert, value => requests.ConfirmRevert = value);
        row.RightClickPopover(() => MessageActions(text, revertable, confirming));
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
        // :191-196 .confirmationDialog("Edit from this message?", isPresented: $isConfirmingRevert) with
        // Button("Revert and keep file changes") { revert(restoreFiles: false) }, Button("Revert files too",
        // role: .destructive) { revert(restoreFiles: true) } and the message "This message and everything after it leave
        // the thread, and your prompt returns to the composer." The port has no confirmationDialog seam (STOP #2 in
        // TimelineRowsTests.cs), so the presentation is not written.
        row.Track(() => state.IsConfirmingRevert, presented =>
        {
            if (presented) throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:191 confirmationDialog (no port seam; STOP #2)");
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
        // :98 HStack(spacing: 5); :109 .fixedSize(): a horizontal StackPanel measures its children at their ideal size.
        var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right };
        chip.Children.Add(glyph);
        chip.Children.Add(excerpt);
        // :110 .modifier(ChipChrome()): App.Views.Composer.ComposerView's, still MARROW_STUB (STOP #1).
        var chrome = new ChipChrome().Body(chip);
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
        var shape = new Avalonia.Controls.Shapes.Path { Stretch = Stretch.None };
        shape.Fill = Chrome.Accent.Opacity(0.14).Brush(shape);
        inset.SizeChanged += (_, e) => shape.Data = new UserBubble().Path(new Rect(e.NewSize));
        var bubble = PlainControls.Background(inset, shape);
        bubble.HorizontalAlignment = HorizontalAlignment.Right;
        return bubble;
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
        // :161 .opacity(isHovering || isEditing ? 1 : 0)
        line.Track(() => state.IsHovering || isEditing(), shown => line.Opacity = shown ? 1 : 0);
        return line;
    }

    // :202-206 private func revert(restoreFiles:): the confirmation dialog's two buttons (:192-193), whose presentation
    // is not written yet (STOP #2).
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
    /// <param name="message">:237 <c>let message: UserMessage</c>.</param>
    // :241-329 body
    public HydraReportRow(UserMessage message) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:241");

    // :332-334 nonisolated static func withoutTrailingStop(_:) -> String
    public static string WithoutTrailingStop(string title) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:332");

    // :340-352 private static func reportSummary(names:text:) -> String
    private static string ReportSummary(IReadOnlyList<string> names, string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:340");

    // :355-358 private static func list(_:) -> String
    private static string List(IReadOnlyList<string> names) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:355");

    // :362-373 private static func reportOutcomes(in:) -> [String: String]
    private static ValueMap<string, string> ReportOutcomes(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:362");

    // :376-381 nonisolated static func leadingURL(in:) -> URL?
    public static SwiftURL? LeadingURL(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:376");

    // :384-387 nonisolated static func withoutFirstLine(_:) -> String
    public static string WithoutFirstLine(string body) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:384");
}

/// <summary>:393-434 <c>struct HydraHeadsWorkingRow: View</c>.</summary>
public sealed class HydraHeadsWorkingRow : Decorator
{
    /// <param name="heads">:395 <c>let heads: [Int]</c>.</param>
    /// <param name="runtime">:396 <c>let runtime: ThreadRuntime</c>.</param>
    // :398-418 body
    public HydraHeadsWorkingRow(ValueList<int> heads, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:398");

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
    public FinishedHeadPill(HydraHeadInfo head) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:443");
}

/// <summary>:480-521 <c>private struct HydraShimmer: ViewModifier</c>.</summary>
public sealed record HydraShimmer
{
    // :484 private static let sweep: TimeInterval = 1.8
    private const double Sweep = 1.8;

    // :486-520 func body(content:)
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:486");
}

/// <summary>:527-596 <c>struct HydraBriefRow: View</c>.</summary>
public sealed class HydraBriefRow : Decorator
{
    /// <param name="message">:528 <c>let message: UserMessage</c>.</param>
    /// <param name="entry">:529 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:530 <c>let runtime: ThreadRuntime</c>.</param>
    // :537-595 body
    public HydraBriefRow(UserMessage message, TimelineEntry entry, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:537");
}

/// <summary>:607-640 <c>private struct HydraReportPopover: View</c>.</summary>
public sealed class HydraReportPopover : Decorator
{
    /// <param name="text">:608 <c>let text: String</c>.</param>
    /// <param name="width">:609 <c>var width: CGFloat = 460</c>.</param>
    // :612-617 @MainActor init(text:width:); :619-639 body
    public HydraReportPopover(string text, double width = 460) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:613");
}

/// <summary>:648-783 <c>struct HydraDelegationBlock: View</c>.</summary>
public sealed class HydraDelegationBlock : Decorator
{
    /// <param name="json">:649 <c>let json: String</c>.</param>
    /// <param name="sent">:650 <c>var sent = false</c>.</param>
    // :652-707 body
    public HydraDelegationBlock(string json, bool sent = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:652");

    // :745-753 private static func title(for:streaming:sent:) -> String
    private static string Title(IReadOnlyList<HydraDelegation> @for, bool streaming, bool sent) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:745");

    // :756-759 private static func persona(named:) -> HydraPersona?
    private static HydraPersona? Persona(string? named) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:756");

    // :762-766 private static func projectName(_:) -> String?
    private static string? ProjectName(string? project) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:762");

    // :776-782 @MainActor private static func reading(of:) -> HydraPrompts.StreamedDelegations
    private static HydraPrompts.StreamedDelegations Reading(string of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:777");
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
    public HydraMergeOutcome(UserMessage message) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:802");
}

/// <summary>:821-841 <c>struct HydraMergeTrack: View</c>.</summary>
public sealed class HydraMergeTrack : Decorator
{
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
    public HydraMergeTrack(int step, int count, Mode mode) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:830");
}

/// <summary>:844-916 <c>private struct HydraMergeTrackBody: View, Animatable</c>.</summary>
public sealed class HydraMergeTrackBody : Decorator
{
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
    public HydraMergeTrackBody(double position, double done, double rest, int count, bool hops) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:884");

    // :872-874 private static func dotCentre(_:) -> CGFloat
    private static double DotCentre(double i) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:872");
}

/// <summary>:926-1162 <c>struct HydraMergeRow: View</c>.</summary>
public sealed class HydraMergeRow : Decorator
{
    // :935 private static let change = Animation.smooth(duration: 0.3)
    private static Animation Change => Animation.Smooth(duration: 0.3);

    /// <param name="runtime">:927 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="phase">:928 <c>let phase: HydraMergePhase</c>.</param>
    // :937-952 body
    public HydraMergeRow(ThreadRuntime runtime, HydraMergePhase phase) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:937");
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
            // :1348 withAnimation(.easeOut(duration: 0.15)) { image = thumbnail.image }
            Transaction.WithAnimation(Marrow.UI.Port.Animation.EaseOut(0.15), () => ShowImage(thumbnail.Image, fade: true));
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
        if (fade) SwiftTransition.Insert(plate, plate.Children.Count, view.Transition(SwiftTransition.Opacity));
        else plate.Children.Add(view);
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

    /// <param name="entry">:1354 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="summary">:1355-1357 <c>let summary: TurnSummary?</c>: the turn's summary, set only on the turn's last
    /// reply, precomputed by the timeline so rows never scan the thread.</param>
    /// <param name="runtime">:1358 <c>let runtime: ThreadRuntime</c>.</param>
    // :1361-1390 body. PR-CHAT-088. It reads entry.item, which a streaming reply keeps changing: the body is built again
    // whenever the text or the streaming flag moves (MarkdownView's .equatable(), :1364), the hover state surviving.
    public AssistantMessageRow(TimelineEntry entry, TurnSummary? summary, ThreadRuntime runtime)
    {
        AssistantMessage? built = null;
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
            Child = Body(message, summary, runtime, state);
        }
        Build(entry.Item.TimelineItemContent);
        this.Track(() => entry.Item.TimelineItemContent, Build);
    }

    // :1363-1388
    private static Control Body(AssistantMessage message, TurnSummary? summary, ThreadRuntime runtime, ViewState state)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Background = Brushes.Transparent }; // :1363
        column.Children.Add(new MarkdownView(message.Text, message.IsStreaming)); // :1364
        // :1365-1374 one side for both kinds of message: the summary sits where the copy control used to be.
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
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
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = TimelineMetrics.RowSpacing }; // :1467

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
        header.Track(() => state.IsCollapsed, collapsed =>
        {
            turn.Set(collapsed ? 0 : 90, Transaction.Current); // :1490
            var help = collapsed ? "Show these steps" : "Hide these steps";
            ToolTip.SetTip(header, help); // :1497
            AutomationProperties.SetName(header, help); // :1498
            if (!collapsed && steps is null) // :1499-1501 if !isCollapsed { WorkSteps(...) }
            {
                steps = new WorkSteps(state.Entries, runtime, new Binding<bool>(() => state.ShowsAll, value => state.ShowsAll = value), workingDirectory);
                SwiftTransition.Insert(stack, 1, steps);
            }
            else if (collapsed && steps is { } shown)
            {
                steps = null;
                SwiftTransition.Remove(stack, shown);
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
        more = PlainControls.Button(line, () => // :1525-1530, :1543 .buttonStyle(.plain)
            Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () =>
            {
                showsAll.Value = true;
                ThreadTimeline.GetRevealTimelineEnd(more)?.Action(); // inside the animation transaction
            }));
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
            var markdown = new MarkdownView(message.Text, message.IsStreaming); // :1549
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
    public HydraHeadsList(ValueList<TimelineEntry> entries, ThreadRuntime runtime, string? workingDirectory = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1569");
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

        var run = new StackPanel { Orientation = Orientation.Horizontal, Spacing = isBadge ? 8 : TimelineMetrics.IconSpacing }; // :1775
        if (head is { } linked) // :1776-1777
            run.Children.Add(new HydraGlyph(linked.Persona, size: 18, isRunning: running && linked.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, status: linked.HydraHeadInfoStatus));
        else if (departing is { } leaving) // :1778-1780
            run.Children.Add(new HydraGlyph(leaving.Persona, size: 18, isRunning: running, status: DepartureStatus(call))
                .Transition(SwiftTransition.Scale(0.6).Combined(SwiftTransition.Opacity)));
        else if (mcp is { } server) // :1781-1782
            run.Children.Add(new MCPGlyph(server.Entry, size: 18, isRunning: running, status: call.ToolCallStatus));
        else // :1783-1784 a read that looked at an image wears the photo glyph
            run.Children.Add(new ToolStatusIcon(call, symbol: imagePath is not null && call.ToolCallKind == ToolCall.Kind.Read ? "photo" : null));

        // :1786-1795 one text run after the icon, so the row reads as icon + space + text.
        var text = head is { } sent ? (running ? "Sending out" : "Sent out") + " " + sent.Persona.Name + ": " + call.Title // :1788
            : departing is { } away ? DepartureLabel(call, away.Name, away.Task) // :1789
            : mcp is { } tool ? ToolPresentation.McpLabel(tool.Entry, tool.Tool, running) // :1790
            : ToolPresentation.Label(call); // :1791
        var label = new MiddleTruncatingTextBlock { FullText = text, MaxLines = 1 }; // :1794-1795
        (isBadge ? ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium) : ChatFont.Chat(SwiftFont.TextStyle.Callout)).Apply(label); // :1792
        label.Foreground = (isBadge ? Chrome.PrimaryText.Opacity(0.9) : SwiftColor.Secondary).Brush(label); // :1793
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
    public HydraHeadStepsPopover(Guid headID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1884");

    // :2010-2025 private static func statusLine(for:) -> String
    private static string StatusLine(HydraHeadInfo @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2010");

    // :2030-2048 private static func steps(in:) -> [TimelineEntry]
    private static ValueList<TimelineEntry> Steps(IReadOnlyList<TimelineEntry> @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2030");

    // :2050-2056 private static func noticeSymbol(for:) -> String
    private static string NoticeSymbol(Notice.Level @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2050");

    // :2058-2064 private static func noticeColor(for:) -> Color
    private static SwiftColor NoticeColor(Notice.Level @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2058");
}

/// <summary>:2067-2092 <c>private struct ToolStatusIcon: View</c>.</summary>
public sealed class ToolStatusIcon : Decorator
{
    /// <param name="call">:2068 <c>let call: ToolCall</c>.</param>
    /// <param name="symbol">:2069-2070 <c>var symbol: String?</c>: stands in for the kind's symbol (a photo for a read
    /// that looked at one).</param>
    // :2072-2091 body. PR-CHAT-099.
    public ToolStatusIcon(ToolCall call, string? symbol = null)
    {
        Control icon = call.ToolCallStatus switch
        {
            // :2075-2077 ProgressView().controlSize(.mini): an indeterminate progress control in the icon column; the
            // mini control's own metrics are platform drawing with no source value (UNVERIFIED OD-0001), the
            // AttachmentPreview.cs precedent for ProgressView.
            ToolCall.Status.Running => new ProgressBar { IsIndeterminate = true },
            ToolCall.Status.Failed => Glyph("xmark.circle.fill", Chrome.Danger), // :2078-2080
            ToolCall.Status.Declined => Glyph("hand.raised.slash", Chrome.Warning), // :2081-2083
            ToolCall.Status.Completed => Glyph(symbol ?? ToolPresentation.Symbol(call.ToolCallKind), Chrome.SecondaryText), // :2084-2086
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
            var text = new SelectableTextBlock { Text = detail, MaxLines = 12 }; // :2121, :2124 .textSelection(.enabled), :2125 .lineLimit(12)
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
            var text = new SelectableTextBlock(); // :2150, :2152 .textSelection(.enabled)
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
                title.Foreground = SwiftColor.AccentColor.Brush(title);
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
    // :2185-2206 static func label(for:) -> String: the row's text run, verb plus subject. Agents that title a call with
    // the tool's own name ("Edit file") would read "Edited Edit file": a leading word that only restates the kind goes,
    // so it reads "Edited file". PR-CHAT-093.
    public static string Label(ToolCall @for)
    {
        string[] roots = @for.ToolCallKind switch // :2190-2199
        {
            ToolCall.Kind.Command => ["run", "bash", "shell", "exec", "execute", "command"],
            ToolCall.Kind.Read => ["read", "view", "open", "cat"],
            ToolCall.Kind.Edit => ["edit", "write", "multiedit", "create", "notebookedit", "apply_patch", "patch"],
            ToolCall.Kind.Search => ["search", "grep", "glob", "find"],
            ToolCall.Kind.Web => ["fetch", "webfetch", "websearch", "browse"],
            ToolCall.Kind.Mcp => ["call"],
            ToolCall.Kind.Agent => ["delegate", "agent", "task"],
            ToolCall.Kind.Other => ["use"],
        };
        var subject = @for.Title; // :2200
        var words = SwiftString.Split(@for.Title, " ", maxSplits: 1); // :2201
        if (words.Count > 0 && SwiftString.Lowercased(words[0]) is var first && roots.Any(root => SwiftString.Equal(root, first))) // :2202
            subject = words.Count > 1 ? words[1] : ""; // :2203
        return string.Join(" ", new[] { Verb(@for), subject }.Where(part => part.Length != 0)); // :2205
    }

    // :2208-2220 static func verb(for:) -> String. PR-CHAT-093.
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
        };
    }

    // :2222-2233 static func symbol(for:) -> String. PR-CHAT-094; the names map to glyphs through SymbolCatalogue
    // (PA-0012).
    public static string Symbol(ToolCall.Kind @for) => @for switch
    {
        ToolCall.Kind.Command => "terminal", // :2224
        ToolCall.Kind.Read => "doc.text", // :2225
        ToolCall.Kind.Edit => "pencil", // :2226
        ToolCall.Kind.Search => "magnifyingglass", // :2227
        ToolCall.Kind.Web => "globe", // :2228
        ToolCall.Kind.Mcp => "puzzlepiece.extension", // :2229
        ToolCall.Kind.Agent => "person.2", // :2230
        ToolCall.Kind.Other => "wrench.and.screwdriver", // :2231
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
    /// <param name="entry">:2258 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:2259 <c>let runtime: ThreadRuntime</c>.</param>
    // :2263-2316 body
    public PlanCard(TimelineEntry entry, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2263");

    // :2318-2324 private static func title(for:) -> String
    private static string Title(ProposedPlan.State @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2318");

    // :2326-2333 private static func caption(for:) -> String?
    private static string? Caption(ProposedPlan.State @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2326");

    // :2335-2337 private static func needsAttention(for:) -> Bool
    private static bool NeedsAttention(ProposedPlan.State @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2335");
}

/// <summary>:2340-2383 <c>struct TodoListRow: View</c>.</summary>
public sealed class TodoListRow : Decorator
{
    /// <param name="entry">:2341 <c>let entry: TimelineEntry</c>.</param>
    // :2343-2374 body
    public TodoListRow(TimelineEntry entry) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2343");

    // :2376-2382 private static func symbol(for:) -> String
    private static string Symbol(TodoStep.Status @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2376");
}

/// <summary>:2385-2431 <c>struct NoticeRow: View</c>.</summary>
public sealed class NoticeRow : Decorator
{
    /// <param name="entry">:2386 <c>let entry: TimelineEntry</c>.</param>
    // :2388-2404 body
    public NoticeRow(TimelineEntry entry) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2388");

    // :2406-2412 private static func symbol(for:) -> String
    private static string Symbol(Notice.Level @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2406");

    // :2416-2422 private static func color(for:) -> Color
    private static SwiftColor Color(Notice.Level @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2416");

    // :2424-2430 private static func background(for:) -> Color
    private static SwiftColor Background(Notice.Level @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2424");
}

/// <summary>:2433-2456 <c>struct TurnEndRow: View</c>.</summary>
public sealed class TurnEndRow : Decorator
{
    /// <param name="summary">:2434 <c>let summary: TurnSummary</c>.</param>
    /// <param name="hasReply">:2437 <c>let hasReply: Bool</c>.</param>
    // :2439-2446 body
    public TurnEndRow(TurnSummary summary, bool hasReply) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2439");

    // :2448-2455 static func label(for:) -> String: read by the reply's hover line (:1369) and the finished turn's header
    // (:2610), both part 1.
    public static string Label(TurnSummary @for)
    {
        var duration = RelativeTime.Duration(TimeSpan.FromSeconds(@for.Duration)); // :2449
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
    public MutedRoundGlyph(string symbol) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2467");
}

/// <summary>:2480-2716 <c>struct TurnFinishedBlock: View</c>.</summary>
public sealed class TurnFinishedBlock : Decorator
{
    private readonly ThreadRuntime runtime;            // :2481
    private readonly Guid turnID;                      // :2482
    private readonly TurnSummary summary;              // :2483
    private readonly ValueList<TimelineEntry> content; // :2485-2487
    private readonly string? workingDirectory;         // :2488
    private readonly bool canUndo;                     // :2489-2491

    // :2497 @State private var isExpanded: Bool and :2523 @State private var legacyChanges: FileChangeSummary?, both read
    // by the body.
    private sealed class ViewState : MainActorModel
    {
        private bool isExpanded;
        private FileChangeSummary? legacyChanges;

        public ViewState(bool isExpanded) => this.isExpanded = isExpanded;

        public bool IsExpanded { get => Read(isExpanded); set => Set(ref isExpanded, value); }

        public FileChangeSummary? LegacyChanges { get => Read(legacyChanges); set => Set(ref legacyChanges, value); }
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
        this.content = content; // :2504
        this.workingDirectory = workingDirectory; // :2505
        this.canUndo = canUndo; // :2506
        // :2507-2508 _isExpanded = State(initialValue: chronological); the timeline always passes false
        // (ThreadTimeline.swift:330), so a finished turn opens folded.
        state = new ViewState(chronological);

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :2598
        foreach (var entry in userEntries) // :2599-2602
            stack.Children.Add(new UserMessageRow(entry, runtime, canUndo) { Margin = new Thickness(0, 0, 0, TimelineMetrics.RowSpacing) });
        stack.Children.Add(Header()); // :2603-2622

        // :2624-2701 if showsBody { the rule; the steps or the answer; the heads; the file card }
        var ruleHost = new Decorator();
        var middleHost = new Panel();
        var headsHost = new Decorator();
        var cardHost = new Decorator();
        stack.Children.Add(ruleHost);
        stack.Children.Add(middleHost);
        stack.Children.Add(headsHost);
        stack.Children.Add(cardHost);

        Control? middle = null;
        (bool ShowsBody, bool Expanded)? middleKey = null;
        FileChangeSummary? shownChanges = null;
        stack.Track(() => (state.IsExpanded, state.LegacyChanges), value =>
        {
            var (expanded, legacy) = value;
            var turn = DerivedFor(expanded); // :2590-2591
            var showsHeads = turn.HeadEntries.Count != 0; // :2592
            var changes = summary.Changes ?? legacy; // :2593-2595 the files as the turn's own diff saw them, or as worked out once
            var hasFiles = changes is { Files.Count: > 0 }; // :2596
            var showsBody = showsHeads || hasFiles || (expanded ? turn.DetailGroups.Count != 0 : turn.HasResponse); // :2597

            // :2625-2629 Divider().opacity(0.6).padding(.vertical, rowSpacing / 2): half a row gap on each side, so the
            // header and the body sit one row gap apart with the rule in between.
            ruleHost.Child = showsBody ? ruleHost.Child ?? Rule() : null;

            // :2631-2680 the full steps in a filled card, or the folded turn's answer, plans and notices.
            if (middleKey != (showsBody, expanded))
            {
                middleKey = (showsBody, expanded);
                if (middle is { } previous) SwiftTransition.Remove(middleHost, previous);
                middle = !showsBody ? null : expanded ? Steps(turn, showsHeads) : turn.HasResponse ? Answer(turn) : null;
                if (middle is { } next) SwiftTransition.Insert(middleHost, middleHost.Children.Count, next);
            }

            // :2682-2686 the heads the turn sent out stay in view under the response, folded or not.
            if (showsBody && showsHeads)
            {
                headsHost.Child ??= new HydraHeadsList(turn.HeadEntries, runtime, workingDirectory);
                headsHost.Child.Margin = new Thickness(0, 0, 0, hasFiles ? TimelineMetrics.RowSpacing : 0); // :2685
            }
            else
            {
                headsHost.Child = null;
            }

            // :2688-2700 the file card, when the turn's summary has files.
            if (showsBody && hasFiles && changes is { } files)
            {
                if (!ReferenceEquals(shownChanges, files)) cardHost.Child = FileCard(files);
                shownChanges = files;
            }
            else
            {
                cardHost.Child = null;
                shownChanges = null;
            }
        });

        // :2707-2714 .task(id: turnID): a turn recorded before per-file summaries existed has its changes worked out once
        // and stored back.
        this.Task(() => turnID, async _ =>
        {
            if (summary.Changes is not null || !(summary.FilesChanged > 0 || DerivedFor(state.IsExpanded).HasEdits)) return; // :2708
            var files = await runtime.ParsedDiff(selection: turnID); // :2709
            if (SwiftTask.IsCancelled) return; // :2710
            var changes = FileChangeSummaryDiff.Create(files); // :2711 FileChangeSummary(files:)
            state.LegacyChanges = changes; // :2712
            runtime.StoreLegacyChanges(changes, turnID); // :2713
        });
        Child = stack;
    }

    // :2603-2622 the header: the turn-end line and a chevron that turns down while the steps show.
    private Button Header()
    {
        var label = new TextBlock { Text = TurnEndRow.Label(summary) }; // :2610
        ChatFont.Chat(SwiftFont.TextStyle.Callout).Apply(label); // :2611
        label.Foreground = Chrome.SecondaryText.Brush(label); // :2612
        var chevron = SwiftImage.SystemName("chevron.right").Font(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)); // :2613-2614
        chevron.Foreground = Chrome.SecondaryText.Brush(chevron); // :2615
        var turn = new AnimatedDouble(angle => chevron.RenderTransform = new RotateTransform(angle)); // :2616
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = Brushes.Transparent }; // :2609, :2618 .contentShape(.rect)
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
                    TimelineEntry.Kind.Tool => new WorkGroup(new ValueList<TimelineEntry>([single.Value]), runtime, workingDirectory), // :2640
                    TimelineEntry.Kind.Plan => new PlanCard(single.Value, runtime), // :2642
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
        return card;
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
        foreach (var entry in turn.CollapsedPlans) column.Children.Add(new PlanCard(entry, runtime)); // :2672-2674
        foreach (var entry in turn.CollapsedNotices) column.Children.Add(new NoticeRow(entry)); // :2675-2677
        return column;
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
        var key = new DerivedKey(content, expanded);
        if (derived is { } cached && cached.Key.Equals(key)) return cached.Value;
        var value = new Derived(content, expanded);
        derived = (key, value);
        return value;
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
            CollapsedPlans = new ValueList<TimelineEntry>(collapsedPlans);
            CollapsedNotices = new ValueList<TimelineEntry>(collapsedNotices);
            HasEdits = hasEdits;
            HasResponse = hasResponse;
            HeadEntries = new ValueList<TimelineEntry>(headEntries);
            if (expanded) // :2580-2585 the full steps, built only while showing; the heads have their own list below
            {
                // TimelineGroup.build is App.Views.Chat.ThreadTimeline's, still MARROW_STUB (STOP #3).
                DetailGroups = new ValueList<TimelineGroup>(TimelineGroup.Build(content, showReasoning: false).Where(group => group is not TimelineGroup.Heads));
            }
        }
    }
}

/// <summary>:2718-2859 <c>private struct TurnFileCard: View</c>.</summary>
public sealed class TurnFileCard : Decorator
{
    /// <param name="changes">:2721 <c>let changes: FileChangeSummary</c>.</param>
    /// <param name="canUndo">:2722 <c>let canUndo: Bool</c>.</param>
    /// <param name="onRevert">:2724 <c>let onRevert: () -> Void</c>.</param>
    /// <param name="onReview">:2726 <c>let onReview: (NSView) -> Void</c>.</param>
    // :2735-2827 body
    public TurnFileCard(FileChangeSummary changes, bool canUndo, Action onRevert, Action<Control> onReview) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2735");
}
