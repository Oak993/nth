using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Window.Native;

// The native view gallery: the port's screens as they really render on this Windows PC (a real window, the real GPU, the Acrylic of
// ED-236, the real fonts) with realistic fixture content, saved as PNGs of the window's own pixels at 100 % zoom and the screen's
// real DPI, in the dark and the light theme, to E:\Droppy\work\gallery\<scene>-<theme>.png. It is a review surface, not a gate
// check: every Fact here returns at once (and passes) unless MARROW_GALLERY=1 is set (GalleryRun); the native child's environment is
// an allow-list, so the opt-in and the output folder travel to the child in its request file. Each Fact is one child with its own
// window and a 90 s bound, so no scene depends on another and a Fact holds only as many scenes as fit in that bound. See
// E:\Droppy\work\gallery\README.md for how to run it.
[Collection("WindowManagerNative")]
public sealed class GalleryNativeTests(WindowManagerNativeSession native)
{
    private void Capture(Action<GalleryStage> scenes, string fact) => native.Run(subject => scenes(new GalleryStage(native, subject)), fact, typeof(GalleryNativeTests));

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_welcome_tour_pictures()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryTourScenes.TourFirst, nameof(Gallery_welcome_tour_pictures)); // RR-22: welcome, Hydra, panels, pairs, slider at 2x (GalleryTourScenes)
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_welcome_tour_pictures_continued()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryTourScenes.TourSecond, nameof(Gallery_welcome_tour_pictures_continued)); // RR-22: threads, four themes, recipes at 2x
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_main_window_welcome_page_of_an_empty_library()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryMainScenes.EmptyState, nameof(Gallery_main_window_welcome_page_of_an_empty_library));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_main_window_project_without_an_open_thread()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryMainScenes.NoThreadSelected, nameof(Gallery_main_window_project_without_an_open_thread));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_main_window_long_thread_pages()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryMainScenes.ThreadPages, nameof(Gallery_main_window_long_thread_pages));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_main_window_long_thread_rows_that_open()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryMainScenes.ThreadRows, nameof(Gallery_main_window_long_thread_rows_that_open));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_composer_effort_slider_plain_and_fast_looks()
    {
        if (!GalleryRun.Enabled) return;
        Capture(stage => GalleryComposerScenes.EffortLooks(stage, 0, 3), nameof(Gallery_composer_effort_slider_plain_and_fast_looks));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_composer_effort_slider_supercharged_fusion_and_pair_looks()
    {
        if (!GalleryRun.Enabled) return;
        Capture(stage => GalleryComposerScenes.EffortLooks(stage, 3, 3), nameof(Gallery_composer_effort_slider_supercharged_fusion_and_pair_looks));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_popover_glass_sites()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryPopoverScenes.Sites, nameof(Gallery_popover_glass_sites));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_composer_model_picker_popover()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryComposerScenes.ModelPicker, nameof(Gallery_composer_model_picker_popover));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_sidebar_column_with_projects_threads_heads_and_the_settled_group()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GallerySidebarScenes.Column, nameof(Gallery_sidebar_column_with_projects_threads_heads_and_the_settled_group));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_sidebar_other_presentations()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GallerySidebarScenes.Presentations, nameof(Gallery_sidebar_other_presentations));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_settings_general_appearance_providers_models()
    {
        if (!GalleryRun.Enabled) return;
        Capture(stage => GallerySettingsScenes.PageSlice(stage, 0, 4), nameof(Gallery_settings_general_appearance_providers_models));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_settings_mcp_hydra_source_control()
    {
        if (!GalleryRun.Enabled) return;
        Capture(stage => GallerySettingsScenes.PageSlice(stage, 4, 3), nameof(Gallery_settings_mcp_hydra_source_control));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_settings_shortcuts_archive_about()
    {
        if (!GalleryRun.Enabled) return;
        Capture(stage => GallerySettingsScenes.PageSlice(stage, 7, 3), nameof(Gallery_settings_shortcuts_archive_about));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_command_palette_empty_and_with_a_query()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryOverlayScenes.Palette, nameof(Gallery_command_palette_empty_and_with_a_query));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_usage_popover_and_floating_panel()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryOverlayScenes.Usage, nameof(Gallery_usage_popover_and_floating_panel));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_welcome_tour_first_page()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryOverlayScenes.Tour, nameof(Gallery_welcome_tour_first_page));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_main_window_hydra_head_panel()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryMainScenes.HydraPanel, nameof(Gallery_main_window_hydra_head_panel));
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    public void Gallery_main_window_turn_under_way()
    {
        if (!GalleryRun.Enabled) return;
        Capture(GalleryMainScenes.LiveTurn, nameof(Gallery_main_window_turn_under_way));
    }
}
