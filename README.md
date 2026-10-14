# nth

nth is a Windows desktop app for working with AI coding agents (Claude Code, Codex and others): chats, Hydra (one chat
that leads a team of heads), the Dextra assistant, Tactus PC control, the Ignis cost saver, a browser, artifacts,
downloads, composer lights and liquid glass.

nth by oak993, https://github.com/Oak993/nth

nth is its own application, designed and built by oak993. It started from a Windows port of Droppy Code (a
translation of its Swift source for macOS into C# for Windows) and contains that code, modified by oak993, together with
new parts built for nth, such as Dextra, Tactus, Ignis and the composer lights. Hydra is a feature that comes from
Droppy Code.

Based on Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app

nth is not Droppy Code and is not made by, endorsed by or affiliated with Droppy or Jordy Spruit.

## Start here

- **Contributors:** read [`CONTRIBUTING.md`](CONTRIBUTING.md) (rules and the contributor agreement) and
  [`docs/next-release/README.md`](https://github.com/Oak993/nth/blob/next-release/docs/next-release/README.md) on branch
  `next-release` first. It covers the branches, how to build and test, where the glass code lives and how to hand in
  work.
- The current release is **1.0.0**: branch `release/1.0.0` (the shipped code is tagged `nth-1.0.0`). Work for the next
  release happens on branch `next-release`.

## Build

```bash
dotnet --version              # must print 10.0.401 (pinned by global.json)
dotnet build src/Marrow.App -v q --nologo
dotnet run --project src/Marrow.App
```

The code projects keep their original working name, `Marrow.*`; the app itself is named nth.

## Legal

- **License.** nth is free software under the GNU Affero General Public License, version 3 only, with additional terms
  under its section 7: Droppy Code's terms (a) to (c) and nth's terms (d) to (i) (attribution, origin, names, warranty
  and liability, publicity, indemnification). See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE). You may run, study,
  change, share and sell nth under those terms. If you give anyone a copy, or let people use a modified version over a
  network, you must offer them its complete source under the same license.
- **Source.** Every release is tagged `nth-<version>` in this repository; the shipped 1.0.0 is tag `nth-1.0.0`.
- **No warranty.** nth comes with ABSOLUTELY NO WARRANTY, to the extent permitted by law. AI agents that you run with
  nth can run commands, change or delete files and use your provider accounts and credit. Keep backups and use version
  control. See sections 15 to 17 of the License and term (g) in `LICENSE`.
- **Names and logos.** "nth", the nth icon and the names Dextra, Tactus and Ignis are trademarks used by oak993 and are
  not licensed with the code; see [`TRADEMARK.md`](TRADEMARK.md). "Droppy", "Droppy Code" and the Droppy marks belong
  to Jordy Spruit; see [`src/Marrow.App/Legal/TRADEMARK.md`](src/Marrow.App/Legal/TRADEMARK.md).
- **Other companies.** nth is an independent project. It is not made, sponsored or endorsed by Anthropic, OpenAI,
  GitHub, Microsoft, Google, xAI, Meta, DeepSeek, Z.ai, Cursor, OpenRouter or any other provider or service it works
  with. Their names and logos belong to their owners and are used only to say what nth works with. You use those
  services under their own terms.
- **Third-party notices:** [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md),
  [`THIRD_PARTY_NOTICES_RUNTIME.md`](THIRD_PARTY_NOTICES_RUNTIME.md) and the upstream files in
  [`src/Marrow.App/Legal/`](src/Marrow.App/Legal/).
- **Contributions** are accepted only under the contributor agreement in [`CONTRIBUTING.md`](CONTRIBUTING.md).
- **Privacy:** [`PRIVACY.md`](PRIVACY.md). **Security:** [`SECURITY.md`](SECURITY.md); report vulnerabilities
  privately, not in issues.
