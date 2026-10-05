using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using System.Linq;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/WallpaperCard.swift (B3 52bc7926), :1-190.
[Collection("Headless")]
public sealed class WallpaperCardTests(HeadlessSession headless)
{
    private static void Frame(WallpaperCardScope scope, double seconds)
    {
        var span=TimeSpan.FromTicks((long)Math.Round(seconds*TimeSpan.TicksPerSecond));
        scope.Time.Advance(span); scope.Frames.Advance(span); scope.Pump();
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-142")] // App/Views/Settings/WallpaperCard.swift:80-90
    public void Drag_wash_fades_in_and_out_along_snappy_point_two() => headless.OnUiThread(() => {
        using var scope=new WallpaperCardScope();
        var button=CardQuery.All<Button>(scope.Card).First(); var content=(Panel)button.Content!;
        var data=scope.Drag(PictureFiles.Text(PictureFiles.Folder(),"n.txt"));
        button.RaiseEvent(new DragEventArgs(DragDrop.DragEnterEvent,data,button,new Point(10,10),KeyModifiers.None)); scope.Pump();
        var wash=content.Children.OfType<Border>().Single(b=> b.Background is SolidColorBrush brush && brush.Color==Chrome.Accent.Opacity(.18).Resolve(b.ActualThemeVariant));
        Assert.Equal(0,wash.Opacity); Frame(scope,.05);
        Assert.Equal(Animation.Snappy(.2).Progress(.05),wash.Opacity,5);
        scope.Settle(); Assert.Equal(1,wash.Opacity);
        button.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent,data,button,new Point(10,10),KeyModifiers.None)); scope.Pump();
        Assert.Contains(wash,content.Children); Frame(scope,.05);
        Assert.Equal(1-Animation.Snappy(.2).Progress(.05),wash.Opacity,5);
        scope.Settle(); Assert.DoesNotContain(wash,content.Children);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-141")] // App/Views/Settings/WallpaperCard.swift:59,74,91
    public void Picture_arrival_and_removal_use_the_cards_own_spring() => headless.OnUiThread(() => {
        using var scope=new WallpaperCardScope();
        var button=CardQuery.All<Button>(scope.Card).First(); var content=(Panel)button.Content!; var swap=(Panel)content.Children[0];
        scope.Store.Install(PictureFiles.Png(PictureFiles.Folder(),"a.png")); scope.WaitFor(()=>!scope.Store.IsInstalling);
        var picture=swap.Children[0]; Assert.Equal(0,picture.Opacity); Frame(scope,.05);
        var progress=Animation.Spring(.45,.2).Progress(.05); Assert.Equal(progress,picture.Opacity,5);
        Assert.Equal(.96+.04*progress,CardQuery.ScaleTo(picture,swap),5);
        scope.Settle(); scope.Store.Remove(); scope.Pump();
        var empty=swap.Children[0]; Assert.Equal(0,empty.Opacity); Frame(scope,.05);
        Assert.Equal(progress,empty.Opacity,5); Assert.Equal(1-progress,picture.Opacity,5);
        Assert.Equal(.96+.04*progress,CardQuery.ScaleTo(empty,swap),5);
        scope.Settle(); Assert.DoesNotContain(picture,swap.Children); Assert.Equal(1,empty.Opacity);
    });
    // PR-SETTINGS-140: card structure, softness row only while a wallpaper is set.

    [Fact]
    [Trait("Req", "PR-SETTINGS-140")] // WallpaperCard.swift:13-23
    [Trait("Req", "UI-THEME-021")] // WallpaperCard.swift:13-22 the softness row is absent entirely, not disabled
    public void With_no_wallpaper_the_card_has_no_softness_row() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        Assert.Empty(CardQuery.TextBlocks(scope.Card, "Softness"));
        Assert.Empty(CardQuery.TextBlocks(scope.Card, "Blurs the picture so text stays easy to read"));
        Assert.Single(CardQuery.TextBlocks(scope.Card, "Wallpaper"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-140")] // WallpaperCard.swift:18-21
    [Trait("Req", "UI-THEME-017")] // WallpaperCard.swift:104-123
    public void Installing_a_wallpaper_adds_the_softness_row_and_removing_it_takes_it_away_entirely() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));
        Assert.Single(CardQuery.TextBlocks(scope.Card, "Softness"));

        scope.Store.Remove();
        scope.Pump();
        Assert.Empty(CardQuery.TextBlocks(scope.Card, "Softness"));
    });

    // PR-SETTINGS-141, PR-THEME-069, UI-SETTINGS-026, UI-THEME-015: empty preview state.

    [Fact]
    [Trait("Req", "PR-SETTINGS-141")] // WallpaperCard.swift:60-75
    [Trait("Req", "PR-THEME-069")] // WallpaperCard.swift:60-74
    [Trait("Req", "UI-SETTINGS-026")] // WallpaperCard.swift:25-102
    [Trait("Req", "UI-THEME-015")] // WallpaperCard.swift:60-75
    public void With_no_wallpaper_the_preview_shows_the_invitation_sentence_once() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var texts = CardQuery.TextBlocks(scope.Card, "Drop a picture here, or choose one");
        Assert.Single(texts);
        Assert.Empty(CardQuery.TextBlocks(scope.Card, "Drop a picture here, or choose one.")); // no full stop
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-141")] // WallpaperCard.swift:25-102
    [Trait("Req", "UI-THEME-013")] // WallpaperCard.swift:25-102
    public void The_preview_well_is_150_tall_and_stretches_the_card_width() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var well = CardQuery.All<Panel>(scope.Card).First(p => p.Height == 150);
        scope.Pump();
        Assert.Equal(150, well.Bounds.Height);
        Assert.Equal(560, well.Bounds.Width); // scope width 592 less the 16pt inset on each side
    });

    // PR-THEME-068, UI-THEME-014: mock window geometry over an installed picture.

    [Fact]
    [Trait("Req", "PR-THEME-068")] // WallpaperCard.swift:35-58
    [Trait("Req", "UI-THEME-014")] // WallpaperCard.swift:35-58
    public void The_mock_window_over_an_installed_picture_is_60_percent_of_the_well_centred() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var well = CardQuery.All<Panel>(scope.Card).First(p => p.Height == 150);
        var dots = CardQuery.All<StackPanel>(scope.Card).First(s => s.Children.Count == 3 && s.Children[0] is Avalonia.Controls.Shapes.Ellipse);
        var mock = (Panel)dots.Parent!;
        Assert.Equal(560 * 0.6, mock.Bounds.Width, 3);
        Assert.Equal(150 * 0.6, mock.Bounds.Height, 3);

        var wellCentre = CardQuery.Centre(well, well);
        var mockCentre = CardQuery.Centre(mock, well);
        Assert.Equal(wellCentre.X, mockCentre.X, 1);
        Assert.Equal(wellCentre.Y, mockCentre.Y, 1);
    });

    [Fact]
    [Trait("Req", "PR-THEME-068")] // WallpaperCard.swift:39-42
    public void The_mock_window_carries_the_glass_effect_as_data_ED_180() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var effects = CardQuery.All<Border>(scope.Card)
            .Select(SwiftGlass.GetEffect)
            .Where(e => e is not null && e.Shape.Kind == GlassShapeKind.RoundedRectangle && e.Shape.CornerRadius == 8)
            .ToList();
        var effect = Assert.Single(effects);
        Assert.Equal(RoundedCornerStyle.Continuous, effect!.Shape.Style);
    });

    // PR-SETTINGS-142, PR-THEME-070: drag and drop.

    [Fact]
    [Trait("Req", "PR-SETTINGS-142")] // WallpaperCard.swift:95-99
    [Trait("Req", "PR-THEME-070")] // WallpaperCard.swift:95-99
    public void Dropping_files_installs_the_first_accepted_picture_and_ignores_the_rest() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        var text = PictureFiles.Text(folder, "notes.txt");
        var picture = PictureFiles.Png(folder, "a.png");
        var picture2 = PictureFiles.Png(folder, "b.png", side: 13);
        var button = CardQuery.All<Button>(scope.Card).First();

        var data = scope.Drag(text, picture, picture2);
        button.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, button, new Point(10, 10), KeyModifiers.None));
        scope.WaitFor(() => !scope.Store.IsInstalling && scope.Store.HasWallpaper);

        Assert.True(scope.Store.HasWallpaper);
        Assert.Equal(new Size(8,8),scope.Store.Image!.Size); // :96 first acceptable a.png, not the 13px b.png.
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-142")] // WallpaperCard.swift:96 `else { return false }`
    [Trait("Req", "PR-THEME-070")] // failure_cases: no acceptable file, drop refused, silent
    public void Dropping_only_non_pictures_installs_nothing_and_sets_no_error() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        var text = PictureFiles.Text(folder, "notes.txt");
        var button = CardQuery.All<Button>(scope.Card).First();

        var data = scope.Drag(text);
        var args = new DragEventArgs(DragDrop.DropEvent, data, button, new Point(10, 10), KeyModifiers.None);
        button.RaiseEvent(args);
        scope.Pump();

        Assert.False(scope.Store.HasWallpaper);
        Assert.Null(scope.Store.LastError);
        Assert.False(args.Handled);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-142")] // WallpaperCard.swift:89-90 scaleEffect(isTargeted ? 1.02 : 1)
    [Trait("Req", "UI-SETTINGS-026")] // states: Drag targeted, scaled to 1.02
    public void A_drag_entering_the_preview_scales_it_to_1_02_and_leaving_returns_it_to_1() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var button = CardQuery.All<Button>(scope.Card).First();
        var well = (Control)button.Content!;
        var scale = Assert.IsType<ScaleTransform>(well.RenderTransform);
        Assert.Equal(1, scale.ScaleX);

        var data = scope.Drag(PictureFiles.Text(PictureFiles.Folder(), "n.txt"));
        button.RaiseEvent(new DragEventArgs(DragDrop.DragEnterEvent, data, button, new Point(10, 10), KeyModifiers.None));
        scope.Settle();
        Assert.Equal(1.02, scale.ScaleX, 3);

        button.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, data, button, new Point(10, 10), KeyModifiers.None));
        scope.Settle();
        Assert.Equal(1, scale.ScaleX, 3);
    });

    // PR-SETTINGS-143, PR-THEME-072, PR-THEME-073: the picture chooser.

    [Fact]
    [Trait("Req", "PR-SETTINGS-143")] // WallpaperCard.swift:168-189
    [Trait("Req", "PR-THEME-072")] // WallpaperCard.swift:26, :93-94
    [Trait("Req", "PR-THEME-073")] // WallpaperCard.swift:168-189
    public void Clicking_the_preview_opens_the_chooser_and_installs_the_chosen_picture() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        var path = PictureFiles.Png(folder, "a.png");
        scope.Dialog.NextResult = path;

        var button = CardQuery.All<Button>(scope.Card).First();
        CardQuery.Invoke(button);
        scope.WaitFor(() => scope.Dialog.CallCount > 0);
        scope.WaitFor(() => !scope.Store.IsInstalling);

        Assert.Equal(1, scope.Dialog.CallCount);
        Assert.True(scope.Store.HasWallpaper);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-143")] // failure_cases: Cancelled: no install, no error, no change.
    public void Cancelling_the_chooser_installs_nothing() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        scope.Dialog.NextResult = null; // :182-187 no OK response

        var button = CardQuery.All<Button>(scope.Card).First();
        CardQuery.Invoke(button);
        scope.WaitFor(() => scope.Dialog.CallCount > 0);
        scope.Pump();

        Assert.False(scope.Store.HasWallpaper);
        Assert.Null(scope.Store.LastError);
    });

    // PR-THEME-071, UI-THEME-016: install progress indicator.

    [Fact]
    [Trait("Req", "PR-THEME-071")] // WallpaperCard.swift:76-79, :116, :121
    [Trait("Req", "UI-THEME-016")] // WallpaperCard.swift:76-79
    public void An_indeterminate_progress_indicator_shows_only_while_installing_and_disables_the_buttons() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var progress = CardQuery.All<ProgressBar>(scope.Card).First();
        Assert.False(progress.IsVisible);

        var folder = PictureFiles.Folder();
        var path = PictureFiles.Png(folder, "a.png");
        // Hold the completion hop, so decode speed and UI-thread scheduling cannot erase this state.
        using var completion = new MainExecutorGate(AvaloniaMainExecutor.Instance);
        using var main = Marrow.Core.Port.MainActor.Executor.Override(completion);
        scope.Store.Install(from: path);
        scope.Pump();

        Assert.True(progress.IsVisible);
        Assert.True(progress.IsIndeterminate);
        var choose = Assert.Single(CardQuery.ButtonsLabelled(scope.Card, "Choose…"));
        Assert.False(choose.IsEnabled);

        completion.Release();
        scope.WaitFor(() => !scope.Store.IsInstalling);
        Assert.False(progress.IsVisible);
    });

    // PR-SETTINGS-144, PR-THEME-074, UI-SETTINGS-027, UI-THEME-017: the Wallpaper row's detail and buttons.

    [Fact]
    [Trait("Req", "PR-SETTINGS-144")] // WallpaperCard.swift:104-124
    [Trait("Req", "UI-THEME-017")] // WallpaperCard.swift:104-123
    [Trait("Req", "UI-THEME-018")] // WallpaperCard.swift:112-117 Remove absent with no wallpaper
    [Trait("Req", "UI-THEME-019")] // WallpaperCard.swift:118-121 title is "Choose…" with no wallpaper
    public void The_wallpaper_row_detail_is_the_no_wallpaper_sentence_by_default() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        Assert.Single(CardQuery.TextBlocks(scope.Card,
            "A picture of your own behind the glass, mixed with the desktop by the transparency slider"));
        Assert.Single(CardQuery.ButtonsLabelled(scope.Card, "Choose…"));
        Assert.Empty(CardQuery.ButtonsLabelled(scope.Card, "Remove"));
        // U+2026, never three full stops.
        Assert.Empty(CardQuery.ButtonsLabelled(scope.Card, "Choose..."));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-144")] // WallpaperCard.swift:107-109
    [Trait("Req", "PR-THEME-074")] // WallpaperCard.swift:104-123
    [Trait("Req", "UI-SETTINGS-027")] // WallpaperCard.swift:104-124
    [Trait("Req", "UI-THEME-018")] // WallpaperCard.swift:112-117 Remove present with a wallpaper installed
    [Trait("Req", "UI-THEME-019")] // WallpaperCard.swift:118-121 title is "Change…" with a wallpaper installed
    public void With_a_wallpaper_the_detail_and_buttons_switch_to_the_installed_state() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        Assert.Single(CardQuery.TextBlocks(scope.Card,
            "Fills the window edge to edge under the glass; the transparency slider lets the desktop show through it"));
        Assert.Single(CardQuery.ButtonsLabelled(scope.Card, "Change…"));
        Assert.Single(CardQuery.ButtonsLabelled(scope.Card, "Remove"));
        Assert.Empty(CardQuery.ButtonsLabelled(scope.Card, "Choose…"));
        // U+2026, never three full stops.
        Assert.Empty(CardQuery.ButtonsLabelled(scope.Card, "Change..."));
    });

    [Fact]
    [Trait("Req", "UI-THEME-018")] // WallpaperCard.swift:112-117 click deletes the wallpaper immediately, with no confirmation
    public void Clicking_Remove_deletes_immediately_with_no_confirmation_dialog() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var before = CardQuery.All<Avalonia.Controls.Window>(scope.Card).Count();
        var remove = CardQuery.ButtonsLabelled(scope.Card, "Remove").Single();
        CardQuery.Invoke(remove);
        scope.Pump();

        Assert.False(scope.Store.HasWallpaper); // deleted at once, no intervening dialog
        Assert.Equal(before, CardQuery.All<Avalonia.Controls.Window>(scope.Card).Count());
    });

    [Fact]
    [Trait("Req", "UI-THEME-018")] // WallpaperCard.swift:116 .disabled(store.isInstalling)
    [Trait("Req", "UI-THEME-019")] // WallpaperCard.swift:121 .disabled(store.isInstalling)
    public void Both_row_buttons_are_disabled_while_an_install_is_running() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var change = CardQuery.ButtonsLabelled(scope.Card, "Change…").Single();
        var remove = CardQuery.ButtonsLabelled(scope.Card, "Remove").Single();
        Assert.True(change.IsEnabled);
        Assert.True(remove.IsEnabled);

        var large = PictureFiles.Png(folder, "b.png", side: 512); // installing stays on screen one turn
        scope.Store.Install(from: large);
        scope.Pump();

        Assert.False(change.IsEnabled);
        Assert.False(remove.IsEnabled);

        scope.WaitFor(() => !scope.Store.IsInstalling);
        Assert.True(change.IsEnabled);
        Assert.True(remove.IsEnabled);
    });

    [Fact]
    [Trait("Req", "UI-THEME-019")] // WallpaperCard.swift:118-121 click opens the picture chooser
    public void Clicking_the_Choose_button_opens_the_chooser_and_installs_the_result() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        var path = PictureFiles.Png(folder, "a.png");
        scope.Dialog.NextResult = path;

        var choose = CardQuery.ButtonsLabelled(scope.Card, "Choose…").Single();
        CardQuery.Invoke(choose);
        scope.WaitFor(() => scope.Dialog.CallCount > 0);
        scope.WaitFor(() => !scope.Store.IsInstalling);

        Assert.Equal(1, scope.Dialog.CallCount);
        Assert.True(scope.Store.HasWallpaper);
    });

    // UI-THEME-020: the dialog's title and OK-button caption, byte-for-byte, as WallpaperCard.swift:174-175 sets
    // them on NSOpenPanel and WindowsWallpaperFileDialog carries them to IFileDialog::SetTitle/SetOkButtonLabel.
    // No test opens the real system dialog (R-TEST-10); the strings are asserted directly off the seam's own
    // constants instead, which is all that can be verified automatically under this rule.

    [Fact]
    [Trait("Req", "UI-THEME-020")] // WallpaperCard.swift:174 panel.message = "Choose a picture for the window"
    public void The_chooser_seam_carries_the_exact_message_as_its_title()
    {
        Assert.Equal("Choose a picture for the window", Marrow.App.Port.WindowsWallpaperFileDialog.Title);
    }

    [Fact]
    [Trait("Req", "UI-THEME-020")] // WallpaperCard.swift:175 panel.prompt = "Use as Wallpaper"
    public void The_chooser_seam_carries_the_exact_confirm_button_caption()
    {
        Assert.Equal("Use as Wallpaper", Marrow.App.Port.WindowsWallpaperFileDialog.OkButtonLabel);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-144")] // WallpaperCard.swift:104-107, failure_cases
    [Trait("Req", "UI-THEME-017")] // states: detail is the last error sentence when one is set
    public void An_install_failure_shows_the_error_sentence_in_the_row_detail() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        var broken = PictureFiles.BrokenPicture(folder, "broken.png");
        scope.Store.Install(from: broken);
        scope.WaitFor(() => !scope.Store.IsInstalling);

        Assert.Equal("Could not use that picture.", scope.Store.LastError);
        Assert.Single(CardQuery.TextBlocks(scope.Card, "Could not use that picture."));
        Assert.Empty(CardQuery.TextBlocks(scope.Card,
            "A picture of your own behind the glass, mixed with the desktop by the transparency slider"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-144")] // WallpaperCard.swift:113 Remove
    public void Clicking_Remove_removes_the_wallpaper_and_clears_the_error() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var remove = CardQuery.ButtonsLabelled(scope.Card, "Remove").Single();
        CardQuery.Invoke(remove);
        scope.Settle();

        Assert.False(scope.Store.HasWallpaper);
        Assert.Null(scope.Store.LastError);
    });

    // PR-SETTINGS-145, PR-THEME-075, PR-THEME-076, PR-THEME-077, UI-SETTINGS-028, UI-THEME-... : the softness row.

    [Fact]
    [Trait("Req", "PR-SETTINGS-145")] // WallpaperCard.swift:126-166
    [Trait("Req", "PR-THEME-075")] // WallpaperCard.swift:126-144
    [Trait("Req", "UI-SETTINGS-028")] // WallpaperCard.swift:126-166
    [Trait("Req", "UI-THEME-021")] // WallpaperCard.swift:126-127 title "Softness", fixed detail sentence
    [Trait("Req", "UI-THEME-022")] // WallpaperCard.swift:133-139 small control size, width 140, range 0...1
    public void The_softness_slider_is_140_points_wide_with_sharp_and_blurred_tooltips() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var slider = CardQuery.All<Slider>(scope.Card).Single();
        Assert.Equal(140, slider.Width);
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(1, slider.Maximum);
        Assert.Single(CardQuery.WithTip(scope.Card, "Sharp"));
        Assert.Single(CardQuery.WithTip(scope.Card, "Blurred"));
        Assert.Single(CardQuery.TextBlocks(scope.Card, "Softness"));
        Assert.Single(CardQuery.TextBlocks(scope.Card, "Blurs the picture so text stays easy to read"));
    });

    [Fact]
    [Trait("Req", "UI-THEME-023")] // WallpaperCard.swift:129-132 the sharp-end glyph: font, colour, tooltip
    [Trait("Req", "UI-THEME-024")] // WallpaperCard.swift:140-143 the blurred-end glyph: font, colour, tooltip
    public void The_two_drop_glyphs_are_subheadline_size_in_Chrome_secondaryText() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var sharp = CardQuery.All<SwiftImage>(scope.Card).Single(i => i.SymbolName == "drop"); // :129
        var blurred = CardQuery.All<SwiftImage>(scope.Card).Single(i => i.SymbolName == "drop.fill"); // :140

        var expectedFont = SwiftFont.System(SwiftFont.TextStyle.Subheadline); // :130, :141; ED-059: 11pt
        Assert.Equal(expectedFont, SwiftImage.GetFont(sharp));
        Assert.Equal(expectedFont, SwiftImage.GetFont(blurred));
        Assert.Equal(11, expectedFont.PointSize);

        var expectedColor = Chrome.SecondaryText.Resolve(sharp.ActualThemeVariant); // Chrome.swift:79
        Assert.Equal(expectedColor, Assert.IsType<SolidColorBrush>(sharp.Foreground).Color);
        Assert.Equal(expectedColor, Assert.IsType<SolidColorBrush>(blurred.Foreground).Color);

        Assert.Equal("Sharp", ToolTip.GetTip(sharp)); // :132
        Assert.Equal("Blurred", ToolTip.GetTip(blurred)); // :143
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-145")] // WallpaperCard.swift:134 set { store.softness = $0 }
    public void Moving_the_slider_writes_softness_to_the_store() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var slider = CardQuery.All<Slider>(scope.Card).Single();
        slider.Value = 0.4;
        scope.Pump();

        Assert.Equal(0.4, scope.Store.Softness, 3);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-145")] // WallpaperCard.swift:136 onEditingChanged
    public void Beginning_and_ending_the_drag_toggles_isAdjustingSoftness() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        // No theme is registered in this test assembly's headless Application (HeadlessFixtures.TestApp), so the
        // Slider's default template (and its Thumb) never materialises; the handler is attached directly to the
        // Slider and listens by RoutingStrategies.Bubble, so raising the same routed event AT the Slider (as its
        // own Thumb would bubble it) exercises the identical handler WallpaperCard.cs attaches.
        var slider = CardQuery.All<Slider>(scope.Card).Single();
        Assert.False(scope.Store.IsAdjustingSoftness);

        slider.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragStartedEvent, Vector = new Vector(0, 0) });
        scope.Pump();
        Assert.True(scope.Store.IsAdjustingSoftness);

        slider.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragCompletedEvent, Vector = new Vector(0, 0) });
        scope.Pump();
        Assert.False(scope.Store.IsAdjustingSoftness);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-145")] // WallpaperCard.swift:11, :145-161
    [Trait("Req", "PR-THEME-076")] // WallpaperCard.swift:145-161
    [Trait("Req", "UI-THEME-025")] // WallpaperCard.swift:11, :145-162 threshold, click action, tooltip
    public void The_reset_appears_only_once_softness_passes_0_005_and_sets_it_to_0() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        Assert.Empty(CardQuery.WithTip(scope.Card, "Back to sharp"));

        scope.Store.Softness = 0.005;
        scope.Pump();
        Assert.Empty(CardQuery.WithTip(scope.Card, "Back to sharp")); // :11 `> 0.005`, not `>=`

        scope.Store.Softness = 0.006;
        scope.Pump();
        var reset = Assert.Single(CardQuery.WithTip(scope.Card, "Back to sharp"));
        var button = Assert.IsAssignableFrom<Button>(reset);
        CardQuery.Invoke(button);
        scope.Pump();

        Assert.Equal(0, scope.Store.Softness);
        scope.Settle();
        Assert.Empty(CardQuery.WithTip(scope.Card, "Back to sharp"));
    });

    [Fact]
    [Trait("Req", "UI-THEME-025")] // WallpaperCard.swift:150-155 22x22 rectangular hit shape, offset(x: -28)
    public void The_reset_button_is_22_by_22_offset_28_points_to_the_left() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));
        scope.Store.Softness = 0.5;
        scope.Pump();

        var reset = Assert.IsAssignableFrom<Button>(Assert.Single(CardQuery.WithTip(scope.Card, "Back to sharp")));
        var frame = Assert.IsType<Border>(reset.Content);
        Assert.Equal(22, frame.Width); // :153
        Assert.Equal(22, frame.Height); // :153
        Assert.Equal(-28, frame.Margin.Left); // :158 .offset(x: -28)
    });

    [Fact]
    [Trait("Req", "UI-THEME-025")] // WallpaperCard.swift:150-152 subheadline weight medium, Chrome.secondaryText
    public void The_reset_glyph_is_subheadline_medium_weight_in_Chrome_secondaryText() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));
        scope.Store.Softness = 0.5;
        scope.Pump();

        var reset = Assert.IsAssignableFrom<Button>(Assert.Single(CardQuery.WithTip(scope.Card, "Back to sharp")));
        var icon = Assert.IsType<SwiftImage>(Assert.IsType<Border>(reset.Content).Child);
        Assert.Equal("arrow.counterclockwise", icon.SymbolName); // :150

        var expectedFont = SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Medium); // :151
        Assert.Equal(expectedFont, SwiftImage.GetFont(icon));

        var expectedColor = Chrome.SecondaryText.Resolve(icon.ActualThemeVariant); // :152, Chrome.swift:79
        Assert.Equal(expectedColor, Assert.IsType<SolidColorBrush>(icon.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "PR-THEME-077")] // WallpaperCard.swift:163-164
    public void The_softness_control_is_one_accessibility_group_labelled_wallpaper_softness() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var folder = PictureFiles.Folder();
        scope.InstallAndSettle(PictureFiles.Png(folder, "a.png"));

        var named = CardQuery.Named(scope.Card, "Wallpaper softness");
        Assert.Single(named);
    });

    // Depends on Marrow.App.Theme.WallpaperStore.Accepts (Wallpaper.swift:98-108), already proven by
    // Marrow.App.Tests.Theme.WallpaperTests; PR-SETTINGS-142/PR-THEME-070's "first accepted" behaviour above proves
    // WallpaperCard's own use of it.
}
