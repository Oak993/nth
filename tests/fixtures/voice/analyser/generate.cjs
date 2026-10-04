// Generator for the Web Audio AnalyserNode fixtures of VT-2 (voice-SPEC V-5.3, test V-T18).
//
// What it does: renders deterministic mono float32 signals through the REAL Web Audio AnalyserNode of headless Chrome
// (an OfflineAudioContext with fftSize 2048, smoothingTimeConstant 0.65, minDecibels -100, maxDecibels -30) and, at fixed
// frame positions (suspend points, multiples of the 128-frame render quantum), records what the page's own code reads:
//   - getByteFrequencyData  (1024 bytes: the smoothed, windowed spectrum in dB scaled to 0..255)
//   - getFloatTimeDomainData (the last 2048 samples; checked here to equal the input slice, which proves the alignment)
//   - all / low / mid / high computed with the design's own fileRead() (voice-composer.js:114-127 = orb-audio.ts read()).
// The C# LevelAnalyser must reproduce the bytes and the four values (see LevelAnalyserFixtureTests).
//
// Run (needs only Chrome; no network):  node generate.cjs
//   playwright-core is taken from E:\Droppy\work\tools\pw\node_modules, Chrome from C:\Program Files\Google\Chrome.
// Output (committed): <name>.f32 (the input samples, little-endian float32) and <name>.json (positions and Chrome's reads).

const fs = require('fs');
const path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');

const OUT = __dirname;

// ---- deterministic signals (device-rate mono) -------------------------------------------------------------------
function mulberry32(a) {
  return () => { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}

function tone1k48() {
  const sr = 48000, n = sr * 0.75, x = new Float32Array(n);
  for (let i = 0; i < n; i++) x[i] = Math.fround(0.5 * Math.sin(2 * Math.PI * 1000 * i / sr));
  return { name: 'tone1k-48000', sr, x };
}

function sweep44() {
  // logarithmic sweep 80 Hz -> 16 kHz: every band, and the bins above 16 kHz stay quiet
  const sr = 44100, n = Math.round(sr * 0.75), x = new Float32Array(n), f0 = 80, f1 = 16000, T = n / sr;
  const k = Math.log(f1 / f0);
  for (let i = 0; i < n; i++) { const t = i / sr; x[i] = Math.fround(0.4 * Math.sin(2 * Math.PI * f0 * T / k * (Math.exp(k * t / T) - 1))); }
  return { name: 'sweep-44100', sr, x };
}

function voicey48() {
  // silence, a voiced stretch (140 Hz, 30 harmonics, 4 Hz amplitude wobble), a noise burst, a tone below the
  // 0.004 RMS dead zone (bytes non-zero, level 0), then silence again
  const sr = 48000, n = Math.round(sr * 1.5), x = new Float32Array(n), rnd = mulberry32(7);
  for (let i = 0; i < n; i++) {
    const t = i / sr; let v = 0;
    if (t >= 0.3 && t < 0.9) {
      let s = 0; for (let h = 1; h <= 30; h++) s += Math.sin(2 * Math.PI * 140 * h * t) / h;
      v = 0.12 * s * (0.6 + 0.4 * Math.sin(2 * Math.PI * 4 * t));
    } else if (t >= 0.9 && t < 1.1) v = 0.2 * (rnd() * 2 - 1);
    else if (t >= 1.1 && t < 1.4) v = 0.004 * Math.sin(2 * Math.PI * 440 * t);
    x[i] = Math.fround(v);
  }
  return { name: 'voicey-48000', sr, x };
}

function low16() {
  // a 16 kHz device: the 2000-16000 Hz band is clamped to Nyquist (bin 2048 -> 1023)
  const sr = 16000, n = sr * 1, x = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    const t = i / sr;
    const env = t < 0.7 ? 1 : 0;
    x[i] = Math.fround(env * (0.2 * Math.sin(2 * Math.PI * 300 * t) + 0.1 * Math.sin(2 * Math.PI * 3000 * t) + 0.05 * Math.sin(2 * Math.PI * 7000 * t)));
  }
  return { name: 'mixed-16000', sr, x };
}

const signals = [tone1k48(), sweep44(), voicey48(), low16()];

// ---- the page: runs the real AnalyserNode ------------------------------------------------------------------------
async function renderInChrome(page, sig, positions) {
  const samples = Array.from(sig.x);
  return page.evaluate(async ({ sr, samples, positions }) => {
    const length = Math.ceil(samples.length / 128) * 128;
    const ctx = new OfflineAudioContext(1, length, sr);
    const buf = ctx.createBuffer(1, length, sr);
    buf.getChannelData(0).set(samples);
    const src = ctx.createBufferSource(); src.buffer = buf;
    const an = ctx.createAnalyser(); an.fftSize = 2048; an.smoothingTimeConstant = 0.65;
    src.connect(an); an.connect(ctx.destination); src.start(0);
    const spec = new Uint8Array(an.frequencyBinCount), wave = new Float32Array(an.fftSize);

    // The design's own reader (voice-composer.js fileRead / orb-audio.ts read()), verbatim in its arithmetic.
    function read() {
      an.getByteFrequencyData(spec); an.getFloatTimeDomainData(wave);
      let s = 0; for (let i = 0; i < wave.length; i++) s += wave[i] * wave[i];
      const rms = Math.sqrt(s / wave.length);
      const out = { rms, all: Math.min(1, Math.max(0, rms - 0.004) * 5.5) };
      const n = spec.length;
      const avg = (a, b) => { const bin = (hz) => Math.max(0, Math.min(n - 1, Math.round(hz / (sr / 2) * n))); const i0 = bin(a), i1 = bin(b); let m = 0; for (let i = i0; i <= i1; i++) m += spec[i]; return out.all === 0 ? 0 : Math.min(1, m / (i1 - i0 + 1) / 255 * 2); };
      out.low = avg(30, 200); out.mid = avg(200, 2000); out.high = avg(2000, 16000);
      return out;
    }

    const reads = [];
    const waveMismatch = [];
    for (const p of positions) {
      ctx.suspend(p / sr).then(() => {
        const r = read();
        let b = ''; for (let i = 0; i < spec.length; i++) b += String.fromCharCode(spec[i]);
        // alignment proof: the analyser's window is exactly the input slice [p - 2048, p) (zeros before the start)
        let bad = 0;
        for (let i = 0; i < 2048; i++) { const j = p - 2048 + i; const e = j >= 0 ? Math.fround(samples[j]) : 0; if (wave[i] !== e) bad++; }
        if (bad) waveMismatch.push({ p, bad });
        reads.push({ p, atFrame: Math.round(ctx.currentTime * sr), spectrum: btoa(b), rms: r.rms, all: r.all, low: r.low, mid: r.mid, high: r.high });
        ctx.resume();
      });
    }
    await ctx.startRendering();
    return { reads, waveMismatch };
  }, { sr: sig.sr, samples, positions });
}

(async () => {
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  const page = await browser.newPage();
  await page.goto('about:blank');
  const chromeVersion = browser.version();
  const meta = { chrome: chromeVersion, generated: 'by generate.cjs; deterministic signals; see the header', fftSize: 2048, smoothingTimeConstant: 0.65, minDecibels: -100, maxDecibels: -30, signals: [] };

  for (const sig of signals) {
    const length = Math.ceil(sig.x.length / 128) * 128;
    const step = 128 * Math.max(1, Math.round(sig.sr / 60 / 128));      // about the 60 Hz cadence, on render-quantum boundaries
    const positions = [128 * 3];                                          // a first read before the window is full
    for (let p = step; p <= length - 128; p += step) if (p > positions[positions.length - 1]) positions.push(p);

    const { reads, waveMismatch } = await renderInChrome(page, sig, positions);
    if (waveMismatch.length) throw new Error(sig.name + ': analyser window differs from the input slice: ' + JSON.stringify(waveMismatch.slice(0, 3)));
    if (reads.length !== positions.length) throw new Error(sig.name + ': ' + reads.length + ' reads for ' + positions.length + ' positions');
    reads.sort((a, b) => a.p - b.p);
    for (const r of reads) if (r.atFrame !== r.p) throw new Error(sig.name + ': suspended at ' + r.atFrame + ' not ' + r.p);

    fs.writeFileSync(path.join(OUT, sig.name + '.f32'), Buffer.from(sig.x.buffer, sig.x.byteOffset, sig.x.byteLength));
    const json = { name: sig.name, sampleRate: sig.sr, sampleCount: sig.x.length, chrome: chromeVersion, reads };
    fs.writeFileSync(path.join(OUT, sig.name + '.json'), JSON.stringify(json));
    meta.signals.push({ name: sig.name, sampleRate: sig.sr, sampleCount: sig.x.length, reads: reads.length });
    console.log(sig.name, 'samples', sig.x.length, 'reads', reads.length, 'maxAll', Math.max(...reads.map(r => r.all)).toFixed(3));
  }
  fs.writeFileSync(path.join(OUT, 'manifest.json'), JSON.stringify(meta, null, 1) + '\n');
  await browser.close();
  console.log('Chrome', chromeVersion);
})().catch(e => { console.error(e); process.exit(1); });
