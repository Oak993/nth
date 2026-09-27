// Source: DroppyCode/UI/Common/Components.swift (B2 1fbf81e), :1-168.

using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Marrow.UI.Common;

// :4-14
public sealed class ProviderIcon : Control
{
    public static readonly StyledProperty<ProviderKind> ProviderProperty =
        AvaloniaProperty.Register<ProviderIcon, ProviderKind>(nameof(Provider));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ProviderIcon, double>(nameof(Size), 14);

    public ProviderKind Provider
    {
        get => GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public ProviderIcon()
    {
        // Icon rendering is handled through data binding to the image resource system
    }
}

// :16-43
public static class RelativeTime
{
    // :19 @MainActor private static var days = RecentCache<Date, String>(limit: 256)
    private static readonly RecentCache<DateTimeOffset, string> DayCache =
        new(limit: 256);

    // :22-35
    public static string Short(DateTimeOffset date, DateTimeOffset? now = null)
    {
        var nowValue = now ?? DateTimeOffset.UtcNow;
        var seconds = nowValue.Subtract(date).TotalSeconds;

        switch (seconds)
        {
            case < 60:
                return "now";
            case < 3_600:
                return $"{(long)(seconds / 60)}m";
            case < 86_400:
                return $"{(long)(seconds / 3_600)}h";
            case < 604_800:
                return $"{(long)(seconds / 86_400)}d";
            default:
                // :30 if let day = days.value(for: date) { return day }
                if (DayCache.TryGetValue(date, out var cached))
                {
                    return cached;
                }

                // :31 let day = date.formatted(.dateTime.month(.abbreviated).day())
                var formatted = date.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);

                // :32 days.insert(day, for: date)
                DayCache.Insert(formatted, date);

                return formatted;
        }
    }

    // :37-42
    public static string Duration(TimeSpan interval)
    {
        // :38 let total = max(0, Int(interval.rounded()))
        var total = Math.Max(0, (long)Math.Round(interval.TotalSeconds));

        if (total < 60)
            return $"{total}s";
        if (total < 3_600)
            return $"{total / 60}m {total % 60}s";
        return $"{total / 3_600}h {(total % 3_600) / 60}m";
    }
}

// :45-56
public sealed class DiffStatLabel : Control
{
    public static readonly StyledProperty<int> AdditionsProperty =
        AvaloniaProperty.Register<DiffStatLabel, int>(nameof(Additions));

    public static readonly StyledProperty<int> DeletionsProperty =
        AvaloniaProperty.Register<DiffStatLabel, int>(nameof(Deletions));

    public int Additions
    {
        get => GetValue(AdditionsProperty);
        set => SetValue(AdditionsProperty, value);
    }

    public int Deletions
    {
        get => GetValue(DeletionsProperty);
        set => SetValue(DeletionsProperty, value);
    }
}

// :58-92
// A compact, borderless control used inside glass surfaces
public static class ChipButtonStyleHelper
{
    // ChipButtonStyle in SwiftUI applies modifiers to buttons.
    // In Avalonia, this would be applied through styling, not as a direct class.
    // This class serves as a helper for consistent chip-style button appearance.
}

// :102-168
public static class Workspace
{
    // :103-107
    public sealed record Editor
    {
        public required string Name { get; init; }
        public required string BundleID { get; init; }

        // :104 var id: String { bundleID }
        public string Id => BundleID;
    }

    // :109-117 DEVIATION OD-0012 G4: Five entries (Xcode and iTerm excluded)
    public static IReadOnlyList<Editor> Editors { get; } = new ReadOnlyCollection<Editor>(
        new List<Editor>
        {
            new() { Name = "Visual Studio Code", BundleID = "com.microsoft.VSCode" },
            new() { Name = "Cursor", BundleID = "com.todesktop.230313mzl4w4u92" },
            new() { Name = "Zed", BundleID = "dev.zed.Zed" },
            new() { Name = "Terminal", BundleID = "com.apple.Terminal" }, // :114, Terminal on Windows
            new() { Name = "Ghostty", BundleID = "com.mitchellh.ghostty" }
            // :113 Xcode and :116 iTerm removed per OD-0012 G4
        }
    );

    // BLOCKED: PR-CHAT-181 and PR-CHAT-182 open questions
    // The Windows detection keys and launch methods for editors are not yet decided.
    // The following methods require that decision:

    // :120-135 BLOCKED: PR-PROVREG-233
    // "What a Windows Login button launches" is unresolved
    // The zsh shebang, .command extension, POSIX permissions, and workspace open
    // all have no Windows equivalent. OD-0012 F1 names "marrow-login" as the folder
    // but the terminal launch mechanism needs an owner decision.
    public static void OpenTerminal(string command)
    {
        throw new NotImplementedException(
            "BLOCKED PR-PROVREG-233: Windows terminal launch mechanism not yet decided. " +
            "See porting/decision_batch_20260922b.md section F1 and open_question in the work packet."
        );
    }

    // :139-141 BLOCKED: PR-CHAT-182
    // "Looked up once per launch" requires Windows detection keys for each editor
    public static IReadOnlyList<Editor> InstalledEditors
    {
        get => throw new NotImplementedException(
            "BLOCKED PR-CHAT-182: Windows detection keys for editors not yet decided. " +
            "See open_question in work packet UI.Common.Components part1.md"
        );
    }

    // :143-146 BLOCKED: PR-CHAT-182 and PR-CHAT-181
    // The Windows launch method depends on detection keys
    public static void Open(string path, Editor editor)
    {
        throw new NotImplementedException(
            "BLOCKED PR-CHAT-181/PR-CHAT-182: Windows editor launch not yet decided. " +
            "Depends on detection keys and launch method for each editor."
        );
    }

    // :150 @MainActor private static var icons: [String: NSImage] = [:]
    private static readonly Dictionary<string, object?> IconCache = new();

    // :153-160 BLOCKED: PR-CHAT-182
    // Icon caching requires the detection keys
    public static object? Icon(Editor editor)
    {
        throw new NotImplementedException(
            "BLOCKED PR-CHAT-182: Icon caching requires Windows detection keys. " +
            "See open_question in work packet UI.Common.Components part1.md"
        );
    }

    // :163 BLOCKED: PR-CHAT-182 and OD-0012 G4
    // "Finder's icon... File Explorer's icon is read from is likewise not fixed"
    public static object? FileExplorerIcon
    {
        get => throw new NotImplementedException(
            "BLOCKED PR-CHAT-182: File Explorer icon path not yet decided. " +
            "The source fixed /System/Library/CoreServices/Finder.app but Windows path is open."
        );
    }

    // :165-167 BLOCKED: PR-CHAT-182 and OD-0012 G4
    // Reveal in File Explorer (not Finder)
    public static void RevealInFileExplorer(string path)
    {
        throw new NotImplementedException(
            "BLOCKED OD-0012 G4: File Explorer reveal not yet implemented. " +
            "Requires platform-specific file explorer invocation."
        );
    }
}
