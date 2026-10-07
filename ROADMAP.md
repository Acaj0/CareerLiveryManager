# Roadmap

Detailed engineering plan for Career Livery Manager. The public summary lives on the website (`clm.antoniodeabreu.dev/roadmap`, linked from the app footer); this file is the long version with the design reasoning, so keep the two in sync when priorities change. Shipped work lives in [`CHANGELOG.md`](CHANGELOG.md).

**Nothing here is a promise or a date.** Items are ordered by priority, not by schedule, and any of them can be reshuffled, shrunk or dropped once real work starts.

## How to read an item

Each item lists: why it exists, what the app does today (with file references), the proposed design, the implementation steps, edge cases, how to know it's done, open questions, rough effort and dependencies. Effort scale: **S** (a day or less), **M** (a few days), **L** (a week or more).

## Principles every item must respect

These come from how the app has worked since v1.0 and from bugs already hit in the field.

1. **Never touch Official content.** The app only reads the Official folder; it writes only inside the Community folder and its own `%APPDATA%\CareerLiveryManager` folder.
2. **Never touch packages the app didn't create.** Everything the app owns is identified by `"creator": "CareerLiveryManager"` in `manifest.json` (see `InstalledPackagesManager`).
3. **Never modify the user's original livery download.** Always copy.
4. **Aircraft-specific fixes stay aircraft-specific.** A fix for one aircraft must be gated on its SimObject name and must not run for any other aircraft (the 737 MAX and Caravan fixes are the template). A shared helper is fine; shared behavior is not.
5. **Analyze before changing.** A reported bug is reproduced or traced to a cause before code changes. The v1.2.1 `FixDrFallback` regression, and the wrong first fix for it, are the reason.
6. **Close MSFS before writing.** The app refuses to apply or remove packages while the sim is running (`SimProcessChecker`); new features that write to Community keep that rule.
7. **Logs are the support channel.** Anything non-default the app does (backfills, copies, conversions) is written to `app.log`, so a bug report doesn't require re-sending the livery.

---

# Next up

## 1. Setup validator

**Why.** Folder misconfiguration is the most common way users end up with "it applied but doesn't show up in Career", and it's invisible: the app accepts the wrong folders and then silently behaves wrongly. A real case: a Steam user pointed both Official and Community at `Community2024`, then to get past the validation error copied their own two liveries into `Official2024` so a `manifest.json` existed there. The log showed `0 have detected Career activities` for every aircraft, with no error anywhere.

**Today.**
- `ConfigService.IsValidOfficialPath` returns true if any direct subfolder has a `manifest.json`, regardless of what kind of package it is (`ConfigService.cs`).
- `SetupViewModel.Continue` only checks that validity, that Community isn't empty, and creates Community if missing (`SetupViewModel.cs`).
- `MsfsPathDetector` already reads `InstalledPackagesPath` from MSFS's own `UserCfg.opt` (Steam and Microsoft Store locations) and picks the `Steam`/`OneStore` child of `Official2024`.
- Nothing compares the two paths, nothing checks package types, nothing explains an empty result.

**Design.** A new `SetupValidator` class in Core returns a list of `SetupIssue { Id, Severity (Error|Warning|Info), Message, SuggestedFix? }`. The Setup screen shows them inline; `MainViewModel.GoToAircraftList` re-runs it (the saved config can go stale); the diagnostic report (item 2) includes the results.

Rules:

| Id | Severity | Condition | Message / fix |
|---|---|---|---|
| S01 | Error | Official path equals Community path (case-insensitive, normalized) | "Official and Community point to the same folder." |
| S02 | Error | One path is inside the other | Same family of mistake |
| S03 | Warning | Selected Official folder is the parent (`Official2024`) and has exactly one valid child (`Steam`/`OneStore`) | Offer one-click "use `Official2024\Steam`" |
| S04 | Error | Official folder has no package whose manifest has `content_type` = `AIRCRAFT` | "No official aircraft packages found." Explain aircraft must be downloaded in-game first |
| S05 | Error | Every package in the "Official" folder was created by this app (`creator` = `CareerLiveryManager`) or has `content_type` = `LIVERY` | Catches the copied-liveries workaround; explain what Official is |
| S06 | Warning | `UserCfg.opt` is readable and its `InstalledPackagesPath` points somewhere that differs from the chosen paths | Offer "use the path MSFS itself uses" (this is also the "game moved to another drive" case) |
| S07 | Error | Community folder isn't writable (try creating and deleting a temp file) | Show the OS error |
| S08 | Warning | Official folder path contains `Official2020` / Community contains a 2020 marker | MSFS 2020 vs 2024 mix-up |
| S09 | Info | Aircraft were found but none has Career activities | "Only aircraft with Career activities get per-activity liveries; the rest use the single-livery recipe" - reduces false alarm |
| S10 | Info | Official folder has `StreamedPackages` next to it and aircraft count is low | Explain that streamed aircraft must be fully downloaded in the Content Manager |

**Implementation steps.**
1. `Core/Models/SetupIssue.cs` and `Core/Services/SetupValidator.cs` (pure functions over paths, no UI).
2. Extend `MsfsPathDetector` with a method returning the `UserCfg.opt` path it used and the raw `InstalledPackagesPath`, so S06 and the diagnostic report don't re-parse.
3. `SetupViewModel`: run the validator on Detect/Browse/Continue; block Continue on any Error, allow on Warning with a confirm; bind issues to a list in `SetupView.xaml`.
4. `MainViewModel.GoToAircraftList`: run the validator, and route to Setup with the first Error as the status message (replacing the current ad-hoc check).
5. Log every issue found at Info level when Setup is saved.

**Edge cases.** Network drives and junctions (compare real paths, not strings); trailing slashes and casing; a read-only Community folder; Official folder on another drive; non-English Windows (don't depend on localized folder names); very large Official folders (the manifest scan must only read manifests, never recurse into SimObjects).

**Done when.** The Steam user's exact configuration (both paths = `Community2024`, then liveries copied into `Official2024`) produces S01 then S05 and cannot be saved; a correct Steam, OneStore and Game Pass setup produces no Errors; every rule has a test (see item 3).

**Open questions.** Whether `content_type` is always present in older official manifests (the scanner already relies on it, so likely yes); whether to allow "save anyway" for Errors for unusual installs (proposal: Errors block, Warnings don't).

**Effort.** M. **Depends on.** none (tests, item 3, make it safer).

## 2. Diagnostic report export

**Why.** Support today means asking users for `app.log`, and several sites (flightsim.to) don't accept `.txt` attachments, so users send cropped screenshots of a 42,000-character log. One button that produces a single file removes that friction and puts the useful context in one place.

**Today.** `OpenLogs` in `MainViewModel` opens `%APPDATA%\CareerLiveryManager\logs\app.log` in the default editor. `LogService` appends forever with no rotation. The log has a `Scanned N aircraft...` line on every screen change, which is most of its volume.

**Design.** A header-menu action "Export diagnostic report" producing `CLM-diagnostic-<yyyyMMdd-HHmm>.zip` through a Save dialog (default: Desktop), plus a "Copy summary" button that puts a short plain-text summary on the clipboard. The zip contains:

- `summary.txt`: app version, OS version, .NET runtime, 64/32-bit, system locale, install storefront guess (Steam / Store from which `UserCfg.opt` was found).
- `config.json`: paths, update flag.
- `setup-validation.txt`: the item 1 results.
- `usercfg-packages-path.txt`: only the `InstalledPackagesPath` line, never the whole `UserCfg.opt`.
- `aircraft.txt`: detected aircraft, vendor, SimObject, activities (what the scan log line already prints, one per line).
- `installed-packages/<package>/manifest.json` and `layout-summary.txt` for each package the app created: file tree with sizes, plus the content of every `livery.cfg` and `texture.cfg` (these are small and are exactly what diagnoses fallback bugs). No textures, models or binaries.
- `app.log` (last 7 days or last 2 MB, whichever is smaller).

Privacy: no network upload, ever; the user chooses where the file goes. A "Hide my Windows username" checkbox (default on) rewrites `C:\Users\<name>\` to `C:\Users\<redacted>\` in every text file before zipping. The dialog states exactly what's included.

**Implementation steps.**
1. `DiagnosticReportBuilder` in Core, taking the services it needs and an output path; pure file IO, unit-testable against a temp directory.
2. Add log rotation to `LogService`: roll `app.log` at ~1 MB, keep 3 files (`app.1.log`...), still never throw.
3. Reduce log noise: collapse repeated identical `Scanned...` lines (write only when the result changes).
4. Header menu entry and View (`MainWindow.xaml`), `SaveFileDialog`, success message with "Open folder".
5. "Copy summary" for pasting into a chat when attaching files isn't possible.

**Edge cases.** Log file locked by another process (copy with `FileShare.ReadWrite`); a package with thousands of files (cap the tree listing at a few thousand lines and say so); non-UTF-8 paths; the report while MSFS is running (read-only, always allowed).

**Done when.** A report from a machine with the Steam setup problem makes the cause obvious from `summary.txt` and `setup-validation.txt` without any follow-up questions; redaction removes the username everywhere; log rotation keeps the log under ~3 MB.

**Effort.** M. **Depends on.** item 1 (for the validation section; can ship without it).

## 3. Automated tests

**Why.** Every serious bug so far was a small, mechanical mistake in package building that a test would have caught: the v1.2.1 `FixDrFallback` contract change (the non-activity branch kept passing the old argument), and then the fix itself using one `..` instead of two. The package recipes are now complicated enough (activity slots, DR fallbacks, two aircraft-specific backfills) that manual in-game testing can't be the only safety net.

**Today.** No test project. `PackageBuilder` is one 1,000-line class whose steps are mostly `private static`, reachable only through `Apply`/`ApplyWithNotes`/`Preview`.

**Design.** New project `tests/CareerLiveryManager.Core.Tests` (xUnit, net8.0, references Core only, no WPF). Tests build tiny fake file trees in a temp directory (a fake "Official" aircraft package, a fake third-party livery) and assert on the files `PackageBuilder` writes. Tests go through the public API; where a helper deserves direct testing, expose it with `InternalsVisibleTo` rather than making it public.

Test fixtures (a small `TestTree` helper that writes files from a dictionary of relative path to content, and a helper that writes a `.gltf.fsc` by zlib-compressing a JSON string):

Planned test groups:
- **Non-activity DR recipe**: base folder unprefixed, DR folder `!`-prefixed, `livery.cfg` name rewritten, `texture.cfg` `fallback.1` equals `..\..\<base>\texture` (the regression test, named after the incident).
- **Activity DR recipe**: base nested in `_fallback_base`, `fallback.1` equals `..\_fallback_base\texture`, `_fallback_base` excluded when searching for `texture.cfg`.
- **Activity tags**: `[Specialization]`/`[Tags]` written for freelance slots, not for the generic slot; existing sections replaced rather than duplicated.
- **Gating**: for every aircraft-specific fix (737 MAX backfill/`_common`/display name, Caravan backfill), an assertion that the same input applied to a different SimObject name produces none of its effects. This is the test that enforces principle 4.
- **737 MAX / Caravan backfill**: missing part gets copied invisible; present part untouched; livery with a dangling `livery.xml` pointer replaced; decal materials hidden by name; `pbrMetallicRoughness` added when missing; `ASOBO_material_emissive` removed; synthetic `lod00` created; branded `_albd` texture substituted from the neutral folder when it exists, and the fallback when it doesn't.
- **Sibling copies**: `CopySameVendorSiblingFolders` copies a referenced `_common` folder and records a note; `CopyCrossSimObjectFallbackSibling` for the C172 G1000 case; neither copies anything for a self-contained livery (the Longitude example).
- **`LiverySourceInspector`**: base/DR pairing, `_DR` detection, SimObject detection from path, thumbnail choice.
- **`CareerActivityDetector`**: the slot-name regex, freelance vs adaptive vs static handling, generic slot selection, unknown activity keys skipped.
- **`InstalledPackagesManager`**: lists only packages with the app's `creator`; falls back to folder enumeration for old manifests; scoped thumbnail lookup.
- **Manifest and `layout.json`**: required fields, `career_*` fields, every file under `simobjects` listed with size.
- **`SetupValidator`** (item 1): one test per rule id.
- **`LiveryCfgEditor`**: name rewrite preserves quoting; `FixDrFallback` writes the value verbatim.

CI: a GitHub Actions workflow running `dotnet test` on every push and pull request (Windows runner, since the solution contains a WPF project; the tests themselves don't need it).

**Edge cases the tests should cover.** Paths with spaces, `!`, and non-ASCII characters; livery folders with no `thumbnail`; a source with a `_DR` folder but `useDr` false; two activities of the same livery applied one after another into the same Community folder.

**Done when.** The two historical regressions each have a test that fails on the broken code (verified by temporarily reintroducing the bug), the gating tests exist for both aircraft fixes, and CI is green on `main`.

**Effort.** M for the first pass, then each future bug adds one test. **Depends on.** none; should land before items 4 to 7 so they're built under test.

## 4. Legacy `aircraft.cfg` livery support

**Why.** A lot of older flightsim.to liveries (for example every Baron G58 listing a user searched) are packaged the old FSX/P3D way and are rejected by the app, even though MSFS shows them fine in Free Flight. The Career integration needs the modern `livery.cfg` structure because the app imitates an official livery slot, so the app can't use them as-is, but converting them is mechanical.

**Today.** `LiverySourceInspector.Inspect` only enumerates `livery.cfg` files (`LiverySourceInspector.cs:13`); with none found, `ApplyLiveryViewModel.BrowseLiverySource` shows "No livery.cfg was found in that folder. This livery isn't supported by this tool."

**What the old format looks like.** One `aircraft.cfg` with a `[fltsim.N]` section per livery; each names a livery (`title`), the texture set (`texture=<suffix>`, meaning a sibling folder `texture.<suffix>`, empty meaning the plain `texture` folder), a registration (`atc_id`), and optionally a different model (`model=<suffix>`, meaning `model.<suffix>`; blank means the base model). Free Flight works with these because the game still reads `[fltsim.N]` entries directly; that is a legacy path, not what Career uses.

**Design.** An `AircraftCfgConverter` in Core, called by `LiverySourceInspector` only when a folder has `aircraft.cfg` and no `livery.cfg` under it. It parses the `[fltsim.N]` sections, and for each pure repaint (blank `model=`) produces one modern-layout livery in a cache folder, returned as a normal `LiverySourceInfo` (no DR variant; this format has no such concept). `PackageBuilder` is unchanged: it copies from `BaseFolderPath` regardless of where that folder came from.

Cache layout, never inside the user's download: `%APPDATA%\CareerLiveryManager\converted\<hash-of-source>\<LiverySlug>\` containing:
```
livery.cfg            -> [GENERAL] Name="<title>" (+ atc_id/registration if present)
texture\              -> files copied from texture.<suffix>\
thumbnail\            -> the legacy thumbnail (FSX kept it inside the texture folder) moved here
```

Entries with a non-empty `model=` are skipped with a reason shown to the user, because a different model means a different UV layout and that is not something a texture copy can fix. The converted livery gets a "converted from legacy format" tag in the list.

**Implementation steps.**
1. INI parser tolerant of the old format: `;` and `//` comments, `[fltsim.N]` case-insensitive, duplicate keys, quoted and unquoted values, CRLF/LF, BOM.
2. Resolve the texture folder: `texture.<suffix>` next to `aircraft.cfg`; case-insensitive match; clear note when it's missing.
3. Texture `texture.cfg` handling: legacy texture folders usually carry a `texture.cfg` with `fallback.1=..\texture` (pointing at the aircraft's own base textures). After the copy that relative path no longer points anywhere useful, so it must be rewritten or dropped. This is the single most important unknown (see open questions).
4. Slug and uniqueness: reuse `SanitizePackageName`-style sanitizing; disambiguate duplicate titles with the registration or an index.
5. Wire into `LiverySourceInspector`, add the tag to `LiverySourceInfo` (`IsConvertedFromLegacy`), show it in `ApplyLiveryView.xaml`.
6. Log every conversion (entries converted, entries skipped and why) at Info level, in the same style as the existing apply notes.
7. Replace the old "isn't supported" message with specifics: either "found N legacy liveries" or the real reason nothing was usable.
8. Cache cleanup: remove cached conversions for a source when it's re-added, and expose "clear converted cache" later in the library (item 6).

**Edge cases.** A pack with several `[fltsim.N]` sharing one texture folder; `texture=` blank; texture folder name differing only by case; a pack that bundles its own full `aircraft.cfg` and model for an add-on aircraft (not a repaint of an Asobo aircraft: warn through the existing SimObject-mismatch message); non-ASCII titles; extremely long titles (path length limit); thumbnails missing or in `.bmp`; the pack containing both formats at once (prefer `livery.cfg`, never double-list).

**Done when.** A real legacy Baron pack from flightsim.to is detected, converted, applied, visible in the in-game livery list, and renders correctly (not stretched or missing parts); entries with `model=` are skipped with a clear message; the original download is untouched.

**Open questions (must be answered by an in-game test before committing to the feature).**
- Does a converted livery on an aircraft *without* Career activity behave in Career as expected (it goes through the same "!"-prefixed recipe used for the Longitude and CJ4)?
- Which fallback should the converted `texture.cfg` use so missing textures resolve (the aircraft's own official textures)?
- Are legacy `.dds`/`.bmp` textures accepted inside a modern-layout livery for MSFS 2024, or do they need conversion to KTX2? Free Flight working proves the sim reads them through the legacy path, not through this one.

**Effort.** M to L, mostly the in-game verification of the open questions. **Depends on.** item 3 (parser and converter are very testable).

## 5. Re-check the other activity-supported aircraft

**Why.** Early on, the code assumed only the 737 MAX was built from many separate model parts, and every other activity aircraft was a "single-body model" where a livery covering the visible paint covers everything. That assumption was wrong for the Caravan: a real report showed the same symptom as the 737 MAX (the official scheme's paint bleeding through gaps), and the real Official content confirmed it has `model.airframe`, `backdoor`, `backdoor_right`, `tail`, `wing_left`, `wing_right`. The same may be true of the others, and each would be reported one user at a time.

**Today.** The missing-part backfill (`FillMissingOfficialActivityFiles`) runs only for `asobo_b737max` and `asobo_c208b` (`PackageBuilder.cs`). C172, AT-802, H125, XCub, CL-415 and ES-30 get the plain copy.

**Method.** The local Official2024 install (`D:\msfs24\Official2024\Steam` on the dev machine) makes this an inspection task, not guesswork. For each of the six aircraft and each freelance slot the app uses:
1. List the `model.*` folders of the slot (that's what the backfill would iterate).
2. Decompress the airframe `.gltf.fsc` (plain zlib; the same decompression `PackageBuilder` does) and list material names, to find the employer name/logo decal materials.
3. Compare the slot's part list with what typical third-party liveries for that aircraft provide (pull a few from flightsim.to and compare folder lists).
4. Record the result in a table in `CAREER_LIVERY_RESEARCH.md` (a new section next to section 21 and 22) before any code change.

Decision per aircraft: leave alone, or add a gated branch with its own constants, exactly like the Caravan. Don't generalize to "all activity aircraft".

**Caravan-specific follow-ups found while reading the Official folders (verify before relying on the v1.2.4 Caravan fix).**
- The Caravan's generic "Default" slot, `official_static_01`, turned out to be the float/amphibian variant: it has `model.floats`, `model.backdoor_right_floats` and `model.tail_floats`, no `texture` folder and no `livery.cfg`. The backfill enumerates the official slot's `model.*` folders, so for that slot (and for medevac/scientific slots that also carry `model.floats`) it would backfill floats parts as invisible overlays. A wheeled Caravan wouldn't care, but an amphibian Caravan might lose visible floats. Proposed handling: skip any part whose name contains `floats` in the Caravan branch, then test on both variants.
- The other Caravan activity slots may use material names beyond the set currently hidden (`CUSTOM_Image_00/01`, `CUSTOM_Text_00/01`, `CUSTOM_TEXT_00_LEFT/RIGHT`); inspect every freelance slot's airframe once, not only cargo.
- `cargo_static_01` is used as the neutral texture donor for the cargo category; other categories (rescue, skydive) have no `*_static_01` and silently use no neutral substitution. Check whether that leaves visible branding on those slots.

**Done when.** A written table exists for all eight activity aircraft (parts, decal materials, float/amphibian variants), each with an explicit decision and, where needed, a gated fix plus tests (item 3).

**Effort.** S for the investigation, M per aircraft that needs a fix. **Depends on.** item 3 for the fixes.

---

# Later

## 6. Livery library (the livery manager)

This is the biggest planned change: the app stops being a one-shot installer and becomes a manager that remembers liveries, so any previous livery can be restored and any added livery can be applied to any slot with one click. It also replaces the old plan for a separate "installed liveries" screen and the "undo" idea; they are the same feature. Because it's large, it's split into phases that each ship value on their own.

**Why.** Today applying a livery to an aircraft or activity replaces the previous one: `ApplyLiveryViewModel.ApplyLivery` builds the new package, then deletes the old one (`_installedPackagesManager.Remove`). The only way back is to find the original download again. There is an "Installed packages" screen (`InstalledPackagesView`), but it only lists and removes. Users who like to switch liveries have to browse to the same folder every time.

**Today (relevant facts).**
- A source livery is picked with a folder dialog (`BrowseLiverySource`) and inspected by `LiverySourceInspector`; nothing about it is remembered after Apply.
- The built package lives only in Community, named `career-livery-<simobject>[-<activity>]-<liveryName>`, with `career_simobject`, `career_activity*` fields in its `manifest.json`.
- Replacement is keyed by SimObject + activity key (`ApplyLiveryViewModel`, the `previousPackages` query), so a Cargo livery never removes a Flightseeing one on the same plane.
- The app's own storage is `%APPDATA%\CareerLiveryManager` (`config.json`, `logs\`).

**Design.**

*Storage.* `%APPDATA%\CareerLiveryManager\library\`:
```
library\
  index.json                      list of entries + assignments (see below)
  entries\<entryId>\
    source\                       normalized copy of the livery folder(s) the app can build from
    thumbnail.png
    entry.json                    metadata
  previous\<packageName>\<ts>\    snapshots of packages replaced by an Apply (phase A)
```
An entry is the *source* (the third-party livery), not the built package. That's what makes switching cheap and lets one entry be applied to several slots, and it keeps `PackageBuilder` untouched: it already builds from a `LiverySourceInfo` whose `BaseFolderPath` can point anywhere, including inside the library.

`entry.json`: `id` (GUID), `displayName`, `addedAtUtc`, `originalName` (folder or archive name), `sourceKind` (`Folder|Zip|Rar|SevenZip|LegacyConverted`), `simObjectName` (as detected), `hasDr`, `baseFolderName`, `drFolderName`, `contentHash` (SHA-256 over the sorted relative path + file size list, plus a hash of the small config files; used for de-duplication), `sizeBytes`, `tags`.

`index.json`: `entries` (ids), and `assignments`: for each `(simObjectName, activityKey or "")` the `currentEntryId`, the options used (`useDr`), `appliedAtUtc`, and a short `history` list (most recent first, capped).

*Flow.*
1. **Add**: the user picks a folder, picks or drops a `.zip`/`.rar`/`.7z`. Archives are extracted to a temp folder first. The inspector runs on the result. Each livery pair found becomes an entry; its files are copied into `entries\<id>\source`. Duplicates (same `contentHash`) are detected and offered as "already in your library".
2. **Apply from library**: choosing an entry for an aircraft/activity builds a normal `ApplyLiveryRequest` using the entry's `source` folder, runs `ApplyWithNotes`, updates the assignment and history. The package in Community is regenerated each time from the entry, never edited in place.
3. **Switch / undo**: the aircraft and activity screens show "current" and the other entries previously used there; one click re-runs step 2 with that entry. "Undo last change" is the same action targeting `history[1]`.
4. **Remove**: removing an entry from the library asks whether to also remove it from Community if it's currently applied; the entry's folder is deleted from the library only after confirmation.

*UI.*
- Apply screen: "Add livery" (folder or archive button + a drop zone), then the added liveries appear as selectable cards instead of a one-off dialog.
- Aircraft/activity screen: a strip of the library entries for that aircraft with thumbnails, a "current" badge on the applied one, and an Apply button on the others.
- A global "Library" screen (extends the existing `InstalledPackagesView`): everything saved, grouped by aircraft, with size, date added, where it's currently applied, rename and remove. Footer shows total disk used by the library and a "clean up" action.

**Phases (each one shippable).**

- **Phase A, keep the previous livery (small, high value).** Before `ApplyLivery` deletes the old package, move it into `library\previous\...` instead (still a plain package folder). Add "Restore previous" to the aircraft/activity screen. No new concepts, no archive support, no index yet. This alone delivers "go back with one click" for the livery you just replaced.
- **Phase B, library of sources.** Introduce entries, `index.json`, adding a folder to the library, and applying from the library. The old browse-and-apply flow becomes "add to library, then apply". Existing installed packages show as "installed (source not saved)" with a snapshot, so nothing existing breaks and nothing is lost.
- **Phase C, switching UI.** The per-aircraft strip, history, undo, and the global Library screen with rename/remove/clean-up and disk usage.
- **Phase D, archives and drag and drop.** `.zip` via `System.IO.Compression`; `.rar`/`.7z` via a library such as SharpCompress (MIT-licensed, which has to be vetted and added as a dependency; this is the one decision that needs care). Drop zone on the apply screen accepting folders and archives.

**Implementation steps (sketch).**
1. `LibraryService` (Core): load/save `index.json` atomically (write temp then replace), add/list/remove entries, hash, de-dup, disk usage. All paths under `%APPDATA%\CareerLiveryManager\library`.
2. `ArchiveExtractor` (Core): extract to a temp folder with limits (see edge cases), returns the folder or a typed error (password-protected, corrupt, too large, unsupported).
3. Make `LiverySourceInspector` usable on library folders (it already is: it takes a path).
4. Extend `InstalledPackagesManager.Remove` into a move-to-previous operation (phase A) and keep a hard `Remove` for explicit deletes.
5. ViewModels: extend `ApplyLiveryViewModel` (add-to-library + apply-from-library), `AircraftDetailViewModel`/`AircraftListViewModel` (library strip, current badge), `InstalledPackagesViewModel` (Library screen).
6. Views and drag and drop: `AllowDrop`, `DragOver`/`Drop` handlers on the apply view; keep the existing folder dialog.
7. Log every add/apply/switch/remove with the entry id and package folder, in the same style as today.
8. One-time migration step on first launch of the version that ships phase B: nothing is moved; existing packages are listed as "no source saved".

**Edge cases.**
- Archives: zip-slip (an entry path escaping the temp folder; normalize and reject), huge archives and decompression bombs (cap total size and file count; ask before extracting past a few GB), nested archives (extract one level, tell the user), password-protected archives (clear error), archives with a single top-level wrapper folder, non-UTF-8 entry names, archives that contain several aircraft or both liveries and unrelated files.
- A source folder that changes or disappears after being added (the library keeps its own copy, so it's unaffected; that's a design goal).
- Long paths on Windows (library entry folders use the short GUID, not the livery name).
- Applying the same entry to several activities at once; applying an entry whose SimObject doesn't match the selected aircraft (keep the existing warning).
- Two entries with the same display name; renaming; an entry whose DR variant is missing.
- Disk space: warn when the library exceeds a threshold; "remove duplicates"; don't copy textures twice when a pair shares a base folder.
- Concurrency: two app instances; a crash mid-add (write to a temp folder, rename on success, clean leftovers on startup).
- The sim running (same rule as today: refuse to write to Community).
- Library damage: a corrupt or hand-edited `index.json` (back it up, rebuild entries from the `entries\` folders).

**Done when.** A user can add three liveries for one aircraft (one by folder, one by `.zip`, one by `.rar`), apply each in turn with one click, return to the original with "Undo", close and reopen the app, and still have all three; removing an entry offers to remove it from Community if it's the active one; nothing outside the app's own folders and the app's own Community packages is ever modified.

**Open questions.**
- Archive library choice and licensing (SharpCompress or alternative) and the effect on the zip size of the release.
- Whether the library should store the pristine source as well as the app-normalized copy (proposal: only the normalized copy; the point is the livery, not the packaging).
- How to present an entry that has a DR variant (one card with a toggle, matching today's "Use Dynamic Registration" checkbox).

**Effort.** Phase A: S. Phase B: L. Phase C: M. Phase D: M. **Depends on.** item 3 (strongly recommended: the replace/restore logic is exactly where silent data loss would hide).

## 7. Fleet profiles

**Why.** Once the library exists, remembering "my whole setup" is cheap. After a MSFS update that wipes or breaks the Community folder, or on a new PC, the user shouldn't have to redo every aircraft.

**Design.** A profile is a named snapshot of the `assignments` table (aircraft + activity -> library entry id + options). Actions: save current setup as a profile, apply a profile (rebuilds each package from the library, with a progress list and per-item result), export a profile as a small `.clmprofile` JSON (entry ids, names, hashes; **not** the livery files, which are third-party content that shouldn't be redistributed), import one (entries missing from the library are listed as "missing: add this livery" with its original name and size). Re-applying a profile after a Community wipe is the main use.

**Edge cases.** Missing entries; entries whose aircraft isn't installed on this PC; profile applied while MSFS is running (refuse); a profile with an assignment for an activity that no longer exists on that aircraft after a sim update.

**Effort.** M. **Depends on.** item 6, phases B and C.

## 8. Conflict detector

**Why.** A common support question is "my livery installed but another one shows". The usual cause is another Community package (not created by this app) overriding the same aircraft or livery slot, or a package with a higher `package_order_hint`.

**Design.** Scan the Community folder for third-party packages that contain `simobjects\airplanes\<same simobject>\liveries\<same vendor>` folders, and report: overlapping folder names, whether the slot name matches one this app writes, and `package_order_hint` ordering. Report only; never modify or delete anything the app didn't create (principle 2). Show it in the aircraft screen as a warning with "show details", and include it in the diagnostic report.

**Edge cases.** Large Community folders (limit the scan to manifests and directory names, not file contents); zipped or symlinked packages; packages without a manifest.

**Effort.** M. **Depends on.** none; benefits from item 2.

## 9. Better preview before applying

**Why.** The preview step lists files but says nothing about what the app will adjust. The apply notes (`ApplyResult.Notes`) already compute exactly that, but only after the package is written.

**Design.** Make `PackageBuilder.Preview` return the planned notes: which parts are missing from the livery and will be backfilled invisibly, which sibling folders will be copied, which fallback path will be written. Show it as a short "What the app will adjust" list, with a plain-language line per note. No filesystem writes in preview (it keeps the existing guarantee). Requires making the backfill steps able to run in a dry-run mode, which is a good reason to refactor them into "plan" and "execute" halves under tests (item 3).

**Effort.** M. **Depends on.** item 3.

## 10. Extend distinct per-activity names

**Why.** Only the 737 MAX labels each activity slot of the same livery (`! [Cargo Transport] Livery name`); the other activity aircraft keep the livery's own name, so installing the same livery for two activities shows identical entries in Free Flight.

**Design.** Wait for a real report or for the library work to land, then gate it per aircraft exactly like the 737 MAX (principle 4), never globally. A per-aircraft flag in the recipe table (item 11) makes it a one-line change.

**Effort.** S per aircraft. **Depends on.** none.

---

# Big ideas

## 11. Data-driven fix recipes

**Why.** Each aircraft fix is hardcoded in `PackageBuilder.cs`: constants per aircraft (`B737MaxSimObjectName`, `CaravanSimObjectName`, their decal material sets and neutral scheme names), plus `if SimObjectName == ...` branches. Adding an aircraft or correcting a decal material name means a code change and a release, and the branches multiply. The fixes are all the same machine (backfill missing parts as invisible, hide decal materials by name, pick a neutral texture donor, copy shared siblings, rename the livery) with different parameters.

**Design.** Move the parameters to one JSON file per aircraft, shipped with the app, for example `recipes/asobo_c208b.json`:
```
{
  "simObject": "asobo_c208b",
  "backfillMissingParts": true,
  "skipParts": ["floats"],
  "decalMaterialsToHide": ["CUSTOM_Image_00", "CUSTOM_Image_01", "CUSTOM_Text_00", ...],
  "neutralScheme": "{activity}_static_01",
  "copySameVendorSiblings": false,
  "rewriteDisplayName": false
}
```
`PackageBuilder` loads the recipe for the aircraft being applied and runs the same generic steps. No recipe file means no extra steps, which preserves principle 4 by construction: an aircraft without a recipe is untouched.

**Implementation steps.** Extract the current 737 MAX and Caravan branches into recipes with **identical behavior** (a pure refactor, verified by item 3's gating and backfill tests passing unchanged); add a recipe loader with strict validation (unknown keys and bad values logged and ignored, never crash); add the recipe name and version to the apply notes in the log. Later: allow a user-supplied recipes folder under `%APPDATA%` so a fix can be shared and tried without a new release, and so a community-reported aircraft can be fixed by sending a file.

**Edge cases.** A malformed recipe must never break applying; recipe versioning when the schema changes; two recipes for one aircraft; the recipe format becoming a promise (keep it internal until it has stabilized).

**Done when.** The 737 MAX and Caravan produce byte-identical packages before and after the refactor (compare on the test fixtures), and adding a new aircraft is a new file with no C# change.

**Effort.** L. **Depends on.** item 3 (mandatory: without tests this refactor is how the previous bugs would come back), benefits item 5.

## 12. Localization

**Why.** The user base is international: reports have come in English, with users of other nationalities in the logs, and the app's maintainer works in Portuguese. All UI strings are currently hardcoded English in XAML and ViewModels.

**Design.** Move strings to `.resx` resources, add a language setting (default: follow the system), with Portuguese first and a clear way for users to contribute a language. Keep log messages and the diagnostic report in English regardless of UI language so support stays uniform.

**Edge cases.** Layout growth with longer translations (some buttons and the aircraft grid are tight); strings built from parts (the status messages with paths); pluralization; RTL languages (not planned).

**Effort.** L. **Depends on.** none, but best after the screens from items 1, 2 and 6 are stable to avoid translating strings that are about to change.

## 13. In-app per-activity preview

**Why.** The aircraft screen shows only the stock thumbnail of each activity slot, and the third-party livery's thumbnail is shown only after it's installed. A user can't tell what a livery will look like in Cargo versus Flightseeing before installing.

**Design.** Show the library entry's own thumbnail on the aircraft's activity cards before applying; add a side-by-side "stock vs this livery" view. Rendering the real model with the livery is out of scope (it would mean implementing a glTF/KTX2 renderer); this is about the images the livery already ships.

**Effort.** M. **Depends on.** item 6, phase B.

## Considered and not planned

- **A built-in livery downloader/browser for third-party sites.** It would put the app in the middle of other sites' terms of service and creators' distribution rules, and the app deliberately never redistributes third-party content. Drag-and-drop from the user's own downloads (item 6) gives most of the convenience without that.
- **Cloud sync of the library.** Third-party liveries can't be redistributed; profiles (item 7) store references, not files.
- **A 3D model renderer for previews.** Disproportionate cost for the benefit; see item 13.

---

# Suggested release sequence

Not a schedule; a dependency-aware order that keeps each release small and safe.

| Release | Contents | Why this grouping |
|---|---|---|
| Next | Items 1 and 2 | Cheap, remove most of the support burden, no package-building risk |
| Next | Item 3 (first pass) | Protects every later change; low user-visible risk |
| After | Item 5 investigation, plus item 6 phase A (keep previous livery) | Phase A is small and gives the "undo" users ask for |
| After | Item 4 | Needs the in-game open questions answered first |
| Then | Item 6 phases B, C, D | The big change, built on tests and the earlier groundwork |
| Then | Items 7, 8, 9, 10 | Built on the library |
| Later | Items 11, 12, 13 | Larger investments with the most value after the app is feature-complete |
