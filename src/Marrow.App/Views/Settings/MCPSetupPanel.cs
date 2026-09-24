using System;
using Avalonia.Controls;
using Marrow.Core.Models;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSetupPanel.swift (B2 1fbf81e), :1-262.
// Skeleton (c100 group 4): the panel and its view are declared; isPresenting (:18-20), present (:22-43), close
// (:45-49), fit (:53-61), the panel's private members (:63-100) and the view's body and helpers (:115-261) are left to
// the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `static let shared` is `Shared`.
// The NSPanel itself is a window, PA-0002's.

/// <summary>:6-101 <c>@MainActor final class MCPSetupPanel</c>: the floating panel shown when a server refuses on its
/// own side.</summary>
public sealed class MCPSetupPanel
{
    // :8 static let shared = MCPSetupPanel()
    public static MCPSetupPanel Shared { get; } = new();

    // :14 private init() {}
    private MCPSetupPanel() { }

    // :18-20 func isPresenting(_ entry: MCPCatalogEntry) -> Bool
    public bool IsPresenting(MCPCatalogEntry entry) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPSetupPanel.swift:18");

    // :22-43 func present(entry:guide:model:)
    public void Present(MCPCatalogEntry entry, MCPSetupGuide guide, AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPSetupPanel.swift:22");

    // :45-49 func close()
    public void Close() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPSetupPanel.swift:45");

    // :53-61 func fit(height: CGFloat)
    public void Fit(double height) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPSetupPanel.swift:53");
}

/// <summary>:105-262 <c>struct MCPSetupPanelView: View</c>: the reason, numbered steps, an open page button with Check
/// again, and the success view once the server connects.</summary>
public sealed class MCPSetupPanelView : Decorator
{
    // :106-109 let entry, let guide, let model, let onDone; :117-194 body
    public MCPSetupPanelView(MCPCatalogEntry entry, MCPSetupGuide guide, AppModel model, Action onDone) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPSetupPanel.swift:117");
}
