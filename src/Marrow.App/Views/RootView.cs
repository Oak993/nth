using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Views;

// Source: DroppyCode/App/Views/RootView.swift (B2 1fbf81e), :1-479.
// Skeleton (c100 group 4): every type the file declares, with the inputs other files pass; every view body and member
// with behaviour is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` and
// `@Environment(WindowLiveResize.self)` are read from the environment, not passed in. The `@Entry` environment value of
// `extension EnvironmentValues` (:116-118) is an inherited attached property on the class named after the Swift file
// (R-NAME-4, R-VIEW-6), which is the file's own view RootView. NSViewRepresentable is the Decorator hosting its NSView
// (NSView is Control). Swift `private` top-level types are `internal`.

/// <summary>:4-111 <c>struct RootView: View</c>, with the file's <c>extension EnvironmentValues</c> (:116-118).</summary>
public sealed class RootView : Decorator
{
    // :8-92 body
    public RootView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:8");

    // :94-110 @discardableResult private func handleDrop(_ urls: [URL]) -> Bool
    private bool HandleDrop(IReadOnlyList<SwiftURL> urls) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:95");

    // :117 @Entry var chatColumnIsShown = true: whether the chat column a view sits in is the one on screen.
    public static readonly AttachedProperty<bool> ChatColumnIsShownProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ChatColumnIsShown", typeof(RootView), true, inherits: true);

    public static bool GetChatColumnIsShown(Control control) => control.GetValue(ChatColumnIsShownProperty);

    public static void SetChatColumnIsShown(Control control, bool value) => control.SetValue(ChatColumnIsShownProperty, value);
}

/// <summary>:120-167 <c>struct DetailView: View</c>: the selected thread's column, or the empty states.</summary>
public sealed class DetailView : Decorator
{
    // :129-166 body
    public DetailView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:129");
}

/// <summary>:177-297 <c>private struct ThreadColumns: NSViewRepresentable</c>: the chat column hosted per thread, the
/// columns of the threads visited last kept hidden in the pane.</summary>
internal sealed class ThreadColumns : Decorator
{
    // :179 static let topRowsBuiltAtLaunch = 6
    public const long TopRowsBuiltAtLaunch = 6;

    // :181-187 let threadID, prebuilt, running, model, liveResize; :189-202 makeNSView, updateNSView, makeCoordinator
    public ThreadColumns(Guid threadID, IReadOnlyList<Guid> prebuilt, IReadOnlySet<Guid> running, AppModel model, WindowLiveResize liveResize) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:189");

    /// <summary>:204-296 <c>@MainActor final class Coordinator</c>.</summary>
    public sealed class Coordinator
    {
        // :206 private static let keptLimit = 8
        private const long KeptLimit = 8;

        // :224-243 func show(_:in:model:liveResize:)
        public void Show(Guid id, Control @in, AppModel model, WindowLiveResize liveResize) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:224");

        // :247-249 func dropHidden(where:)
        public void DropHidden(Func<Guid, bool> where) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:247");

        // :254-263 func prebuild(_:in:model:liveResize:)
        public void Prebuild(IReadOnlyList<Guid> ids, Control @in, AppModel model, WindowLiveResize liveResize) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:254");
    }
}

/// <summary>:299-350 <c>struct WelcomeView: View</c>: the first-run screen.</summary>
public sealed class WelcomeView : Decorator
{
    // :302-349 body
    public WelcomeView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:302");
}

/// <summary>:352-363 <c>struct ProviderChecklist: View</c>.</summary>
public sealed class ProviderChecklist : Decorator
{
    // :355-362 body
    public ProviderChecklist() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:355");
}

/// <summary>:365-410 <c>private struct ProviderStatusRow: View</c>.</summary>
internal sealed class ProviderStatusRow : Decorator
{
    // :367 let provider: ProviderKind; :369-409 body
    public ProviderStatusRow(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:369");
}

/// <summary>:413-422 <c>private struct OpenProvidersSettingsButton: View</c>.</summary>
internal sealed class OpenProvidersSettingsButton : Decorator
{
    // :414-421 body
    public OpenProvidersSettingsButton() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:414");
}

/// <summary>:424-451 <c>struct CopyCommandButton: View</c>.</summary>
public sealed class CopyCommandButton : Decorator
{
    // :425 let command: String; :428-450 body
    public CopyCommandButton(string command) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:428");
}

/// <summary>:453-479 <c>struct NoThreadView: View</c>.</summary>
public sealed class NoThreadView : Decorator
{
    // :456-478 body
    public NoThreadView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/RootView.swift:456");
}
