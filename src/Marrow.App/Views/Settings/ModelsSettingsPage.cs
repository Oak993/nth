using System;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/ModelsSettingsPage.swift (B2 1fbf81e), :1-452.
// Skeleton (c100 group 4): every type the file declares, with its inputs; every view body and helper is left to the
// implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; `@Environment(AppModel.self)` is read from the environment, not passed in; a Swift
// parameter without a default that follows one with a default moves before it (ProviderModelsSection's isExpanded).
// Animation is Marrow.UI.Port.Animation. Swift `private` top-level types are `internal`.

/// <summary>:4-160 <c>struct ModelsSettingsPage: View</c>: the models the composer's picker offers.</summary>
public sealed class ModelsSettingsPage : Decorator
{
    // :6 var query = ""; :19-100 body
    public ModelsSettingsPage(string query = "") =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:19");

    // :17 private static let slide = Animation.spring(response: 0.28, dampingFraction: 0.82)
    private static Animation Slide =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:17");

    // :155-159 static func matches(_:id:provider:query:) -> Bool
    public static bool Matches(ModelOption? option, string id, ProviderKind provider, string query) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:155");

    /// <summary>:143-152 <c>private struct PinnedRowHeightReporter: ViewModifier</c>.</summary>
    private sealed record PinnedRowHeightReporter(ModelPin Pin, Binding<ValueMap<ModelPin, double>> Heights)
    {
        // :147-151 func body(content:)
        public Control Body(Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:147");
    }
}

/// <summary>:162-289 <c>private struct PinnedModelRow: View</c>: one chosen model, with its reorder grip.</summary>
internal sealed class PinnedModelRow : Decorator
{
    // :164-168 let pin, isDragged, onDragChanged, onDragEnded; :175-279 body
    public PinnedModelRow(ModelPin pin, bool isDragged, Action<double> onDragChanged, Action onDragEnded) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:175");
}

/// <summary>:291-311 <c>private struct RowControl: View</c>.</summary>
internal sealed class RowControl : Decorator
{
    // :292-295 let symbol, help, isEnabled, action; :297-310 body
    public RowControl(string symbol, string help, bool isEnabled, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:297");
}

/// <summary>:313-451 <c>private struct ProviderModelsSection: View</c>: one provider's catalogue.</summary>
internal sealed class ProviderModelsSection : Decorator
{
    // :315-320 let provider, var query = "", @Binding var isExpanded; :322-450 body
    public ProviderModelsSection(ProviderKind provider, Binding<bool> isExpanded, string query = "") =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/ModelsSettingsPage.swift:322");
}
