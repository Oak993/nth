using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/EditorAttachments.swift (B2 1fbf81e), :1-53.

namespace Marrow.App.Views.Composer;

/// <summary>:3-53 <c>struct EditorAttachments: View</c>. Same shape as the chat strip: one preview panel for the strip, so
/// every draft photo opens in a single tap.</summary>
public sealed class EditorAttachments : Decorator
{
    private readonly Binding<ValueList<Attachment>> attachments; // :4 @Binding var attachments: [Attachment]

    // :8 @State private var preview = AttachmentPreviewCoordinator(). Read by the thumbnails' taps, the anchor, the
    // change handler and the disappear handler, never by the body's structure, so a plain field (R-VIEW-2).
    private readonly AttachmentPreviewCoordinator preview = new();

    // :9 @State private var drawn: Set<Attachment.ID> = []. The body reads it only for each cell's isNew, which the
    // cells take through AttachmentArrival.Update (AttachmentFlight.swift:181-183), so a plain set.
    private readonly HashSet<Guid> drawn = [];

    // :48-50 the .onChange(of: attachments) watcher, held for the control's life (as TokenActivitySection holds its own).
    private readonly OnChange<ValueList<Attachment>> changes;

    /// <param name="attachments">:4 <c>@Binding var attachments: [Attachment]</c>.</param>
    // :11-52 body
    public EditorAttachments(Binding<ValueList<Attachment>> attachments)
    {
        this.attachments = attachments;

        // :13-20 the delete badge straddles the thumbnail's far top-right corner; each cell's own top and trailing
        // padding reserves that overhang, so the badge sits inside its own cell and every photo stays deletable.
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 }; // :21 HStack(spacing: 0)
        var cells = new ForEach<Attachment, Guid>(row, attachment => attachment.Id, Cell, // :22 ForEach(attachments)
            (cell, attachment) => ((AttachmentArrival)cell).Update(!drawn.Contains(attachment.Id)));

        // :12 ScrollView(.horizontal, showsIndicators: false); :44-46 .background { AttachmentAnchorCapture { ... } }
        Child = new ScrollViewer
        {
            Content = PlainControls.Background(row, new AttachmentAnchorCapture(view => preview.SetAnchor(view))),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        this.Track(() => attachments.Value, cells.Apply);
        // :48-50 .onChange(of: attachments) { preview.retire(except: Set(attachments.map(\.id))) }
        changes = new OnChange<ValueList<Attachment>>(() => attachments.Value,
            (_, now) => preview.Retire(now.Select(attachment => attachment.Id).ToHashSet()));
        // :51 .onDisappear { preview.close() }
        DetachedFromVisualTree += (_, _) => preview.Close();
    }

    // :23-41 one cell: the thumbnail in an arrival wrapper, with its remove badge.
    private Control Cell(Attachment attachment)
    {
        var id = attachment.Id;
        var thumbnail = new AttachmentThumbnail(attachment, preview, size: 48); // :24

        // :28-33 Image(systemName: "xmark.circle.fill").symbolRenderingMode(.palette).foregroundStyle(.white,
        // .black.opacity(0.6)).padding(4). The palette's second layer is recorded, not drawn (ED-182 (Q6)).
        var glyph = SwiftImage.SystemName("xmark.circle.fill")
            .SymbolRenderingMode(SymbolRenderingMode.Palette)
            .ForegroundStyle(SwiftColor.White, SwiftColor.Black.Opacity(0.6));
        var label = new Border { Padding = new Thickness(4), Child = glyph }; // :32
        // :26-27 attachments.removeAll { $0.id == attachment.id }, written back through the binding.
        var remove = PlainControls.Button(label, () =>
            attachments.Value = new ValueList<Attachment>(attachments.Value.Where(item => item.Id != id)));
        remove.HorizontalAlignment = HorizontalAlignment.Right; // :25 .overlay(alignment: .topTrailing)
        remove.VerticalAlignment = VerticalAlignment.Top;
        remove.RenderTransform = new TranslateTransform(6, -6); // :35 .offset(x: 6, y: -6)
        AutomationProperties.SetName(remove, "Remove " + attachment.Name); // :36

        var content = new Panel { Margin = new Thickness(0, 2, 8, 0) }; // :38-39 .padding(.top, 2) .padding(.trailing, 8)
        content.Children.Add(thumbnail);
        content.Children.Add(remove);

        var cell = new AttachmentArrival(!drawn.Contains(id), content: content); // :23
        // :41 .onAppear { drawn.insert(attachment.id) }. SwiftUI applies the state change on a later update
        // (AttachmentFlight.swift:176-180): the cell's first frame reports still see isNew, so a fresh drop flies into
        // it once; the cell turns not-new after that, and later relayouts take nothing.
        cell.AttachedToVisualTree += (_, _) =>
        {
            if (!drawn.Add(id)) return;
            MainActor.Post(() => cell.Update(false));
        };
        return cell;
    }
}
