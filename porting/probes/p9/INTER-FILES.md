# Inter italic and weight-800 files in the app (ED-107, OD-0020 item 2)

Downloaded 2026-09-23 with the owner's approval.

- Release: rsms/inter `v4.1`, `https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip`
- Archive size 33,707,794 bytes; SHA-256 `9883fdd4a49d4fb66bd8177ba6625ef9a64aa45899767dde3d36aa425756b11e`.
- The upright text weights come from the Avalonia.Fonts.Inter package (ED-107). The package has no italic and no
  weight 800, so these static files are copied unmodified from the archive's `extras/ttf/` to
  `src/Marrow.UI/Assets/Fonts/`: the italic of every weight the app can reach (400, 500, 600, 700), and weight 800
  upright and italic. The archive's `LICENSE.txt` (SIL Open Font License 1.1) ships as `Inter-OFL.txt`.

| File | Bytes | SHA-256 |
| --- | --- | --- |
| Inter-Italic.ttf | 417,388 | bbc051dd204b5019a1aa0bc0ae2aa8a05ab13e7a3f979fa357631dc7feb6833a |
| Inter-MediumItalic.ttf | 423,452 | 51c2c8d7c36f7c26e6e2678b5c3069b329bde9a081154553b0f5bc2d4fc14075 |
| Inter-SemiBoldItalic.ttf | 423,976 | eff2930c3d3b35d3fcf5f76252b6baef4c3e907d9d2fde1d16cf5d417f8deef4 |
| Inter-BoldItalic.ttf | 425,296 | 948405a16cdc62701da5f4005ed068ca5f4d27061d98f7974ccfc37831d9581d |
| Inter-ExtraBold.ttf | 421,796 | e6756ad5690b77606aa62249a7b420d9902d45cae4b0048a24911fd4324b0a22 |
| Inter-ExtraBoldItalic.ttf | 428,284 | 31b58a00cb9e8d00cb936057285282310e81d2f834461264c543c96983af64a6 |
| Inter-OFL.txt | 4,380 | 262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a |

Registering these beside the package's Inter family is the font unit's job (F.SwiftFont, ED-107).
