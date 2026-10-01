namespace Marrow.App.Tests.Fixtures;

// UI/Theme/AppTheme.swift:14-176 transcribed: every case in declaration order (CaseIterable) with its raw value, displayName (:44-72),
// the picker subtitle `detail` (:75-103) and the palette (:106-134) as (fixed scheme or null, accent hex or null, surface hex).
public static class SettingsThemeTable
{
    public static readonly (string Raw, string Name, string Detail, string? Scheme, string? Accent, string Surface)[] All =
    [
        ("system", "System", "Follows your Mac", null, null, "1E1E20"),
        ("light", "Light", "Light \u00B7 System accent", "light", null, "FFFFFF"),
        ("dark", "Dark", "Dark \u00B7 System accent", "dark", null, "1E1E20"),
        ("catppuccinMocha", "Catppuccin Mocha", "Dark \u00B7 Mauve", "dark", "CBA6F7", "1E1E2E"),
        ("catppuccinLatte", "Catppuccin Latte", "Light \u00B7 Mauve", "light", "8839EF", "EFF1F5"),
        ("dracula", "Dracula", "Dark \u00B7 Purple", "dark", "BD93F9", "282A36"),
        ("tokyoNight", "Tokyo Night", "Dark \u00B7 Blue", "dark", "7AA2F7", "1A1B26"),
        ("nord", "Nord", "Dark \u00B7 Frost", "dark", "88C0D0", "2E3440"),
        ("gruvbox", "Gruvbox", "Dark \u00B7 Orange", "dark", "FE8019", "282828"),
        ("gruvboxLight", "Gruvbox Light", "Light \u00B7 Rust", "light", "AF3A03", "FBF1C7"),
        ("oneDark", "One Dark", "Dark \u00B7 Blue", "dark", "61AFEF", "282C34"),
        ("everforest", "Everforest", "Dark \u00B7 Green", "dark", "A7C080", "2D353B"),
        ("kanagawa", "Kanagawa", "Dark \u00B7 Wave blue", "dark", "7E9CD8", "1F1F28"),
        ("rosePine", "Ros\u00E9 Pine", "Dark \u00B7 Rose", "dark", "EB6F92", "191724"),
        ("solarizedDark", "Solarized Dark", "Dark \u00B7 Blue", "dark", "268BD2", "002B36"),
        ("solarizedLight", "Solarized Light", "Light \u00B7 Blue", "light", "268BD2", "FDF6E3"),
        ("githubDark", "GitHub Dark", "Dark \u00B7 Blue", "dark", "4493F8", "0D1117"),
        ("githubLight", "GitHub Light", "Light \u00B7 Blue", "light", "0969DA", "FFFFFF"),
        ("ayu", "Ayu", "Dark \u00B7 Amber", "dark", "E6B450", "0F1419"),
        ("nightOwl", "Night Owl", "Dark \u00B7 Blue", "dark", "82AAFF", "011627"),
        ("monokai", "Monokai", "Dark \u00B7 Pink", "dark", "F92672", "272822"),
        ("claude", "Claude", "Dark \u00B7 Terracotta", "dark", "C15F3C", "1A1816"),
        ("claudeLight", "Claude Light", "Light \u00B7 Terracotta", "light", "C15F3C", "FAF9F5"),
        ("codex", "Codex", "Dark \u00B7 Magenta", "dark", "D946EF", "101014"),
        ("cursor", "Cursor", "Dark \u00B7 Frost", "dark", "88C0D0", "181818"),
        ("matrix", "Matrix", "Dark \u00B7 Green", "dark", "00E676", "000000"),
    ];
}
