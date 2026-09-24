using System;
using Marrow.Core.Port;

namespace Marrow.App.Views.Tour;

// Source: DroppyCode/App/Views/Tour/TourPages.swift (B2 1fbf81e), :1-29.
// Skeleton (c100 group 4): the case-less enum Tour is a static class; pages (:4-13), themeCount (:18-24) and present
// (:26-28) are left to the implementing unit (MARROW_STUB). `static let pages` interpolates themeCount, so it is not
// plain data.
// Name clash (the ED-095 Chrome pattern): the Swift type Tour keeps its name in namespace Marrow.App.Views.Tour; a file
// in another namespace under Marrow.App.Views writes `using Tour = Marrow.App.Views.Tour.Tour;` after its namespace
// line, so `Tour.Present(...)` finds the type, not the namespace.

/// <summary>:3-29 <c>enum Tour</c>: the welcome tour's pages and how it is shown.</summary>
public static class Tour
{
    // :4-13 static let pages: [TourPage]
    public static ValueList<TourPage> Pages =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Tour/TourPages.swift:4");

    // :18-24 private static var themeCount: String
    private static string ThemeCount =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Tour/TourPages.swift:18");

    // :26-28 @MainActor static func present(model:)
    public static void Present(AppModel model) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Tour/TourPages.swift:26");
}
