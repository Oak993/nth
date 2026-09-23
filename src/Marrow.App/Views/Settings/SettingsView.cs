using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B2 1fbf81e), :1-1232.
// Skeleton (c100 group 4): every type the file declares, private ones included, with its inputs; SettingsPage's cases
// and raw values and SettingsNavigation's stored property are complete; every view body, computed property and helper
// is left to the implementing unit (MARROW_STUB), as are the views' private members.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` is read
// from the environment, not passed in; `enum SettingsPage: String` carries [RawValue] on every case (R-ENUM-1) and its
// computed members are C# 14 extension members of SettingsPageExtensions (R-NAME-4); `@MainActor @Observable` is
// MainActorModel (R-OBS-6); Color is SwiftColor; URL is SwiftURL. Swift `private` top-level types are `internal`.

/// <summary>:4-80 <c>enum SettingsPage: String, CaseIterable, Identifiable</c>.</summary>
public enum SettingsPage
{
    [RawValue("general")] General, // :5
    [RawValue("hydra")] Hydra, // :7
    [RawValue("appearance")] Appearance, // :8
    [RawValue("providers")] Providers, // :9
    [RawValue("mcp")] Mcp, // :10
    [RawValue("models")] Models, // :11
    [RawValue("sourceControl")] SourceControl, // :12
    [RawValue("shortcuts")] Shortcuts, // :13
    [RawValue("archive")] Archive, // :14
    [RawValue("about")] About, // :15
}

/// <summary>:17-79 the computed members of <c>SettingsPage</c>.</summary>
public static class SettingsPageExtensions
{
    extension(SettingsPage page)
    {
        // :17 var id: String { rawValue }
        public string Id =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:17");

        // :19-32 var title: String
        public string Title =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:19");

        // :34-49 var symbol: String
        public string Symbol =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:34");

        // :51-64 var tint: Color
        public SwiftColor Tint =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:51");

        // :66-79 var keywords: [String]
        public ValueList<string> Keywords =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:66");
    }
}

/// <summary>:84-89 <c>@MainActor @Observable final class SettingsNavigation</c>: a page asked for from outside the
/// window.</summary>
public sealed class SettingsNavigation : MainActorModel
{
    // :87 static let shared = SettingsNavigation()
    public static SettingsNavigation Shared { get; } = new();

    // :88 var requestedPage: SettingsPage?
    private SettingsPage? requestedPage;

    public SettingsPage? RequestedPage
    {
        get => Read(requestedPage);
        set => Set(ref requestedPage, value);
    }
}

/// <summary>:91-294 <c>struct SettingsView: View</c>: the Settings window's content.</summary>
public sealed class SettingsView : Decorator
{
    // :109-134 body
    public SettingsView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:109");
}

/// <summary>:296-305 <c>struct SettingsSwitch: View</c>: a small switch with no label.</summary>
public sealed class SettingsSwitch : Decorator
{
    // :297 @Binding var isOn: Bool; :299-304 body
    public SettingsSwitch(Binding<bool> isOn) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:299");
}

/// <summary>:307-413 <c>private struct GeneralSettingsPage: View</c>.</summary>
internal sealed class GeneralSettingsPage : Decorator
{
    // :310-412 body
    public GeneralSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:310");
}

/// <summary>:417-458 <c>private struct AppearanceSettingsPage: View</c>.</summary>
internal sealed class AppearanceSettingsPage : Decorator
{
    // :420-457 body
    public AppearanceSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:420");
}

/// <summary>:462-517 <c>private struct AppZoomCard: View</c>: the app-wide zoom.</summary>
internal sealed class AppZoomCard : Decorator
{
    // :463 @Binding var percent: Int; :467-516 body
    public AppZoomCard(Binding<long> percent) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:467");
}

/// <summary>:521-564 <c>private struct BackdropOpacitySlider: View</c>.</summary>
internal sealed class BackdropOpacitySlider : Decorator
{
    // :522 @Binding var value: Double; :526-563 body
    public BackdropOpacitySlider(Binding<double> value) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:526");
}

/// <summary>:566-580 <c>private struct ProvidersRefreshButton: View</c>.</summary>
internal sealed class ProvidersRefreshButton : Decorator
{
    // :569-579 body
    public ProvidersRefreshButton() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:569");
}

/// <summary>:585-615 <c>private struct ProvidersSettingsPage: View</c>.</summary>
internal sealed class ProvidersSettingsPage : Decorator
{
    // :592-614 body
    public ProvidersSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:592");
}

/// <summary>:617-824 <c>private struct ProviderSettingsSection: View</c>: one provider's settings.</summary>
internal sealed class ProviderSettingsSection : Decorator
{
    // :619 let provider, :622 @Binding var isExpanded; :644-823 body
    public ProviderSettingsSection(ProviderKind provider, Binding<bool> isExpanded) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:644");
}

/// <summary>:826-872 <c>private struct SourceControlSettingsPage: View</c>.</summary>
internal sealed class SourceControlSettingsPage : Decorator
{
    // :829-871 body
    public SourceControlSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:829");
}

/// <summary>:877-909 <c>private struct ArchiveDeleteAllButton: View</c>.</summary>
internal sealed class ArchiveDeleteAllButton : Decorator
{
    // :881-908 body
    public ArchiveDeleteAllButton() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:881");
}

/// <summary>:912-959 <c>private struct ArchiveSettingsPage: View</c>.</summary>
internal sealed class ArchiveSettingsPage : Decorator
{
    // :919-958 body
    public ArchiveSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:919");
}

/// <summary>:961-1028 <c>private struct AboutSettingsPage: View</c>.</summary>
internal sealed class AboutSettingsPage : Decorator
{
    // :965-989 body
    public AboutSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:965");
}

/// <summary>:1032-1080 <c>private struct LicensesRow: View</c>: the license, notices and trademark policy.</summary>
internal sealed class LicensesRow : Decorator
{
    // :1041-1067 body
    public LicensesRow() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1041");

    // :1077-1079 private static func text(of:) -> String
    private static string Text(string of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1077");
}

/// <summary>:1083-1125 <c>private struct MadeByDroppyRow: View</c>.</summary>
internal sealed class MadeByDroppyRow : Decorator
{
    // :1084 let url: URL; :1087-1124 body
    public MadeByDroppyRow(SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1087");
}

/// <summary>:1127-1144 <c>private struct CreditLink: View</c>.</summary>
internal sealed class CreditLink : Decorator
{
    // :1128-1129 let title, let url; :1131-1143 body
    public CreditLink(string title, SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1131");
}

/// <summary>:1148-1178 <c>private struct ProjectMarksRow: View</c>: the per-project marks behind one button.</summary>
internal sealed class ProjectMarksRow : Decorator
{
    // :1153-1170 body
    public ProjectMarksRow() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1153");
}

/// <summary>:1182-1232 <c>private struct ThemePickerButton: View</c>: the theme menu.</summary>
internal sealed class ThemePickerButton : Decorator
{
    // :1183 @Binding var selection: AppTheme; :1191-1225 body
    public ThemePickerButton(Binding<AppTheme> selection) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1191");
}
