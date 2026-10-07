using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    internal Marrow.Core.Port.MultiAccount.Settings.HandedOverExample? AccountHandoff { get; private set; }
    internal void RecordAccountHandoff(Marrow.Core.Accounts.HandoffSummary summary, Marrow.Core.Accounts.HandoffPackage package, string to, bool ready)
    {
        // The sheet describes the actual recorded archive; it never uses Settings' illustrative counts.
        AccountHandoff = new("What was handed over", "to " + to,
            package.Bring == Marrow.Core.Accounts.Bring.AiSummary ? package.Briefing ?? summary.Text : summary.Text,
            package.Bring == Marrow.Core.Accounts.Bring.AiSummary ? "A summary by AI" : null, [],
            summary.FilePath, summary.FilePath is null ? null : "Full conversation archive", ready ? "In sync" : null);
    }
    internal void AppendCombinedOpinion(Guid round, string text, string byline, CancellationToken ct)
    {
        MainActor.AssumeIsolated(); ct.ThrowIfCancellationRequested();
        var id = "opinion-combined-" + round.ToString("N");
        if (Entries.Any(entry => entry.Item.Id == id)) return;
        Append(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text + "\n\n" + byline }), id));
        SaveNow();
    }
    private void CheckCompletedAccountAnswer(Guid turn, Guid author)
    {
        var items = Entries.Where(entry => entry.Item.TurnID == turn).Select(entry => entry.Item.TimelineItemContent).ToArray();
        var message = items.OfType<TimelineItem.Content.User>().FirstOrDefault()?.Value.Text;
        var answer = string.Join("\n", items.OfType<TimelineItem.Content.Assistant>().Where(item => !item.Value.IsStreaming).Select(item => item.Value.Text));
        if (!string.IsNullOrWhiteSpace(message) && !string.IsNullOrWhiteSpace(answer)) App?.CheckCompletedAccountAnswer(this, turn, author, message, answer);
    }
}
