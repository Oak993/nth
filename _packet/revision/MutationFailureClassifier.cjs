// Reject infrastructure/input ownership failures independently of a runner's historical status.
// Behavioral waits such as a delivered caption command failing to minimize remain admissible.
const patterns = [
  /NATIVE_PRECONDITION/,
  /TopLevel must be a headless window/,
  /activation click did not complete/,
  /requires .*opt-in/,
  /Native child STA did not stop/,
  /Native child Fact or VSTest runner did not finish/,
  /Native child survived its completed VSTest runner/,
  /Native runner exited before the selected Fact/,
  /Native child data\/temp were not isolated/,
  /Injected pointer input would leave the test-owned windows without capture/,
  /A foreign capture target would receive the injected pointer input/,
  /Native pointer target is covered or is no longer the foreground test window/,
  /A mouse button is already held/,
];
function fixtureFailure(tests) {
  for (const test of tests || []) {
    for (const pattern of patterns) {
      if (pattern.test(test.error || '')) return pattern.source;
    }
  }
  return null;
}
module.exports = { fixtureFailure };
if (require.main === module) {
  const assert = require('node:assert/strict');
  assert.ok(fixtureFailure([{error:'Injected pointer input would leave the test-owned windows without capture.'}]));
  assert.ok(fixtureFailure([{error:'NATIVE_PRECONDITION: foreground acquisition denied'}]));
  assert.equal(fixtureFailure([{error:'The second actual maximize caption click did not restore its HWND.'}]), null);
  assert.equal(fixtureFailure([{error:'The actual retained-window resize did not publish its active state on attempt 2.'}]), null);
  assert.equal(fixtureFailure([{error:'Assert.Equal() Failure: Expected Dark Actual Light'}]), null);
  console.log('Mutation failure classifier: 5/5 checks passed');
}
