import test from 'node:test';
import assert from 'node:assert/strict';
import { packetBuildFloorPasses } from './packet_acceptance.mjs';

const floor = 'the solution builds and every test passes, none skipped';
const pending = 'source-derived tests, interaction tests, visual comparison, persistence and cancellation';
const result = (results, status = 1) => ({ status, signal: null, stdout: JSON.stringify({ results }) });
const passed = { gate: 'D', name: floor, status: 'PASS' };
const unfinished = { gate: 'D', name: pending, status: 'NOT_IMPLEMENTED' };

test('packet implementation can pass its build floor while full acceptance remains unfinished', () => {
  assert.equal(packetBuildFloorPasses(result([passed, unfinished])), true);
});

test('a fully passing Gate D also satisfies packet acceptance', () => {
  assert.equal(packetBuildFloorPasses(result([passed, { ...unfinished, status: 'PASS' }], 0)), true);
});

test('failed builds, failed tests, skipped tests and no tests refuse packet acceptance', () => {
  for (const detail of ['build failed', '1 test(s) failed', '1 test(s) SKIPPED', 'no tests ran']) {
    assert.equal(packetBuildFloorPasses(result([{ ...passed, status: 'FAIL', detail }, unfinished])), false);
  }
});

test('missing, duplicated, wrong-gate or unfinished build-floor evidence refuses acceptance', () => {
  for (const results of [[], [unfinished], [passed, passed], [{ ...passed, gate: 'C' }], [{ ...passed, status: 'NOT_IMPLEMENTED' }]]) {
    assert.equal(packetBuildFloorPasses(result(results)), false);
  }
});

test('other failures and newly unfinished checks are never ignored', () => {
  for (const status of ['FAIL', 'NOT_IMPLEMENTED', 'SKIP', 'UNKNOWN']) {
    assert.equal(packetBuildFloorPasses(result([passed, unfinished, { gate: 'D', name: 'additional check', status }])), false);
  }
  assert.equal(packetBuildFloorPasses(result([passed, { ...unfinished, status: 'FAIL' }])), false);
});

test('missing, malformed or abnormally terminated verifier output refuses acceptance', () => {
  for (const output of [null, {}, { status: 1, stdout: '' }, { status: 1, stdout: '{}' }, { status: 1, stdout: 'not json' }, { ...result([passed, unfinished]), status: 2 }, { ...result([passed, unfinished]), signal: 'SIGTERM' }]) {
    assert.equal(packetBuildFloorPasses(output), false);
  }
});
