# DroidTrakr Overlay

DroidTrakr is a native Windows companion for **Droid Tycoon in Fortnite**. It keeps Rebirth requirements, team progress, spawn timers, Droid search, Secret Vendor Limited Deals, and DroidTrakr chat available over the game window.

> DroidTrakr is a fan-made companion and is not affiliated with, endorsed by, or sponsored by Epic Games or Lucasfilm.

## What it does

- Tracks Rebirth requirements and credit goals through RB30
- Synchronizes progress with your DroidTrakr account and group
- Shows Beskar, Mythic, and Galactic spawn timers
- Searches Droid requirements with `F10`
- Provides Team View and General Chat
- Lets users submit private Secret Vendor Limited Deal choices
- Anchors to the Fortnite window and hides when the game is minimized
- Provides tray controls and `F8`, `F9`, and `F10` shortcuts

DroidTrakr **does not control Fortnite, inject into the game, read game memory, or automate gameplay**.

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

The client connects only to DroidTrakr’s HTTPS endpoints for account access, synchronization, timers, chat, Limited Deals, release manifests, and updates. Account session material stored locally is protected using Windows data-protection APIs where supported.

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
