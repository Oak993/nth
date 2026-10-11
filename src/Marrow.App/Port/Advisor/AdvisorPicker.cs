// PORT-ONLY: ED-NEW-ignisadvisor-1 (owner 2026-10-11: the Claude advisor is a model choice in the chat's model picker). No Swift
// twin. The model list's part of it: the advisor entries go right after their main model among the Claude models, an entry shows
// its subtitle, and while the installed Claude Code cannot use the advisor it shows why instead, with an "Update" action that
// opens Settings › Updates, where nth updates Claude Code ("claude update", the "cli:claude" row). Choosing the entry still runs
// the chat on its main model; the launch never passes the flag to a CLI too old for it (ClaudeSession).
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port.Advisor;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Advisor;

public static class AdvisorPicker
{
    /// <summary>The Settings page the Update action opens: Updates, where the Claude Code row runs its own updater.</summary>
    public const SettingsPage UpdatePage = SettingsPage.Updates;

    /// <summary>The Update action (a test replaces it): opens Settings on <see cref="UpdatePage"/>.</summary>
    internal static Action<SettingsPage> OpenSettings { get; set; } = page => Marrow.App.Window.WindowManager.Shared.ShowSettings(page);

    /// <summary>Whether an entry is a Claude advisor entry.</summary>
    public static bool IsAdvisor(ModelCatalog.Entry entry) => entry.Provider == ProviderKind.Claude && ClaudeAdvisorModels.Find(entry.Option.Id) is not null;

    /// <summary>
    /// Adds each advisor entry once the list holds Claude models: right after its main model (its id or alias), else after the
    /// last Claude model. A list with no Claude model is left as it is, and so is every list while nth has not found Claude Code
    /// (the provider check found no executable): the advisor is a Claude Code launch flag, so there is nothing to run it with.
    /// </summary>
    public static void AddEntries(ProviderRegistry registry, List<ModelCatalog.Entry> entries)
    {
        if (!HasClaudeCode(registry)) return;
        foreach (var advisor in ClaudeAdvisorModels.All)
        {
            if (entries.Any(entry => entry.Provider == ProviderKind.Claude && string.Equals(entry.Option.Id, advisor.Id, StringComparison.Ordinal))) continue;
            var main = entries.FindIndex(entry => entry.Provider == ProviderKind.Claude && ClaudeAdvisorModels.IsMainOf(advisor, entry.Option.Id));
            var after = main >= 0 ? main : entries.FindLastIndex(entry => entry.Provider == ProviderKind.Claude);
            if (after < 0) continue;
            entries.Insert(after + 1, new ModelCatalog.Entry(ProviderKind.Claude, registry.AdvisorOption(advisor)));
        }
    }

    /// <summary>Whether nth's provider check found Claude Code (its executable), so the advisor entries can be offered.</summary>
    public static bool HasClaudeCode(ProviderRegistry registry) => registry.Status(ProviderKind.Claude).Executable is not null;

    /// <summary>Whether choosing <paramref name="next"/> over <paramref name="current"/> changes the launch flags (onto or off an advisor entry, or between two).</summary>
    public static bool ChangesLaunch(string? current, string? next) =>
        !string.Equals(current, next, StringComparison.Ordinal) && (ClaudeAdvisorModels.Find(current) is not null || ClaudeAdvisorModels.Find(next) is not null);

    /// <summary>The note the advisor entries show now (from the version nth's provider check read); null when Claude Code can use the advisor.</summary>
    public static string? Note(ProviderRegistry registry) => Note(registry.Status(ProviderKind.Claude).Version);

    /// <summary>The note for an installed Claude Code version; null when it can use the advisor.</summary>
    public static string? Note(string? cliVersion) => ClaudeAdvisorModels.Note(ClaudeAdvisorModels.VersionGate(cliVersion));

    /// <summary>The lines an advisor entry's detail may take: its subtitle and the note do not fit the 330-point list on one.</summary>
    public const int DetailLines = 2;

    /// <summary>An advisor entry's second line: the note when there is one, else its subtitle.</summary>
    public static string? Detail(ModelCatalog.Entry entry, string? note) => note ?? entry.Option.Detail;

    /// <summary>The row's "Update" action: accent words (the Settings credit link's style), opening Settings › Updates.</summary>
    public static Button UpdateLink()
    {
        var label = new TextBlock { Text = ClaudeAdvisorModels.UpdateAction, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Chrome.Accent.Brush(label);
        // It flows at the end of the row's words (an inline control sits on the line's baseline): its own descent hangs below the
        // baseline, as a word's does, so its words share the line's baseline and the line keeps a text line's height.
        label.Measure(Avalonia.Size.Infinity);
        if (label.TextLayout.TextLines is [var line, ..]) label.Margin = new Avalonia.Thickness(0, 0, 0, -(line.Height - line.Baseline));
        var button = PlainControls.Button(label, () => OpenSettings(UpdatePage));
        // The link sits inside the row's own button: its click must not bubble on and choose the model as well.
        button.Click += (_, args) => args.Handled = true;
        AutomationProperties.SetName(button, ClaudeAdvisorModels.UpdateActionAccessibleName);
        ToolTip.SetTip(button, ClaudeAdvisorModels.UpdateActionAccessibleName);
        return button;
    }
}
