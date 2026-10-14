# nth

nth is a Windows desktop app for working with AI coding agents (Claude Code, Codex and others): chats, Hydra (one chat
that leads a team of heads), the Dextra assistant, Tactus PC control, the Ignis cost saver, a browser, artifacts,
downloads, composer lights and liquid glass.

nth is its own application, designed and built by oak993. It includes code from **Droppy Code by Jordy Spruit
(Droppy), https://getdroppycode.app**: nth started from a Windows port of Droppy Code, and that code, modified for nth,
is used under the GNU AGPL v3.0. Much of nth is new and was built for nth, including Dextra, Tactus, Ignis and the
composer lights. nth is not Droppy Code and is not made by, endorsed by or affiliated with Droppy or Jordy Spruit.

## Start here

- **Contributors:** read [`docs/next-release/README.md`](https://github.com/Oak993/nth/blob/next-release/docs/next-release/README.md)
  on branch `next-release` first. It covers the branches, how to build and test, where the glass code lives and the
  rules for pull requests.
- The current release is **1.0.0**: branch `release/1.0.0` (the shipped code is tagged `nth-1.0.0`). Work for the next
  release happens on branch `next-release`.

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
