# Colour baseline bake

The app has an offline, headless `--colour-bake` entry point. It runs before desktop,
settings, providers or network bindings. Nothing is downloaded or published.

```
dotnet Marrow.App.dll --colour-bake --baseline pack.v1.json.gz
dotnet Marrow.App.dll --colour-bake manifest.json pack.v2.json.gz
```

The baseline embeds the checked-in source inputs, pinned toc/ICANN PSL, frozen D3
reservations and computed IR-3 shade tables. It has zero fabricated owner/baked seeds
and zero fabricated placeholder hashes. Unknown makers stay pending until the chain
has decisive evidence.

A manifest names `version`, `images` and `placeholders`. Each image names `maker`,
`path`, official HTTPS `url`, `fetchedAt` (ISO 8601) and `type`. Each placeholder names
`kind` (`hf` or `favicon`), `path`, official HTTPS `url` and `fetchedAt`. Paths resolve
against the manifest. The generator reads the actual artifact, computes SHA-256 and
byte count, decodes through the production Skia decoder, retains its 64 x 64 RGBA
resample and computes the seed with S2. Supplied hashes and colour values are never
accepted. Baked seeds must re-extract from their evidence. The owner obtains official
artifacts locally; tests use synthetic images and a fake decoder only.

`PaletteReleasePack.Bake` accepts a previous verified pack for append-only epochs,
predecessor palettes and the frozen post-pack reserved set. Delivery uses
`PaletteReleasePack.Adopt` with the host's signature verifier; rollback is rejected.
No update-feed delivery is enabled without the owner's distribution decision.

For a forward bake, `previousPack` points to the verified preceding compressed pack.
`catalogueDate` and optional `catalogueAge` must come from the catalogue's captured
HTTP headers; they freeze the pack's market boundary. Never supply a workstation
clock in their place. Withdrawn facts carry that server date for 24 calendar months.
`aliases` is an optional source-authorized maker rename map. Placeholder artifacts
retain their URL, fetch date, measured size and complete hash in the pack itself.

The shipped source-only baseline has no trustworthy captured catalogue Date/Age or
official binary placeholder artifacts. Its market boundary is explicitly absent;
it is not represented as the first real release bake. T9 must produce/inspect that
bake with official artifacts before granting production palette acceptance.
