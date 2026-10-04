VT-0 loader probe (voice SPEC V-3.2, Q-V-9 C feasibility), run 2026-10-04 on the owner PC.

What it shows: Whisper.net 1.9.1 loads a native runtime from a folder it is pointed at through RuntimeOptions.LibraryPath (read through its
directory), for the CPU runtime and the Vulkan runtime, and it skips a CUDA runtime that cannot be loaded when a later runtime is listed.

What it does not show: the real Whisper.net.Runtime.Cuda12.Windows pack loading. The pack's own natives were loaded once earlier the same day with
NVIDIA 12.4 libraries beside them, but that console output was not saved and the NVIDIA copies are no longer on this PC, so this run uses an
unloadable stand-in file for the CUDA runtime (cases 5 to 7). voice-pins.json records the CUDA 12 load as "observed-not-reproduced".

Files: probe.csproj.txt and Program.cs.txt (the console; renamed .txt so no build or scan picks them up: copy them back to build), run.sh (the case folders and the seven runs), run-output.txt (the saved output of
`run.sh <probe build folder> src/Marrow.Services/Port/Voice/Assets <empty scratch folder>`, the clip being the bundled self-test clip).
The probe is a scratch console: it is not part of the solution and no test runs it. VoicePinsTests reads run-output.txt.
