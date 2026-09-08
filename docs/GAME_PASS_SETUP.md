# Finding your folders on Game Pass / Xbox app

If you installed MSFS 2024 through the **Xbox app** (Game Pass for PC), your `Official2024` folder is **not** next to where the game itself is installed (e.g. `C:\XboxGames\Microsoft Flight Simulator 2024\...`). It lives in a separate, hidden location that Windows uses for all Microsoft Store apps.

## Option 1: Let the app find it for you (recommended)

On the first-run screen (or Options → Change folders), click **Detect automatically**. It reads the path straight from MSFS's own `UserCfg.opt`, the same file the game itself uses, so it works for Steam, Microsoft Store, and Xbox app installs without you having to dig through folders.

If that doesn't find it (e.g. a very customized install), use Option 2.

## Option 2: Find it manually

1. Open File Explorer and enable hidden items: **View → Show → Hidden items** (Windows 11), or **View tab → Hidden items** checkbox (Windows 10).
2. Navigate to:
   ```
   C:\Users\<your username>\AppData\Local\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\
   ```
3. Inside, you'll find two folders:
   - `Official2024\OneStore` → this is what you point **Career Livery Manager**'s "Official content folder" at.
   - `Community` → this is your Community folder.

> If `C:` isn't where MSFS installed its content, the folder above might be empty or missing. Check `UserCfg.opt` at:
> ```
> C:\Users\<your username>\AppData\Local\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt
> ```
> and look for the `InstalledPackagesPath` line - it points to wherever you actually chose during install.

## Still stuck?

[Open an issue](https://github.com/Acaj0/CareerLiveryManager/issues) with your `UserCfg.opt`'s `InstalledPackagesPath` line and what you see (or don't see) at that path.
