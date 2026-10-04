VT-0 loader probe (voice SPEC V-3.2, Q-V-9 C feasibility), first run 2026-10-04 on the owner PC, round 3 run the same day.

What it shows (run-output.txt, 17 cases): Whisper.net 1.9.1 loads a native runtime from a folder it is pointed at through RuntimeOptions.LibraryPath (read
through its directory), for the CPU runtime (case 1), the Vulkan runtime (case 4) and the REAL Whisper.net.Runtime.Cuda12.Windows 1.9.1 runtime (case 9):
with the NVIDIA 12.4 cudart, cublas and cublasLt copied beside the pack's five files in <root>\runtimes\cuda12\win-x64, order [Cuda12], RuntimeOptions.LoadedLibrary
reads Cuda12 and the Silero VAD finds the same three segments as on the CPU. It also shows three things the CUDA offer (VT-7, VT-17) has to design around:
(1) Whisper.net skips the Cuda12 runtime, when later runtimes are listed, unless its own CUDA check finds cudart64_12.dll by its bare name (the process PATH or the
application folder): with the pack and the NVIDIA libraries only in runtimes\cuda12\win-x64 the order [Cuda12, Vulkan, Cpu] loads Vulkan (cases 8, 10, 16); with the
NVIDIA folder on PATH it loads Cuda12 (cases 14, 17). (2) A pack with no NVIDIA libraries anywhere is skipped before any file of it is loaded (case 11). (3) A pack whose
cudart is findable but whose cuBLAS is missing passes the check, fails half way, and then ABORTS THE PROCESS when the next runtime is loaded (case 15; ExitCode=127 is how Git Bash reports
the abort).

What it does not show: inference speed or the transcription itself on CUDA (the probe loads the Silero VAD model only); the CUDA 13 pack (this PC has no cuBLAS 13); the
NVIDIA redistributables as downloaded from NVIDIA (the copies used are NVIDIA 12.4 libraries from the benchmark folder, listed with their sizes and sha256 in
cuda12-pack-imports.txt; the pinned redistributable archives are a different file set, same major version). The fall-back of cases 5 to 7 uses an unloadable stand-in file
for the CUDA runtime; cases 11 to 13 repeat the missing-libraries situation with the real pack.

Round 3 corrected the earlier statement that the NVIDIA copies were no longer on this PC (they are in E:\Droppy\work\voice\bench\bin\cuda\Release) and that the
real pack could not be fetched (it restores from nuget.org with dotnet restore; its SHA-512 equals the nuget.org entry).

Files: probe.csproj.txt and Program.cs.txt (the console; renamed .txt so no build or scan picks them up: copy them back to build; the project references the CPU, Vulkan and
Cuda12 packs, the restore of the last is 250 MB), run.sh (the case folders and the seventeen runs; its first lines say how to call it), run-output.txt (the saved output of
`run.sh <probe build folder> src/Marrow.Services/Port/Voice/Assets <empty scratch folder> <folder with the three NVIDIA 12 libraries>`, the clip being the bundled self-test
clip), cuda12-pack-imports.txt (the pack's five files with size and sha256, the PE import tables of the pack and of the NVIDIA libraries used).
The probe is a scratch console: it is not part of the solution and no test runs it. VoicePinsTests and VoicePinsReview3Tests read run-output.txt and cuda12-pack-imports.txt.
