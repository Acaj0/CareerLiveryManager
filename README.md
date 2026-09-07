<p align="center">
  <img src="docs/clm-logo-horizontal.svg" width="360" alt="Career Livery Manager" />
</p>

<p align="center">
  Use your custom liveries in Microsoft Flight Simulator 2024 Career Mode.
</p>

<p align="center">
  <a href="https://buymeacoffee.com/acaj0">☕ Buy me a coffee</a>
</p>

---

## What is this?

Microsoft Flight Simulator 2024's Career Mode does not currently let you choose which livery your owned aircraft uses — the game always defaults to the same paint job, no matter which liveries you have installed.

**Career Livery Manager** works around this by generating a small Community package that registers your chosen livery under the aircraft's official vendor namespace, using an ordering trick the game happens to respect. It never touches your Official content and never edits any third-party livery package in place — everything it creates lives in its own dedicated folder inside your Community folder, and can be removed with one click.

## Features

- Detects every aircraft in your Official content folder automatically, with thumbnails.
- Flags aircraft that don't support the modern `livery.cfg` format instead of silently failing on them.
- Lets you pick any third-party livery folder (e.g. downloaded from flightsim.to) and previews exactly what will be written before touching disk.
- Prefers the Dynamic Registration variant when a livery ships one, for a more natural-looking tail number.
- Tracks everything it installs so you can remove a package cleanly later.
- Works with both the **Steam** and **Microsoft Store (OneStore)** builds of MSFS 2024.

## How it works, in short

1. You point the app at your `Official2024` folder and your `Community` folder (one-time setup).
2. You pick an aircraft and a livery folder.
3. The app copies that livery into a new Community package, named so it wins the ordering the game already uses internally, and — if the livery has a Dynamic Registration variant — wires the fallback texture path correctly.
4. Restart MSFS and the livery shows up on your Career aircraft.

Official game files are never modified. Community folder only.

## Getting started

1. Download the latest release from the [Releases page](../../releases).
2. Run `CareerLiveryManager.exe`.
3. On first launch, point it at your `Official2024\Steam` (or `Official2024\OneStore`) folder and your `Community` folder.
4. Pick an aircraft, pick a livery folder, apply.

> Windows may show a SmartScreen warning the first time you run the `.exe`, since it isn't code-signed. This is expected for a small open-source tool — click "More info" → "Run anyway".

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/Acaj0/career-livery-manager.git
cd career-livery-manager
dotnet build
dotnet run --project src/CareerLiveryManager.App
```

To publish a self-contained single-file `.exe`:

```bash
dotnet publish src/CareerLiveryManager.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Project structure

- `src/CareerLiveryManager.Core` — all file/folder logic (aircraft detection, livery packaging), no UI, testable on its own.
- `src/CareerLiveryManager.App` — WPF UI (MVVM, CommunityToolkit.Mvvm).

## License

MIT — see [LICENSE](LICENSE).

Montserrat and JetBrains Mono are bundled under the [SIL Open Font License](src/CareerLiveryManager.App/Resources/Fonts/OFL.txt).

## Author

Made by [Acaj0](https://github.com/Acaj0). If this saved you some time, a [coffee](https://buymeacoffee.com/acaj0) is always appreciated.
