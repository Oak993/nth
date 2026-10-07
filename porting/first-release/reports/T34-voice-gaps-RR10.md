# T34 RR-10 — optional CUDA and NVIDIA companions

**DONE (implementation and fake acceptance); Windows acceptance remains listed below.** Base: `285b6b7a99b39e0e517f34c8d34a1860c945f83f`, `origin/claude/verify-terminal-20261001`. Authority: release audit RR-10 on `origin/codex/release-audit`, OD-0104, OD-0106 CUDA A, voice-SPEC V-16.3.2/V-16.3.3 and BUILD-PLAN §1.1/VT-17. No existing implementation qualified as ALREADY DONE.

## Missing-path proof

At the base, `src/Marrow.App/Port/Voice/Cuda/FailClosedCudaCompanions.cs:6` implements only `Available = false`; installation always fails. `CudaOffer.cs` receives this seam but has no production construction. `VoiceComposition.cs` binds capture/probe/model storage, without a CUDA offer. `VoiceHardwareCard.cs` places the recommendation directly before the preference and note; there is no CUDA control. Repository searches for `new CudaOffer` and `ICudaCompanions` find tests and scripted/fail-closed implementations only. The existing NuGet pack installer supplies the five Whisper files, not NVIDIA's three companion DLLs.

## Change

`NvidiaCompanionArchive` copies URL, compressed size and SHA-256 from the existing VT-0 `voice-pins.json` entries for libcublas 12.4.2.65 and cuda_cudart 12.4.99. `NvidiaCompanionInstaller` uses the shared downloader and extracts only the three allowlisted DLLs from authenticated archives. It stages the complete existing pack plus all companions and a receipt in a sibling directory, then swaps directories. Failed hashes, missing/duplicate members, cancellation or insufficient staging space leave the existing pack intact. The receipt binds current archive identities/hashes and rechecks DLL hashes, allowing repair after corruption. Verified archives/partials remain available after an interrupted attempt. No DLL hash from the separate benchmark copy is treated as a hash of these archives.

Production startup installs a lazy shared offer. The hardware card mounts CUDA A below the recommendation, with the picked strings, 14/20 title and 12/16 detail, 96 px state column, 6 px state gap, 2 px inset progress bar, Download, cancel and installed check. It uses the model-row transitions. Closing/reopening Settings preserves the shared download and cancel target. Reevaluation cannot replace a running state. Cancellation after the probe leaves the previous row; a start gate ensures worker progress cannot overtake initial Downloading delivery. Hash validation and installation run off the UI thread.

## Tests and limits

Tests were added before implementation. The first Services build failed because `NvidiaCompanionInstaller`/archive types did not exist. Tiny in-memory ZIPs and held HTTP handlers cover pins, checksum refusal, exact-entry refusal, cancellation/retry, stale receipts, corruption/repair, low disk space and preservation of the existing pack; no connection or actual native library is used. Existing CUDA offer tests are unchanged. Two additional boundary tests first failed on post-probe cancellation and worker delivery order, then drove those repairs. New App tests cover reevaluation during a held download, exact states, and the actual factory → hardware-card attachment → Download → cancel path. The targeted App run including notes/countdown/Add passed 59/59 before test metadata/fixture organization corrections; the final results are in `T34-voice-gaps-validation.md`.

Windows run: repeat the fake installer and mounted-control tests; exercise a dummy open-file handle refusing the directory swap; compare CUDA A at 100/125/150/200%, light/dark, Reduce Motion and screen-reader focus/live progress. The exact entry paths in the pinned real NVIDIA archives and a complete CUDA loader restart remain native resource acceptance work: this task downloaded no CUDA archive, model or native DLL and exercised no GPU. Do not treat Linux fake acceptance as that native proof.

Engineering entry: ED-557. Independent review found and corrected receipt pin identity, shared lifetime/reevaluation, UI-thread hashing and locked geometry. Other threads' source, tests and references remain untouched.


## Round 2 (2026-10-07)

**ALREADY DONE; awaiting T9.** Lead ruling `45096401f90e1d0ae74a9e156cb9f0cb3fa67e60:R-gaps-round2.md`, T34, accepts RR-10 as done. No RR-10 implementation was changed in Round 2. The final affected App run rechecks its fake offer/installer boundary and mounted controls: 121/121 across the combined scope. The Services voice run remains 491 passed, 12 Windows-native skips. T9 owns the native CUDA companion publication/loader/backend and Windows rendering acceptance listed above.
