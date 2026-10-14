# Privacy

Last updated: 14 October 2026. This page describes nth 1.0.0 and the code on branch `next-release`. When a release
changes what nth keeps or sends, this page changes in the same release.

## In short

- nth is a desktop app that runs on your PC. oak993, who publishes nth, runs no server for it and receives no data from
  it.
- nth has no telemetry, usage analytics, advertising, crash reporting or nth account.
- nth connects to the internet for the purposes listed below: mainly to talk to the AI providers and services you
  choose, and to check GitHub for updates.
- Your chats, settings and files stay on your PC, except what you, or the agents you run, send to the providers and
  services you use.

## Who is responsible

nth is published by oak993 (https://github.com/Oak993/nth). Because the app sends nothing to oak993, oak993 does not
collect personal data about you through it. The AI providers, websites and other services that you use with nth
receive data from you directly and handle it under their own terms and privacy policies; oak993 does not control them.

## What nth keeps on your PC

- Chats, projects, settings and caches: in `%LOCALAPPDATA%\nth`.
- API keys and MCP tokens that you give nth: in Windows Credential Manager, never in a plain-text file. If Credential
  Manager refuses a key, nth stores nothing.
- The in-app browser (Visus): its own profiles in `%LOCALAPPDATA%\nth\WebView2` (cookies, cache and site storage of the
  sites you open in nth). nth does not import cookies, logins or history from your other browsers.
- Tactus (PC control, off by default): an activity log. Screenshots are not kept after the turn unless you turn that on,
  and typed text is logged only as a length.
- Teach (Dextra): when you record a lesson, the screen frames, typed text (with secrets masked) and spoken lines are kept
  on your PC.
- Voice typing: recordings are not written to disk.
- On its first start, nth 1.0.0 copied the data of its earlier name (Marrow Code) and left the originals in place; you
  may delete them.

The AI coding tools that nth runs (for example Claude Code and Codex) keep their own files, logs and transcripts in
their own folders, under their own policies.

## What nth sends, and where

| When | To | What is sent |
|---|---|---|
| You chat, or an agent or Hydra head works | The AI provider you chose, through its own command-line tool (for example Claude Code or Codex) or its API with your key (for example OpenRouter, DeepSeek, Z.ai, Meta or Command Code) | Your prompts and whatever the agent reads or you attach: files, code, command output, images, screenshots, web pages. The provider handles it under its terms. |
| You sign in to a provider | The provider, through its own sign-in command | nth runs the provider's own sign-in; nth does not ask for your provider password. |
| nth shows plan usage and limits | `api.anthropic.com`; `chatgpt.com`; `api.commandcode.ai` | For the default Claude Code sign-in, nth reads the access token that Claude Code saved on your PC (`.credentials.json`) and sends it to Anthropic to read your usage. For Pi sign-ins, nth reads Pi's saved tokens to ask Anthropic or ChatGPT for your usage. For Command Code, nth reads the API key that Command Code saved. For accounts that you add in nth, nth asks the provider's own tool instead. |
| At start and every 6 hours | `github.com` (repository Oak993/marrow-releases), through Velopack | An update check. GitHub receives your IP address and the header `nth/<version>`. |
| Ignis (on by default) | `raw.githubusercontent.com`: oak993's points file (Oak993/marrow-releases) and LiteLLM's public price file (BerriAI/litellm) | Plain downloads. Ignis also sends short keep-warm turns to your provider between tasks (Claude, OpenRouter and OpenCode chats); these use a little of your plan or credit. |
| Version checks for coding tools | `registry.npmjs.org` (Claude Code), `api.github.com` (Codex releases) | Requests for the latest version number. nth also runs Claude Code's and Codex's own updaters in the background, at most every 12 hours; they contact their vendors. |
| A model maker's colour is not known yet | `openrouter.ai`, `huggingface.co`, `cdn-avatars.huggingface.co`, `t0.gstatic.com` (Google's icon service) | A request for the maker's public logo, which names the maker's web domain. |
| Voice typing | `huggingface.co` (model download, when you start it); `api.openai.com` (only if you choose cloud transcription) | The download request; for cloud transcription, your recording, sent with your OpenAI key. |
| Optional NVIDIA speed-up | `api.nuget.org`, `developer.download.nvidia.com` | Downloads that you start. |
| The browser (Visus) | The sites you open; your search engine (Google, Bing, Brave, Startpage or DuckDuckGo) | Normal web traffic. With "Show search suggestions" on, what you type as a search goes to your search engine; never addresses, text with "@", text with nine or more digits, or anything typed in a private or agent tab. |
| A chat shows a YouTube link | `www.youtube.com` (oEmbed) | The video's id, to show its title. |
| Downloads and skills that you choose | The vendor's own host, for example `github.com`, `raw.githubusercontent.com`, `www.python.org`, `nodejs.org`, `files.pythonhosted.org`, `registry.npmjs.org`, `download.documentfoundation.org`, `cdn.playwright.dev` | The download request. |
| MCP servers that you add | That server and its sign-in page | What the server needs; its tokens are kept in Credential Manager. |
| WebView2 is missing and you press Install | `go.microsoft.com` | Microsoft's installer download. |
| Tactus, and Dextra's PC access, when you turn them on | Your AI provider | Screenshots and on-screen text of the apps you allow nth to control, and the results of its actions. |
| You import MCP servers | Nothing is sent | nth reads the MCP settings of other apps on your PC, for example Claude Code and Codex. |

## Your choices

- Choose which providers and accounts nth uses. If you use a model that runs on your PC, prompts to it stay on your PC.
- Turn off Ignis, search suggestions, cloud voice typing and Tactus in Settings.
- To delete nth's data, uninstall nth and tick "Also remove my chats and settings". This removes `%LOCALAPPDATA%\nth`
  and nth's secrets in Credential Manager. Without it, they are kept so that a reinstall can pick up where you left off.
  Data held by providers or services must be deleted with them.

## Other people's data

If you let agents or Tactus see screens, files or recordings that contain other people's personal data, you are
responsible for having the right to share that data with your provider. Recording other people can require their
consent where you live.

## Building nth from source

The .NET SDK and the Avalonia build tools send anonymous build-time telemetry to Microsoft and to AvaloniaUI unless you
set `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `AVALONIA_TELEMETRY_OPTOUT=1`. This happens only when you build nth, not when
you run it.

## Questions

Open an issue at https://github.com/Oak993/nth/issues. Report a privacy or security problem privately as
[`SECURITY.md`](SECURITY.md) describes.
