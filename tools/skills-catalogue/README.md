# Skills catalogue builder (unit SK-2, DL-2)

Reads the pins in `src/Marrow.Core/Port/Skills/Catalogue/catalogue.json`, downloads every pinned artifact and every file of every skill set from the vendors' official
sources, checks what can be checked, records sizes and SHA-256 hashes, and writes a **new** `catalogue.json` plus a **change report** for the owner into a folder of its own.
It never writes into the repository. The lead reads the report; only then is the new file copied over the embedded one (ONDEMAND-PLAN.md 2.1: "a pin changes only with the
owner's approval of its change report").

**Run by hand only. It uses the network. No test runs it.** The tests of this unit (`Feature=SK-2`, in `tests/Marrow.Core.Tests/Port/Skills/Catalogue/`) cover the report and the
writer offline.

## Parts

| File | What it is |
| --- | --- |
| `build-catalogue.mjs` | The pin run (Node 22 or newer). All network, hashing and signature work. |
| `sources.json` | How each item is read: repository layouts, release tags, which signature method. Only rules the plan states, or marked `unverified`. |
| `lib.mjs` | The pure helpers (hashes, front matter, trees, lock files). |
| `CatalogueTool.cs` | A single-file .NET 10 program over `Marrow.Core` (`dotnet run tools/skills-catalogue/CatalogueTool.cs -- ...`): `finalize` (derive pin states, run SK-1's gate, write canonical JSON), `report` (the change report), `gate`. It is not a project of the solution. |
| `selftest.mjs` | Offline check of `lib.mjs`: `node tools/skills-catalogue/selftest.mjs`. |
| `rehearsal.mjs` | Offline rehearsal of the whole run against a **fake** GitHub and release server and the real .NET tool: `node tools/skills-catalogue/rehearsal.mjs`. Proves the pieces fit; says nothing about the real sources. |

## Needs

Node 22+, the .NET 10 SDK, and for a full run: `gpg` (Node and LibreOffice signatures), `cosign` **or** `python -m sigstore` (python.org), `pip` and `npm` (package sets).
`GITHUB_TOKEN` (or `--token-env NAME`) in the environment raises GitHub's API limit; it goes to `api.github.com` only and is never printed.

## Run

```
node tools/skills-catalogue/build-catalogue.mjs --out <a new folder> \
  --sigstore-identity <release manager identity> --sigstore-issuer <its OIDC issuer> \
  --node-keys <file with the Node release signing keys> --tdf-key <file with The Document Foundation's release key>
```

The identity and issuer are the ones python.org publishes for the release; the key files must come from the vendors' own pages. Nothing here ships a key or an identity, and
the run does not decide whether a key can be trusted: it records the fingerprint that signed. `--skip-signatures` lets a run go on without them; every item is then reported
as NOT VERIFIED in the change report. Other options: `--only A1,T3` (some items), `--dry-run` (print the plan, fetch nothing), `--reuse-cache` (keep downloads of an earlier
run), `--strict` (the .NET step also fails while any item is unpinned), `--max-files N` (shorten the file lists of the report), `--read-on YYYY-MM-DD`.

## Output (in the folder given to `--out`)

| File | Content |
| --- | --- |
| `catalogue.json` | The new catalogue, canonical, pin states derived from the values. Written whenever the run itself succeeded, even if the gate has findings (so it can be read). |
| `change-report.md` | Versions, hashes, sizes, licence changes and "asks the terms again" flags, item by item, then a **Pin run** section: every verification, warning and note of the run. |
| `catalogue.raw.json`, `run-record.json`, `run.log` | The builder's own output, the machine-readable record of the run, and its log. |

If any item fails (a hash that does not match a published digest, a signature that does not verify, a count the layout rule cannot find) the run writes `catalogue.raw.json` and
`run-record.json` only, exits 1, and says which. Nothing is "fixed up".

## What the run checks

- Repository sets: every file against the size and git blob id in GitHub's tree at the pin; no symbolic links or submodules; each skill's front matter name; counts, folder names
  and sizes against the plan's (differences are warnings in the report, not silent).
- Tools: the exact size the plan prints, the first digits of the digest the plan prints, the digest GitHub or PyPI publishes, and the signature where the plan names one
  (Sigstore for python.org, `SHASUMS256.txt.sig` for Node, `.asc` for LibreOffice).
- Package sets: `pip download` for `win_amd64`, Python 3.14, wheels only, then each wheel against the size and sha256 PyPI lists; `npm install --package-lock-only
  --ignore-scripts`, then each tarball against the integrity the lock records. A set's pin is `lock-` and 16 digits of the hash of its file list.

## After a run: what to expect (details in ED-NEW-f-sk-2)

- Licence files: `licenceFile` is the licence at the root of the repository when there is one; folders that carry their own licences are listed in the Pin run section, and
  an item with none at the root stays unpinned until `licenceFile` is set in `sources.json` or the schema changes. Tools and package sets have no licence file or URL source at
  all, so they stay unpinned the same way.
- Plugins list their skills so that the gate's name rule can be checked; the same skill folder name in two plugins of one item is a gate violation to be decided.
- The new `catalogue.json` replaces plan estimates with measured sizes and nulls with values, so tests that transcribe today's data (the pins, counts and sizes tests of SK-1,
  `UpdateCheckTests` and `ItemTermsTests` of SK-7) move with it.
