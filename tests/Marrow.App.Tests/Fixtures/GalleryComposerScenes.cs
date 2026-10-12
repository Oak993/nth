using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// The scenes of the composer's model chip: its popover with the effort slider in each look, and the model picker. A person opens
// them by clicking the chip at the foot of the thread, so the scenes do the same, with the real pointer.
internal static class GalleryComposerScenes
{
    // The five looks of the track (EffortSlider.swift:969-1000): the plain levels, fast, supercharged (the top level) solo, the top
    // level with fast (fusion), and a pair's blend. The Claude Opus model has five levels and a fast tier.
    private static readonly (string Name, string Effort, bool Fast, bool Pair)[] Looks =
    [
        ("plain-low", "low", false, false), ("plain-high", "high", false, false), ("fast", "medium", true, false),
        ("supercharged", "max", false, false), ("fusion", "max", true, false), ("pair", "high", false, true),
    ];

    /// <summary>The effort slider's popover in the named looks (a slice of the five-plus-pair list), in each theme.</summary>
    internal static void EffortLooks(GalleryStage stage, int skip, int take)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (_, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            foreach (var look in Looks.Skip(skip).Take(take))
            {
                stage.Native.OnUi(() => Apply(model, data, look.Effort, look.Fast, look.Pair));
                Thread.Sleep(600);
                OpenChip(stage, window);
                stage.Shoot(window, "composer-effort-" + look.Name, label, 1200, 2500, GalleryCrop.PopupsOnly, animated: true);
                if (look.Name == "supercharged") stage.Shoot(window, "composer-effort-popover-in-window", label, 600, 2500, GalleryCrop.WithPopups, animated: true);
                CloseChip(stage, window);
            }
        });
    }

    /// <summary>The model picker: the chip's popover turned to its list of models and the pairs (a click on the title).</summary>
    internal static void ModelPicker(GalleryStage stage)
    {
        stage.Begin();
        var data = GalleryMainScenes.Library(stage);
        var (_, window, model) = GalleryMainScenes.Open(stage, data, _ => data.Main.Id);
        stage.Register(window);
        stage.ForEachTheme(model, window, label =>
        {
            Thread.Sleep(1500);
            stage.Native.OnUi(() => Apply(model, data, "high", false, false));
            Thread.Sleep(600);
            OpenChip(stage, window);
            var modelButton = ModelButton(stage);
            stage.Click(stage.CenterOf(modelButton));
            Thread.Sleep(1200);
            stage.Shoot(window, "composer-model-picker", label, 1000, 4000, GalleryCrop.PopupsOnly);
            stage.Shoot(window, "composer-model-picker-in-window", label, 600, 4000, GalleryCrop.WithPopups);
            CloseChip(stage, window);
        });
    }

    /// <summary>The model button of the open chip popover, found by its identity (the "Choose a model" tooltip, as the interaction recorder does). Throws when it is missing, so a rename can never silently skip a scene.</summary>
    internal static Button ModelButton(GalleryStage stage) =>
        stage.Native.OnUi(() => stage.Popups.Open.Select(popup => popup.Child).OfType<Control>()
            .SelectMany(child => child.GetVisualDescendants().OfType<Button>())
            .FirstOrDefault(button => Equals(ToolTip.GetTip(button), "Choose a model")))
        ?? throw new InvalidOperationException("The open composer popover has no \"Choose a model\" button; the model-picker scenes cannot run.");

    // The thread's effort, fast mode and pair as a look needs them (through the model's own update, as the slider does).
    private static void Apply(AppModel model, GalleryData data, string effort, bool fast, bool pair)
    {
        var id = data.Main.Id;
        if (pair) model.EnterHydraPair(data.Pair, id);
        else model.LeaveHydraPair(id);
        model.UpdateThread(id, thread => thread with { Effort = effort, FastMode = fast });
    }

    private static void OpenChip(GalleryStage stage, AvaloniaWindow window)
    {
        var chip = stage.Find<ModelEffortButton>(window, _ => true) ?? throw new InvalidOperationException("The composer has no model chip.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            stage.Click(stage.CenterOf(chip));
            Thread.Sleep(1400); // the popover grows in from its arrow
            if (stage.Native.OnUi(() => stage.Popups.Open.Count) > 0) return;
        }
        throw new InvalidOperationException("The model chip did not open its popover.");
    }

    private static void CloseChip(GalleryStage stage, AvaloniaWindow window)
    {
        stage.Escape();
        Thread.Sleep(400);
        if (stage.Native.OnUi(() => stage.Popups.Open.Count) == 0) return;
        var chip = stage.Find<ModelEffortButton>(window, _ => true)!;
        stage.Click(stage.CenterOf(chip));
        Thread.Sleep(600);
    }
}
