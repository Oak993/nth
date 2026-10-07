// PORT-ONLY: T11 step 2. The rows the Settings pages draw, as plain data (no Avalonia): one grouped row has a plain title, one grey line, and its value on the right as a menu, a switch or a button.
namespace Marrow.Core.Port.MultiAccount.Settings;

public enum SettingKind
{
    Menu,
    Switch,
    Button,
}

/// <summary>One choice of a menu row. <see cref="Chosen"/> is the one the row shows now (a row's page default counts).</summary>
public sealed record MenuOption(string Value, string Label, bool Recommended, bool Chosen);

public sealed record SettingRow(
    string Key,
    string Title,
    SettingKind Kind,
    string ValueLabel,
    string Description,
    bool Visible,
    bool Indented,
    bool Unset,
    bool On,
    IReadOnlyList<MenuOption> Options,
    string? ButtonText);

/// <summary>One entry of a ⋯ menu. A <see cref="Separator"/> has no other fields.</summary>
public sealed record MenuEntry(string Key, string Label, string? Hint = null, bool Checked = false, bool Disabled = false, bool Quiet = false, bool Separator = false)
{
    public static MenuEntry Line { get; } = new("-", "", Separator: true);
}
