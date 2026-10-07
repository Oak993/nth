#!/usr/bin/env bash
set -u
cd E:/Droppy/wt/fl/sk17-fix || exit 2
export HEAVY_NO_TRAP=1
source E:/Droppy/work/scratch/lead/heavy.sh
heavy_acquire
trap heavy_release EXIT
export MARROW_PORT_DATA_DIR='E:\Droppy\work\scratch\codex-W2-sk17-fix\green-build-data'
mkdir -p "$(cygpath -u "$MARROW_PORT_DATA_DIR")"
dotnet build tests/Marrow.App.Tests -v q --nologo > E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/green-build.log 2>&1
status=$?
cat E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/green-build.log
[ "$status" -eq 0 ] || exit "$status"
bash E:/Droppy/work/claude-verify/one-full.sh E:/Droppy/wt/fl/sk17-fix Marrow.App.Tests '(FullyQualifiedName~SkillsPreparationErrorTests|FullyQualifiedName~AntigravitySkillsLaunchTests|FullyQualifiedName~CursorSkillsLaunchTests|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native' > E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/green-App-launch-traceability.log 2>&1
cat E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/green-App-launch-traceability.log
bash E:/Droppy/work/claude-verify/one-full.sh E:/Droppy/wt/fl/sk17-fix Marrow.Services.Tests '(FullyQualifiedName~AntigravitySkillsComposerTests|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native' > E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/green-Services-composer-traceability.log 2>&1
cat E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/green-Services-composer-traceability.log
