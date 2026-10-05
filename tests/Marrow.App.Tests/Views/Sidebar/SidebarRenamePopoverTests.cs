using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift:239-252 renamePopover(for:on:) and :986-1023 RenameThreadPopover;
// DroppyCode/UI/Chrome/Popovers.swift:8-12 presentedChrome. The rename popover's dismissal binding (PR-SIDEBAR-091,
// UI-SIDEBAR-028) and its drawn controls on the mounted popover (UI-SIDEBAR-028, -029, -030): the leftovers pass proved the
// column's geometry, the focus, the trim rule and the Rename and Cancel callbacks, but never raised the popup's own Closed
// and never asserted the controls' fills. A class of its own (TraceabilityTests: one class per file).
[Collection("Headless")]
public sealed class SidebarRenamePopoverTests(HeadlessSession headless)
{
    private static Popup RenamePopup(Control root) =>
        LogicalQuery.SelfAndDescendants<Popup>(root).Single(p => PopoverHost.Content(p) is RenameThreadPopover);

    private static Button Named(Control root, string title) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(b => LogicalQuery.SelfAndDescendants<TextBlock>(b).Any(t => t.Text == title));

    [Fact]
    [Trait("Req", "PR-SIDEBAR-091")] // SidebarView.swift:243-244 `set: { if !$0 { renaming = nil } }`: "clicking away closes the popover and changes nothing / keeps the title"
    [Trait("Req", "UI-SIDEBAR-028")]
    public void Clicking_away_from_the_rename_popover_closes_it_and_keeps_the_title_even_after_the_field_was_edited() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept title", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        object State() => Field(view, "state")!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() => Call(view, "RequestRename", thread)); // the ellipsis path: renaming = thread, text = the title (:354-358)
            Popup popup = null!;
            harness.Main.OnMain(() =>
            {
                Assert.Equal(thread.Id, ((Marrow.Core.Models.ChatThread)Get(State(), "Renaming")!).Id); // asked
                popup = RenamePopup(view);
                Assert.True(recorder.Calls.Last(c => c.Popup == popup).Presented);
                RaisePopup(popup, "Opened");
                var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(PopoverHost.Content(popup)!));
                field.Text = "Something typed and never submitted";
                RaisePopup(popup, "Closed"); // a click away or Escape: the popup's own close
            });
            harness.Main.OnMain(() => { });
            harness.Main.OnMain(() =>
            {
                Assert.Null(Get(State(), "Renaming")); // :244 renaming = nil
                Assert.False(recorder.Calls.Last(c => c.Popup == popup).Presented); // no longer presented
                Assert.Equal("Kept title", model.Thread(thread.Id)!.Title); // nothing was renamed
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-028")] // SidebarView.swift:246 arrowEdge .bottom; Popovers.swift:10-11 presentedChrome: capsule + tint(ThemeManager.spec.accent)
    [Trait("Req", "UI-SIDEBAR-029")] // :1006-1007 TextField("Title", text: $text).textFieldStyle(.roundedBorder)
    [Trait("Req", "UI-SIDEBAR-030")] // :1012-1016 Cancel .bordered; Rename .borderedProminent tinted with the theme accent
    public void The_mounted_rename_popover_points_down_and_draws_a_rounded_border_field_a_bordered_Cancel_and_an_accent_prominent_Rename() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presented = Presentation.Presenter.Override(new RecordingPresenter());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() => Call(view, "RequestRename", thread));
            harness.Main.OnMain(() =>
            {
                Assert.Equal(PlacementMode.Bottom, RenamePopup(view).Placement); // :246 arrowEdge: .bottom
                // The popup's content is built by BuildRenamePopoverContent (headless cannot open the popup itself, so its
                // content is hung on a window, as the leftovers pass does).
                var content = (Control)Call(view, "BuildRenamePopoverContent", thread.Id)!;
                content.HorizontalAlignment = HorizontalAlignment.Left; content.VerticalAlignment = VerticalAlignment.Top;
                var window = new AvaloniaWindow { Content = new Border { Child = content, Width = 360, Height = 200 } };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                try
                {
                    var theme = content.ActualThemeVariant;
                    // UI-SIDEBAR-029: the Title field carries the RoundedBorder editor chrome (ChatDialogControls.TextEditor).
                    var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(content));
                    Assert.Equal("RoundedBorder", field.GetValue(ChatDialogControls.InputStyleProperty));
                    var fieldBezel = field.GetVisualDescendants().OfType<Border>().First(b => b.CornerRadius == new CornerRadius(6));
                    Assert.Equal(new Thickness(1), fieldBezel.BorderThickness);
                    Assert.Equal(ChatDialogControls.FieldFill.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(fieldBezel.Background).Color);
                    Assert.Equal(ChatDialogControls.FieldStroke.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(fieldBezel.BorderBrush).Color);
                    // UI-SIDEBAR-030: Rename is .borderedProminent (the accent fill, a white label), Cancel .bordered (the standard fill).
                    var rename = Named(content, "Rename"); var cancel = Named(content, "Cancel");
                    var renameBezel = rename.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Bezel");
                    var cancelBezel = cancel.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Bezel");
                    Assert.Equal(Chrome.Accent.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(renameBezel.Background).Color);
                    Assert.Equal(SwiftColor.White.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(LogicalQuery.SelfAndDescendants<TextBlock>(rename).Single().Foreground).Color);
                    Assert.Equal(SystemButtons.StandardFill.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(cancelBezel.Background).Color);
                    Assert.Equal(SystemButtons.LabelPrimary.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(LogicalQuery.SelfAndDescendants<TextBlock>(cancel).Single().Foreground).Color);
                    Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(renameBezel.Background).Color, Assert.IsAssignableFrom<ISolidColorBrush>(cancelBezel.Background).Color);
                }
                finally { window.Close(); }
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });
}
