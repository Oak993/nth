#!/usr/bin/env bash
set -u
cd E:/Droppy/wt/fl/sk17-fix || exit 2
export HEAVY_NO_TRAP=1
source E:/Droppy/work/scratch/lead/heavy.sh
heavy_acquire
trap heavy_release EXIT
export MARROW_PORT_DATA_DIR='E:\Droppy\work\scratch\codex-W2-sk17-fix\red-build-data'
mkdir -p "$(cygpath -u "$MARROW_PORT_DATA_DIR")"
dotnet build tests/Marrow.App.Tests -v q --nologo > E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/red-build.log 2>&1
status=$?
cat E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/red-build.log
[ "$status" -eq 0 ] || exit "$status"
for provider in Antigravity Cursor; do
 for attempt in 1 2; do
  bash E:/Droppy/work/claude-verify/one-full.sh E:/Droppy/wt/fl/sk17-fix Marrow.App.Tests "(FullyQualifiedName~SkillsPreparationErrorTests&DisplayName~$provider)&FullyQualifiedName!~Native" > E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/red-$provider-$attempt.log 2>&1
  cat E:/Droppy/work/scratch/codex-W2-sk17-fix/evidence/red-$provider-$attempt.log
 done
done
