// PORT-ONLY: DESIGN.md 7.1, 9.2; ED-NEW-dextra-open-4 (LEAD-REVIEW).
// AS-10's owner Save confirms permanent configuration. Ideas never register a watcher by themselves.
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class AsturDutiesSheet : StackPanel
{
    public AsturDutiesSheet(Func<AssistantProfile?> profile, DutyHost host)
    {
        Spacing = 10;
        Children.Add(TaskLook.Text("Review a duty before creating it. Leave the schedule empty for Run now only.", 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true));
        foreach (var idea in profile()?.Duties.Where(d => d.Keep) ?? [])
        {
            var schedule = new TextBox { PlaceholderText = "Run now only, or e.g. weekdays 09:00" };
            AutomationProperties.SetName(schedule, "Schedule for " + idea.Text);
            var status = new LiveStatusText();
            var review = new DetailButton("Review duty");
            var confirm = new DetailButton("Create duty", primary: true) { IsVisible = false };
            Duty? reviewed = null;
            schedule.TextChanged += (_, _) => { reviewed = null; confirm.IsVisible = false; };
            review.Click += (_, _) =>
            {
                if (profile() is not { Retired: false } current) { status.Text = "This Astur is no longer active."; return; }
                var built = AsturDutyDrafts.Build(current, idea.Text, schedule.Text ?? "", host.Options.Time.GetUtcNow(), host.Options.Time.LocalTimeZone);
                reviewed = built.Duty;
                if (reviewed is { } proposed && host.Scheduler.Find(proposed.Id) is { } existing)
                    reviewed = existing with { Trigger = proposed.Trigger, Instructions = proposed.Instructions };
                status.Text = built.Problem ?? $"{current.Name}: {DutyCardWords.Trigger(reviewed!.Trigger)}. Report: {DutyCardWords.Report(reviewed.Report)}. {DutyCardWords.Budget(reviewed)}. Inherits this Astur's grants and budget. You can cancel by closing this sheet.";
                confirm.Title = reviewed is not null && host.Scheduler.Find(reviewed.Id) is not null ? "Save duty changes" : "Create duty";
                confirm.IsVisible = reviewed is not null;
            };
            confirm.Click += (_, _) =>
            {
                if (reviewed is not { } duty || profile() is not { Retired: false } current || current.Id != duty.AssistantId) return;
                if (host.Scheduler.Find(duty.Id) is { } existing)
                    duty = existing with { Trigger = duty.Trigger, Instructions = duty.Instructions };
                var saved = host.Save(duty);
                status.Text = saved.Problem ?? "Saved. The duty is ready.";
                if (saved.Duty is not null) { reviewed = null; confirm.IsVisible = false; review.Focus(); }
            };
            Children.Add(new StackPanel { Spacing = 6, Children = { TaskLook.Text(idea.Text, 12, SwiftFont.Weight.Semibold, TaskLook.Ink, wrap: true), schedule, review, status, confirm } });
        }
        if (profile()?.Duties.Any(d => d.Keep) != true) Children.Add(TaskLook.Text("No kept duty ideas yet. Add them in this Astur's settings.", 12, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true));
    }
}
