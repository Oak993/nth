namespace Marrow.App.Tests.Fixtures;

// The conditions ThreadRuntimeAutoMergeGateTests drives one at a time (ThreadRuntime.swift:2494-2505); a top-level type,
// as test classes carry no nested types.
public enum Blocker
{
    None,
    TurnFailed,
    TurnInterrupted,
    AutoMergeSettingOff,
    HydraOffForTheChat,
    BatchOutstanding,
    HeadWaiting,
    NativeHeadStillRunning,
    TurnContinues,
}
