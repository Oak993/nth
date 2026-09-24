using System;
using Avalonia.Controls;
using Marrow.Core.Models;

namespace Marrow.App.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsagePanel.swift (B2 1fbf81e), :1-120.
// Skeleton (c100 group 4): the view, its inputs and tokens(_:) are declared; body (:26-79), providerSection (:83-108)
// and tokens (:110-119) are left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; Int is long;
// `@Environment(AppModel.self)` is read from the environment, not passed in.

/// <summary>:8-120 <c>struct UsagePanel: View</c>: the context window, the plan's limits or the credits left.</summary>
public sealed class UsagePanel : Decorator
{
    // :19-24 init(usage:provider:headsProvider:fillsWidth:); :26-79 body
    public UsagePanel(ContextUsage? usage, ProviderKind provider, ProviderKind? headsProvider = null, bool fillsWidth = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsagePanel.swift:19");

    // :110-119 static func tokens(_ count: Int) -> String
    public static string Tokens(long count) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Usage/UsagePanel.swift:110");
}
