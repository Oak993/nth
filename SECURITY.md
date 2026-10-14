# Security policy

nth runs AI agents that can run commands, change files, use the network and, with Tactus, operate apps on your PC.
Security reports are welcome and are taken seriously.

## Supported versions

| Version | Supported |
|---|---|
| nth 1.0.0 (tag `nth-1.0.0`) and newer releases | Yes: fixes ship in the next release or a point release |
| Branch `next-release` | Yes |
| Unofficial builds, forks and modified versions | No |

## Report a vulnerability privately

Do not open a public issue, discussion or pull request about a security problem.

Report it through GitHub's private reporting: https://github.com/Oak993/nth/security/advisories/new (the "Report a
vulnerability" button on the repository's Security tab). Only the repository's maintainers can see it. If that form is
not available, open an issue that says only "Security contact request", with no details, and oak993 will contact you.

Please include:

- the nth version (Settings > About) and the Windows version;
- what an attacker can do, and what they need first (for example a malicious web page, repository, file, MCP server or
  model output);
- the steps to reproduce it, or a proof of concept;
- whether you have told anyone else.

Do not include real secrets or other people's personal data; describe them instead.

## What happens next

nth has a small, independent maintainer. The aim is to:

- acknowledge your report within 7 days;
- tell you within 30 days whether the problem is accepted and what the plan is;
- fix accepted problems as quickly as their severity requires;
- publish an advisory when the fix is released, within 90 days of your report unless we agree otherwise.

You are credited in the advisory and the release notes unless you prefer not to be. There is no bug bounty.

## Scope

In scope:

- the nth app, its installer and its uninstaller;
- the update path: the feed, and the download, check and install of updates;
- ways around nth's own safeguards: approval prompts, runtime modes, the Tactus and Dextra policies (apps that are never
  allowed, guarded actions, purchase hand-over), the Visus browser sandbox, and the isolation of artifacts and web
  surfaces;
- prompt injection (from a web page, file, repository, tool result or MCP server) that makes nth act without the
  approval that nth says it requires;
- exposure of the secrets that nth stores (API keys, MCP tokens), or of local data, to another program, web page or
  network host;
- the local listeners and named pipes that nth opens.

Out of scope (report these to the vendor):

- problems in the AI providers' own command-line tools, models, APIs or services, in WebView2 or in Windows;
- actions that an agent takes after you allowed them, for example in Full access mode; do report it if nth's warnings or
  prompts are misleading;
- social engineering, physical access to the PC, and denial of service against GitHub or a provider;
- automated scanner output without a demonstrated impact.

## Good-faith research

If you make a good-faith effort to follow this policy, oak993 will not take or support legal action against you for
your research, and considers it authorised to the extent that oak993 can authorise it. Good faith means that you test
only on computers and accounts that you own or may use, you do not access, change or keep other people's data beyond
what is needed to show the problem, you do not disrupt services, and you give oak993 reasonable time to fix the problem
before you publish it. oak993 cannot authorise testing of third-party services (AI providers, GitHub, Microsoft);
follow their own policies.

## Official builds

Only builds published at https://github.com/Oak993/nth and through nth's update feed, https://github.com/Oak993/marrow-releases,
are official (see [`TRADEMARK.md`](TRADEMARK.md)). Builds may not be code-signed yet; when a build is signed, nth Setup
shows the signer's name. Do not install nth from anywhere else.

## Safer use

- Prefer the Supervised or Auto-accept edits modes, and use Full access only in projects under version control.
- Keep Tactus off unless you need it, and allow only the apps it needs.
- Never give an agent your passwords or payment details.
