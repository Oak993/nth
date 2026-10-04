#!/bin/bash
# VT-0 evidence run (Q-V-9 C feasibility). Builds nothing: runs the probe exe from a copy of its build output with the runtimes folder
# removed (so the loader's fallback to the application folder finds nothing), against case folders made from the restored runtime packs.
# Round 3: cases 8 to 17 use the REAL Whisper.net.Runtime.Cuda12.Windows 1.9.1 pack (restored from nuget.org by dotnet, build\win-x64) with and without
# the NVIDIA 12.4 libraries beside it or on PATH (copied from the benchmark folder, NVIDIA_DIR); the probe also prints the whisper, ggml, CUDA and
# cuBLAS modules it holds and its exit code is printed after each case.
# Usage: run.sh <probe build folder> src/Marrow.Services/Port/Voice/Assets <empty scratch folder> <folder with the three NVIDIA 12 libraries>
set -u
PROBE_BIN="$1"    # the probe's build output folder
ASSETS="$2"       # src/Marrow.Services/Port/Voice/Assets
WORK="$3"         # an empty scratch folder
NVIDIA_DIR="$4"   # a folder holding cudart64_12.dll, cublas64_12.dll and cublasLt64_12.dll
NUGET="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
rm -rf "$WORK"; mkdir -p "$WORK/exe"
cp -r "$PROBE_BIN"/. "$WORK/exe/"; rm -rf "$WORK/exe/runtimes"
mkroot() { # root-name cuda12-kind (none | standin | pack | packnv)
  local r="$WORK/$1"; mkdir -p "$r/runtimes/win-x64" "$r/runtimes/vulkan/win-x64" "$r/runtimes/cuda12/win-x64"
  cp "$NUGET"/whisper.net.runtime/1.9.1/build/win-x64/*.dll "$r/runtimes/win-x64/"
  cp "$NUGET"/whisper.net.runtime.vulkan/1.9.1/build/win-x64/*.dll "$r/runtimes/vulkan/win-x64/"
  case "$2" in
    standin) echo "not a library" > "$r/runtimes/cuda12/win-x64/whisper.dll";;
    pack|packnv) cp "$NUGET"/whisper.net.runtime.cuda12.windows/1.9.1/build/win-x64/*.dll "$r/runtimes/cuda12/win-x64/";;
  esac
  if [ "$2" = packnv ]; then cp "$NVIDIA_DIR"/cudart64_12.dll "$NVIDIA_DIR"/cublas64_12.dll "$NVIDIA_DIR"/cublasLt64_12.dll "$r/runtimes/cuda12/win-x64/"; fi
}
mkroot root standin   # cases 1 to 7: a stand-in for a CUDA runtime whose libraries cannot be loaded (a file that LoadLibrary cannot load)
mkroot rootnv packnv  # cases 8 to 10: the real CUDA 12 pack with the three NVIDIA libraries beside it
mkroot rootnn pack    # cases 11 to 13: the real CUDA 12 pack with no NVIDIA libraries
mkdir -p "$WORK/nvpath" "$WORK/cudartonly"; cp "$NVIDIA_DIR"/cudart64_12.dll "$NVIDIA_DIR"/cublas64_12.dll "$NVIDIA_DIR"/cublasLt64_12.dll "$WORK/nvpath/"; cp "$NVIDIA_DIR"/cudart64_12.dll "$WORK/cudartonly/"
VAD="$ASSETS/ggml-silero-v6.2.0.bin"; CLIP="$ASSETS/selftest-clip.wav"
R=$(cygpath -w "$WORK/root"); RV=$(cygpath -w "$WORK/rootnv"); RN=$(cygpath -w "$WORK/rootnn")
X=$(cygpath -w "$WORK/exe"); WW=$(cygpath -w "$WORK")
P() { "$WORK/exe/probe.exe" "$@" "$VAD" "$CLIP" 2>&1 | while IFS= read -r line; do line="${line%$'\r'}"; line="${line//"$RV"/<rootnv>}"; line="${line//"$RN"/<rootnn>}"; line="${line//"$R"/<root>}"; line="${line//"$X"/<exe>}"; line="${line//"$WW"/<work>}"; printf '%s\n' "$line"; done; echo "ExitCode=${PIPESTATUS[0]}"; }
# the arguments of each case are: libraryPath order gpu
echo "== case 1: LibraryPath is a file path inside the root, order Cpu"; P "$R\runtime.dll" Cpu cpu
echo "== case 2: LibraryPath is the root folder itself, order Cpu"; P "$R" Cpu cpu
echo "== case 3: LibraryPath is whisper.dll inside runtimes\win-x64, order Cpu"; P "$R\runtimes\win-x64\whisper.dll" Cpu cpu
echo "== case 4: LibraryPath is a file path inside the root, order Vulkan, gpu on"; P "$R\runtime.dll" Vulkan gpu
echo "== case 5: order Cuda12,Vulkan,Cpu with an unloadable runtimes\cuda12\win-x64\whisper.dll"; P "$R\runtime.dll" Cuda12,Vulkan,Cpu gpu
echo "== case 6: order Cuda12,Cpu with the same unloadable cuda12 runtime"; P "$R\runtime.dll" Cuda12,Cpu cpu
echo "== case 7: order Cuda12 only with the same unloadable cuda12 runtime"; P "$R\runtime.dll" Cuda12 cpu
echo "== case 8: REAL Cuda12 pack with the NVIDIA 12.4 libraries beside it, order Cuda12,Vulkan,Cpu, gpu on"; P "$RV\runtime.dll" Cuda12,Vulkan,Cpu gpu
echo "== case 9: REAL Cuda12 pack with the NVIDIA 12.4 libraries beside it, order Cuda12 only, gpu on"; P "$RV\runtime.dll" Cuda12 gpu
echo "== case 10: REAL Cuda12 pack with the NVIDIA 12.4 libraries beside it, order Cuda12,Vulkan,Cpu, gpu off"; P "$RV\runtime.dll" Cuda12,Vulkan,Cpu cpu
echo "== case 11: REAL Cuda12 pack WITHOUT the NVIDIA libraries, order Cuda12,Vulkan,Cpu, gpu on"; P "$RN\runtime.dll" Cuda12,Vulkan,Cpu gpu
echo "== case 12: REAL Cuda12 pack WITHOUT the NVIDIA libraries, order Cuda12,Cpu, gpu off"; P "$RN\runtime.dll" Cuda12,Cpu cpu
echo "== case 13: REAL Cuda12 pack WITHOUT the NVIDIA libraries, order Cuda12 only"; P "$RN\runtime.dll" Cuda12 gpu
# Whisper.net decides whether a CUDA runtime is available (CudaHelper.IsCudaAvailable) by loading cudart by its bare name, so a cudart that is only in the pack's own
# folder is not seen when Cuda12 is not the last runtime listed (case 8). Cases 14 and 15 put a folder on the process PATH.
echo "== case 14: REAL Cuda12 pack WITHOUT the NVIDIA libraries beside it, the three NVIDIA libraries in a folder on PATH, order Cuda12,Vulkan,Cpu, gpu on"; PATH="$WORK/nvpath:$PATH" P "$RN\runtime.dll" Cuda12,Vulkan,Cpu gpu
echo "== case 15: REAL Cuda12 pack WITHOUT the NVIDIA libraries beside it, only cudart64_12.dll on PATH (cuBLAS missing), order Cuda12,Vulkan,Cpu, gpu on"; PATH="$WORK/cudartonly:$PATH" P "$RN\runtime.dll" Cuda12,Vulkan,Cpu gpu
# Cases 16 and 17: cases 8 and 14 again with Whisper.net's debug log switched on (PROBE_LOG=1), reduced to the CUDA lines, to show why Cuda12 was skipped or chosen.
Q() { PROBE_LOG=1 P "$@" | grep -E "CUDA|Cuda driver|^LoadedLibrary|^ExitCode"; }
echo "== case 16: case 8 again with the Whisper.net debug log (the CUDA lines)"; Q "$RV\runtime.dll" Cuda12,Vulkan,Cpu gpu
echo "== case 17: case 14 again with the Whisper.net debug log (the CUDA lines)"; PATH="$WORK/nvpath:$PATH" Q "$RN\runtime.dll" Cuda12,Vulkan,Cpu gpu
