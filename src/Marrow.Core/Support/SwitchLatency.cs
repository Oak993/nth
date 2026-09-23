// Source: DroppyCode/Core/Support/SwitchLatency.swift (B2 1fbf81e), :1-58.
// DEVIATION ED-005: not ported. Swift :20-21 is
// `guard AppInfo.isDevelopment, observer == nil else { return }`, and Text.swift:5 makes
// isDevelopment `Bundle.main.bundleIdentifier == "iordv.droppycode.dev"` (project.yml:56: the
// separate Dev target). With observer never created in the shipped bundle, Note (:42-45) and
// NoteSwitch (:47-57) both return immediately at their own `observer != nil` guards too, so the
// whole file's observable behavior in a release build is nothing at all: no UI, no file, no log
// line, no signpost. Its only output even in the dev app is os_log developer instrumentation, not
// product. ED-005 decided to drop the CFRunLoopObserver, the Logger/OSLog instrumentation and the
// timing math entirely, and to keep these three members as no-ops so App-layer callers
// (AppModel.swift, DroppyCodeApp.swift, RootView.swift) keep compiling against this shape.
namespace Marrow.Core.Support;

public static class SwitchLatency
{
    // :20-39. ED-005: no CFRunLoopObserver is ever installed; no-op.
    public static void Start()
    {
    }

    // :41-45. ED-005: the Swift guard `observer != nil` never opens in the shipped bundle; no-op.
    public static void Note(string message)
    {
    }

    // :47-57. ED-005: the Swift guard `observer != nil` never opens in the shipped bundle; no-op.
    public static void NoteSwitch(string title)
    {
    }
}
