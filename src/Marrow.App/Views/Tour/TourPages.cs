using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Views.Tour;

// Source: DroppyCode/App/Views/Tour/TourPages.swift (B3 52bc792), :1-29.
// The Windows welcome title and descriptions follow OD-0012.
public static class Tour
{
    // TourPages.swift:4-13
    // ED-NEW-sidebarmodes-11: built at each read (the tour reads it as it opens), so the Toggle sidebar chord on page 5 is the one the
    // owner has set, not a Ctrl+B written into the words.
    private static ValueList<TourPage> Build() => new(new[]
    {
        // DEVIATION OD-0012: the Windows welcome title and description are truthful to the port.
        new TourPage("tour-welcome", "Welcome to nth", "One native Windows window for every coding agent you use. Claude, Codex, Gemini, Copilot and more, each in its own thread, on the same glass."),
        new TourPage("tour-hydra", "Meet Hydra", "One chat, many heads. The lead writes the briefs, sends helper agents out in parallel, each in a copy of your project, and their work lands back in your checkout."),
        new TourPage("tour-pairs", "Pair a strong lead with quick heads", "Put a Claude lead over Gemini or OpenCode heads. Pick the pair from the model picker, cap how many heads run at once, and give each side its own effort."),
        new TourPage("tour-slider", "Effort, on a slider", "Drag from low to max on any model. In a pair the track blends the lead's colour into the heads', fast mode streaks toward the knob, and the title opens the model picker."),
        // DEVIATION (ED-NEW-sidebarmodes-7): the Mac's words described the Column-mode popover this port no longer has.
        new TourPage("tour-threads", "Threads, when you want them", ThreadsDescription),
        new TourPage("tour-recipes", "Recipes for a team", "Hydra's cookbook holds ready-made pairs: a lead, its heads and the effort each side gets, from the providers you have signed in. Pick one in Settings and every new chat starts on it."),
        new TourPage("tour-panels", "Panels that float", "Changes, the terminal and the team's panel dock to any corner and can pop out on their own. The conversation makes room, and everything stays in one window."),
        // DEVIATION OD-0012: the Windows themes description avoids unsupported platform claims.
        new TourPage("tour-themes", "Make it yours", ThemeCount + " themes, from Catppuccin to Kanagawa, in real glass. Built in C#, so it scrolls smoothly and starts in a blink."),
    });

    // TourPages.swift:4-13
    public static ValueList<TourPage> Pages => Build();

    // The chord as the shortcut store has it. With no settings store bound (a reader outside the app's start-up) nothing can be remapped,
    // so the default chord is the truthful one, read without making the process-wide store, whose first read would trap there.
    private static string? ToggleSidebarChord =>
        UserDefaults.IsBound || ShortcutStore.Shared.IsDefaultCreated ? ShortcutStore.Label(AppShortcut.ToggleSidebar) : AppShortcut.ToggleSidebar.DefaultChord()?.Description;

    // DEVIATION (ED-NEW-sidebarmodes-7, -11): page 5's words, with the Toggle sidebar chord as remapped, or without one once it is cleared.
    private static string ThreadsDescription =>
        "Hide the sidebar and the chat has the whole window. The list button in the toolbar"
        + (ToggleSidebarChord is { } chord ? ", or " + chord + "," : "")
        + " slides it back. Set it to Floating and the list floats as a panel over the chat instead: the same search, the same rows.";

    // TourPages.swift:18-24
    private static string ThemeCount
    {
        get
        {
            var count = SwiftEnum<AppTheme>.AllCases.Count;
            var spelled = SwiftFormatters.SpellOut(count, "en_US") ?? count.ToString(CultureInfo.InvariantCulture);
            return SwiftString.Uppercased(SwiftString.Prefix(spelled, 1)) + SwiftString.DropFirst(spelled, 1);
        }
    }

    // TourPages.swift:26-28
    public static void Present(AppModel model)
    {
        var seen = false;
        void MarkSeen()
        {
            if (seen) return;
            seen = true;
            model.Settings.HasSeenTour = true;
        }

        TourWindowController.Shared.Present(
            pages: Pages,
            onFinish: MarkSeen,
            onClose: MarkSeen);

    }
}
