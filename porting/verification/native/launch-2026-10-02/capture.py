"""OD-0055 launch capture: the port's own Debug build on a clean, redirected data root (ED-212). Records every top-level
window the process shows during its first seconds, screenshots, and what the data root holds afterwards."""
import ctypes, ctypes.wintypes as wt, json, os, subprocess, sys, tempfile, time
from PIL import ImageGrab

user32 = ctypes.windll.user32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))  # per-monitor v2: physical pixels
OUT = os.path.dirname(os.path.abspath(__file__))
EXE = r"E:\Droppy\wt\cl\terminal-verify\src\Marrow.App\bin\Debug\net10.0-windows10.0.19041.0\Marrow.App.exe"

root = tempfile.mkdtemp(prefix="marrow-launch-")
env = dict(os.environ)
env["MARROW_DEV_DATA_ROOT"] = root
for k in list(env):
    if k.endswith("_API_KEY"): env.pop(k)

EnumProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

def windows(pid):
    found = []
    def cb(h, _):
        p = wt.DWORD(); user32.GetWindowThreadProcessId(h, ctypes.byref(p))
        if p.value == pid and user32.IsWindowVisible(h):
            n = user32.GetWindowTextLengthW(h); b = ctypes.create_unicode_buffer(n + 1); user32.GetWindowTextW(h, b, n + 1)
            r = wt.RECT(); user32.GetWindowRect(h, ctypes.byref(r))
            c = wt.RECT(); user32.GetClientRect(h, ctypes.byref(c))
            cls = ctypes.create_unicode_buffer(256); user32.GetClassNameW(h, cls, 256)
            dpi = user32.GetDpiForWindow(h)
            found.append({"hwnd": h, "title": b.value, "class": cls.value, "rect": [r.left, r.top, r.right, r.bottom],
                          "client": [c.right, c.bottom], "dpi": dpi})
        return True
    user32.EnumWindows(EnumProc(cb), 0)
    return found

mon = wt.RECT(); user32.SystemParametersInfoW(0x0030, 0, ctypes.byref(mon), 0)  # SPI_GETWORKAREA
start = time.perf_counter()
proc = subprocess.Popen([EXE], env=env, cwd=os.path.dirname(EXE), stdout=open(os.path.join(OUT,"stdout.txt"),"w"), stderr=open(os.path.join(OUT,"stderr.txt"),"w"))
timeline, seen, shots = [], {}, []
first_main = None
while time.perf_counter() - start < 20:
    t = round((time.perf_counter() - start) * 1000)
    ws = windows(proc.pid)
    for w in ws:
        key = (w["hwnd"], w["title"], tuple(w["rect"]))
        if key not in seen:
            seen[key] = t
            timeline.append({"t_ms": t, **w})
    if ws and first_main is None: first_main = t
    if first_main is not None:
        for at in (300, 1500, 6500, 9000, 14000):
            if t - first_main >= at and at not in [s[0] for s in shots]:
                path = os.path.join(OUT, f"launch-{at}ms.png")
                ImageGrab.grab(all_screens=False).save(path)
                shots.append((at, path, [x["title"] for x in ws]))
    if proc.poll() is not None: break
    time.sleep(0.03)

final = windows(proc.pid)
for w in final: user32.PostMessageW(w["hwnd"], 0x0010, 0, 0)  # WM_CLOSE: the tour, then the main window quits the app
try: proc.wait(10)
except subprocess.TimeoutExpired: proc.kill(); proc.wait()
files = []
for d, _, fs in os.walk(root):
    for f in fs: files.append(os.path.relpath(os.path.join(d, f), root))
json.dump({"exe": EXE, "dataRoot": root, "workArea": [mon.left, mon.top, mon.right, mon.bottom], "firstWindowMs": first_main,
           "timeline": timeline, "shots": shots, "exitCode": proc.returncode, "dataRootFiles": sorted(files)},
          open(os.path.join(OUT, "launch.json"), "w"), indent=1)
print(json.dumps({"firstWindowMs": first_main, "timeline": [(e["t_ms"], e["title"], e["class"], e["rect"], e["client"], e["dpi"]) for e in timeline],
                  "workArea": [mon.left, mon.top, mon.right, mon.bottom], "exit": proc.returncode, "files": sorted(files)}, indent=1))
