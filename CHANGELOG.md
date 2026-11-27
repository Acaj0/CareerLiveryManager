# Changelog

This file always lists every release, newest first, with a link to that version's full notes in [`docs/changelog/`](docs/changelog). Bookmark this page - it's the one link that always shows the current state.

## [vB01 (beta)](docs/changelog/vB01.md)
- Experimental, `beta` branch only: simple aircraft that aren't downloaded are listed and can get a livery.

## [v1.3.5](docs/changelog/v1.3.5.md)
- Changed: on the A320neo, A321 and LVFR A330-900, the livery is installed with a `!000` prefix and the Career tags, so Career should pick it over others - not yet confirmed in-game. Apply your livery again after updating. Other aircraft are unchanged.

## [v1.3.4](docs/changelog/v1.3.4.md)
- Fixed: Cessna Citation X liveries showed the standard livery in flight even though the menu showed the custom one. The standard and winglets versions of a livery are now installed together. Citation X only.

## [v1.3.3](docs/changelog/v1.3.3.md)
- Changed: the Pilatus PC-12 NGX is back to a single livery for the whole aircraft; the per-role entries from v1.3.2 are removed (the game doesn't pick a livery per role for it, and the override could leave the plane unpainted). Applying a livery also removes any leftover v1.3.2 role package.

## [v1.3.2](docs/changelog/v1.3.2.md)
- Added: Pilatus PC-12 NGX liveries per role (Medevac, Cargo, Private Charter) with a "Default" entry that keeps the old behaviour - experimental, not yet confirmed in-game.
- Fixed: replacing a livery failed on read-only files (e.g. `panel.cfg`) and left a half-deleted package behind.

## [v1.3.1](docs/changelog/v1.3.1.md)
- Added: a **Community** screen (header button) with livery creators to follow (MR Liveries, Kiwi Flight Sim) and recommended apps, starting with the MSFS Livery Toolkit.

## [v1.3.0](docs/changelog/v1.3.0.md)
- Added: drop a livery `.zip`, `.rar` or `.7z` (or its folder) onto the Apply screen and the app extracts it for you.
- Added: a setup check that explains wrong Official/Community folders, with one-click fixes.
- Added: *Options > Export diagnostic report* for bug reports (nothing is uploaded, your Windows user name is hidden).
- Added: aircraft list filters and Career-activity badges, a size and disk-space summary in the preview, a progress bar, and a safer remove confirmation.
- Changed: clearer messages when a chosen folder isn't a usable livery; log rotation; dark scrollbars.
- Fixed: the preview listed wrong file paths for a Dynamic Registration livery on a Career activity.

## [v1.2.4](docs/changelog/v1.2.4.md)
- Fixed: Cessna 208B Grand Caravan EX Career activity liveries showed the official scheme's branding/color through parts the third-party livery didn't cover - same fix as the 737 MAX, scoped to the Caravan only.

## [v1.2.3](docs/changelog/v1.2.3.md)
- Fixed: a Dynamic Registration livery on an aircraft with no Career activity (e.g. Cessna Citation Longitude, CJ4) could show missing/wrong-colored textures on parts the registration variant doesn't cover - regressed in v1.2.1, activity aircraft (737 MAX, C172, Caravan, etc.) were never affected.

## [v1.2.2](docs/changelog/v1.2.2.md)
- Fixed: on the 737 MAX, a livery missing coverage for one of the plane's parts (wing root, landing gear, tail, or the whole fuselage on a texture-only livery) now renders invisibly instead of showing the wrong color or a demo airline's own branding.
- Fixed: some 737 MAX multi-registration livery packs (a shared "_common" assets folder) made the plane show up blank/white - now detected and copied automatically.
- Fixed: installing the same 737 MAX livery for more than one Career activity showed identical, indistinguishable names in Free Flight - each one now gets an activity label.
- Added: applying a livery now logs exactly what the app backfilled or adjusted, for easier bug reports.

## [v1.2.1](docs/changelog/v1.2.1.md)
- Fixed: a Dynamic Registration livery on an activity-supported aircraft (e.g. 737 MAX) could flicker between default and custom depending on camera angle, and look wrong in the hangar even when correct in a mission.

## [v1.2.0](docs/changelog/v1.2.0.md)
- Added: Career activities support (Cargo, Flightseeing, Skydive, VIP/Charter, etc.) for Cessna 172, Caravan, 737 MAX, AT-802, H125, XCub, CL-415, and ES-30 - a different livery per job, not just one for the whole plane.
- Added: responsive grid for the aircraft selection screen.
- Fixed: some aircraft showed no thumbnail (or a generic gray "Placeholder" image) instead of a real photo in the aircraft list.

## [v1.1.5](docs/changelog/v1.1.5.md)
- Fixed: the setup screen had no scrollbar and could hide the "Continue" button below the visible area, so the app never saved your folders.
- Fixed: a stale/invalid Official folder path crashed the aircraft list with a raw error instead of sending you back to setup.

## [v1.1.4](docs/changelog/v1.1.4.md)
- Changed: feedback/changelog/roadmap links now point to the new site.
- Docs: noted that streamed aircraft must be downloaded in-game first.

## [v1.1.3](docs/changelog/v1.1.3.md)
- Fixed: update downloads timing out after 10 seconds on anything but a very fast connection.

## [v1.1.2](docs/changelog/v1.1.2.md)
- Added: "Detect automatically" button for the Official2024/Community folders (works with Game Pass/Xbox app installs).
- Added: [Game Pass setup guide](docs/GAME_PASS_SETUP.md).

## [v1.1.1](docs/changelog/v1.1.1.md)
- Fixed: "Check for updates automatically" checkbox not showing whether it was on/off.
- Changed: moved "Send feedback" to the footer.

## [v1.1.0](docs/changelog/v1.1.0.md)
- Added: auto-updater (first version that can update itself).
- Added: automatic replacement of a previous livery on the same aircraft.

## [v1.0.0](../../releases/tag/v1.0)
- Initial public release.
