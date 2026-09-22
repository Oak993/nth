# P11 RESULT (T-M0-010): PASS, with one defect in the probe text itself

Run: 2026-09-22, 21:31 to 21:38 local (+03:00). Stack: .NET SDK 10.0.401, Velopack vpk 1.2.158 (local tool),
Velopack NuGet 1.2.158. Probe app: `Program.cs` in this folder (package id `MarrowPortP11Probe`, title
"Marrow Port P11 Probe"), self-contained win-x64, WinExe, one update check per run, output to a file, then exit.

## Verdict

| Condition from the probe text | Measured | Result |
| --- | --- | --- |
| `dotnet publish -r win-x64` self-contained | exit 0, 193 files, `coreclr.dll` present; at runtime `corelib_dir == app_dir`, `framework=.NET 10.0.12` (the bundled runtime) | PASS |
| `vpk pack` | exit 0, `Verified VelopackApp.Run()`, Setup.exe 46,974,174 bytes | PASS |
| Setup.exe installs and launches the app as an ordinary desktop app | Setup (not silent) started `%LOCALAPPDATA%\MarrowPortP11Probe\current\MarrowPortP11Probe.exe` with no arguments, which wrote its output 1.66 s after Setup started. The Start menu shortcut launched it again. No new msedge/chrome/firefox/wsl/wslhost/node/dotnet process appeared | PASS |
| Exactly one unauthenticated GET per run | `requests_sent=1` on every network run, `0` on every fixture run. Raw wire capture shows only `Host`, `User-Agent`, `Accept`: no Authorization, no token, no cookie | PASS |
| Success path, real GitLab | HTTP 200, 96,439 bytes, 17 releases decoded (six fields each), newest qualifying `v1.6.1`, `lastError=<nil>` | PASS |
| Unreachable path (`gitlab.invalid`) | `HttpRequestException: No such host is known.`, `lastError` bytes identical to the pinned source literal | PASS |
| PR-UPDATE-020 against a real non-2xx | real GitLab 404, `lastError=GitLab answered with HTTP 404.` byte-identical to the source template | PASS |
| PR-UPDATE-027 all-or-nothing lenient decode | 5 offline fixtures, all as the source specifies (table below) | PASS |
| "including the U+2019 apostrophe in "Couldn't reach GitLab."" | **The pinned source has no U+2019 there.** Its byte is `0x27` (ASCII). The probe emits `0x27`. `E2 80 99` is absent from the output, correctly | **Probe text is wrong. See the defect section** |
| Clean uninstall | `%LOCALAPPDATA%\MarrowPortP11Probe` gone, no Start menu or Desktop shortcut, no uninstall registry key, `%TEMP%\velopack_MarrowPortP11Probe` gone. Checked after all three uninstalls | PASS |

**Verdict: PASS.** The stack can publish, pack, install, launch, check for updates and uninstall. The
"U+2019" clause cannot be met by a faithful port, because the source it claims to describe does not have
U+2019. Byte-for-byte parity with the source (the probe's other clause) needs `0x27`, which is what the probe
emits. If the orchestrator treats the U+2019 wording as a hard condition, this is a FAIL of the probe text,
not of the stack.

## DEFECT: PR-UPDATE-019 and the P11 text say U+2019, but the source has U+0027

SOURCE FACT, read-only `git show` plus `xxd`:

```
$ git -C E:/Droppy/reference/droppy-code show dd2b86efef9e77168f304a86e8a8411877578bbe:DroppyCode/App/UpdateChecker.swift | sed -n 182p | grep -o 'Couldn.t reach GitLab\.' | xxd
00000000: 436f 756c 646e 2774 2072 6561 6368 2047  Couldn't reach G
00000010: 6974 4c61 622e 0a                        itLab..
$ git -C E:/Droppy/reference/droppy-code-417ab7d3 show 1fbf81e61e28a079508a074c01ee1830a9cc23dc:DroppyCode/App/UpdateChecker.swift | sed -n 182p | grep -o 'Couldn.t reach GitLab\.' | xxd
00000000: 436f 756c 646e 2774 2072 6561 6368 2047  Couldn't reach G     (live v1.6.2: same, 0x27)
$ git grep -n "reach GitLab" dd2b86ef -- .     -> only UpdateChecker.swift:182; no .strings/.xcstrings files exist in the tree
```

The spec's own quoted string is ASCII too: `grep -o 'Couldn.\{1,3\}t reach GitLab\.' porting/spec/UPDATE.json | xxd`
gives `...646e 2774...` (7 occurrences, all 0x27), and so does `porting/critiques/stack_decision.json`.
Only the prose ("note the typographic apostrophe U+2019", "uses U+2019, not an ASCII apostrophe") is wrong.

Needed from the orchestrator (a worker must not edit `porting/`): correct PR-UPDATE-019's `expected_result`
and `windows_path` in `porting/spec/UPDATE.json`, the P11 text in `porting/critiques/stack_decision.json`,
and `porting/tasks/T-M0-010.md`, to say U+0027. Any test written from the current prose would fail a faithful
port.

## Byte verification and controls

`verify.py` pulls the three literals out of the pinned blob with `git show` and compares them with each output
file's `lastError=` bytes. It then checks that it rejects four wrong variants. Real output (`py verify.py evidence`):

```
source line 182 literal bytes: 43 6F 75 6C 64 6E 27 74 20 72 65 61 63 68 20 47 69 74 4C 61 62 2E
source line 272 template     : GitLab answered with HTTP \(status).
source line 271 literal      : No release with a disk image was found.
apostrophe byte(s) in source : 27 74 20 (first byte 0x27)
OK   install3_setup_launch.txt    lastError=b'<nil>' requests_sent=b'1'
OK   shortcut_launch.txt          lastError=b'<nil>' requests_sent=b'1'
OK   unreachable.txt              lastError=b"Couldn't reach GitLab." requests_sent=b'1'
OK   http_non2xx.txt              lastError=b'GitLab answered with HTTP 404.' requests_sent=b'1'
OK   loopback.txt                 lastError=b'No release with a disk image was found.' requests_sent=b'1'
OK   fx_valid.txt                 lastError=b'<nil>' requests_sent=b'0'
OK   fx_nulls.txt                 lastError=b'<nil>' requests_sent=b'0'
OK   fx_minimal.txt               lastError=b'No release with a disk image was found.' requests_sent=b'0'
OK   fx_missing_tag.txt           lastError=b"Couldn't reach GitLab." requests_sent=b'0'
OK   fx_type_mismatch.txt         lastError=b"Couldn't reach GitLab." requests_sent=b'0'
CONTROL REJECTED: output vs U+2019 variant (task text)
CONTROL REJECTED: U+2019 variant vs source
CONTROL REJECTED: output vs trailing-dot-dropped
CONTROL REJECTED: 404 output vs HTTP 403 template
E2 80 99 present in unreachable output: False
VERIFY: PASS          (exit 0; re-run on the kept evidence folder after cleanup, same result)
```

Output hex written by the installed app itself: `lastError_utf8_hex=436F756C646E2774207265616368204769744C61622E`
(unreachable) and `4769744C616220616E73776572656420776974682048545450203430342E` (404).

Controls: each one must fail when the thing it checks is missing.

1. **Launch control.** Before install, the install folder, output file, shortcuts and registry key were all
   absent. After the final uninstall, `Start-Process` on the installed exe path gave
   `System.InvalidOperationException: ... The system cannot find the file specified.` and no output file
   appeared (`control output exists: False`). So the launch evidence exists only because the installed app ran.
2. **Byte-comparator control.** Shown above: the verifier rejects the U+2019 variant, a truncated string and a
   wrong status.
3. **Network control.** The unreachable run shows a real DNS failure (`SocketException: No such host is
   known.`) with `requests_sent=1`. The success run shows a real 200 with a body hash
   (`EED5131579BEC3FE4D246F576052FBAEE7E0DEF9659976BCCBAB0E5641938BD9`). Fixture runs show `requests_sent=0`,
   so the counter separates "sent" from "not sent".
4. **Counter control.** This one caught a real bug. The first installed build reported `requests_sent=0` on a
   run that got HTTP 200 with 96,439 bytes (`evidence/install2_default_counterbug.txt`). `HttpClient.Send`
   (sync) goes through `DelegatingHandler.Send`, and the handler had overridden only `SendAsync`. I fixed it,
   rebuilt, repacked, reinstalled and re-ran everything. Every result above comes from the fixed build.

PR-UPDATE-027 fixtures (`fixtures/`, offline, run on both the published exe and the installed exe with the same results):

| Fixture | What it tests | lastError |
| --- | --- | --- |
| `valid.json` | all six fields, unknown extra key ignored, `direct_asset_url` preferred | `<nil>`, newest 1.6.2 via `d.dmg` |
| `nulls.json` | name/description/released_at/upcoming_release/assets all `null`, then absent | `<nil>`, `upcoming_release=false`, `links=0` |
| `minimal.json` | only `tag_name` | `No release with a disk image was found.` |
| `missing_tag.json` | second element lacks `tag_name`, which must fail the whole array | `Couldn't reach GitLab.` (`keyNotFound tag_name`) |
| `type_mismatch.json` | `"name": 5` | `Couldn't reach GitLab.` (`typeMismatch name (Number)`) |

## Commands and real output (trimmed to the lines that decide the result)

Environment:
```
> cmd /c ver                -> Microsoft Windows [Version 10.0.26200.9457]
> dotnet --version          -> 10.0.401
> CPU                       -> AMD Ryzen 7 9800X3D 8-Core Processor   (GPU not relevant to this probe)
> MARROW_PORT_DATA_DIR      -> C:\Users\omera\AppData\Local\Temp\marrow-p11-988834159 (created fresh, deleted at the end)
```

How p1-glass handles the root props (checked by evaluation only, nothing written to p1-glass):
```
> dotnet msbuild porting/probes/p1-glass/p1-glass.csproj -getProperty:ManagePackageVersionsCentrally -getProperty:TreatWarningsAsErrors
  "ManagePackageVersionsCentrally": "true", "TreatWarningsAsErrors": "true"
```
p1-glass has no workaround. It predates `Directory.Packages.props` (p1 was committed in da77ff7 on 2026-09-20;
the props arrived in 90bd97d on 2026-09-22) and still puts `Version=` on its `PackageReference`s. Under
central management that is error NU1008. I did not restore p1-glass, so whether it fails today is UNKNOWN.
p11 keeps both root props (warnings as errors stay on) and pins Velopack with a `PackageVersion` item in its
own csproj. Evaluated: `ManagePackageVersionsCentrally=true, TreatWarningsAsErrors=true, SelfContained=true`.

Tooling (inside this folder, never `-g`):
```
> dotnet new tool-manifest     -> The template "Dotnet local tool manifest file" was created successfully.
> dotnet tool install vpk      -> Tool 'vpk' (version '1.2.158') was successfully installed. Entry is added to the manifest file ...\p11\dotnet-tools.json.
```

Build and pack (final build, 21:36):
```
> dotnet publish p11.csproj -c Release -r win-x64 --self-contained true -o bin\publish
  p11 -> ...\bin\publish\          exit=0   (warnings are errors, so exit 0 means no warnings)
> dotnet vpk --skip-updates --legacyConsole pack --packId MarrowPortP11Probe --packVersion 0.0.1 --packDir bin\publish
      --mainExe MarrowPortP11Probe.exe --packTitle "Marrow Port P11 Probe" --runtime win-x64 --outputDir bin\releases
  [INF] Verified VelopackApp.Run() in 'System.Int32 P11.Program::Main(System.String)'.
  [WRN] No signing parameters provided, 191 file(s) will not be signed.
  [INF] Setup bundle created 'MarrowPortP11Probe-win-Setup.exe'.      exit=0
  MarrowPortP11Probe-win-Setup.exe 46,974,174 bytes, SHA-256 44F3BE67C061C85AB8337FC05A891BAC9DCB6B2B411679838A8FB0B5BD5E0231
  MarrowPortP11Probe-0.0.1-full.nupkg 39,458,526 bytes
```

Install and launch:
```
install#1  21:34:07  Setup.exe --silent --log ...   exit=0 in 2.06 s; only the --veloapp-install hook ran, the app was NOT started (setup.log)
uninstall#1 21:35:21 Update.exe --uninstall --silent  exit=0; folder, both shortcuts, registry key all gone
install#2  21:35:34  Setup.exe --log ... (not silent)  exit=0; "Starting app..." -> output after 2.15 s  (counter bug build)
uninstall#2           exit=0; clean
install#3  21:36:24  Setup.exe --log ... (not silent)  exit=0; output after 1.66 s
  setup3.log: [INFO] Starting app...
              [INFO] About to launch: '...\MarrowPortP11Probe\current\MarrowPortP11Probe.exe' ... with arguments: "...MarrowPortP11Probe.exe"
              [INFO] Installation completed successfully!
  Shortcuts created: ...\Start Menu\Programs\Marrow Port P11 Probe.lnk, ...\Desktop\Marrow Port P11 Probe.lnk
  Registry: DisplayName "Marrow Port P11 Probe", DisplayVersion 0.0.1, InstallLocation C:\Users\omera\AppData\Local\MarrowPortP11Probe
  new browser/wsl/node/dotnet processes since install start: []
```

Output written by the app Setup launched (`evidence/install3_setup_launch.txt`, release lines omitted):
```
started_utc=2026-09-22T18:36:25.0746860+00:00
process_path=C:\Users\omera\AppData\Local\MarrowPortP11Probe\current\MarrowPortP11Probe.exe
self_contained=true
framework=.NET 10.0.12
url=https://gitlab.com/api/v4/projects/86415974/releases?per_page=20
http_status=200
body_bytes=96439
releases_decoded=17
requests_sent=1
update.version=1.6.1
update.downloadURL=https://gitlab.com/droppyformac1/droppy-code/-/releases/v1.6.1/downloads/Droppy-Code.dmg
updateAvailable_vs_1.6.0=true
lastError=<nil>
finished_utc=2026-09-22T18:36:25.5832568+00:00
```
Note: the live Releases API returned 17 releases, v1.0.0 to v1.6.1, and no v1.6.2, even though the upstream
repository commit 1fbf81e6 is v1.6.2. The API is what the app reads, so this is recorded as seen.

Start menu shortcut launch (`Start-Process` on the .lnk; target `...\current\MarrowPortP11Probe.exe`, no args):
output after 0.83 s, `http_status=200`, `releases_decoded=17`, `requests_sent=1`, `lastError=<nil>`.

Other runs of the installed exe (21:36:53 to 21:36:56):
```
unreachable  --url https://gitlab.invalid/...  exit=2  exception=HttpRequestException: No such host is known. (gitlab.invalid:443)  requests_sent=1  lastError=Couldn't reach GitLab.
http_non2xx  --url https://gitlab.com/api/v4/projects/0/releases?per_page=20  exit=2  http_status=404  requests_sent=1  lastError=GitLab answered with HTTP 404.
loopback     --url http://127.0.0.1:48711/...  exit=2  http_status=200 body "[]"  requests_sent=1  lastError=No release with a disk image was found.
  raw request bytes the one-shot server received (evidence/loopback_raw_request.bin, connections=1):
    GET /api/v4/projects/86415974/releases?per_page=20 HTTP/1.1
    Host: 127.0.0.1:48711
    User-Agent: Droppy Code/1.6.0
    Accept: application/json
```
(In the `request=` line of the output files, .NET's header enumerator splits the User-Agent into two product
tokens and prints them as `Droppy,Code/1.6.0`. The bytes actually sent are shown above.)

Final uninstall (21:37:52) and clean check:
```
uninstall#3 exit=0
LOCALAPPDATA\MarrowPortP11Probe exists: False
TEMP\velopack_MarrowPortP11Probe exists: False
StartMenu *P11* / *Marrow Port*: []      (user and all-users Start menu)
Desktop: []                              (user and public desktop)
Uninstall key HKCU: False
probe procs: []
Owner Marrow dir LastWriteTime: 2026-09-22T11:02:02 (same before and after; the probe never used that folder)
```

## Deviations from the probe text, with reasons

1. `vpk` was installed as a local tool (`dotnet new tool-manifest` plus `dotnet tool install vpk`), not with
   `-g`, as the task instructed.
2. The U+2019 clause was not followed, because the source has U+0027 (see the defect section).
3. **User-Agent.** The probe sends the source literal `Droppy Code/1.6.0` (UpdateChecker.swift:108). This is
   UNRESOLVED for production: the UPDATE spec says the rename's reach onto the wire must be decided
   explicitly. The probe did not decide it.
4. The source's 15 s per-request timeout has no direct `HttpClient` equivalent. The probe uses only the 30 s
   whole-resource timeout (ENGINEERING DECISION for the probe, UNRESOLVED for production).
5. The source's HEAD request for the download size (`downloadSize`, :171-173) was left out so that each run
   sends exactly one request. `size` is therefore not measured.
6. Not ported, because they are outside this capability: UserDefaults persistence (`updateChecker_lastChecked`),
   the `isDevelopment` guard, `--preview-update`, notifications, and the -1 "not an HTTP response" sentinel
   (`HttpClient` always returns an HTTP response, so that branch is untested here and UNRESOLVED for the port).
7. `AppVersion` accepts ASCII digits only. Swift's `isNumber` plus `Int()` also rejects non-ASCII digits, so
   the result is the same for every input tried. Not proven exhaustively.
8. Runs beyond the probe text, each sending at most one request: a real GitLab 404 (project 0), a loopback wire
   capture, five offline fixtures, and a Start menu shortcut launch.
9. `Setup.exe --silent` installs but does not start the app (setup.log). Launch evidence therefore comes from
   the non-silent Setup.exe. That run finished on its own in about 1.7 s with no prompt. Whether it showed a
   splash window is UNKNOWN: I did not look at the screen.
10. By default vpk also created a Desktop shortcut (`Shortcuts: Desktop,StartMenuRoot`). Uninstall removed it.
11. Installer signature verification (WinVerifyTrust / X509Certificate2) was not attempted. It stays blocked
    on UNK-0009 (no Windows release asset, no certificate). vpk reported 191+1 unsigned files.

## Files kept and cleanup

Kept: `p11.csproj`, `Program.cs`, `verify.py`, `dotnet-tools.json`, `fixtures/*.json`, `evidence/` (18 files,
the largest 6.6 KB). Deleted: `bin/`, `obj/`, the fresh `MARROW_PORT_DATA_DIR` folder, and my one-shot
loopback server script. Every process I started has exited. Left in place: vpk 1.2.158 and Velopack 1.2.158 in
the shared NuGet package cache (`%USERPROFILE%\.nuget\packages`), which is a shared cache and not a temp folder
I created. Nothing was written under `E:\Droppy\reference`, `%LOCALAPPDATA%\Marrow`, or the installed Marrow app.
No git command changed the port repository.
