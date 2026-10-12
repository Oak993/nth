// PORT-ONLY: Tactus, canvas parity for the pill's motion (P30; ED-NEW-pcctl-30; DESIGN.md 10.11 "Canvas parity").
//
// Drives the design canvas (E:/Droppy/work/design/pc-control-ui/index.html, pill v2 "P9" and the card pill "P10") in a
// headless Chromium (Edge or Chrome, found on this PC) with requestAnimationFrame sampling, for the gestures of the
// frame-capture suite at 1280 and 375 px wide, with and without Reduce Motion, and prints one "metric" line per gesture
// in the same words PillFrameTests prints for the build (dotnet test ... --logger "console;verbosity=detailed"), so the
// two can be compared number for number. Needs Node 22 or later (its own fetch and WebSocket; no packages).
//
//   node scripts/pill-canvas-metrics.mjs [--canvas <index.html>] [--browser <path to msedge.exe or chrome.exe>]
//
// It opens only the canvas file, in a throwaway profile under %TEMP% that it deletes after; it sends no input outside
// the page and reaches no network.
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const canvas = option('--canvas', 'E:/Droppy/work/design/pc-control-ui/index.html');
const browser = option('--browser', [
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
].find(existsSync));
if (!existsSync(canvas)) throw new Error(`no canvas at ${canvas}`);
if (!browser || !existsSync(browser)) throw new Error('no Edge or Chrome found; pass --browser');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---- the browser, through the DevTools protocol ----
const profile = mkdtempSync(join(tmpdir(), 'tactus-canvas-'));
const child = spawn(browser, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--no-first-run',
  '--no-default-browser-check', '--disable-extensions', '--disable-background-timer-throttling', 'about:blank'], { stdio: 'ignore' });
let port;
for (let i = 0; i < 100 && !port; i++) {
  await sleep(100);
  const file = join(profile, 'DevToolsActivePort');
  if (existsSync(file)) port = readFileSync(file, 'utf8').split('\n')[0].trim();
}
if (!port) throw new Error('the browser did not open its DevTools port');
const page = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find((t) => t.type === 'page');
const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let nextId = 1;
const waiting = new Map();
const events = [];
socket.onmessage = (message) => {
  const data = JSON.parse(message.data);
  if (data.id && waiting.has(data.id)) {
    const { resolve, reject } = waiting.get(data.id);
    waiting.delete(data.id);
    if (data.error) reject(new Error(JSON.stringify(data.error))); else resolve(data.result);
  } else if (data.method) events.push(data.method);
};
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const id = nextId++;
  waiting.set(id, { resolve, reject });
  socket.send(JSON.stringify({ id, method, params }));
});
const evaluate = async (expression) => {
  const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
  return result.result.value;
};

// ---- the sampler, run inside the page ----
// One gesture: settle the pill where it starts, then run its steps at their times while every animation frame records
// the pill's drawn box in canvas units (rel() undoes the stage's fit), its logical anchor and width (the springs), and
// Stop's opacity.
const sampler = async (g) => {
  const pill = g.card ? P10 : P9;
  const wrap = g.card ? st10 : st9;
  const raf = () => new Promise((r) => requestAnimationFrame(r));
  const reduceOn = () => document.querySelector('#rm9').getAttribute('aria-checked') === 'true';
  if (reduceOn() !== g.reduce) document.querySelector('#rm9').click();
  wrap.scrollIntoView({ block: 'center' });
  for (const step of g.setup) pill[step[0]](...step.slice(1));
  for (let i = 0; i < 150; i++) await raf();
  const frames = [];
  const steps = g.steps.map((s) => ({ at: s[0], call: s[1], args: s.slice(2), done: false }));
  let t0 = null;
  for (;;) {
    const now = await raf();
    if (t0 === null) t0 = now;
    const t = now - t0;
    for (const s of steps) {
      if (s.done || t < s.at) continue;
      s.done = true;
      if (s.call === 'openCard') openCard(); else if (s.call === 'closeCard') closeCard(); else if (s.call === 'addAsk') addAsk(...s.args); else pill[s.call](...s.args);
    }
    const box = rel(pill.el, wrap);
    const stop = pill.stp ? Number(getComputedStyle(pill.stp).opacity) : 1;
    frames.push({ t, x: box.x, y: box.y, w: box.w, h: box.h, ax: sVal(pill.sx, now), aw: sVal(pill.sw, now), stop });
    if (t > g.ms) break;
  }
  return frames;
};

// ---- the gestures (the frame-capture suite's, as far as the canvas has them) ----
const gestures = [
  { name: 'travel', setup: [['goAnchor', 'left']], steps: [[0, 'goAnchor', 'right']], ms: 1200 },
  { name: 'retarget', setup: [['goAnchor', 'left']], steps: [[0, 'goAnchor', 'right'], [130, 'goAnchor', 'left']], ms: 1500 },
  { name: 'width', setup: [['setGroup', 'none'], ['setText', 'Stopped', 'All apps', true]], steps: [[0, 'setGroup', 'run'], [0, 'setText', 'Using Excel', 'Filling row 12 of 40']], ms: 900 },
  { name: 'group swap', setup: [['setGroup', 'run'], ['setText', 'Using Excel', 'Filling row 12 of 40', true]], steps: [[0, 'setGroup', 'paused'], [0, 'setText', 'Paused in Excel', 'Ctrl+Alt+Space resumes'], [80, 'setGroup', 'run'], [80, 'setText', 'Using Excel', 'Filling row 12 of 40']], ms: 900 },
  { name: 'card', card: true, setup: [], steps: [[0, 'addAsk', 'ap'], [40, 'openCard'], [700, 'closeCard']], ms: 1600 },
];

const metric = (label, frames) => {
  const first = frames[0];
  const last = frames[frames.length - 1];
  let step = 0;
  let widthStep = 0;
  let reversals = 0;
  let lastDirection = 0;
  for (let i = 1; i < frames.length; i++) {
    step = Math.max(step, Math.abs(frames[i].x - frames[i - 1].x));
    widthStep = Math.max(widthStep, Math.abs(frames[i].w - frames[i - 1].w));
    const d = Math.sign(Math.round((frames[i].x - frames[i - 1].x) * 100));
    if (d !== 0 && lastDirection !== 0 && d !== lastDirection) reversals++;
    if (d !== 0) lastDirection = d;
  }
  const direction = Math.sign(last.x - first.x);
  const overshoot = direction === 0 ? 0 : Math.max(0, ...frames.map((f) => (f.x - last.x) * direction));
  const widthOvershoot = Math.max(0, ...frames.map((f) => (f.w - last.w) * Math.sign(last.w - first.w || 1)));
  let landed = null;
  for (let i = frames.length - 1; i >= 0 && Math.abs(frames[i].x - last.x) <= 1; i--) landed = frames[i].t;
  const intervals = frames.slice(1).map((f, i) => f.t - frames[i].t).sort((a, b) => a - b);
  const p99 = intervals[Math.min(intervals.length - 1, Math.floor(intervals.length * 0.99))];
  const fixed = (v) => (v === null || v === undefined ? 'n/a' : Number(v).toFixed(2));
  console.log(`metric ${label}: frames ${frames.length}, largest step ${fixed(step)} DIP, overshoot ${fixed(overshoot)} DIP, `
    + `within 1 DIP from ${fixed(landed)} ms, width ${fixed(first.w)} to ${fixed(last.w)} DIP (largest step ${fixed(widthStep)}, `
    + `overshoot ${fixed(widthOvershoot)}), reversals ${reversals}, Stop opacity min ${fixed(Math.min(...frames.map((f) => f.stop)))}, `
    + `frame p99 ${fixed(p99)} ms`);
};

try {
  await send('Page.enable');
  await send('Runtime.enable');
  for (const width of [1280, 375]) {
    for (const reduce of [false, true]) {
      await send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: width < 500 });
      await send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: reduce ? 'reduce' : 'no-preference' }] });
      events.length = 0;
      await send('Page.navigate', { url: pathToFileURL(canvas).href });
      for (let i = 0; i < 100 && !events.includes('Page.loadEventFired'); i++) await sleep(100);
      await sleep(500);
      for (const g of gestures) {
        const label = `${g.name} at ${width} px${reduce ? ', Reduce Motion' : ''}`;
        try {
          metric(label, await evaluate(`(${sampler.toString()})(${JSON.stringify({ ...g, reduce })})`));
        } catch (error) {
          console.log(`metric ${label}: not measured (${error.message.split('\n')[0]})`);
        }
      }
    }
  }
} finally {
  socket.close();
  child.kill();
  await sleep(500);
  rmSync(profile, { recursive: true, force: true });
}
