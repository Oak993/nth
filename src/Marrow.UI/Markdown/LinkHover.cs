using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.Core.Models;
using Marrow.Core.Port;

// Source: DroppyCode/UI/Markdown/LinkHover.swift (B3 52bc792), :1-51. New at B3 (N9): the continuous-hover cursor
// and head-mention hover card, shared by InlineText (MarkdownView.swift:1147-1193) and ProseRunView.swift:21-59.
// Avalonia has no onContinuousHover primitive; PointerMoved/PointerExited/DetachedFromVisualTree stand in for
// it (ENGINEERING DECISION carried over from the pre-B3 inline copies this unit moves).

namespace Marrow.UI.Markdown;

/// <summary>
/// :6-51 <c>struct LinkHoverModifier: ViewModifier</c>: the pointing hand over a link and the head card over a
/// mention. <c>LinkTextView</c> has no tracking areas of its own (AppKit would rebuild them every scrolled
/// frame), so the cursor comes from here instead.
/// </summary>
public static class LinkHover
{
    /// <summary>
    /// :14-44 <c>body(content:)</c>: wires the continuous hover for one link-bearing paragraph control.
    /// <paramref name="hostTextView"/> resolves the <see cref="LinkTextView"/> the paragraph hosts (set after
    /// construction through <c>onHost</c>, so a function rather than a value at attach time - :8 <c>WeakView</c>
    /// in the source; a plain captured reference here needs no weak box, since the closures' own lifetime is
    /// already bound to <paramref name="paragraph"/>'s event subscriptions).
    /// </summary>
    public static void Attach(
        Control paragraph,
        Func<LinkTextView?> hostTextView,
        Func<IReadOnlyList<HydraPersona>> personas,
        Func<IReadOnlyDictionary<string, HydraMentionTarget>> targets) =>
        Track(paragraph, hostTextView, personas, targets);

    /// <summary><see cref="Attach"/>, returning the hover's state (for the tests). The mentions and targets are read when the pointer
    /// moves, not when the hover is attached: SwiftUI hands the modifier the values of the body that ran last (:7-11, :14), and a
    /// port host changes the environment under a paragraph it has already built (InlineText, MarkdownView.swift:1074-1075 at B3).</summary>
    internal static HoverState Track(
        Control paragraph,
        Func<LinkTextView?> hostTextView,
        Func<IReadOnlyList<HydraPersona>> personas,
        Func<IReadOnlyDictionary<string, HydraMentionTarget>> targets)
    {
        var state = new HoverState();
        paragraph.PointerMoved += (_, e) => Moved(paragraph, hostTextView(), e.GetPosition(paragraph), personas(), targets(), state);
        paragraph.PointerExited += (_, _) => End(paragraph, state); // :37-39 case .ended
        paragraph.DetachedFromVisualTree += (_, _) => End(paragraph, state); // :41-43 .onDisappear
        return state;
    }

    internal sealed class HoverState
    {
        public bool ShowsHand; // :9 @Binding var showsHand: Bool
        public Guid? HoveredHeadID; // :10 @Binding var hoveredHeadID: UUID?
    }

    // :19-36 the .active(point) case
    internal static void Moved(
        Control paragraph,
        LinkTextView? view,
        Point point,
        IReadOnlyList<HydraPersona> personas,
        IReadOnlyDictionary<string, HydraMentionTarget> targets,
        HoverState state)
    {
        if (view is null) return; // :18 guard let view = linkView.value as? LinkTextView else { return }

        var overLink = view.HasLink(point); // :21
        if (overLink != state.ShowsHand) // :22
        {
            state.ShowsHand = overLink;
            paragraph.Cursor = overLink // :24 NSCursor.pointingHand.push() / .pop()
                ? new Cursor(StandardCursorType.Hand)
                : Cursor.Default;
        }

        var mention = view.HeadMentionRect(point); // :26
        if (mention is { } m && targets.TryGetValue(m.Name, out var target) && target.Url == m.Url // :26-27
            && personas.FirstOrDefault(p => p.Name == m.Name) is { } persona) // :28
        {
            if (state.HoveredHeadID != target.ThreadID) // :29
            {
                state.HoveredHeadID = target.ThreadID; // :30
                HydraHeadHoverPopover.Instance.Show(persona, target, m.Rect, paragraph); // :31
            }
        }
        else if (state.HoveredHeadID is not null) // :33
        {
            state.HoveredHeadID = null; // :34
            HydraHeadHoverPopover.Instance.Hide(); // :35
        }
    }

    // :46-50 private func endHover()
    internal static void End(Control paragraph, HoverState state)
    {
        if (state.ShowsHand) { paragraph.Cursor = Cursor.Default; state.ShowsHand = false; } // :47
        state.HoveredHeadID = null; // :48
        HydraHeadHoverPopover.Instance.Hide(); // :49
    }
}
