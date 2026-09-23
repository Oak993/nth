using System;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;

namespace Marrow.App.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageFloatingPanel.swift (B2 1fbf81e), :1-112.
// Skeleton (c100 group 4): the view and its inputs are declared; body (:24-74) and strip (:78-111) are left to the
// implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; CGSize is Avalonia Size;
// `@Environment(WindowLiveResize.self)` is read from the environment, not passed in.

/// <summary>:9-112 <c>struct UsageFloatingPanel: View</c>: the chat's usage as a floating glass panel.</summary>
public sealed class UsageFloatingPanel : Decorator
{
    // :12-22 let runtime, provider, headsProvider, size, onContentHeight, onDrag, onDragEnd, dismiss; :24-74 body
    public UsageFloatingPanel(
        ThreadRuntime runtime,
        ProviderKind provider,
        ProviderKind? headsProvider,
        Size size,
        Action<double> onContentHeight,
        Action<Size> onDrag,
        Action onDragEnd,
        Action dismiss) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsageFloatingPanel.swift:24");
}
