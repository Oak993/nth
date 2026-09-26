using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/FollowUpEditor.swift (B2 1fbf81e), :1-36.

namespace Marrow.App.Views.Composer;

/// <summary>:3-36 <c>struct FollowUpEditor: View</c>. Edits a queued follow-up in place. The text and files being
/// edited live on the thread (<c>ThreadRuntime.followUpEdit</c>), not here, so the editor closing with the thread and
/// opening again later shows the edit exactly as it was left.</summary>
public sealed class FollowUpEditor : Decorator
{
    private readonly PromptEdit edit;       // :7 @Bindable var edit: PromptEdit
    private readonly ThreadRuntime runtime; // :8 let runtime: ThreadRuntime

    /// <param name="edit">:7 <c>@Bindable var edit: PromptEdit</c>.</param>
    /// <param name="runtime">:8 <c>let runtime: ThreadRuntime</c>.</param>
    // :10-25 body
    public FollowUpEditor(PromptEdit edit, ThreadRuntime runtime)
    {
        this.edit = edit;
        this.runtime = runtime;
        Child = new PromptEditor(
            "Edit follow-up", // :12
            new Binding<string>(() => edit.Text, value => edit.Text = value), // :13 $edit.text
            new Binding<ValueList<Attachment>>(() => edit.Attachments, value => edit.Attachments = value), // :14
            Save, // :15 onSubmit: save
            Cancel, // :16 onCancel: cancel
            () => new Panel(), // :17-18 an empty content builder
            Actions); // :18-24 actions:
    }

    // :19-23. The two buttons sit in PromptEditor's action row, whose HStack spacing is 8 (PromptEditor.swift:78).
    private Control Actions()
    {
        // :19-20 Button("Cancel", role: .cancel, action: cancel).buttonStyle(.glass). The cancel role maps to Escape
        // (PR-COMPOSER-174 windows_path), which is Avalonia's IsCancel.
        var cancel = GlassButton(new TextBlock { Text = "Cancel" }, Chrome.CapsuleContentHeight, Cancel);
        cancel.IsCancel = true;

        // :21-23 Button("Save", action: save).buttonStyle(.glassProminent).disabled(edit.isEmpty)
        var save = WelcomeView.GlassProminentButton(new TextBlock { Text = "Save" }, Chrome.CapsuleContentHeight, Save);
        save.Track(() => edit.IsEmpty, empty => save.IsEnabled = !empty); // :23

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8, // PromptEditor.swift:78
            Children = { cancel, save },
        };
    }

    // :27-31
    private void Save()
    {
        if (edit.IsEmpty) return; // :28
        runtime.UpdateFollowUp(edit.Id, edit.Text, edit.Attachments); // :29
        runtime.FollowUpEdit = null; // :30
    }

    // :33-35
    private void Cancel() => runtime.FollowUpEdit = null; // :34

    // ENGINEERING DECISION: `.buttonStyle(.glass)` (:20) as the untinted twin of WelcomeView.GlassProminentButton (RootView.cs), the
    // recipe TourView's primaryActionButton established for `.glassProminent`: the same continuous capsule, height
    // and horizontal padding, with the regular interactive glass effect and no accent tint or accent fill (plain
    // `.glass` takes no tint; `.glassProminent` adds the accent). OD-0005 governs the material itself.
    private static Button GlassButton(Control label, double height, Action action)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        var framed = new Border
        {
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0),
            Height = height,
            Child = label,
        };
        framed.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(framed, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        return PlainControls.Button(framed, action);
    }
}
