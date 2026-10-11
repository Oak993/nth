// PORT-ONLY: T11 step 2 (OD-0114, OD-0117). Settings > Pills: "Your pills" (the pills drawn as pills, New pill…, ⋯ Edit / Duplicate / Rename / Delete), "Use for new threads", and the pill editor one step in. The page is always in the sidebar.
// With fewer than two accounts connected its controls are present but dimmed and inert under one short message that says what a pill is and what turns it on, with a button to Settings > Providers (OD-0139: where accounts are connected); connecting a second account enables everything,
// live, with a transition (the message folds away, the controls come up) (OD-0076; Reduce Motion lands it at once).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class PillsPageView : Decorator
{
    private readonly MultiAccountContext context;
    private readonly PillsSettingsModel model;
    private readonly ModelPulse pulse;
    private readonly CrossfadeSlot slot = new();
    private readonly StackPanel listPage = new() { Spacing = 0 };
    private readonly StackPanel rows = new() { Spacing = 0 };
    private readonly Dictionary<Guid, PillRowControl> rowViews = [];
    private readonly CzFoldRow explainFold;
    private readonly Border controls = new();
    private readonly SettingRowView use;
    private readonly Control newPill;
    private readonly AnimatedScalar dim;
    private PillEditorView? editor;

    internal PillsPageView(MultiAccountContext context)
    {
        this.context = context;
        model = context.Pills;
        pulse = new ModelPulse(h => model.Changed += h);

        var info = LibraryUi.Glyph("info.circle", 14, ChromeTokens.Accent);
        info.VerticalAlignment = VerticalAlignment.Top;
        var what = LibraryUi.Text(model.WhatText, 13, wrap: true);
        var need = LibraryUi.Text(model.NeedText, 12.5, secondary: true, wrap: true);
        var goProviders = LibraryButtons.Make(MultiAccountWords.DisabledButton, context.OpenProviders, ButtonLook.Primary, "pills-providers");
        goProviders.HorizontalAlignment = HorizontalAlignment.Left;
        var text = new StackPanel { Spacing = 4, Children = { what, need } };
        var explain = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        Grid.SetColumn(text, 1);
        explain.Children.Add(info);
        explain.Children.Add(text);
        explainFold = new CzFoldRow(new Border { Padding = new Thickness(0, 0, 0, ChromeTokens.SectionSpacing), Child = LibraryUi.Panel(new StackPanel { Spacing = 10, Children = { explain, goProviders } }, 12, 0.06, new Thickness(14, 12)) }, !model.Enabled);

        newPill = NewPillRow();
        var pillCard = new ChromeCard(() => new StackPanel { Children = { rows, newPill } });
        var foot = LibraryUi.Text(MultiAccountWords.PillsFoot, 12, secondary: true, wrap: true);
        foot.Margin = new Thickness(16, 0);
        var listSection = new StackPanel { Spacing = 8, Children = { new ChromeSection(MultiAccountWords.PillsHeading, () => pillCard), foot } };

        use = new SettingRowView(model.UseRow, () => model.UseRow, pulse, choose: model.ChooseUse);
        var useCard = new ChromeCard(() => use);

        var body = new StackPanel { Spacing = ChromeTokens.SectionSpacing, Children = { listSection, useCard } };
        controls.Child = body;
        dim = new AnimatedScalar(controls, model.Enabled ? 1 : 0.45, v => controls.Opacity = v);
        controls.IsEnabled = model.Enabled;
        listPage.Children.Add(explainFold);
        listPage.Children.Add(controls);

        slot.HorizontalAlignment = HorizontalAlignment.Stretch;
        slot.Show(listPage, null);
        Child = slot;
        this.Track(() => pulse.Revision, _ => Apply());
        Apply();
    }

    internal bool ExplainOpen => explainFold.Open;

    internal double ExplainFraction => explainFold.Fraction;

    internal bool ControlsEnabled => controls.IsEnabled;

    internal double Dimness => dim.Value;

    internal IReadOnlyDictionary<Guid, PillRowControl> Rows => rowViews;

    internal SettingRowView Use => use;

    internal PillEditorView? Editor => editor;

    internal Control? Live => slot.Live;

    private Control NewPillRow()
    {
        var plus = LibraryUi.Glyph("plus", 12, ChromeTokens.Accent, SwiftFont.Weight.Medium);
        var text = LibraryUi.Text(MultiAccountWords.NewPill, 13, weight: SwiftFont.Weight.Medium);
        text.Foreground = ChromeTokens.Accent.Brush(text);
        var button = PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 10), Children = { plus, text } }, () => { if (model.Create() is { } made) Edit(made.Id); });
        button.Focusable = true;
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Named(MultiAccountWords.NewPill, "pills-new");
        return button;
    }

    private void Apply()
    {
        var enabled = model.Enabled;
        explainFold.Open = !enabled;
        controls.IsEnabled = enabled;
        dim.Set(enabled ? 1 : 0.45, LibraryUi.Ease(this, LibraryUi.Fade));
        var next = model.Rows;
        foreach (var gone in rowViews.Keys.Where(k => next.All(r => r.Id != k)).ToArray())
        {
            rows.Children.Remove(rowViews[gone]);
            rowViews.Remove(gone);
        }
        for (var i = 0; i < next.Count; i++)
        {
            var row = next[i];
            if (!rowViews.TryGetValue(row.Id, out var control))
            {
                rowViews[row.Id] = control = new PillRowControl(model, row, Edit);
                rows.Children.Insert(Math.Min(i, rows.Children.Count), control);
            }
            else control.Apply(row);
            if (rows.Children.IndexOf(control) != i)
            {
                rows.Children.Remove(control);
                rows.Children.Insert(Math.Min(i, rows.Children.Count), control);
            }
        }
        use.Apply(model.UseRow);
        if (editor is not null && !editor.Model.Exists) Back();
    }

    internal void Edit(Guid id)
    {
        if (!model.Enabled) return;
        editor = new PillEditorView(context, context.Editor(id), Back);
        slot.Show(editor, LibraryUi.Ease(this, LibraryUi.Fade));
    }

    internal void Back()
    {
        editor = null;
        slot.Show(listPage, LibraryUi.Ease(this, LibraryUi.Fade));
    }
}
