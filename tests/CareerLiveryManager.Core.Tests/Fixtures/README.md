Real, tiny archives (under 2 KB each) used by `ArchiveExtractorFormatTests`. They all hold the same
one-livery pack: `Pack\SimObjects\Airplanes\asobo_c172sp\liveries\me\N1\` with a livery.cfg, a
texture.cfg, one texture and a thumbnail.

| File | How it was made |
|---|---|
| `pack.7z` | `tar.exe --format 7zip -cf pack.7z Pack` (Windows' built-in bsdtar) |
| `pack.rar` | `Rar.exe a -r -ep1 -ma5 pack.rar Pack` (RAR5, not solid) |
| `pack-solid.rar` | same with `-s` (solid) |
| `pack-pw.rar` | same with `-pSECRET` (file data encrypted, names readable) |
| `pack-hp.rar` | same with `-hpSECRET` (file names encrypted too) |

The password is `SECRET`; the tests only check that such archives are refused with a clear message.

Two hostile archives, to prove the extractor refuses them:

| File | What's wrong with it |
|---|---|
| `evil-dots.7z` | one entry named `../evil.txt` (`tar.exe -P --format 7zip -cf evil-dots.7z ../evil.txt`) |
| `evil-abs.rar` | one entry with a full drive path (`Rar.exe a -ep3 evil-abs.rar C:\...\evil.txt`) |
