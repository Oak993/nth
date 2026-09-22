// DONE records IMPLEMENTED, not VERIFIED. The build/test floor is required now;
// full Gate D remains strict and reports its explicitly unfinished acceptance work.
const BUILD_FLOOR = 'the solution builds and every test passes, none skipped';
const UNFINISHED_ACCEPTANCE = 'source-derived tests, interaction tests, visual comparison, persistence and cancellation';

export function packetBuildFloorPasses(output) {
  if (!output || ![0, 1].includes(output.status) || output.signal) return false;
  try {
    const report = JSON.parse(output.stdout);
    if (!Array.isArray(report.results)) return false;
    const floors = report.results.filter((r) => r.gate === 'D' && r.name === BUILD_FLOOR);
    if (floors.length !== 1 || floors[0].status !== 'PASS') return false;
    return report.results.every((r) => r.gate === 'D' && (
      r.status === 'PASS' ||
      (r.name === UNFINISHED_ACCEPTANCE && r.status === 'NOT_IMPLEMENTED')
    ));
  } catch {
    return false;
  }
}
