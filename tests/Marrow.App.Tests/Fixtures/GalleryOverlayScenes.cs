using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Tour;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// The windows and panels that open over the main window: the command palette (empty, and with a query typed), the usage panel (its floating
// form; see the note in Usage), and the welcome tour.
internal static class GalleryOverlayScenes
{
    internal static void Palette(GalleryStage stage)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (_, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            stage.Native.OnUi(() => model.IsCommandPalettePresented = true);
            Thread.Sleep(1200);
            stage.Shoot(window, "command-palette", label, 1000);
            stage.Type("dark");
            Thread.Sleep(900);
            stage.Shoot(window, "command-palette-query", label, 1000);
            stage.Native.OnUi(() => model.IsCommandPalettePresented = false);
            Thread.Sleep(700);
        });
    }

    internal static void Usage(GalleryStage stage)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (_, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.Native.OnUi(() => PublishClaudeLimits(model));
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            // NOT DRIVEN: a click on the composer's usage ring opens its popover, whose UsagePanel reads the AppModel from its environment, which
            // that popover's content (ComposerView.cs:1403-1413) never receives; the real click throws InvalidOperationException ("No AppModel is
            // attached ...", UsagePanel.cs:115) on the UI thread, so the gallery cannot capture it. The same panel is shown by its floating form.
            // "Open as a floating panel beside the chat": the setting the popover's arrow button turns on
            stage.Native.OnUi(() => model.Settings.ShowsUsagePanel = true);
            Thread.Sleep(1500);
            stage.Shoot(window, "usage-floating-panel", label, 1000, 3000, animated: true);
            stage.Native.OnUi(() => model.Settings.ShowsUsagePanel = false);
            Thread.Sleep(900);
        });
    }

    // The plan windows a signed-in Claude shows (UsageViews.swift's limit rows): published to the registry as a fetch would have.
    private static void PublishClaudeLimits(AppModel model)
    {
        var now = SwiftDate.Now;
        var limits = new PlanLimits
        {
            PlanName = "Max 5x",
            Windows = new ValueList<PlanLimits.Window>([
                new PlanLimits.Window { Id = "five_hour", Title = "5-hour window", Percent = 34, ResetsAt = now.AddHours(2.4) },
                new PlanLimits.Window { Id = "seven_day", Title = "Weekly", Percent = 58, ResetsAt = now.AddDays(3.2) },
                new PlanLimits.Window { Id = "seven_day_opus", Title = "Opus weekly", Percent = 21, ResetsAt = now.AddDays(3.2) },
            ]),
        };
        var property = typeof(ProviderRegistry).GetProperty(nameof(ProviderRegistry.PlanLimits))!;
        var current = (ValueMap<ProviderKind, PlanLimits>)property.GetValue(model.Providers)!;
        property.SetValue(model.Providers, current.Mutating(map => map[ProviderKind.Claude] = limits));
    }

    internal static void Tour(GalleryStage stage)
    {
        stage.Begin();
        var (manager, model) = stage.Native.OnUi(() =>
        {
            var manager = new Marrow.App.Window.WindowManager();
            GalleryContent.Equip(manager.Model, null);
            return (manager, manager.Model);
        }, TimeSpan.FromSeconds(40));
        var tour = stage.Native.OnUi(() => TourWindowController.Shared.Present(Marrow.App.Views.Tour.Tour.Pages), TimeSpan.FromSeconds(40));
        stage.Register(tour);
        GC.KeepAlive(manager);
        stage.ForEachTheme(model, tour, label =>
        {
            Thread.Sleep(1200);
            stage.Shoot(tour, "tour-page-1", label, 1500);
        });
    }
}
