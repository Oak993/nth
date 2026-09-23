using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The actions the one message call site hands to <c>rightClickPopover</c>: a trimmed
/// <c>TimelineRows.messageActions(text:canRevert:confirmRevert:)</c> (App/Views/Chat/TimelineRows.swift:215-228), with
/// titles, symbols, confirmations and <c>startsGroup</c> as the Swift writes them. The action bodies only record
/// their title here; copying and the revert binding are TimelineRows' behaviour, not this unit's.
/// </summary>
public static class MessageActionsSample
{
    public static IReadOnlyList<RowAction> Make(bool canRevert, List<string> ran)
    {
        var items = new List<RowAction>
        {
            new() { Title = "Copy message", Symbol = "doc.on.doc", Confirms = "Copied", Action = () => ran.Add("Copy message") }, // TimelineRows.swift:219
            new() { Title = "Copy as markdown", Symbol = "number", Confirms = "Copied", Action = () => ran.Add("Copy as markdown") }, // TimelineRows.swift:220
        };
        if (canRevert) // TimelineRows.swift:222-226
        {
            items.Add(new() { Title = "Edit from here", Symbol = "arrow.uturn.backward", StartsGroup = true, Action = () => ran.Add("Edit from here") });
        }
        return items;
    }
}
