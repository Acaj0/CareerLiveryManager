# Doing it manually (without the app)

This is the exact recipe **Career Livery Manager** automates. If you'd rather do it by hand, or want to understand what the app is doing before running it, follow these steps.

**Requirements:** the livery you want to use must ship with a `livery.cfg` file (the modern MSFS format). Liveries in the old texture-only format (no `livery.cfg`) are not supported by this technique.

> **The aircraft must already be downloaded in-game first.** MSFS 2024 streams most aircraft on demand - the `Official2024\Steam` (or `\OneStore`) folder itself exists either way, but it'll look empty (or be missing that specific plane's package) until you've downloaded the aircraft from the in-game Marketplace/Content Manager at least once. Download it in-game once, then come back here.

## 1. Find the aircraft's official "vendor" folder

Open your `Official2024\Steam` (or `Official2024\OneStore`) folder, find the package for your aircraft, and look inside:

```
<package>\simobjects\airplanes\<simobject>\liveries\
```

Note the name of the folder(s) inside `liveries\`: that's the **vendor** name (e.g. `asobo`, `microsoft`, `inibuilds`). Also note the `<simobject>` folder name itself.

## 2. Get the livery you want to use

Download or locate a third-party livery for that aircraft. It should look like this:

```
SimObjects\Airplanes\<simobject>\liveries\<creator>\<LiveryName>\livery.cfg
SimObjects\Airplanes\<simobject>\liveries\<creator>\<LiveryName>_DR\livery.cfg   (optional, "Dynamic Registration" variant)
```

## 3. Create a new Community package

Make a new, empty folder anywhere inside your `Community` folder, e.g.:

```
Community\career-livery-<simobject>-<liveryname>\
```

Inside it, recreate this structure, using the **vendor** name you found in step 1 (not the third-party creator's name):

```
career-livery-<simobject>-<liveryname>\
  manifest.json
  layout.json
  simobjects\airplanes\<simobject>\liveries\<vendor>\
    <LiveryName>\...          (only if using the Dynamic Registration variant, unprefixed - see step 5)
    !<LiveryName>\...         (or !<LiveryName>_DR\... if using Dynamic Registration)
```

Copy the livery folder(s) from step 2 into that path.

## 4. Prefix the winning livery's name with `!`

- Rename the copied livery folder itself to start with `!` (e.g. `!MyAirlineName`).
- Open its `livery.cfg`, find the `[GENERAL]` section, and prefix the `Name=` value with `!` too:

  ```ini
  [GENERAL]
  Name="!My Airline Name"
  ```

`!` sorts before letters and numbers, which is what makes this specific livery win over the aircraft's default paint.

## 5. If you're using the Dynamic Registration (`_DR`) variant

The `_DR` variant only contains the tail-number texture. It depends on its sibling folder for the rest of the paint job. Keep that sibling folder **without** the `!` prefix (so it doesn't compete for the win, but still exists on disk), and fix the fallback path inside the `_DR` folder's `texture\texture.cfg`:

```ini
[fltsim]
fallback.1=..\..\<exact name of the unprefixed sibling folder>\texture
```

Only the `_DR` folder itself gets the `!` prefix (folder name and `livery.cfg` `Name=`).

## 6. Write `manifest.json`

```json
{
  "dependencies": [],
  "content_type": "LIVERY",
  "title": "Career Livery - <Aircraft Name>",
  "manufacturer": "",
  "creator": "ManualInstall",
  "package_version": "1.0.0",
  "minimum_game_version": "1.0.67",
  "minimum_compatibility_version": "6.0.0.113",
  "export_type": "Community",
  "builder": "Microsoft Flight Simulator 2024",
  "package_order_hint": "SIMOBJECTS_PATCH",
  "release_notes": { "neutral": { "LastUpdate": "", "OlderHistory": "" } }
}
```

## 7. Write `layout.json`

List every file you copied under `simobjects\`, with its size in bytes and a Windows FILETIME timestamp:

```json
{
  "content": [
    { "path": "simobjects/airplanes/<simobject>/liveries/<vendor>/!<LiveryName>/livery.cfg", "size": 123, "date": 133700000000000000 }
  ]
}
```

If you don't want to compute the FILETIME by hand, any recent-ish value works fine in practice, since the game doesn't validate it strictly for Community content. (`date = (unix_seconds + 11644473600) * 10000000`, if you want to be precise.)

## 8. Restart MSFS and test

Close MSFS completely, reopen it, and check the aircraft in Free Flight first, then in Career. The livery should now be the one used by your Career-owned aircraft.

## Never do this

- Never edit files inside `Official2024` directly.
- Never edit the third-party livery's original folder in place. Always work from a copy inside your own new Community package.

If any of this sounds tedious, that's exactly what [Career Livery Manager](../README.md) automates for you.
