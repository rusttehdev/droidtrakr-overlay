# DroidTrakr Overlay

DroidTrakr is a native Windows companion for **Droid Tycoon in Fortnite**. It keeps Rebirth and Flawless progress, team tracking, spawn timers, Droid search, Secret Vendor Limited Deals, and DroidTrakr chat available over the game window.

> DroidTrakr is a fan-made companion and is not affiliated with, endorsed by, or sponsored by Epic Games or Lucasfilm.

## What it does

- Tracks Rebirth requirements, credit goals, and Flawless progress
- Switches between individual and group views
- Synchronizes progress with your DroidTrakr account and group
- Shows Beskar, Mythic, and Galactic spawn timers
- Searches Droid requirements by Droid name or tier
- Provides Limited Deal reporting and General Chat
- Anchors separate Droid, toolbar, and timer windows to Fortnite
- Provides configurable layouts, scale, position, visibility, and global hotkeys

DroidTrakr **does not control Fortnite, inject into the game, read game memory, or automate gameplay**.

## Default hotkeys

All five bindings can be changed under **Settings → Hotkeys**.

| Default | Action |
|---|---|
| `F7` | Switch Rebirth / Flawless |
| `F8` | Switch Individual / Group |
| `F9` | Minimize / restore the Droid area |
| `F10` | Open / close search |
| `F11` | Open / close settings |

Function keys, letters, digits, numpad and navigation keys, Mouse4, Mouse5, and Middle Mouse are supported. Duplicate bindings are rejected.

## Fortnite display mode

DroidTrakr requires **Windowed Fullscreen**. It does not alter Fortnite binaries or gameplay files. If Exclusive Fullscreen is detected, it can offer to update `GameUserSettings.ini` after confirmation and creates a backup first.

## Build from source

### Requirements

- Windows 10 or Windows 11
- Windows PowerShell 5.1
- .NET Framework with WPF assemblies available

### Build

Open Windows PowerShell in the repository directory:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build-overlay.ps1
.\build-launcher.ps1
```

The scripts compile:

- `DroidTrakr Fortnite Overlay.exe`
- `DroidTrakr Launcher.exe`

Build output and executables are intentionally excluded from source control.

## Release verification

Official downloads are published at:

- https://droidtrakr.com/app
- https://droidtrakr.com/downloads/overlay/manifest.json

Compare the downloaded package’s SHA-256 with the value in the release manifest:

```powershell
Get-FileHash .\droidtrakr-overlay-package.zip -Algorithm SHA256
```

## Data and networking

The client connects to DroidTrakr’s HTTPS endpoints for account access, synchronization, timers, chat, Limited Deals, release manifests, and updates. When the user chooses to save a login, the account name and password are encrypted with Windows DPAPI for the current Windows user. Startup still requires the user to select **Connect Overlay**.

See [PRIVACY.md](PRIVACY.md) and [SECURITY.md](SECURITY.md).

## Repository layout

- `DroidTrakrOverlay.cs` — overlay application
- `DroidTrakrLauncher.cs` — launcher and updater
- `build-overlay.ps1` — overlay build script
- `build-launcher.ps1` — launcher build script
- `rebirth-cycles.json` — Rebirth requirement data
- `flawless-data.json` — Flawless data
- `assets/` — runtime artwork and UI assets

## License

Code is released under the [MIT License](LICENSE). Third-party game names and artwork remain the property of their respective owners and are included only for this fan-made companion’s operation.
