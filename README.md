<p align="center">
  <img src="docs/clm-logo-horizontal.svg" width="360" alt="Career Livery Manager" />
</p>

<p align="center">
  Use your custom liveries in Microsoft Flight Simulator 2024 Career Mode.
</p>

<p align="center">
  <a href="https://buymeacoffee.com/acaj0">☕ Buy me a coffee</a> · <a href="CHANGELOG.md">Changelog</a>
</p>

---

## What is this?

Microsoft Flight Simulator 2024's Career Mode does not currently let you choose which livery your owned aircraft uses. The game always defaults to the same paint job, no matter which liveries you have installed.

**Career Livery Manager** works around this by generating a small Community package that registers your chosen livery under the aircraft's official vendor namespace, using an ordering trick the game happens to respect. It never touches your Official content and never edits any third-party livery package in place. Everything it creates lives in its own dedicated folder inside your Community folder, and can be removed with one click.


<img width="979" height="471" alt="1" src="https://github.com/user-attachments/assets/9f8f0347-3818-4a71-bc09-7379781f687d" />

If you want this amazing livery from TimHH, you can have it in your career!

[Cessna Citation Longitude](https://pl.flightsim.to/addon/94977/microsoft-cessna-citation-longitude-n233cl)


## Features

- Detects every aircraft in your Official content folder automatically, with thumbnails.
- Flags aircraft that don't support the modern `livery.cfg` format instead of silently failing on them.
- Lets you pick any third-party livery folder (e.g. downloaded from flightsim.to) and previews exactly what will be written before touching disk.
- Prefers the Dynamic Registration variant when a livery ships one, for a more natural-looking tail number.
- Tracks everything it installs so you can remove a package cleanly later.
- Applying a new livery to an aircraft that already has one automatically replaces the old one, so you never end up with two competing Career Livery Manager packages for the same plane. It only ever touches packages it created itself.
- Checks GitHub Releases on startup and offers a one-click update when a newer version is available.
- Works with both the **Steam** and **Microsoft Store (OneStore)** builds of MSFS 2024.

Prefer doing this by hand, or want to understand the mechanism first? See the [manual step-by-step guide](docs/MANUAL_GUIDE.md).

## How it works, in short

1. You point the app at your `Official2024` folder and your `Community` folder (one-time setup).
2. You pick an aircraft and a livery folder.
3. The app copies that livery into a new Community package, named so it wins the ordering the game already uses internally. If the livery has a Dynamic Registration variant, it wires the fallback texture path correctly.
4. Restart MSFS and the livery shows up on your Career aircraft.

Official game files are never modified. Community folder only.

## Tested aircraft

This is just what I've personally tested. It does not mean other aircraft don't work; most modern aircraft that ship liveries with a `livery.cfg` should work the same way.

**Airbus**
- A310-300
- A330
- A400M Atlas

**Beechcraft**
- Bonanza G36
- King Air 350i

**Boeing**
- 737 MAX

**Cessna / Textron Aviation**
- 185F Skywagon
- 400 Corvalis TT
- Citation CJ4
- Citation Longitude

**Cirrus**
- Vision Jet G2 (SF50)

**Daher**
- TBM 950

**Honda Aircraft Company**
- HondaJet

**Pilatus**
- PC-12
- PC-24

Also reported working by the community (not personally tested by me):

**Diamond Aircraft**
- DA40

## Getting started

1. Download the latest release from the [Releases page](../../releases).
2. Run `CareerLiveryManager.exe`.
3. On first launch, click **Detect automatically** to have it find your `Official2024` and `Community` folders on its own (works for Steam, Microsoft Store, and Xbox app/Game Pass installs). If that doesn't find them, point it at the folders manually - see the troubleshooting note below for Game Pass.
4. Pick an aircraft, pick a livery folder, apply.

> Windows may show a SmartScreen warning the first time you run the `.exe`, since it isn't code-signed. This is expected for a small open-source tool. Click "More info" then "Run anyway".

> **An aircraft not showing up?** MSFS 2024 streams most aircraft on demand - the `Official2024\Steam` (or `\OneStore`) folder itself exists either way, but it'll look empty (or be missing that specific plane) until you've downloaded the aircraft from the in-game Marketplace/Content Manager at least once. Download it in-game first, then try again.

### Game Pass / Xbox app: "no package with a manifest.json was found"

If you installed MSFS 2024 through the Xbox app (Game Pass), your `Official2024` folder isn't next to the game's install folder (e.g. `C:\XboxGames\...`) - it's in a separate, hidden location inside `AppData`. **Detect automatically** on the setup screen should find it for you; if it doesn't, see [docs/GAME_PASS_SETUP.md](docs/GAME_PASS_SETUP.md) for the exact path and how to locate it manually.

## Automatic updates

Starting with **v1.1.0**, the app checks GitHub Releases for a newer version once on startup. If one is found, a small banner offers to download and install it with one click; picking "Later" just dismisses it for that session, nothing is forced. This can be turned off from Options in the header ("Check for updates automatically").

Versions before v1.1.0 (the original v1.0.0 release) don't have this check built in and won't update themselves; you'd need to grab a newer release manually the first time, after which the updater takes over.

Updating downloads the new release's zip to a temp folder, verifies its checksum when the release publishes one, then hands off to a small script that waits for the app to close, replaces the files in its install folder, and relaunches it. It never touches your MSFS Official/Community folders or Career save data - only its own install folder.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/Acaj0/CareerLiveryManager.git
cd CareerLiveryManager
dotnet build
dotnet run --project src/CareerLiveryManager.App
```

To publish a self-contained single-file `.exe`:

```bash
dotnet publish src/CareerLiveryManager.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

### Publishing a release (for the auto-updater to detect it)

1. Bump `<Version>` in `src/CareerLiveryManager.App/CareerLiveryManager.App.csproj` (semantic versioning, e.g. `1.2.0`).
2. Run the packaging script from the repo root:

   ```powershell
   ./scripts/publish-release.ps1
   ```

   It publishes the self-contained build, then writes `dist/CareerLiveryManager-v1.2.0-win-x64.zip` and its `.sha256` checksum - the version in the filename is read straight from the csproj, no manual renaming.
3. Create a GitHub Release tagged `v1.2.0` (matching the csproj version, with the `v` prefix) and upload both files from `dist/` as assets.

The updater always reads the **latest** GitHub Release via the API, so no other configuration is needed.

## Project structure

- `src/CareerLiveryManager.Core`: all file/folder logic (aircraft detection, livery packaging), no UI, testable on its own.
- `src/CareerLiveryManager.App`: WPF UI (MVVM, CommunityToolkit.Mvvm).

## License

MIT, see [LICENSE](LICENSE).

Montserrat and JetBrains Mono are bundled under the [SIL Open Font License](src/CareerLiveryManager.App/Resources/Fonts/OFL.txt).

## Author

Made by [Acaj0](https://github.com/Acaj0). If this saved you some time, a [coffee](https://buymeacoffee.com/acaj0) is always appreciated.
