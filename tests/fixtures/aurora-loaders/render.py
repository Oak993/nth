"""Offline Chrome references from the locked page, without executing any C# drawing.

Run with the native lock and a heavy slot. Requires installed Chrome and Pillow; no downloads.
Canvas loaders execute the verbatim PAL/prep/LOADERS block. Sheen uses the verbatim CSS
and PLACES[diff].sk; Chrome draws the actual DOM gradient and bars.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

from PIL import Image

root = Path(__file__).resolve().parents[3]
source = root / "porting/first-release/design-src/loading-aurora/loading-aurora.html"
raw = source.read_bytes()
html = raw.decode("utf-8")
css = html.split("<style>", 1)[1].split("</style>", 1)[0]
block = html.split("const PAL =", 1)[1].split("function pane(place, L, rm)", 1)[0]
block = "const PAL =" + block
scratch = Path("E:/Droppy/work/scratch/codex-boot-loaders/chrome")
scratch.mkdir(parents=True, exist_ok=True)
chrome = Path("C:/Program Files/Google/Chrome/Application/chrome.exe")
out = Path(__file__).parent / "png"
out.mkdir(exist_ok=True)
entries = []
for theme in ["dark", "light"]:
    background = "transparent"  # Preserve RGBA for the lead's premultiplied acceptance metric.
    frames = []
    for kind in [2, 4, 5]:
        for scale in [1, 1.25, 1.5, 2]:
            for time in [.6, .84, 1.2]:
                frames.append(dict(kind=kind, scale=scale, time=time, width=180 if kind == 5 else 48 if kind == 2 else 40,
                                   height=88 if kind == 5 else 48 if kind == 2 else 40, theme=theme))
    payload = json.dumps(frames)
    page = f'''<!doctype html><html data-theme="{theme}"><head>
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'">
<style>{css}
html,body{{margin:0;padding:0;overflow:hidden;background:{background}}}
#grid{{position:absolute;left:0;top:0;width:1600px;height:1710px;background:{background}}}
.sample{{position:absolute;overflow:hidden;transform-origin:0 0}}
.sk{{position:relative;width:180px!important;height:88px;display:grid;gap:7px;overflow:hidden;border-radius:10px}}
.sample canvas{{display:block}}
</style></head><body><div id="grid"></div><script>
const PI=Math.PI, lerp=(a,b,u)=>a+(b-a)*u;let DPR=1;
{block}
const frames={payload};
frames.forEach((f,i)=>{{
 const d=document.createElement('div');d.className='sample';
 Object.assign(d.style,{{left:(i%4*400)+'px',top:(Math.floor(i/4)*190)+'px',width:f.width+'px',height:f.height+'px',transform:'scale('+f.scale+')'}});
 if(f.kind===5){{d.innerHTML='<div class="sk">'+PLACES[1].sk+'<div class="sh"></div></div>';d.querySelector('.sh').style.transform='translateX('+(-130+260*((f.time/1.8)%1))+'%)';}}
 else{{DPR=f.scale;const c=document.createElement('canvas');c.style.width=c.style.height=f.width+'px';d.appendChild(c);LOADERS.find(x=>x.n===f.kind).draw(c,f.width,f.time,f.theme);}}
 document.getElementById('grid').appendChild(d);
}});
</script></body></html>'''
    path = scratch / f"frames-{theme}.html"
    path.write_text(page, encoding="utf-8", newline="")
    shot = scratch / f"frames-{theme}.png"
    profile = scratch / f"profile-{theme}"
    args = [str(chrome), "--headless", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
            "--disable-component-update", "--disable-sync", "--disable-default-apps", "--disable-extensions",
            "--host-resolver-rules=MAP * ~NOTFOUND", "--disable-features=MediaRouter,OptimizationHints,Translate",
            "--hide-scrollbars", "--default-background-color=00000000", "--force-device-scale-factor=1", "--window-size=1600,1800", "--virtual-time-budget=500",
            "--run-all-compositor-stages-before-draw", f"--user-data-dir={profile}", f"--screenshot={shot}", path.as_uri()]
    completed = subprocess.run(args, capture_output=True, timeout=60)
    if completed.returncode != 0 or not shot.exists():
        raise RuntimeError(completed.stderr.decode(errors="replace"))
    with Image.open(shot) as mosaic:
        if mosaic.size[0] < 1600 or mosaic.size[1] < 1710:
            raise RuntimeError(f"Chrome screenshot too small: {mosaic.size}")
        for i, f in enumerate(frames):
            x, y = i % 4 * 400, i // 4 * 190
            width, height = round(f["width"] * f["scale"]), round(f["height"] * f["scale"])
            name = f'{f["kind"]}-{theme}-{f["scale"]}-{f["time"]}.png'
            target = out / name
            mosaic.crop((x, y, x + width, y + height)).convert("RGBA").save(target)
            entries.append({**f, "file": name, "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
manifest = {"source_sha256": hashlib.sha256(raw).hexdigest(), "browser": str(chrome),
            "browser_version": os.environ.get("MARROW_CHROME_VERSION"), "tool_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            "viewport": [1600, 1800], "dpr": 1, "canvas_dpr": "per frame scale", "offline": True, "frames": entries}
(Path(__file__).parent / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="")
print(f"Wrote {len(entries)} Chrome frames from locked page {manifest['source_sha256']}")
