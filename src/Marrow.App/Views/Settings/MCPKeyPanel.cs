using System;
using Avalonia.Controls;
using Marrow.Core.Models;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift (B2 1fbf81e), :1-251.
// Skeleton (c100 group 4): the panel and its view are declared; present (:15-31), close (:33-36), the panel's private
// members (:38-75) and the view's body and helpers (:87-250) are left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `static let shared` is `Shared`.
// The NSPanel itself is a window, PA-0002's.

/// <summary>:6-76 <c>@MainActor final class MCPKeyPanel</c>: the floating panel a user pastes an MCP key into.</summary>
public sealed class MCPKeyPanel
{
    // :8 static let shared = MCPKeyPanel()
    public static MCPKeyPanel Shared { get; } = new();

    // :13 private init() {}
    private MCPKeyPanel() { }

    // :15-31 func present(entry:model:)
    public void Present(MCPCatalogEntry entry, AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPKeyPanel.swift:15");

    // :33-36 func close()
    public void Close() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPKeyPanel.swift:33");
}

/// <summary>:79-251 <c>struct MCPKeyPanelView: View</c>: numbered steps, one field per entry field, and Save.</summary>
public sealed class MCPKeyPanelView : Decorator
{
    // :80-82 let entry, let model, let onDone; :87-120 body
    public MCPKeyPanelView(MCPCatalogEntry entry, AppModel model, Action onDone) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/MCPKeyPanel.swift:87");
}
