using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B2 1fbf81e), :1-2859.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
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
    // :31-34 private func copyMessageText(_:)
    internal static void CopyMessageText(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:31");

    // :38-54 private func lastCodeBlock(in:) -> String?
    internal static string? LastCodeBlock(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:38");

    // :1420-1428 func messageMenu(text:runtime:) on View
    public static Control MessageMenu(this Control view, string text, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1420");
}

/// <summary>:59-62 <c>@Observable private final class MessageMenuRequests</c> (R-OBS-1).</summary>
public sealed class MessageMenuRequests : ObservableModel
{
    private bool confirmRevert; // :61 = false

    // :61 var confirmRevert = false
    public bool ConfirmRevert { get => Read(confirmRevert); set => Set(ref confirmRevert, value); }
}

/// <summary>:64-229 <c>struct UserMessageRow: View</c>.</summary>
public sealed class UserMessageRow : Decorator
{
    /// <param name="entry">:65 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:66 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="canRevert">:69 <c>let canRevert: Bool</c>.</param>
    // :78-200 body
    public UserMessageRow(TimelineEntry entry, ThreadRuntime runtime, bool canRevert) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:78");

    // :216-228 private static func messageActions(text:canRevert:confirmRevert:) -> [RowAction]
    private static IReadOnlyList<RowAction> MessageActions(string text, bool canRevert, Binding<bool> confirmRevert) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:216");
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

    // :423-433 static func finishedHeads(in:) -> [HydraHeadInfo]
    public static ValueList<HydraHeadInfo> FinishedHeads(IReadOnlyList<ChatThread> @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:423");
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

    // :1171-1206 func path(in:) -> Path
    public Geometry Path(Rect @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1171");
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
    /// <param name="entry">:1354 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="summary">:1357 <c>let summary: TurnSummary?</c>.</param>
    /// <param name="runtime">:1358 <c>let runtime: ThreadRuntime</c>.</param>
    // :1361-1390 body
    public AssistantMessageRow(TimelineEntry entry, TurnSummary? summary, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1361");

    // :1394-1412 static func messageActions(text:code:runtime:) -> [RowAction]
    public static IReadOnlyList<RowAction> MessageActions(string text, string? code, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1394");
}

/// <summary>:1431-1439 <c>private struct FoldedAnswerView: View</c>.</summary>
public sealed class FoldedAnswerView : Decorator
{
    /// <param name="text">:1432 <c>let text: String</c>.</param>
    /// <param name="runtime">:1433 <c>let runtime: ThreadRuntime</c>.</param>
    // :1435-1438 body
    public FoldedAnswerView(string text, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1435");
}

/// <summary>:1446-1510 <c>struct WorkGroup: View</c>.</summary>
public sealed class WorkGroup : Decorator
{
    /// <summary>:1455-1461 <c>init(entries:runtime:workingDirectory:startsCollapsed:)</c>; :1463-1509 body.</summary>
    public WorkGroup(ValueList<TimelineEntry> entries, ThreadRuntime runtime, string? workingDirectory = null, bool startsCollapsed = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1455");
}

/// <summary>:1515-1559 <c>struct WorkSteps: View</c>.</summary>
public sealed class WorkSteps : Decorator
{
    /// <param name="entries">:1516 <c>let entries: [TimelineEntry]</c>.</param>
    /// <param name="runtime">:1517 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="showsAll">:1519 <c>@Binding var showsAll: Bool</c>; no default, so it moves before
    /// workingDirectory (ED-095).</param>
    /// <param name="workingDirectory">:1518 <c>var workingDirectory: String?</c>.</param>
    // :1522-1558 body
    public WorkSteps(ValueList<TimelineEntry> entries, ThreadRuntime runtime, Binding<bool> showsAll, string? workingDirectory = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1522");
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
    private static ValueList<ToolCall.Kind> Order =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1588");

    // :1590-1610 static func text(for:) -> String
    public static string Text(IReadOnlyList<TimelineEntry> @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1590");

    // :1612-1620 static func symbol(for:) -> String
    public static string Symbol(IReadOnlyList<TimelineEntry> @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1612");

    // :1622-1633 private static func label(for:running:) -> String
    private static string Label(ToolCall.Kind @for, bool running) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1622");
}

/// <summary>:1636-1854 <c>struct ToolRow: View</c>.</summary>
public sealed class ToolRow : Decorator
{
    /// <param name="entry">:1638 <c>let entry: TimelineEntry</c>.</param>
    /// <param name="runtime">:1639 <c>let runtime: ThreadRuntime</c>.</param>
    /// <param name="workingDirectory">:1640 <c>var workingDirectory: String?</c>.</param>
    // :1657-1717 body
    public ToolRow(TimelineEntry entry, ThreadRuntime runtime, string? workingDirectory = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1657");

    // :1723-1735 private static func departingHead(for:) -> (persona:, name:, task:)
    private static (HydraPersona Persona, string? Name, string Task) DepartingHead(ToolCall @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1723");

    // :1738-1746 private static func departureLabel(for:name:task:) -> String
    private static string DepartureLabel(ToolCall @for, string? name, string task) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1738");

    // :1749-1755 private static func departureStatus(for:) -> HydraHeadInfo.Status?
    private static HydraHeadInfo.Status? DepartureStatus(ToolCall @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1749");
}

/// <summary>:1858-1872 <c>private struct HeadStepsPopoverModifier: ViewModifier</c>.</summary>
/// <param name="HeadID">:1859 <c>let headID: UUID?</c>.</param>
/// <param name="IsPresented">:1860 <c>@Binding var isPresented: Bool</c>.</param>
public sealed record HeadStepsPopoverModifier(Guid? HeadID, Binding<bool> IsPresented)
{
    // :1862-1871 func body(content:)
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:1862");
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
    /// <param name="symbol">:2070 <c>var symbol: String?</c>.</param>
    // :2072-2091 body
    public ToolStatusIcon(ToolCall call, string? symbol = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2072");
}

/// <summary>:2094-2167 <c>private struct ToolDetailView: View</c>.</summary>
public sealed class ToolDetailView : Decorator
{
    // :2103 private static let outputLineLimit = 30
    private const int OutputLineLimit = 30;

    /// <param name="call">:2095 <c>let call: ToolCall</c>.</param>
    /// <param name="workingDirectory">:2096 <c>var workingDirectory: String?</c>.</param>
    // :2115-2166 body
    public ToolDetailView(ToolCall call, string? workingDirectory = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2115");

    // :2106-2113 private static func head(of:lines:) -> String
    private static string Head(string of, int lines) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2106");
}

/// <summary>:2171-2182 <c>private enum DiffDetailCache</c>.</summary>
public static class DiffDetailCache
{
    // :2174-2181 @MainActor static func file(diff:path:) -> DiffFile
    public static DiffFile File(string diff, string path) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2175");
}

/// <summary>:2184-2255 <c>enum ToolPresentation</c>.</summary>
public static class ToolPresentation
{
    // :2189-2206 static func label(for:) -> String
    public static string Label(ToolCall @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2189");

    // :2208-2220 static func verb(for:) -> String
    public static string Verb(ToolCall @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2208");

    // :2222-2233 static func symbol(for:) -> String
    public static string Symbol(ToolCall.Kind @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2222");

    // :2235-2240 static func stats(for:) -> (additions: Int, deletions: Int)?
    public static (int Additions, int Deletions)? Stats(ToolCall @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2235");

    // :2242-2250 static func mcpParts(_:) -> (entry: MCPCatalogEntry, tool: String)?
    public static (MCPCatalogEntry Entry, string Tool)? McpParts(ToolCall call) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2242");

    // :2252-2254 static func mcpLabel(entry:tool:running:) -> String
    public static string McpLabel(MCPCatalogEntry entry, string tool, bool running) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2252");
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

    // :2448-2455 static func label(for:) -> String
    public static string Label(TurnSummary @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2448");
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
    /// <summary>:2499-2509 <c>init(runtime:turnID:summary:userEntries:content:workingDirectory:canUndo:chronological:)</c>;
    /// :2589-2715 body.</summary>
    public TurnFinishedBlock(
        ThreadRuntime runtime,
        Guid turnID,
        TurnSummary summary,
        ValueList<TimelineEntry> userEntries,
        ValueList<TimelineEntry> content,
        string? workingDirectory,
        bool canUndo,
        bool chronological) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2499");

    /// <summary>:2518-2521 <c>private struct DerivedKey: Equatable</c>. <c>ids: [ObjectIdentifier]</c> (:2519) keeps
    /// the entries, compared by identity by the implementing unit (no port type for ObjectIdentifier yet).</summary>
    private sealed record DerivedKey(IReadOnlyList<TimelineEntry> Ids, bool Expanded); // :2519-2520

    /// <summary>:2527-2587 <c>private struct Derived</c>.</summary>
    private sealed record Derived
    {
        public ValueList<TimelineEntry> AssistantEntries { get; init; } = ValueList<TimelineEntry>.Empty; // :2529
        public ValueList<TimelineEntry> AnswerEntries { get; init; } = ValueList<TimelineEntry>.Empty;    // :2534
        public ValueList<TimelineEntry> CollapsedPlans { get; init; } = ValueList<TimelineEntry>.Empty;   // :2535
        public ValueList<TimelineEntry> CollapsedNotices { get; init; } = ValueList<TimelineEntry>.Empty; // :2538
        public bool HasEdits { get; init; }                                                              // :2539
        public bool HasResponse { get; init; }                                                           // :2540
        public ValueList<TimelineEntry> HeadEntries { get; init; } = ValueList<TimelineEntry>.Empty;      // :2542
        public ValueList<TimelineGroup> DetailGroups { get; init; } = ValueList<TimelineGroup>.Empty;     // :2545

        // :2547-2586 @MainActor init(content:expanded:)
        public Derived(IReadOnlyList<TimelineEntry> content, bool expanded) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/TimelineRows.swift:2548");
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
