# nth

nth is a Windows desktop app for working with AI coding agents (Claude Code, Codex and others): chats, Hydra (one chat
that leads a team of heads), the Dextra assistant, Tactus PC control, the Ignis cost saver, a browser, artifacts,
downloads, composer lights and liquid glass.

**Based on Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app.** nth is a modified version: it began as a
line-by-line port of the macOS app Droppy Code to Windows and has changed since. It is not the original Droppy Code and
is not made by, endorsed by or affiliated with Droppy or Jordy Spruit.

## Start here

- **Contributors:** read [`docs/next-release/README.md`](docs/next-release/README.md) first. It covers the branches, how
  to build and test, where the glass code lives and the rules for pull requests.
- The current release is **1.0.0**, on branch `release/1.0.0`. Work for the next release happens on `next-release`.
  Other branches are the owner's AI agents' working branches; you can ignore them.

## Build

```bash
dotnet --version              # must print 10.0.401 (pinned by global.json)
dotnet build src/Marrow.App -v q --nologo
dotnet run --project src/Marrow.App
```

The code projects keep their original working name, `Marrow.*`; the app itself is named nth.

## License

nth is free software under the **GNU Affero General Public License v3.0** with the additional terms of Droppy Code
(attribution, origin and names). See [`LICENSE`](LICENSE), [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) and the
upstream files in [`src/Marrow.App/Legal/`](src/Marrow.App/Legal/). "Droppy", "Droppy Code" and the Droppy marks belong
to Jordy Spruit and are not licensed with the code (see `src/Marrow.App/Legal/TRADEMARK.md`).
