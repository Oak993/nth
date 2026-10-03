using Marrow.App.Views.Sidebar;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>The window, the sidebar and the model of the sidebar capture rig's reference scene (SidebarCaptureTests): a
/// fixture type of its own, since a test file declares no type besides its test class (TraceabilityTests).</summary>
internal sealed record SidebarReferenceScene(AvaloniaWindow Window, SidebarView View, AppModel Model);
