# Cascadia Mono in the app (ED-107, OD-0020)

Downloaded 2026-09-23 with the owner's approval (OD-0020 item 1).

- Release: microsoft/cascadia-code `v2407.24`,
  `https://github.com/microsoft/cascadia-code/releases/download/v2407.24/CascadiaCode-2407.24.zip`
- Archive size 150,454,761 bytes; SHA-256 `e67a68ee3386db63f48b9054bd196ea752bc6a4ebb4df35adce6733da50c8474`.
- The archive holds no licence file. The licence is the repository's `LICENSE` at the same tag
  (`https://raw.githubusercontent.com/microsoft/cascadia-code/v2407.24/LICENSE`, 4,489 bytes, SIL Open Font License 1.1,
  Reserved Font Name "Cascadia Code"), shipped as `CascadiaMono-OFL.txt`.
- The release contains all six static files ED-107 names. They are copied unmodified from `ttf/static/` to
  `src/Marrow.UI/Assets/Fonts/` (not subset, not renamed). The Nerd Font, Powerline and variable files, and the
  Light, SemiLight and ExtraLight weights, are not shipped.

| File | Bytes | SHA-256 |
| --- | --- | --- |
| CascadiaMono-Regular.ttf | 575,912 | 06520d032ec274fa5040b22c6f4a1d829081b24ba40b2da56dae89bf10c7b481 |
| CascadiaMono-SemiBold.ttf | 581,840 | 8e04c1b811913a20773a3761d8994f15efe3029509c8d0556d74ae989558146d |
| CascadiaMono-Bold.ttf | 581,704 | b22cb603ed23cac36e8444846e1841caca21719a5e852780a8d37ae7a49b0a36 |
| CascadiaMono-Italic.ttf | 435,820 | 8d98a33e7617c14dd1d200b4900bc6c2d5d7165abb5c09e9d2fd51c38ce9c9ff |
| CascadiaMono-SemiBoldItalic.ttf | 436,744 | b07005577596da2218e7a5c16d1084314768280a7303fe2e850094bbdd6028b3 |
| CascadiaMono-BoldItalic.ttf | 439,072 | ecaeab55e6334408f8ff1fde78a99ddaeb3bb7fad4fcd177df8475c88a7d240f |
| CascadiaMono-OFL.txt | 4,489 | 51882cd3cdba4e16f220f44ddb08a635c38c44ea6e0975db2574f4be6f958238 |

The files are AvaloniaResource items of Marrow.UI (`Assets/Fonts/**`); registering the family is the font unit's job
(F.SwiftFont, ED-107).
