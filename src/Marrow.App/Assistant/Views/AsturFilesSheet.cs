// PORT-ONLY: DESIGN.md 9.1; ED-NEW-dextra-open-4.
// Retirement keeps files. A second, explicit owner action erases only the selected Astur's data.
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class AsturFilesSheet : StackPanel
{
    public AsturFilesSheet(Func<IReadOnlyList<AssistantProfile>> profiles, Func<string, Task<string?>> delete, Action<string> said)
    {
        Spacing = 10;
        var retired = profiles().Where(p => !p.IsRoot && p.Retired).ToArray();
        if (retired.Length == 0) Children.Add(TaskLook.Text("No retired Asturs.", 12, SwiftFont.Weight.Regular, TaskLook.Ink2));
        foreach (var profile in retired)
        {
            var status = new LiveStatusText();
            var ask = new DetailButton("Delete files");
            var confirm = new DetailButton("Permanently delete files", primary: true) { IsVisible = false };
            var keep = new DetailButton("Keep files") { IsVisible = false };
            ask.Click += (_, _) =>
            {
                status.Text = "Delete " + profile.Name + "'s folder, browser sign-ins, own memory and recordings? Old missions keep their history. This cannot be undone.";
                confirm.IsVisible = keep.IsVisible = true;
                keep.Focus();
            };
            keep.Click += (_, _) => { confirm.IsVisible = keep.IsVisible = false; status.Text = "Files kept."; ask.Focus(); };
            confirm.Click += async (_, _) =>
            {
                confirm.IsEnabled = ask.IsEnabled = keep.IsEnabled = false;
                string? problem;
                try { problem = await delete(profile.Id); }
                catch (Exception error) when (error is not OperationCanceledException) { problem = "Files could not be deleted: " + error.Message; }
                status.Text = problem ?? "Files deleted. Old missions keep their history.";
                said(status.Text);
                confirm.IsEnabled = ask.IsEnabled = keep.IsEnabled = true;
                confirm.IsVisible = keep.IsVisible = false;
                ask.Focus();
            };
            var row = new StackPanel { Spacing = 6, Children = { TaskLook.Text(profile.Name, 12, SwiftFont.Weight.Semibold, TaskLook.Ink), ask, status, keep, confirm } };
            row.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape || !confirm.IsVisible || !keep.IsEnabled) return;
                e.Handled = true; confirm.IsVisible = keep.IsVisible = false; status.Text = "Files kept."; ask.Focus();
            };
            Children.Add(row);
        }
    }
}
