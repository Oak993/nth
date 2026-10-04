#!/bin/bash
# VT-0 evidence run (Q-V-9 C feasibility). Builds nothing: runs the probe exe from a copy of its build output with the runtimes folder
# removed (so the loader's fallback to the application folder finds nothing), against case folders made from the restored runtime packs.
set -u
PROBE_BIN="$1"    # the probe's build output folder
ASSETS="$2"       # src/Marrow.Services/Port/Voice/Assets
WORK="$3"         # an empty scratch folder
NUGET="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
rm -rf "$WORK"; mkdir -p "$WORK/exe" "$WORK/root/runtimes/win-x64" "$WORK/root/runtimes/vulkan/win-x64" "$WORK/root/runtimes/cuda12/win-x64"
cp -r "$PROBE_BIN"/. "$WORK/exe/"; rm -rf "$WORK/exe/runtimes"
cp "$NUGET"/whisper.net.runtime/1.9.1/build/win-x64/*.dll "$WORK/root/runtimes/win-x64/"
cp "$NUGET"/whisper.net.runtime.vulkan/1.9.1/build/win-x64/*.dll "$WORK/root/runtimes/vulkan/win-x64/"
# A stand-in for a CUDA runtime whose NVIDIA libraries are missing: a file that LoadLibrary cannot load (the real pack was not available here).
echo "not a library" > "$WORK/root/runtimes/cuda12/win-x64/whisper.dll"
VAD="$ASSETS/ggml-silero-v6.2.0.bin"; CLIP="$ASSETS/selftest-clip.wav"
R=$(cygpath -w "$WORK/root"); P() { "$WORK/exe/probe.exe" "$@" "$VAD" "$CLIP" 2>&1 | sed "s#$R#<root>#g"; }
# the arguments of each case are: libraryPath order gpu
echo "== case 1: LibraryPath is a file path inside the root, order Cpu"; P "$R\runtime.dll" Cpu cpu
echo "== case 2: LibraryPath is the root folder itself, order Cpu"; P "$R" Cpu cpu
echo "== case 3: LibraryPath is whisper.dll inside runtimes\win-x64, order Cpu"; P "$R\runtimes\win-x64\whisper.dll" Cpu cpu
echo "== case 4: LibraryPath is a file path inside the root, order Vulkan, gpu on"; P "$R\runtime.dll" Vulkan gpu
echo "== case 5: order Cuda12,Vulkan,Cpu with an unloadable runtimes\cuda12\win-x64\whisper.dll"; P "$R\runtime.dll" Cuda12,Vulkan,Cpu gpu
echo "== case 6: order Cuda12,Cpu with the same unloadable cuda12 runtime"; P "$R\runtime.dll" Cuda12,Cpu cpu
echo "== case 7: order Cuda12 only with the same unloadable cuda12 runtime"; P "$R\runtime.dll" Cuda12 cpu
