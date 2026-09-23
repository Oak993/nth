using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.UI.Theme;

// Source: DroppyCode/UI/Theme/ShortcutTint.swift (B2 1fbf81e), :1-12.

/// <summary>
/// Extension of <see cref="AppShortcutSection"/> providing the color each shortcut section wears in Settings.
/// Kept with the theme so the shortcut model stays free of chrome colours (:1-4).
/// </summary>
public static class ShortcutTint
{
    /// <summary>
    /// Returns the fixed tint color for a shortcut section.
    /// Threads section wears Chrome.blue; Window section wears a fixed purple.
    /// Neither follows the theme accent (:6-11).
    /// </summary>
    public static SwiftColor Tint(this AppShortcutSection self) => self switch
    {
        AppShortcutSection.Threads => Marrow.UI.Chrome.Chrome.Blue, // :8
        AppShortcutSection.Window => new SwiftColor(red: 0.686, green: 0.322, blue: 0.871), // :9
        _ => throw new UnreachableException(),
    };
}
