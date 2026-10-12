namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-jewelwarmup-1: what StepFrames drew: the frames, those of them before the warm-up's first step was asked (its
// waking: a hurried run notices at its next animation frame), and the times no step was asked within Settle (the last frame)
internal readonly record struct JewelWarmupStepRun(int Frames, int Waking, int Idle);
